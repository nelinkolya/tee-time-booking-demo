using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
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
public sealed class TicketService(BookingDb db, ITicketKeys keys, TimeProvider clock)
{
    private const string Algorithm = "ES256";

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
        var header = Encode(new Header(Algorithm, "JWT", kid));
        var payload = Encode(new TicketClaims(
            booking.Id, booking.TeeTimeId, booking.Players, booking.StartsAt, clock.GetUtcNow().ToUnixTimeSeconds()));
        var signature = await keys.SignAsync(kid, Encoding.ASCII.GetBytes($"{header}.{payload}"), ct);
        return (TicketIssue.Issued, $"{header}.{payload}.{Base64Url.EncodeToString(signature)}");
    }

    public async Task<(TicketCheck Outcome, TicketClaims? Claims)> VerifyAsync(string token, CancellationToken ct)
    {
        var parts = token.Split('.');
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
