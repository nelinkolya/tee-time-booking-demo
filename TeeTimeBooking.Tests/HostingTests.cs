using System.Net;

namespace TeeTimeBooking.Tests;

[Collection(ApiCollection.Name)]
public class HostingTests(ApiFixture api)
{
    [Fact]
    public async Task Health_is_healthy_when_the_database_answers()
    {
        var response = await api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_API_path_is_a_404_problem_not_the_UI()
    {
        var response = await api.CreateClient().GetAsync("/api/no-such-thing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
