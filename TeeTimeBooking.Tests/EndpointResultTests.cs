using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TeeTimeBooking.Api.Bookings;

namespace TeeTimeBooking.Tests;

public class EndpointResultTests
{
    [Theory]
    [InlineData(null, "Alex", 2, BookingEndpoints.IdempotencyKeyHeader)]
    [InlineData("key", " ", 2, nameof(BookingRequest.PlayerName))]
    [InlineData("key", "Alex", 5, nameof(BookingRequest.Players))]
    public async Task Invalid_booking_requests_return_a_validation_problem_naming_the_field(
        string? key, string name, int players, string field)
    {
        var response = await BookingEndpoints.CreateBooking(
            1, new BookingRequest(name, players), key, bookings: null!, new DefaultHttpContext(), CancellationToken.None);

        var problem = Assert.IsType<ValidationProblem>(response.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal([field], problem.ProblemDetails.Errors.Keys);
    }
}

[Collection(ApiCollection.Name)]
public class ProblemResponseTests(ApiFixture api)
{
    [Fact]
    public async Task A_full_tee_time_answers_with_problem_json_and_the_spots_left()
    {
        var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
        var client = api.CreateClient();
        await ApiFixture.BookAsync(client, teeTimeId, "Trio", 3, Guid.NewGuid().ToString());

        var response = await ApiFixture.BookAsync(client, teeTimeId, "Pair", 2, Guid.NewGuid().ToString());

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not enough free spots on this tee time.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("availableSpots").GetInt32());
    }
}
