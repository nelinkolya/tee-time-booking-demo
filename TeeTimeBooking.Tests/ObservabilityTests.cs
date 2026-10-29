using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TeeTimeBooking.Api.Observability;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

/// <summary>Tests in this collection run one at a time, so a collector sees only its own test.</summary>
[Collection(ApiCollection.Name)]
public class ObservabilityTests(ApiFixture api)
{
    [Fact]
    public async Task A_stampede_is_counted_by_outcome_and_players_booked()
    {
        var meters = api.Services.GetRequiredService<IMeterFactory>();
        using var attempts = new MetricCollector<long>(meters, BookingTelemetry.Name, "teetime.booking.attempts");
        using var players = new MetricCollector<long>(meters, BookingTelemetry.Name, "teetime.booking.players");
        using var duration = new MetricCollector<double>(meters, BookingTelemetry.Name, "teetime.booking.duration");
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();

        await AllAtOnce(20, i => BookAsync(client, teeTimeId, $"Player {i}", 1, Guid.NewGuid().ToString()));

        Assert.Equal(4, Count(attempts, "created"));
        Assert.Equal(16, Count(attempts, "full"));
        Assert.Equal(4, players.GetMeasurementSnapshot().EvaluateAsCounter());
        Assert.Equal(20, duration.GetMeasurementSnapshot().Count);
        Assert.All(duration.GetMeasurementSnapshot(), m => Assert.InRange(m.Value, 0, 30));
    }

    [Fact]
    public async Task Retries_are_counted_as_replays_not_as_new_bookings()
    {
        using var attempts = new MetricCollector<long>(
            api.Services.GetRequiredService<IMeterFactory>(), BookingTelemetry.Name, "teetime.booking.attempts");
        var teeTimeId = await api.NewTeeTimeAsync();
        var client = api.CreateClient();
        var key = Guid.NewGuid().ToString();

        await AllAtOnce(5, _ => BookAsync(client, teeTimeId, "Alex", 2, key));

        Assert.Equal(1, Count(attempts, "created"));
        Assert.Equal(4, Count(attempts, "replayed"));
    }

    [Fact]
    public async Task Each_booking_is_a_span_tagged_with_tee_time_and_outcome()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == BookingTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (spans) spans.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();

        await BookAsync(client, teeTimeId, "Alex", 4, Guid.NewGuid().ToString());
        await BookAsync(client, teeTimeId, "Sam", 1, Guid.NewGuid().ToString());

        var mine = spans.Where(s => Equals(s.GetTagItem("teetime.id"), teeTimeId)).ToList();
        Assert.Equal(["created", "full"], mine.Select(s => s.GetTagItem("booking.outcome")));
        Assert.All(mine, s => Assert.Equal("BookTeeTime", s.OperationName));
        Assert.NotNull(mine[0].GetTagItem("booking.id"));
    }

    [Fact]
    public async Task Logs_are_structured_and_carry_ids_but_never_the_players_name()
    {
        var logs = api.Services.GetFakeLogCollector();
        logs.Clear();
        var teeTimeId = await api.NewTeeTimeAsync();
        var client = api.CreateClient();

        var response = await BookAsync(client, teeTimeId, "Private Person", 2, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booked = Assert.Single(logs.GetSnapshot(), r => r.Message.StartsWith("Booked") && r.Message.Contains($"tee time {teeTimeId} "));
        Assert.Equal(LogLevel.Information, booked.Level);
        Assert.Equal(teeTimeId.ToString(), booked.GetStructuredStateValue("TeeTimeId"));
        Assert.Equal("2", booked.GetStructuredStateValue("Players"));
        Assert.True(Guid.TryParse(booked.GetStructuredStateValue("BookingId"), out _));
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Message.Contains("Private Person"));
    }

    [Fact]
    public async Task Handled_duplicate_collisions_do_not_log_errors()
    {
        var logs = api.Services.GetFakeLogCollector();
        logs.Clear();
        var teeTimeId = await api.NewTeeTimeAsync();
        var client = api.CreateClient();
        var key = Guid.NewGuid().ToString();

        var responses = await AllAtOnce(10, _ => BookAsync(client, teeTimeId, "Alex", 2, key));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Level >= LogLevel.Error);
    }

    private static long Count(MetricCollector<long> collector, string outcome) =>
        collector.GetMeasurementSnapshot()
            .Where(m => Equals(m.Tags["booking.outcome"], outcome))
            .Sum(m => m.Value);
}
