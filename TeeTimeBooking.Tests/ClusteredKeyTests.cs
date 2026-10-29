using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeeTimeBooking.Api.Bookings;
using static TeeTimeBooking.Tests.ApiFixture;

namespace TeeTimeBooking.Tests;

[Collection(ApiCollection.Name)]
public class ClusteredKeyTests(ApiFixture api)
{
    // Checked with SQL Server's ORDER BY: .NET sorts GUIDs differently.
    [Fact]
    public async Task Booking_ids_ascend_in_SQL_Server_order_so_inserts_append_to_the_clustered_index()
    {
        var client = api.CreateClient();
        var created = new List<Guid>();
        for (var slot = 0; slot < 5; slot++)
        {
            var teeTimeId = await api.NewTeeTimeAsync(capacity: 4);
            for (var player = 0; player < 4; player++)
            {
                var response = await BookAsync(client, teeTimeId, $"Player {player}", 1, Guid.NewGuid().ToString());
                created.Add((await response.Content.ReadFromJsonAsync<BookingDto>())!.Id);
            }
        }

        await using var db = api.NewDb();
        var inDatabaseOrder = await db.Bookings.Where(b => created.Contains(b.Id)).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();

        Assert.Equal(created, inDatabaseOrder);
    }
}
