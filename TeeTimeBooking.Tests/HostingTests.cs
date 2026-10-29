using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using TeeTimeBooking.Api;

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
    public async Task Liveness_does_not_depend_on_the_database()
    {
        var unreachableDb = api.WithWebHostBuilder(b => b.UseSetting(
            "ConnectionStrings:TeeTimes", "Server=tcp:127.0.0.1,1;Database=TeeTimeBooking_Test;Connect Timeout=1;TrustServerCertificate=True"));
        var client = unreachableDb.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task Version_reports_the_release_and_the_environment()
    {
        var version = await api.CreateClient().GetFromJsonAsync<VersionDto>("/api/version");

        Assert.Equal("1.0.0", version!.Version);
        Assert.Equal("Testing", version.Environment);
    }

    [Fact]
    public async Task An_unknown_API_path_is_a_404_problem_not_the_UI()
    {
        var response = await api.CreateClient().GetAsync("/api/no-such-thing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
