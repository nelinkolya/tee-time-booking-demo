namespace TeeTimeBooking.Api.Tickets;

/// <summary>
/// A value read from Key Vault and kept for a while. Requests drive the reads, and the ticket endpoints are open to
/// anyone, so the vault must not see one read per request:
/// <list type="bullet">
/// <item>one read at a time, and whoever waited on it takes its result (a burst after the value expires is one read);</item>
/// <item>a caller may ask for a fresher value (a ticket naming an unknown key version), but gets one at most every
/// <c>minRefreshInterval</c>: a stream of forged kids costs one read per interval.</item>
/// </list>
/// </summary>
public sealed class CachedRead<T>(
    Func<CancellationToken, Task<T>> read, TimeSpan ttl, TimeSpan minRefreshInterval, TimeProvider clock)
    where T : class
{
    // One immutable object, swapped whole: readers never see a value with another read's timestamp.
    private sealed record Entry(T Value, DateTimeOffset At);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _entry;

    public async Task<T> GetAsync(bool refresh, CancellationToken ct)
    {
        if (Usable(refresh) is { } value)
        {
            return value;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (Usable(refresh) is { } justRead)
            {
                return justRead;
            }
            var fresh = await read(ct);
            _entry = new Entry(fresh, clock.GetUtcNow());
            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forget the value: the next call reads, whatever the interval. For the app's own use, not per request.</summary>
    public void Invalidate() => _entry = null;

    private T? Usable(bool refresh) =>
        _entry is { } entry && clock.GetUtcNow() - entry.At < (refresh ? minRefreshInterval : ttl) ? entry.Value : null;
}
