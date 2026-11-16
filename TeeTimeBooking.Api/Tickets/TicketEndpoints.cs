using System.Buffers.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TeeTimeBooking.Api.Data;

namespace TeeTimeBooking.Api.Tickets;

public record TicketDto(string Token);

public record TicketToVerify(string Token);

public record TicketCheckDto(
    Guid BookingId,
    int TeeTimeId,
    int Players,
    DateTime StartsAt,
    DateTime IssuedAtUtc,
    // The booking now: a ticket stays validly signed after its booking is cancelled.
    [property: JsonConverter(typeof(JsonStringEnumConverter<BookingStatus>))] BookingStatus Status);

/// <summary>A JSON Web Key Set (RFC 7517) with the public halves of the ticket keys.</summary>
public record JsonWebKeySet(List<JsonWebKey> Keys);

public record JsonWebKey(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("alg")] string Alg);

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireRateLimiting(RateLimits.Api).WithTags("Tickets");

        api.MapGet("/bookings/{id:guid}/ticket", IssueTicket)
            .WithSummary("A signed ticket for a confirmed booking")
            .WithDescription(
                "A JWT (ES256) stating the booking, its tee time and players. Check it with `POST /api/tickets/verify`, " +
                "or offline against the keys at `/.well-known/jwks.json`.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost("/tickets/verify", VerifyTicket)
            .WithSummary("Check a ticket's signature and its booking's current status")
            .WithDescription("**422** — not a ticket signed by this service: malformed, an unknown or retired key, or a bad signature.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/.well-known/jwks.json", GetKeys)
            .RequireRateLimiting(RateLimits.Api)
            .WithTags("Tickets")
            .WithSummary("The public keys tickets are signed with, one per key version");
    }

    public static async Task<Results<Ok<TicketDto>, ProblemHttpResult>> IssueTicket(
        Guid id, TicketService tickets, CancellationToken ct)
    {
        var (outcome, token) = await tickets.IssueAsync(id, ct);
        return outcome switch
        {
            TicketIssue.Issued => TypedResults.Ok(new TicketDto(token!)),
            TicketIssue.BookingCancelled => TypedResults.Problem(
                title: "The booking is cancelled; it has no ticket.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(title: "Booking not found.", statusCode: StatusCodes.Status404NotFound),
        };
    }

    public static async Task<Results<Ok<TicketCheckDto>, ProblemHttpResult>> VerifyTicket(
        TicketToVerify request, TicketService tickets, BookingDb db, CancellationToken ct)
    {
        var (outcome, claims) = await tickets.VerifyAsync(request.Token ?? "", ct);
        if (outcome != TicketCheck.Valid)
        {
            return TypedResults.Problem(
                title: "Not a valid ticket.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["reason"] = outcome.ToString() });
        }

        var status = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == claims!.BookingId)
            .Select(b => (BookingStatus?)b.Status)
            .SingleOrDefaultAsync(ct);
        if (status is null)
        {
            // Signed by us for a booking that no longer exists: bookings are never deleted, except by the demo reset.
            return TypedResults.Problem(
                title: "Not a valid ticket.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["reason"] = "BookingNotFound" });
        }

        return TypedResults.Ok(new TicketCheckDto(
            claims!.BookingId, claims.TeeTimeId, claims.Players, claims.StartsAt,
            DateTimeOffset.FromUnixTimeSeconds(claims.IssuedAt).UtcDateTime, status.Value));
    }

    public static async Task<Ok<JsonWebKeySet>> GetKeys(ITicketKeys keys, CancellationToken ct)
    {
        var publicKeys = await keys.GetPublicKeysAsync(refresh: false, ct);
        return TypedResults.Ok(new JsonWebKeySet(publicKeys
            .Select(k => new JsonWebKey(
                "EC", "P-256", Base64Url.EncodeToString(k.Value.Q.X), Base64Url.EncodeToString(k.Value.Q.Y), k.Key, "sig", "ES256"))
            .ToList()));
    }
}
