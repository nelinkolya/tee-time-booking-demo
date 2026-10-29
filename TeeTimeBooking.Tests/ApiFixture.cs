using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TeeTimeBooking.Api.Bookings;
using TeeTimeBooking.Api.Data;

namespace TeeTimeBooking.Tests;

/// <summary>The API in-process over a real SQL Server: concurrency bugs need real locks.</summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private int _nextSlot;

    public string ConnectionString { get; } = TestDatabase.ConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TeeTimes", ConnectionString);
        // The load tests send thousands of requests from one client; RateLimitTests turns limits back on.
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.ConfigureLogging(l => l.AddFakeLogging().SetMinimumLevel(LogLevel.Debug));
    }

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public BookingDb NewDb() => new(new DbContextOptionsBuilder<BookingDb>().UseSqlServer(ConnectionString).Options);

    public async Task<int> NewTeeTimeAsync(int capacity = 4)
    {
        await using var db = NewDb();
        var slot = Interlocked.Increment(ref _nextSlot);
        var teeTime = new TeeTime
        {
            Course = "Test Course",
            StartsAt = new DateTime(2030, 1, 1, 7, 0, 0).AddMinutes(10.0 * slot),
            Capacity = capacity,
        };
        db.TeeTimes.Add(teeTime);
        await db.SaveChangesAsync();
        return teeTime.Id;
    }

    public async Task<TeeTime> LoadTeeTimeAsync(int id)
    {
        await using var db = NewDb();
        return await db.TeeTimes.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    public async Task<int> ConfirmedPlayersAsync(int teeTimeId)
    {
        await using var db = NewDb();
        return await db.Bookings
            .Where(b => b.TeeTimeId == teeTimeId && b.Status == BookingStatus.Confirmed)
            .SumAsync(b => (int?)b.Players) ?? 0;
    }

    public static async Task<HttpResponseMessage> BookAsync(
        HttpClient client, int teeTimeId, string playerName, int players, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tee-times/{teeTimeId}/bookings")
        {
            Content = JsonContent.Create(new BookingRequest(playerName, players)),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add(BookingEndpoints.IdempotencyKeyHeader, idempotencyKey);
        }
        return await client.SendAsync(request);
    }

    /// <summary>Releases all calls from one gate, so they reach the server together.</summary>
    public static async Task<T[]> AllAtOnce<T>(int count, Func<int, Task<T>> call)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await gate.Task;
            return await call(i);
        }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

public static class TestDatabase
{
    /// <summary>The suite drops this database, so it refuses any name not ending in <c>_Test</c>.</summary>
    public static string ConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<ApiFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = config.GetConnectionString("TeeTimesTest")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings:TeeTimesTest: dotnet user-secrets set ConnectionStrings:TeeTimesTest \"...\" --project TeeTimeBooking.Tests");
        EnsureTestDatabase(connectionString);
        return connectionString;
    }

    public static void EnsureTestDatabase(string connectionString)
    {
        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!database.EndsWith("_Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to run: the tests drop their database, and '{database}' is not named *_Test.");
        }
    }
}
