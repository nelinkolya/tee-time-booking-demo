using System.Security.Cryptography;

namespace TeeTimeBooking.Api.Tickets;

/// <summary>
/// The ticket keys' public halves, read from Key Vault and kept for <see cref="Ttl"/>. A token with an unknown
/// <c>kid</c> may ask for a fresh read, but <c>POST /api/tickets/verify</c> is open to anyone, so such reads are
/// limited to one per <see cref="MinRefreshInterval"/>, and callers waiting on a read share its result: a stream of
/// forged kids costs the vault at most one read every 30 seconds.
/// </summary>
public sealed class PublicKeyCache(
    Func<CancellationToken, Task<IReadOnlyDictionary<string, ECParameters>>> load, TimeProvider clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (IReadOnlyDictionary<string, ECParameters> Keys, DateTimeOffset At)? _cached;

    public async Task<IReadOnlyDictionary<string, ECParameters>> GetAsync(bool refresh, CancellationToken ct)
    {
        if (Usable(refresh) is { } keys)
        {
            return keys;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Whoever queued behind a read takes its result instead of reading again.
            if (Usable(refresh) is { } justRead)
            {
                return justRead;
            }
            var loaded = await load(ct);
            _cached = (loaded, clock.GetUtcNow());
            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forget the keys: the next read goes to the vault whatever its age. Not for request-driven use.</summary>
    public void Invalidate() => _cached = null;

    private IReadOnlyDictionary<string, ECParameters>? Usable(bool refresh) =>
        _cached is { } cached && clock.GetUtcNow() - cached.At < (refresh ? MinRefreshInterval : Ttl) ? cached.Keys : null;
}
