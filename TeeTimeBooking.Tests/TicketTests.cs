using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TeeTimeBooking.Api.Bookings;
using TeeTimeBooking.Api.Data;
using TeeTimeBooking.Api.Tickets;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

/// <summary>
/// Tickets are signed with the in-memory key here; in Azure the same code signs in Key Vault (KeyVaultTicketKeys).
/// What is tested is the contract both share: JWS ES256, a kid per key version, old versions verifying until retired.
/// </summary>
[Collection(ApiCollection.Name)]
public class TicketTests(ApiFixture api)
{
    [Fact]
    public async Task A_confirmed_booking_gets_a_ticket_that_verifies_and_states_the_booking()
    {
        var (client, booking) = await BookedAsync(players: 3);

        var ticket = await TicketAsync(client, booking.Id);
        var check = await client.PostAsJsonAsync("/api/tickets/verify", new TicketToVerify(ticket));
        var checkedTicket = await check.Content.ReadFromJsonAsync<TicketCheckDto>();

        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        Assert.Equal(booking.Id, checkedTicket!.BookingId);
        Assert.Equal(booking.TeeTimeId, checkedTicket.TeeTimeId);
        Assert.Equal(3, checkedTicket.Players);
        Assert.Equal((await api.LoadTeeTimeAsync(booking.TeeTimeId)).StartsAt, checkedTicket.StartsAt);
        Assert.Equal(BookingStatus.Confirmed, checkedTicket.Status);
    }

    [Fact]
    public async Task A_ticket_verifies_offline_against_the_published_keys()
    {
        var (client, booking) = await BookedAsync();
        var parts = (await TicketAsync(client, booking.Id)).Split('.');

        // Standard JWS checked with nothing but the JWKS: no code of the service involved.
        var header = JsonNode.Parse(Base64Url.DecodeFromChars(parts[0]))!;
        var jwks = JsonNode.Parse(await client.GetStringAsync("/.well-known/jwks.json"))!;
        var jwk = jwks["keys"]!.AsArray().Single(k => (string)k!["kid"]! == (string)header["kid"]!)!;
        using var ec = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64Url.DecodeFromChars((string)jwk["x"]!),
                Y = Base64Url.DecodeFromChars((string)jwk["y"]!),
            },
        });

        Assert.Equal("ES256", (string)header["alg"]!);
        Assert.True(ec.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task A_ticket_changed_after_signing_is_rejected()
    {
        var (client, booking) = await BookedAsync(players: 1);
        var parts = (await TicketAsync(client, booking.Id)).Split('.');

        var payload = JsonNode.Parse(Base64Url.DecodeFromChars(parts[1]))!;
        payload["players"] = 4;
        var forged = $"{parts[0]}.{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload))}.{parts[2]}";

        await AssertRejectedAsync(client, forged, "BadSignature");
    }

    [Fact]
    public async Task A_cancelled_booking_gets_no_new_ticket_and_its_earlier_ticket_shows_the_cancellation()
    {
        var (client, booking) = await BookedAsync();
        var ticket = await TicketAsync(client, booking.Id);
        await client.PostAsync($"/api/bookings/{booking.Id}/cancel", null);

        var newTicket = await client.GetAsync($"/api/bookings/{booking.Id}/ticket");
        var check = await (await client.PostAsJsonAsync("/api/tickets/verify", new TicketToVerify(ticket)))
            .Content.ReadFromJsonAsync<TicketCheckDto>();

        Assert.Equal(HttpStatusCode.Conflict, newTicket.StatusCode);
        Assert.Equal(BookingStatus.Cancelled, check!.Status);
    }

    [Fact]
    public async Task Tickets_signed_before_a_key_rotation_verify_until_that_key_version_is_retired()
    {
        var keys = api.Services.GetRequiredService<LocalTicketKeys>();
        var (client, booking) = await BookedAsync();
        var before = await TicketAsync(client, booking.Id);
        var oldKid = KidOf(before);

        keys.Rotate();
        var after = await TicketAsync(client, booking.Id);

        Assert.NotEqual(oldKid, KidOf(after));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/tickets/verify", new TicketToVerify(before))).StatusCode);

        keys.Disable(oldKid);

        await AssertRejectedAsync(client, before, "UnknownKey");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/tickets/verify", new TicketToVerify(after))).StatusCode);
    }

    [Fact]
    public async Task No_ticket_for_an_unknown_booking_and_no_pass_for_garbage()
    {
        var client = api.CreateClient();

        var missing = await client.GetAsync($"/api/bookings/{Guid.NewGuid()}/ticket");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await AssertRejectedAsync(client, "not.a.ticket", "Malformed");
        await AssertRejectedAsync(client, "", "Malformed");
        // Megabytes of dots are refused by length, before anything is split.
        await AssertRejectedAsync(client, new string('.', 4_000_000), "Malformed");
    }

    private async Task<(HttpClient Client, BookingDto Booking)> BookedAsync(int players = 2)
    {
        var client = api.CreateClient();
        var teeTimeId = await api.NewTeeTimeAsync();
        var response = await BookAsync(client, teeTimeId, "Ticket Holder", players, Guid.NewGuid().ToString());
        return (client, (await response.Content.ReadFromJsonAsync<BookingDto>())!);
    }

    private static async Task<string> TicketAsync(HttpClient client, Guid bookingId)
    {
        var response = await client.GetAsync($"/api/bookings/{bookingId}/ticket");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDto>())!.Token;
    }

    private static string KidOf(string ticket) =>
        (string)JsonNode.Parse(Base64Url.DecodeFromChars(ticket.Split('.')[0]))!["kid"]!;

    private static async Task AssertRejectedAsync(HttpClient client, string ticket, string reason)
    {
        var response = await client.PostAsJsonAsync("/api/tickets/verify", new TicketToVerify(ticket));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(reason, problem!.Extensions["reason"]?.ToString());
    }
}
