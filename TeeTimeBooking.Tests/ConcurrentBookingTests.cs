using System.Net;
using Microsoft.EntityFrameworkCore;
using TeeTimeBooking.Api.Data;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

[Collection(ApiCollection.Name)]
public class ConcurrentBookingTests(ApiFixture api)
{
    [Fact]
    public async Task Twenty_parallel_single_player_requests_fill_a_four_ball_exactly()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();

        var responses = await AllAtOnce(20, i => BookAsync(client, teeTimeId, $"Player {i}", 1, Guid.NewGuid().ToString()));

        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(16, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(4, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
        Assert.Equal(4, await api.ConfirmedPlayersAsync(teeTimeId));
    }

    [Fact]
    public async Task Two_groups_of_three_racing_for_one_tee_time_only_one_gets_it()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();

        var responses = await AllAtOnce(2, i => BookAsync(client, teeTimeId, $"Group {i}", 3, Guid.NewGuid().ToString()));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(3, await api.ConfirmedPlayersAsync(teeTimeId));
    }

    [Fact]
    public async Task Mixed_group_sizes_under_load_never_overbook_and_the_counter_matches_the_bookings()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();
        var sizes = new Random(42).GetItems([1, 2, 3, 4], 40);

        var responses = await AllAtOnce(sizes.Length, i => BookAsync(client, teeTimeId, $"Group {i}", sizes[i], Guid.NewGuid().ToString()));

        var bookedBySuccesses = responses.Select((r, i) => r.StatusCode == HttpStatusCode.Created ? sizes[i] : 0).Sum();
        var teeTime = await api.LoadTeeTimeAsync(teeTimeId);
        Assert.InRange(bookedBySuccesses, 1, 4);
        Assert.Equal(bookedBySuccesses, teeTime.BookedPlayers);
        Assert.Equal(bookedBySuccesses, await api.ConfirmedPlayersAsync(teeTimeId));
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
    }

    [Fact]
    public async Task Requests_for_different_tee_times_do_not_block_each_other()
    {
        var first = await api.NewTeeTimeAsync();
        var second = await api.NewTeeTimeAsync();
        var client = api.CreateClient();

        var responses = await AllAtOnce(8, i => BookAsync(client, i % 2 == 0 ? first : second, $"Player {i}", 1, Guid.NewGuid().ToString()));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(4, (await api.LoadTeeTimeAsync(first)).BookedPlayers);
        Assert.Equal(4, (await api.LoadTeeTimeAsync(second)).BookedPlayers);
    }

    [Fact]
    public async Task Database_rejects_a_counter_above_capacity_even_if_code_tries()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        await using var db = api.NewDb();

        await Assert.ThrowsAnyAsync<Exception>(() => db.TeeTimes
            .Where(t => t.Id == teeTimeId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BookedPlayers, 5)));
    }

    /// <summary>The approach the service avoids, paused inside its race window: it passes because the bug happens.</summary>
    [Fact]
    public async Task Naive_check_then_insert_lets_two_groups_overbook()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var bothChecked = new CountdownGate(2);

        await AllAtOnce(2, async i =>
        {
            await using var db = api.NewDb();
            return await NaiveBookAsync(db, teeTimeId, players: 3, afterCheck: bothChecked.SignalAndWaitAsync);
        });

        Assert.Equal(6, await api.ConfirmedPlayersAsync(teeTimeId));
    }

    private static async Task<bool> NaiveBookAsync(BookingDb db, int teeTimeId, int players, Func<Task> afterCheck)
    {
        var capacity = await db.TeeTimes.Where(t => t.Id == teeTimeId).Select(t => t.Capacity).SingleAsync();
        var taken = await db.Bookings
            .Where(b => b.TeeTimeId == teeTimeId && b.Status == BookingStatus.Confirmed)
            .SumAsync(b => (int?)b.Players) ?? 0;
        if (taken + players > capacity)
        {
            return false;
        }

        await afterCheck();

        db.Bookings.Add(new Booking
        {
            TeeTimeId = teeTimeId,
            PlayerName = "Naive",
            Players = players,
            IdempotencyKey = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return true;
    }

    private sealed class CountdownGate(int participants)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remaining = participants;

        public Task SignalAndWaitAsync()
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                _open.SetResult();
            }
            return _open.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
