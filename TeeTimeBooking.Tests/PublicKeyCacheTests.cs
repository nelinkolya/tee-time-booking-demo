using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using TeeTimeBooking.Api.Tickets;

namespace TeeTimeBooking.Tests;

/// <summary>
/// The cache in front of Key Vault's public keys. <c>POST /api/tickets/verify</c> is open to anyone, and a token
/// naming an unknown kid asks for a fresh read: these pin that forged kids cannot turn into vault traffic.
/// </summary>
public class PublicKeyCacheTests
{
    private readonly FakeTimeProvider _clock = new();
    private int _reads;

    private PublicKeyCache NewCache(TimeSpan? readTakes = null) => new(async ct =>
    {
        Interlocked.Increment(ref _reads);
        if (readTakes is { } delay)
        {
            await Task.Delay(delay, ct);
        }
        return new Dictionary<string, ECParameters>();
    }, _clock);

    [Fact]
    public async Task Fifty_refreshes_for_unknown_kids_read_the_vault_once_per_interval()
    {
        var cache = NewCache();
        await cache.GetAsync(refresh: false, default);

        for (var i = 0; i < 50; i++)
        {
            await cache.GetAsync(refresh: true, default);
        }
        _clock.Advance(PublicKeyCache.MinRefreshInterval);
        await cache.GetAsync(refresh: true, default);

        Assert.Equal(2, _reads); // the first fill, then one refresh once the interval has passed
    }

    [Fact]
    public async Task Parallel_refreshes_share_one_read()
    {
        var cache = NewCache(readTakes: TimeSpan.FromMilliseconds(50));

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => cache.GetAsync(refresh: true, default)));

        Assert.Equal(1, _reads);
    }

    [Fact]
    public async Task Keys_are_read_again_after_the_ttl_or_when_invalidated()
    {
        var cache = NewCache();
        await cache.GetAsync(refresh: false, default);

        _clock.Advance(PublicKeyCache.Ttl - TimeSpan.FromSeconds(1));
        await cache.GetAsync(refresh: false, default);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await cache.GetAsync(refresh: false, default);
        cache.Invalidate();
        await cache.GetAsync(refresh: false, default);

        Assert.Equal(3, _reads);
    }
}
