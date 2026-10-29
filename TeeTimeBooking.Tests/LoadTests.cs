using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using TeeTimeBooking.Api.Data;
using Xunit.Abstractions;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

/// <summary>Skip with <c>dotnet test --filter Category!=Load</c>.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Load")]
public class LoadTests(ApiFixture api, ITestOutputHelper output)
{
    [Theory]
    [InlineData(1_000)]
    public async Task A_stampede_on_one_tee_time_books_exactly_its_capacity(int requests)
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();
        var watch = Stopwatch.StartNew();

        var responses = await AllAtOnce(requests, i => BookAsync(client, teeTimeId, $"Player {i}", 1, Guid.NewGuid().ToString()));

        Report(requests, watch, responses);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(requests - 4, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(4, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
        Assert.Equal(4, await api.ConfirmedPlayersAsync(teeTimeId));
    }

    [Theory]
    [InlineData(1_000)]
    public async Task A_busy_morning_over_many_tee_times_never_overbooks_or_duplicates(int requests)
    {
        var teeTimes = new List<int>();
        for (var i = 0; i < 100; i++)
        {
            teeTimes.Add(await api.NewTeeTimeAsync(capacity: 4));
        }
        var random = new Random(7);
        var attempts = Enumerable.Range(0, requests)
            .Select(i => (TeeTime: teeTimes[random.Next(teeTimes.Count)], Players: random.Next(1, 5), Key: $"load-{requests}-{i}"))
            .ToList();
        // Every tenth attempt arrives twice: same tee time, same group, same key.
        var sent = attempts.Concat(attempts.Where((_, i) => i % 10 == 0)).ToArray();
        var client = api.CreateClient();
        var watch = Stopwatch.StartNew();

        var responses = await AllAtOnce(sent.Length, i => BookAsync(client, sent[i].TeeTime, "Player", sent[i].Players, sent[i].Key));

        Report(sent.Length, watch, responses);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict }));

        await using var db = api.NewDb();
        var slots = await db.TeeTimes.AsNoTracking().Where(t => teeTimes.Contains(t.Id)).ToListAsync();
        var booked = await db.Bookings.AsNoTracking()
            .Where(b => teeTimes.Contains(b.TeeTimeId) && b.Status == BookingStatus.Confirmed)
            .GroupBy(b => b.TeeTimeId)
            .Select(g => new { TeeTimeId = g.Key, Players = g.Sum(b => b.Players) })
            .ToDictionaryAsync(g => g.TeeTimeId, g => g.Players);
        Assert.All(slots, t =>
        {
            Assert.InRange(t.BookedPlayers, 0, t.Capacity);
            Assert.Equal(booked.GetValueOrDefault(t.Id), t.BookedPlayers);
        });
        var keys = sent.Select(s => s.Key).Distinct().ToList();
        Assert.Equal(
            responses.Count(r => r.StatusCode == HttpStatusCode.Created),
            await db.Bookings.CountAsync(b => keys.Contains(b.IdempotencyKey)));
    }

    private void Report(int requests, Stopwatch watch, HttpResponseMessage[] responses)
    {
        var byStatus = responses.GroupBy(r => (int)r.StatusCode).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}");
        output.WriteLine($"{requests} requests in {watch.Elapsed.TotalSeconds:F1}s ({requests / watch.Elapsed.TotalSeconds:F0}/s): {string.Join(", ", byStatus)}");
    }
}
