using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TeeTimeBooking.Api.Bookings;
using TeeTimeBooking.Api.Tickets;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

/// <summary>
/// Key Vault out of reach with nothing cached: the ticket endpoints say so (503, Retry-After), rather than failing
/// with a 500 or calling a possibly good ticket invalid.
/// </summary>
[Collection(ApiCollection.Name)]
public class VaultOutageTests(ApiFixture api)
{
    [Fact]
    public async Task Every_ticket_operation_answers_503_with_retry_after()
    {
        var teeTimeId = await api.NewTeeTimeAsync();
        var booking = await (await BookAsync(api.CreateClient(), teeTimeId, "Alex", 2, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<BookingDto>();
        using var vaultDown = api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ITicketKeys, UnreachableVault>()));
        var client = vaultDown.CreateClient();

        HttpResponseMessage[] responses =
        [
            await client.GetAsync($"/api/bookings/{booking!.Id}/ticket"),
            await client.PostAsJsonAsync("/api/tickets/verify", new TicketToVerify("eyJhbGciOiJFUzI1NiIsImtpZCI6ImsxIn0.e30.AAAA")),
            await client.GetAsync("/.well-known/jwks.json"),
        ];

        Assert.All(responses, r =>
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Equal("30", r.Headers.RetryAfter?.ToString());
        });
    }

    private sealed class UnreachableVault : ITicketKeys
    {
        public Task<string> GetCurrentKidAsync(CancellationToken ct) => throw new KeyVaultUnavailableException();

        public Task<byte[]> SignAsync(string kid, byte[] data, CancellationToken ct) => throw new KeyVaultUnavailableException();

        public Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct) =>
            throw new KeyVaultUnavailableException();
    }
}
