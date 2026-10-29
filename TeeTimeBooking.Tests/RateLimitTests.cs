using System.Net;
using Microsoft.AspNetCore.Hosting;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

[Collection(ApiCollection.Name)]
public class RateLimitTests(ApiFixture api)
{
    [Fact]
    public async Task Bookings_over_the_client_limit_get_429_with_retry_after_and_book_nothing()
    {
        var limited = api.WithWebHostBuilder(b => b
            .UseSetting("RateLimiting:Enabled", "true")
            .UseSetting("RateLimiting:BookingPermits", "3"));
        var client = limited.CreateClient();
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await BookAsync(client, teeTimeId, $"Player {i}", 1, Guid.NewGuid().ToString()));
        }

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var rejected = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.Equal(2, rejected.Count);
        Assert.All(rejected, r =>
        {
            Assert.True(r.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        });
        Assert.Equal(3, (await api.LoadTeeTimeAsync(teeTimeId)).BookedPlayers);
    }

    [Fact]
    public async Task Reads_have_their_own_limit()
    {
        var limited = api.WithWebHostBuilder(b => b
            .UseSetting("RateLimiting:Enabled", "true")
            .UseSetting("RateLimiting:ApiPermitsPerMinute", "2"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.GetAsync("/api/tee-times")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
