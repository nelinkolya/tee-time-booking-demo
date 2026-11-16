namespace TeeTimeBooking.Api.Tickets;

/// <summary>
/// A value read from Key Vault and kept for a while. Requests drive the reads, and the ticket endpoints are open to
/// anyone, so the vault must not see one read per request:
/// <list type="bullet">
/// <item>one read at a time, and whoever waited on it takes its result (a burst after the value expires is one read);</item>
/// <item>at most one attempt per <c>minRefreshInterval</c>, whether it succeeded, failed or was asked for by a ticket
/// naming an unknown key version: forged kids, aborted requests and a vault outage all cost one read per interval;</item>
/// <item>a failed read keeps the value it had, if any.</item>
/// </list>
/// </summary>
public sealed class CachedRead<T>(
    Func<CancellationToken, Task<T>> read, TimeSpan ttl, TimeSpan minRefreshInterval, TimeProvider clock)
    where T : class
{
    // One immutable object, swapped whole: readers never see a value with another read's timestamp.
    private sealed record State(T? Value, DateTimeOffset ReadAt, DateTimeOffset LastAttempt);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile State _state = new(null, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    public async Task<T> GetAsync(bool refresh, CancellationToken ct)
    {
        if (Current(refresh) is { } value)
        {
            return value;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Whoever queued behind a read takes its result instead of reading again.
            if (Current(refresh) is { } justRead)
            {
                return justRead;
            }

            var state = _state;
            var now = clock.GetUtcNow();
            if (now - state.LastAttempt < minRefreshInterval)
            {
                // A read was tried moments ago and gave nothing newer: no second one yet.
                return state.Value ?? throw new KeyVaultUnavailableException();
            }

            _state = state with { LastAttempt = now };
            try
            {
                // Not the caller's token: an aborted request must not abort the read everyone else is waiting for.
                var fresh = await read(CancellationToken.None);
                _state = new State(fresh, clock.GetUtcNow(), now);
                return fresh;
            }
            catch (Exception) when (state.Value is not null)
            {
                return state.Value; // stale, but signed tickets still verify against it
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forget the value: the next call reads, whatever the interval. For the app's own use, not per request.</summary>
    public void Invalidate() => _state = new State(null, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    private T? Current(bool refresh)
    {
        var state = _state;
        if (state.Value is null)
        {
            return null;
        }
        var now = clock.GetUtcNow();
        var fresh = refresh ? now - state.LastAttempt < minRefreshInterval : now - state.ReadAt < ttl;
        return fresh ? state.Value : null;
    }
}

/// <summary>Key Vault could not be read just now, and there is nothing cached to fall back on.</summary>
public sealed class KeyVaultUnavailableException()
    : Exception("Key Vault could not be read just now; the next attempt is allowed shortly.");
