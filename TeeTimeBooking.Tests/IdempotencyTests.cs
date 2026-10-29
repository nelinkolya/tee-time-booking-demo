using System.Net;
using System.Net.Http.Json;
using TeeTimeBooking.Api.Bookings;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

[Collection(ApiCollection.Name)]
public class IdempotencyTests(ApiFixture api)
{
    [Fact]
    public async Task A_retry_with_the_same_key_returns_the_original_booking_and_books_nothing_new()
    {
        var teeTimeId = await api.NewTeeTimeAsync();
        var client = api.CreateClient();
        var key = Guid.NewGuid().ToString();

        var first = await BookAsync(client, teeTimeId, "Alex", 2, key);
        var retry = await BookAsync(client, teeTimeId, "Alex", 2, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues(BookingEndpoints.ReplayedHeader).Single());
        Assert.Equal((await first.Content.ReadFromJsonAsync<BookingDto>())!.Id, (await retry.Content.ReadFromJsonAsync<BookingDto>())!.Id);
        Assert.Equal(2, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
    }

    [Fact]
    public async Task Ten_simultaneous_copies_of_one_request_create_exactly_one_booking()
    {
        var teeTimeId = await api.NewTeeTimeAsync();
        var client = api.CreateClient();
        var key = Guid.NewGuid().ToString();

        var responses = await AllAtOnce(10, _ => BookAsync(client, teeTimeId, "Alex", 2, key));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<BookingDto>())!.Id));
        Assert.Single(ids.Distinct());
        Assert.Equal(2, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
    }

    [Fact]
    public async Task Copies_of_a_request_that_takes_the_last_spots_get_the_booking_not_a_conflict()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();
        var key = Guid.NewGuid().ToString();

        var responses = await AllAtOnce(10, _ => BookAsync(client, teeTimeId, "Four-ball", 4, key));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(4, await api.ConfirmedPlayersAsync(teeTimeId));
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_rejected()
    {
        var teeTimeId = await api.NewTeeTimeAsync();
        var client = api.CreateClient();
        var key = Guid.NewGuid().ToString();

        await BookAsync(client, teeTimeId, "Alex", 1, key);
        var different = await BookAsync(client, teeTimeId, "Alex", 3, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        Assert.Equal(1, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
    }

    [Theory]
    [InlineData(null, "Alex", 2)]
    [InlineData("", "Alex", 2)]
    [InlineData("key", "", 2)]
    [InlineData("key", "Alex", 0)]
    [InlineData("key", "Alex", 5)]
    public async Task Invalid_requests_are_rejected_before_touching_the_slot(string? key, string name, int players)
    {
        var teeTimeId = await api.NewTeeTimeAsync();
        var client = api.CreateClient();

        // "key" stands for a valid key; made unique so these cases never replay each other.
        var response = await BookAsync(client, teeTimeId, name, players, key == "key" ? key + Guid.NewGuid() : key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
    }

    [Fact]
    public async Task Booking_an_unknown_tee_time_is_404()
    {
        var response = await BookAsync(api.CreateClient(), int.MaxValue, "Alex", 1, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
