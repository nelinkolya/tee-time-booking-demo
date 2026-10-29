using Microsoft.Data.SqlClient;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TeeTimeBooking.Api.Data;
using TeeTimeBooking.Api.Observability;
using static TeeTimeBooking.Api.Observability.BookingTelemetry;

namespace TeeTimeBooking.Api.Bookings;

public record BookingRequest(string PlayerName, int Players);

public enum BookingOutcome
{
    Created,
    Replayed,
    Full,
    TeeTimeNotFound,
    KeyReused,
}

public record BookingResult(BookingOutcome Outcome, Booking? Booking = null, int AvailableSpots = 0);

/// <summary>
/// Concurrency: the capacity check and the claim are one conditional UPDATE, which locks the tee-time row until
/// commit. Duplicates: the idempotency key is under a unique index, so racing copies collide and the loser replays.
/// </summary>
public partial class BookingService(
    BookingDb db, TimeProvider clock, BookingTelemetry telemetry, ILogger<BookingService> logger)
{
    public async Task<BookingResult> BookAsync(
        int teeTimeId, BookingRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        using var activity = Source.StartActivity("BookTeeTime");
        activity?.SetTag("teetime.id", teeTimeId);
        activity?.SetTag("booking.players", request.Players);
        var started = clock.GetTimestamp();

        BookingResult result;
        try
        {
            // A transient failure retries the whole attempt; if the first try did commit, the retry finds the
            // booking by its idempotency key and replays it.
            result = await db.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            {
                db.ChangeTracker.Clear();
                return BookCoreAsync(teeTimeId, request, idempotencyKey, ct);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            telemetry.RecordBooking(Outcomes.Error, request.Players, clock.GetElapsedTime(started));
            throw; // the exception handler logs it
        }

        var outcome = Outcomes.Of(result.Outcome);
        activity?.SetTag("booking.outcome", outcome);
        activity?.SetTag("booking.id", result.Booking?.Id);
        telemetry.RecordBooking(outcome, request.Players, clock.GetElapsedTime(started));

        switch (result.Outcome)
        {
            case BookingOutcome.Created:
                LogBooked(logger, result.Booking!.Id, teeTimeId, request.Players);
                break;
            case BookingOutcome.Replayed:
                LogReplayed(logger, result.Booking!.Id, teeTimeId, idempotencyKey);
                break;
            case BookingOutcome.Full:
                LogFull(logger, teeTimeId, request.Players, result.AvailableSpots);
                break;
            case BookingOutcome.KeyReused:
                LogKeyReused(logger, idempotencyKey, result.Booking!.Id, teeTimeId);
                break;
            default:
                LogTeeTimeNotFound(logger, teeTimeId);
                break;
        }
        return result;
    }

    private async Task<BookingResult> BookCoreAsync(
        int teeTimeId, BookingRequest request, string idempotencyKey, CancellationToken ct)
    {
        if (await FindByKeyAsync(idempotencyKey, ct) is { } earlier)
        {
            return Replay(earlier, teeTimeId, request);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await db.TeeTimes
            .Where(t => t.Id == teeTimeId && t.BookedPlayers + request.Players <= t.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BookedPlayers, t => t.BookedPlayers + request.Players), ct);

        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            // A copy with the same key may have taken the last spots while this one waited: a replay, not "full".
            if (await FindByKeyAsync(idempotencyKey, ct) is { } twin)
            {
                return Replay(twin, teeTimeId, request);
            }

            var teeTime = await db.TeeTimes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == teeTimeId, ct);
            return teeTime is null
                ? new BookingResult(BookingOutcome.TeeTimeNotFound)
                : new BookingResult(BookingOutcome.Full, AvailableSpots: teeTime.Capacity - teeTime.BookedPlayers);
        }

        var booking = new Booking
        {
            TeeTimeId = teeTimeId,
            PlayerName = request.PlayerName,
            Players = request.Players,
            IdempotencyKey = idempotencyKey,
            Status = BookingStatus.Confirmed,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new BookingResult(BookingOutcome.Created, booking);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A copy with the same key committed first; the rollback also releases the spots claimed above.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(idempotencyKey, ct)
                ?? throw new InvalidOperationException("Unique violation on the idempotency key, but no booking has it.");
            LogConcurrentDuplicate(logger, winner.Id, teeTimeId);
            return Replay(winner, teeTimeId, request);
        }
    }

    public Task<Booking?> CancelAsync(Guid bookingId, CancellationToken ct = default) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => CancelCoreAsync(bookingId, ct));

    private async Task<Booking?> CancelCoreAsync(Guid bookingId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var cancelled = await db.Bookings
            .Where(b => b.Id == bookingId && b.Status == BookingStatus.Confirmed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.Cancelled)
                .SetProperty(b => b.CancelledAtUtc, now), ct);

        if (cancelled == 1)
        {
            // Only the cancel that flipped the status frees the spots.
            var freed = await db.Bookings.Where(b => b.Id == bookingId)
                .Select(b => new { b.TeeTimeId, b.Players }).SingleAsync(ct);
            await db.TeeTimes
                .Where(t => t.Id == freed.TeeTimeId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BookedPlayers, t => t.BookedPlayers - freed.Players), ct);
            telemetry.RecordCancellation();
            LogCancelled(logger, bookingId, freed.TeeTimeId, freed.Players);
        }

        await tx.CommitAsync(ct);
        return await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
    }

    private Task<Booking?> FindByKeyAsync(string key, CancellationToken ct) =>
        db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.IdempotencyKey == key, ct);

    private static BookingResult Replay(Booking earlier, int teeTimeId, BookingRequest request) =>
        earlier.TeeTimeId == teeTimeId && earlier.Players == request.Players && earlier.PlayerName == request.PlayerName
            ? new BookingResult(BookingOutcome.Replayed, earlier)
            : new BookingResult(BookingOutcome.KeyReused, earlier);

    // The player's name is personal data and is never logged.
    [LoggerMessage(LogLevel.Information, "Booked {Players} player(s) on tee time {TeeTimeId} as booking {BookingId}")]
    private static partial void LogBooked(ILogger logger, Guid bookingId, int teeTimeId, int players);

    [LoggerMessage(LogLevel.Information, "Replayed booking {BookingId} on tee time {TeeTimeId} for idempotency key {IdempotencyKey}")]
    private static partial void LogReplayed(ILogger logger, Guid bookingId, int teeTimeId, string idempotencyKey);

    [LoggerMessage(LogLevel.Information, "A concurrent copy of booking {BookingId} on tee time {TeeTimeId} collided on its idempotency key and was rolled back")]
    private static partial void LogConcurrentDuplicate(ILogger logger, Guid bookingId, int teeTimeId);

    [LoggerMessage(LogLevel.Information, "Tee time {TeeTimeId} is full: {Requested} requested, {AvailableSpots} left")]
    private static partial void LogFull(ILogger logger, int teeTimeId, int requested, int availableSpots);

    [LoggerMessage(LogLevel.Warning, "Idempotency key {IdempotencyKey} of booking {BookingId} on tee time {TeeTimeId} was reused for a different request")]
    private static partial void LogKeyReused(ILogger logger, string idempotencyKey, Guid bookingId, int teeTimeId);

    [LoggerMessage(LogLevel.Information, "Tee time {TeeTimeId} not found")]
    private static partial void LogTeeTimeNotFound(ILogger logger, int teeTimeId);

    [LoggerMessage(LogLevel.Information, "Cancelled booking {BookingId} on tee time {TeeTimeId}, freeing {Players} spot(s)")]
    private static partial void LogCancelled(ILogger logger, Guid bookingId, int teeTimeId, int players);

    // 2601: duplicate key in a unique index; 2627: unique constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
