using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeeTimeBooking.Api.Data;

namespace TeeTimeBooking.Api.Bookings;

public record TeeTimeDto(int Id, string Course, DateTime StartsAt, int Capacity, int BookedPlayers, int AvailableSpots);

public record BookingDto(Guid Id, int TeeTimeId, string PlayerName, int Players, string Status, DateTime CreatedAtUtc);

public static class BookingEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static void MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/tee-times", GetTeeTimes)
            .WithTags("Tee times")
            .WithSummary("List the tee times of one day")
            .WithDescription("`date` is `yyyy-MM-dd`, course-local; today when omitted.");

        api.MapGet("/tee-times/{id:int}/bookings", GetBookings)
            .WithTags("Bookings")
            .WithSummary("List the bookings of a tee time, cancelled ones included");

        // Errors are ProblemHttpResult (application/problem+json), so their status codes are declared here.
        api.MapPost("/tee-times/{id:int}/bookings", CreateBooking)
            .WithTags("Bookings")
            .WithSummary("Book spots on a tee time")
            .WithDescription(
                $"Send a unique `{IdempotencyKeyHeader}` per booking attempt (a UUID works) and reuse it when retrying.\n\n" +
                "- **201** — booked.\n" +
                $"- **200** — a retry of an attempt that already succeeded: the original booking, header `{ReplayedHeader}: true`; nothing new is booked.\n" +
                "- **409** — not enough free spots; `availableSpots` says how many are left.\n" +
                "- **422** — the key was already used for a different request.\n\n" +
                "Concurrent requests for the same tee time are serialised by the database, so it is never overbooked.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/bookings/{id:guid}/cancel", CancelBooking)
            .WithTags("Bookings")
            .WithSummary("Cancel a booking and free its spots")
            .WithDescription("Idempotent: cancelling an already cancelled booking returns it unchanged.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static async Task<Ok<List<TeeTimeDto>>> GetTeeTimes(
        DateOnly? date, BookingDb db, TimeProvider clock, CancellationToken ct)
    {
        var day = (date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime)).ToDateTime(TimeOnly.MinValue);
        var teeTimes = await db.TeeTimes.AsNoTracking()
            .Where(t => t.StartsAt >= day && t.StartsAt < day.AddDays(1))
            .OrderBy(t => t.StartsAt)
            .Select(t => new TeeTimeDto(t.Id, t.Course, t.StartsAt, t.Capacity, t.BookedPlayers, t.Capacity - t.BookedPlayers))
            .ToListAsync(ct);
        return TypedResults.Ok(teeTimes);
    }

    public static async Task<Ok<List<BookingDto>>> GetBookings(int id, BookingDb db, CancellationToken ct)
    {
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.TeeTimeId == id)
            .OrderBy(b => b.CreatedAtUtc)
            .Select(b => new BookingDto(b.Id, b.TeeTimeId, b.PlayerName, b.Players, b.Status.ToString(), b.CreatedAtUtc))
            .ToListAsync(ct);
        return TypedResults.Ok(bookings);
    }

    public static async Task<Results<Created<BookingDto>, Ok<BookingDto>, ValidationProblem, ProblemHttpResult>> CreateBooking(
        int id,
        BookingRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        BookingService bookings,
        HttpContext http,
        CancellationToken ct)
    {
        var errors = Validate(request, idempotencyKey);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await bookings.BookAsync(id, request with { PlayerName = request.PlayerName.Trim() }, idempotencyKey!, ct);

        switch (result.Outcome)
        {
            case BookingOutcome.Created:
                http.Response.Headers[ReplayedHeader] = "false";
                return TypedResults.Created($"/api/bookings/{result.Booking!.Id}", ToDto(result.Booking));
            case BookingOutcome.Replayed:
                http.Response.Headers[ReplayedHeader] = "true";
                return TypedResults.Ok(ToDto(result.Booking!));
            case BookingOutcome.Full:
                return TypedResults.Problem(
                    title: "Not enough free spots on this tee time.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["availableSpots"] = result.AvailableSpots });
            case BookingOutcome.KeyReused:
                return TypedResults.Problem(
                    title: "This idempotency key was already used for a different booking request.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            default:
                return TypedResults.Problem(title: "Tee time not found.", statusCode: StatusCodes.Status404NotFound);
        }
    }

    public static async Task<Results<Ok<BookingDto>, ProblemHttpResult>> CancelBooking(
        Guid id, BookingService bookings, CancellationToken ct) =>
        await bookings.CancelAsync(id, ct) is { } booking
            ? TypedResults.Ok(ToDto(booking))
            : TypedResults.Problem(title: "Booking not found.", statusCode: StatusCodes.Status404NotFound);

    private static Dictionary<string, string[]> Validate(BookingRequest request, string? idempotencyKey)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > BookingDb.MaxIdempotencyKeyLength)
        {
            errors[IdempotencyKeyHeader] = [$"Send a unique key of up to {BookingDb.MaxIdempotencyKeyLength} characters per booking attempt."];
        }
        if (string.IsNullOrWhiteSpace(request.PlayerName) || request.PlayerName.Trim().Length > 100)
        {
            errors[nameof(request.PlayerName)] = ["Player name is required, up to 100 characters."];
        }
        if (request.Players is < 1 or > 4)
        {
            errors[nameof(request.Players)] = ["A booking is for 1 to 4 players."];
        }
        return errors;
    }

    private static BookingDto ToDto(Booking b) =>
        new(b.Id, b.TeeTimeId, b.PlayerName, b.Players, b.Status.ToString(), b.CreatedAtUtc);
}
