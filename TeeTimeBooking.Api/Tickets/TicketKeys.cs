using System.Collections.Concurrent;
using System.Security.Cryptography;
using Azure.Core;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace TeeTimeBooking.Api.Tickets;

/// <summary>
/// The EC P-256 key tickets are signed with. Signing happens where the private key lives; verification needs only
/// the public keys, one per key version (<c>kid</c>), so tickets signed before a rotation still verify.
/// </summary>
public interface ITicketKeys
{
    /// <summary>The version new tickets are signed with.</summary>
    Task<string> GetCurrentKidAsync(CancellationToken ct);

    /// <summary>An ES256 signature (r‖s, 64 bytes, as JWS wants it) with that version of the key.</summary>
    Task<byte[]> SignAsync(string kid, byte[] data, CancellationToken ct);

    /// <summary>
    /// The public key of every version still allowed to verify, by <c>kid</c>. They may be cached;
    /// <paramref name="refresh"/> asks for a fresh read, for a <c>kid</c> that appeared after the cache was filled
    /// (rate-limited: see <see cref="PublicKeyCache"/>).
    /// </summary>
    Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct);
}

/// <summary>
/// Azure Key Vault: the private key never leaves the vault; the app may only sign with it and read the public parts
/// (role "Key Vault Crypto User" on this key). Disabling a version in the vault retires the tickets it signed.
/// </summary>
public sealed class KeyVaultTicketKeys : ITicketKeys
{
    // After a rotation, signing goes on with the previous version for up to this long; it still verifies.
    private static readonly TimeSpan CurrentKidTtl = TimeSpan.FromMinutes(5);

    private readonly KeyClient _keys;
    private readonly TokenCredential _credential;
    private readonly string _keyName;
    private readonly TimeProvider _clock;
    private readonly PublicKeyCache _publicKeys;
    private readonly ConcurrentDictionary<string, CryptographyClient> _signers = new();
    private (string Kid, DateTimeOffset At)? _current;

    public KeyVaultTicketKeys(KeyClient keys, TokenCredential credential, string keyName, TimeProvider clock)
    {
        _keys = keys;
        _credential = credential;
        _keyName = keyName;
        _clock = clock;
        _publicKeys = new PublicKeyCache(LoadPublicKeysAsync, clock);
    }

    public async Task<string> GetCurrentKidAsync(CancellationToken ct)
    {
        if (_current is { } cached && _clock.GetUtcNow() - cached.At < CurrentKidTtl)
        {
            return cached.Kid;
        }
        // Reads the key's public part and properties only; the private key cannot be read at all.
        KeyVaultKey key = await _keys.GetKeyAsync(_keyName, cancellationToken: ct);
        var kid = key.Properties.Version;

        // A new version (a rotation) goes into the published keys before it signs anything, so a ticket never
        // names a kid that /.well-known/jwks.json does not list yet.
        if (!(await _publicKeys.GetAsync(refresh: false, ct)).ContainsKey(kid))
        {
            _publicKeys.Invalidate();
            await _publicKeys.GetAsync(refresh: false, ct);
        }

        _current = (kid, _clock.GetUtcNow());
        return kid;
    }

    public async Task<byte[]> SignAsync(string kid, byte[] data, CancellationToken ct)
    {
        // SignDataAsync hashes locally (SHA-256): only the digest goes to the vault.
        var signer = _signers.GetOrAdd(kid, v => new CryptographyClient(new Uri(_keys.VaultUri, $"keys/{_keyName}/{v}"), _credential));
        var result = await signer.SignDataAsync(SignatureAlgorithm.ES256, data, ct);
        return result.Signature;
    }

    public Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct) =>
        _publicKeys.GetAsync(refresh, ct);

    private async Task<IReadOnlyDictionary<string, ECParameters>> LoadPublicKeysAsync(CancellationToken ct)
    {
        var found = new Dictionary<string, ECParameters>();
        await foreach (var version in _keys.GetPropertiesOfKeyVersionsAsync(_keyName, ct))
        {
            if (version.Enabled != true)
            {
                continue;
            }
            KeyVaultKey key = await _keys.GetKeyAsync(_keyName, version.Version, ct);
            using var ec = key.Key.ToECDsa();
            found[version.Version] = ec.ExportParameters(includePrivateParameters: false);
        }
        return found;
    }
}

/// <summary>
/// A key in memory, for development and tests: lost on restart, and its tickets with it. Azure signs in Key Vault.
/// </summary>
public sealed class LocalTicketKeys : ITicketKeys
{
    private readonly ConcurrentDictionary<string, ECDsa> _versions = new();
    private string _current = "";

    public LocalTicketKeys() => Rotate();

    /// <summary>A new current version; the old ones still verify, as after a rotation in Key Vault.</summary>
    public string Rotate()
    {
        var kid = Guid.NewGuid().ToString("N");
        _versions[kid] = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _current = kid;
        return kid;
    }

    /// <summary>Like disabling a version in Key Vault: what it signed no longer verifies.</summary>
    public void Disable(string kid) => _versions.TryRemove(kid, out _);

    public Task<string> GetCurrentKidAsync(CancellationToken ct) => Task.FromResult(_current);

    // IEEE P1363 (r‖s) is .NET's default format, and what Key Vault returns and JWS expects.
    public Task<byte[]> SignAsync(string kid, byte[] data, CancellationToken ct) =>
        Task.FromResult(_versions[kid].SignData(data, HashAlgorithmName.SHA256));

    public Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, ECParameters>>(
            _versions.ToDictionary(v => v.Key, v => v.Value.ExportParameters(includePrivateParameters: false)));
}
