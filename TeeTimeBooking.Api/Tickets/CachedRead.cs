namespace TeeTimeBooking.Api.Tickets;

/// <summary>
/// A value read from Key Vault and kept for a while. Requests drive the reads, and the ticket endpoints are open to
/// anyone, so the vault must not see one read per request:
/// <list type="bullet">
/// <item>one read at a time, and whoever waited on it takes its result (a burst after the value expires is one read);</item>
/// <item>at most one attempt per <c>minRefreshInterval</c>, whether it succeeded, failed or was asked for by a ticket
/// naming an unknown key version: forged kids, aborted requests and a vault outage all cost one read per interval;</item>
/// <item>a failed read keeps the value it had. With nothing to keep, it fails with <see cref="KeyVaultUnavailableException"/>.</item>
/// </list>
/// </summary>
public sealed class CachedRead<T>(
    Func<CancellationToken, Task<T>> read, TimeSpan ttl, TimeSpan minRefreshInterval, TimeProvider clock)
    where T : class
{
    // One immutable object, swapped whole: readers never see a value with another read's timestamp.
    // Attempts counts reads tried, so a forced read can tell whether one happened while it waited for the gate.
    private sealed record State(T? Value, DateTimeOffset ReadAt, DateTimeOffset LastAttempt, long Attempts);

    private enum Mode { Cached, Refresh, Force }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile State _state = new(null, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0);

    /// <summary>
    /// The value; with <paramref name="refresh"/>, one read no older than the refresh interval (for a ticket naming a
    /// key version not seen yet).
    /// </summary>
    public Task<T> GetAsync(bool refresh, CancellationToken ct) => GetAsync(refresh ? Mode.Refresh : Mode.Cached, ct);

    /// <summary>
    /// Read now, whatever the interval: for the app's own use when it knows the value changed (a rotation), never per
    /// request. A failure keeps the value it had.
    /// </summary>
    public Task<T> ReadNowAsync(CancellationToken ct) => GetAsync(Mode.Force, ct);

    private async Task<T> GetAsync(Mode mode, CancellationToken ct)
    {
        var attemptsBefore = _state.Attempts;
        if (mode != Mode.Force && Current(mode) is { } value)
        {
            return value;
        }

        await _gate.WaitAsync(ct);
        try
        {
            var state = _state;
            // Whoever queued behind a read takes its result instead of reading again.
            if (mode == Mode.Force ? state.Attempts != attemptsBefore : Current(mode) is not null)
            {
                return state.Value ?? throw new KeyVaultUnavailableException();
            }

            var now = clock.GetUtcNow();
            if (mode != Mode.Force && now - state.LastAttempt < minRefreshInterval)
            {
                // A read was tried moments ago and gave nothing newer: no second one yet.
                return state.Value ?? throw new KeyVaultUnavailableException();
            }

            _state = state with { LastAttempt = now, Attempts = state.Attempts + 1 };
            try
            {
                // Not the caller's token: an aborted request must not abort the read everyone else is waiting for.
                var fresh = await read(CancellationToken.None);
                _state = new State(fresh, clock.GetUtcNow(), now, state.Attempts + 1);
                return fresh;
            }
            // Whatever the vault or the network threw (RequestFailedException, an authentication failure, a timeout):
            // fall back to the value there was, or report the vault unavailable. Fatal runtime errors pass through.
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                return state.Value ?? throw new KeyVaultUnavailableException(ex); // stale keys still verify issued tickets
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private T? Current(Mode mode)
    {
        var state = _state;
        if (state.Value is null)
        {
            return null;
        }
        var now = clock.GetUtcNow();
        var fresh = mode == Mode.Refresh ? now - state.LastAttempt < minRefreshInterval : now - state.ReadAt < ttl;
        return fresh ? state.Value : null;
    }
}

/// <summary>Key Vault could not be read just now, and there is nothing cached to fall back on: a 503.</summary>
public sealed class KeyVaultUnavailableException(Exception? inner = null)
    : Exception("Key Vault could not be read just now; the next attempt is allowed shortly.", inner);
