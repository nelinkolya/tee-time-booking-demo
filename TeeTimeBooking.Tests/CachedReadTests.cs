using Microsoft.Extensions.Time.Testing;
using TeeTimeBooking.Api.Tickets;

namespace TeeTimeBooking.Tests;

/// <summary>
/// The cache in front of every Key Vault read the ticket endpoints cause (the current key version, the public keys).
/// Those endpoints are open to anyone: these pin that request bursts and forged kids cannot become vault traffic.
/// </summary>
public class CachedReadTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _clock = new();
    private int _reads;

    private CachedRead<string> NewCache(TimeSpan? readTakes = null) => new(async ct =>
    {
        var n = Interlocked.Increment(ref _reads);
        if (readTakes is { } delay)
        {
            await Task.Delay(delay, ct);
        }
        return $"read {n}";
    }, Ttl, MinRefreshInterval, _clock);

    [Fact]
    public async Task Fifty_refreshes_for_unknown_kids_read_the_vault_once_per_interval()
    {
        var cache = NewCache();
        await cache.GetAsync(refresh: false, default);

        for (var i = 0; i < 50; i++)
        {
            await cache.GetAsync(refresh: true, default);
        }
        _clock.Advance(MinRefreshInterval);
        await cache.GetAsync(refresh: true, default);

        Assert.Equal(2, _reads); // the first fill, then one refresh once the interval has passed
    }

    [Fact]
    public async Task A_burst_after_the_value_expires_is_one_read_shared_by_all()
    {
        var cache = NewCache(readTakes: TimeSpan.FromMilliseconds(50));
        await cache.GetAsync(refresh: false, default);
        _clock.Advance(Ttl);

        var values = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => cache.GetAsync(refresh: false, default)));

        Assert.Equal(2, _reads);
        Assert.All(values, v => Assert.Equal("read 2", v));
    }

    [Fact]
    public async Task Parallel_refreshes_share_one_read()
    {
        var cache = NewCache(readTakes: TimeSpan.FromMilliseconds(50));

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => cache.GetAsync(refresh: true, default)));

        Assert.Equal(1, _reads);
    }

    [Fact]
    public async Task The_value_is_read_again_after_the_ttl_or_when_invalidated()
    {
        var cache = NewCache();
        await cache.GetAsync(refresh: false, default);

        _clock.Advance(Ttl - TimeSpan.FromSeconds(1));
        await cache.GetAsync(refresh: false, default);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await cache.GetAsync(refresh: false, default);
        cache.Invalidate();
        await cache.GetAsync(refresh: false, default);

        Assert.Equal(3, _reads);
    }
}
