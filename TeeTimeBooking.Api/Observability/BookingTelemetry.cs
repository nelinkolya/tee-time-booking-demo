using System.Diagnostics;
using System.Diagnostics.Metrics;
using TeeTimeBooking.Api.Bookings;

namespace TeeTimeBooking.Api.Observability;

public sealed class BookingTelemetry
{
    public const string Name = "TeeTimeBooking";

    public static readonly ActivitySource Source = new(Name);

    private readonly Counter<long> _attempts;
    private readonly Counter<long> _playersBooked;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _cancellations;

    public BookingTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        _attempts = meter.CreateCounter<long>(
            "teetime.booking.attempts", unit: "{attempt}",
            description: "Booking requests that reached the service, by outcome.");
        _playersBooked = meter.CreateCounter<long>(
            "teetime.booking.players", unit: "{player}",
            description: "Players booked onto tee times (new bookings only, not replays).");
        _duration = meter.CreateHistogram<double>(
            "teetime.booking.duration", unit: "s",
            description: "Time the service spent on a booking request, by outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5] });
        _cancellations = meter.CreateCounter<long>(
            "teetime.booking.cancellations", unit: "{booking}",
            description: "Bookings cancelled (repeated cancels of one booking count once).");
    }

    // Outcome is the only tag: per-request ids would make a time series per tee time.
    public void RecordBooking(string outcome, int players, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>("booking.outcome", outcome);
        _attempts.Add(1, tag);
        _duration.Record(elapsed.TotalSeconds, tag);
        if (outcome == Outcomes.Created)
        {
            _playersBooked.Add(players);
        }
    }

    public void RecordCancellation() => _cancellations.Add(1);

    public static class Outcomes
    {
        public const string Created = "created";
        public const string Replayed = "replayed";
        public const string Full = "full";
        public const string KeyReused = "key_reused";
        public const string NotFound = "not_found";
        public const string Error = "error";

        public static string Of(BookingOutcome outcome) => outcome switch
        {
            BookingOutcome.Created => Created,
            BookingOutcome.Replayed => Replayed,
            BookingOutcome.Full => Full,
            BookingOutcome.KeyReused => KeyReused,
            _ => NotFound,
        };
    }
}
