using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TeeTimeBooking.Api.Data;

namespace TeeTimeBooking.Api.Tickets;

/// <summary>What a ticket states, as signed: a JWT payload whose <c>sub</c> is the booking id.</summary>
public record TicketClaims(
    [property: JsonPropertyName("sub")] Guid BookingId,
    [property: JsonPropertyName("teeTime")] int TeeTimeId,
    [property: JsonPropertyName("players")] int Players,
    // Course-local, like the tee time itself.
    [property: JsonPropertyName("startsAt")] DateTime StartsAt,
    [property: JsonPropertyName("iat")] long IssuedAt);

public enum TicketIssue { Issued, BookingNotFound, BookingCancelled }

public enum TicketCheck { Valid, Malformed, UnknownKey, BadSignature }

/// <summary>
/// Booking tickets: compact JWS (a JWT) signed with ES256, so anyone holding the public keys
/// (<c>/.well-known/jwks.json</c>) can check one offline. The header's <c>kid</c> names the key version.
/// A ticket proves what was booked, not that it still stands: the booking's status is looked up separately.
/// </summary>
public sealed class TicketService(BookingDb db, ITicketKeys keys, IssuedTickets issued, TimeProvider clock)
{
    private const string Algorithm = "ES256";

    /// <summary>A real ticket is about 400 characters.</summary>
    public const int MaxTokenLength = 2048;

    private record Header(
        [property: JsonPropertyName("alg")] string Alg,
        [property: JsonPropertyName("typ")] string Typ,
        [property: JsonPropertyName("kid")] string Kid);

    public async Task<(TicketIssue Outcome, string? Token)> IssueAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new { b.Id, b.TeeTimeId, b.Players, b.Status, b.TeeTime.StartsAt })
            .SingleOrDefaultAsync(ct);
        if (booking is null)
        {
            return (TicketIssue.BookingNotFound, null);
        }
        if (booking.Status != BookingStatus.Confirmed)
        {
            return (TicketIssue.BookingCancelled, null);
        }

        var kid = await keys.GetCurrentKidAsync(ct);
        // One signature per booking and key version: a signature in Key Vault is billed, the same ticket again is not.
        if (issued.Find(booking.Id, kid) is { } again)
        {
            return (TicketIssue.Issued, again);
        }

        var header = Encode(new Header(Algorithm, "JWT", kid));
        var payload = Encode(new TicketClaims(
            booking.Id, booking.TeeTimeId, booking.Players, booking.StartsAt, clock.GetUtcNow().ToUnixTimeSeconds()));
        var signature = await keys.SignAsync(kid, Encoding.ASCII.GetBytes($"{header}.{payload}"), ct);
        var token = $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
        issued.Remember(booking.Id, kid, token);
        return (TicketIssue.Issued, token);
    }

    public async Task<(TicketCheck Outcome, TicketClaims? Claims)> VerifyAsync(string token, CancellationToken ct)
    {
        // Checked before any parsing: the endpoint is open, and a request body may be megabytes of dots.
        if (token.Length > MaxTokenLength)
        {
            return (TicketCheck.Malformed, null);
        }
        var parts = token.Split('.', 4);
        if (parts.Length != 3
            || Decode<Header>(parts[0]) is not { Alg: Algorithm, Kid: { Length: > 0 } kid }
            || !Base64Url.IsValid(parts[2]))
        {
            return (TicketCheck.Malformed, null);
        }

        // A kid not seen yet may be a version created after the cache was filled.
        if (!(await keys.GetPublicKeysAsync(refresh: false, ct)).TryGetValue(kid, out var publicKey)
            && !(await keys.GetPublicKeysAsync(refresh: true, ct)).TryGetValue(kid, out publicKey))
        {
            return (TicketCheck.UnknownKey, null);
        }

        using var ec = ECDsa.Create(publicKey);
        var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        if (!ec.VerifyData(signed, Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256))
        {
            return (TicketCheck.BadSignature, null);
        }

        // Signed by us, so the payload is ours; still, refuse one that does not parse.
        return Decode<TicketClaims>(parts[1]) is { } claims ? (TicketCheck.Valid, claims) : (TicketCheck.Malformed, null);
    }

    private static string Encode<T>(T value) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));

    private static T? Decode<T>(string part) where T : class
    {
        try
        {
            return Base64Url.IsValid(part) ? JsonSerializer.Deserialize<T>(Base64Url.DecodeFromChars(part)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Tickets already signed, by booking and key version, so asking again costs no new signature. In memory: a restart
/// (or scaling to zero) forgets them, and the next request signs once more. Bounded, so it cannot grow without limit.
/// </summary>
public sealed class IssuedTickets : IDisposable
{
    private readonly MemoryCache _tickets = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public string? Find(Guid bookingId, string kid) => _tickets.Get<string>((bookingId, kid));

    public void Remember(Guid bookingId, string kid, string token) =>
        _tickets.Set((bookingId, kid), token, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1),
        });

    public void Dispose() => _tickets.Dispose();
}
