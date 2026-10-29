using System.Net;
using System.Text.Json;

namespace TeeTimeBooking.Tests;

[Collection(ApiCollection.Name)]
public class OpenApiTests(ApiFixture api)
{
    [Fact]
    public async Task The_booking_operation_documents_the_idempotency_header_and_every_response()
    {
        using var doc = JsonDocument.Parse(await api.CreateClient().GetStringAsync("/openapi/v1.json"));

        var post = doc.RootElement.GetProperty("paths").GetProperty("/api/tee-times/{id}/bookings").GetProperty("post");
        var header = post.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header");
        Assert.Equal("Idempotency-Key", header.GetProperty("name").GetString());
        Assert.Equal(
            ["200", "201", "400", "404", "409", "422"],
            post.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order());
        Assert.True(post.GetProperty("responses").GetProperty("409").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task Swagger_UI_is_served()
    {
        var response = await api.CreateClient().GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());
    }
}
