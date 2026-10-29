using System.Net;
using System.Net.Http.Json;
using TeeTimeBooking.Api.Bookings;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

[Collection(ApiCollection.Name)]
public class CancellationTests(ApiFixture api)
{
    [Fact]
    public async Task Cancelling_frees_the_spots_for_someone_else()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();
        var booking = await (await BookAsync(client, teeTimeId, "Alex", 4, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<BookingDto>();

        var full = await BookAsync(client, teeTimeId, "Sam", 2, Guid.NewGuid().ToString());
        await client.PostAsync($"/api/bookings/{booking!.Id}/cancel", null);
        var afterCancel = await BookAsync(client, teeTimeId, "Sam", 2, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal(HttpStatusCode.Created, afterCancel.StatusCode);
        Assert.Equal(2, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
    }

    [Fact]
    public async Task Parallel_cancels_of_one_booking_free_its_spots_once()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();
        await BookAsync(client, teeTimeId, "Stays", 1, Guid.NewGuid().ToString());
        var booking = await (await BookAsync(client, teeTimeId, "Leaves", 3, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<BookingDto>();

        var responses = await AllAtOnce(5, _ => client.PostAsync($"/api/bookings/{booking!.Id}/cancel", null));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
        Assert.Equal(1, await api.ConfirmedPlayersAsync(teeTimeId));
    }

    [Fact]
    public async Task Cancelling_an_unknown_booking_is_404()
    {
        var response = await api.CreateClient().PostAsync($"/api/bookings/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
