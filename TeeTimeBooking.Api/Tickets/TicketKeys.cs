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
    /// (rate-limited: see <see cref="CachedRead{T}"/>).
    /// </summary>
    Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct);
}

/// <summary>
/// Azure Key Vault: the private key never leaves the vault; the app may only sign with it and read the public parts
/// (a custom role with keys/read and keys/sign/action on this key, infra/main.bicep). Disabling a version in the vault
/// retires the tickets it signed.
/// </summary>
public sealed class KeyVaultTicketKeys : ITicketKeys
{
    private readonly KeyClient _keys;
    private readonly TokenCredential _credential;
    private readonly string _keyName;
    private readonly ConcurrentDictionary<string, CryptographyClient> _signers = new();

    // After a rotation, signing goes on with the previous version for up to 5 minutes; it still verifies.
    private readonly CachedRead<string> _currentKid;
    // The public keys change only on rotation; a ticket naming an unknown kid may refresh them, once per 30 s.
    private readonly CachedRead<IReadOnlyDictionary<string, ECParameters>> _publicKeys;

    public KeyVaultTicketKeys(KeyClient keys, TokenCredential credential, string keyName, TimeProvider clock)
    {
        _keys = keys;
        _credential = credential;
        _keyName = keyName;
        _currentKid = new CachedRead<string>(ReadCurrentKidAsync, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), clock);
        _publicKeys = new CachedRead<IReadOnlyDictionary<string, ECParameters>>(
            ReadPublicKeysAsync, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), clock);
    }

    public Task<string> GetCurrentKidAsync(CancellationToken ct) => _currentKid.GetAsync(refresh: false, ct);

    public async Task<byte[]> SignAsync(string kid, byte[] data, CancellationToken ct)
    {
        // SignDataAsync hashes locally (SHA-256): only the digest goes to the vault.
        var signer = _signers.GetOrAdd(kid, v => new CryptographyClient(new Uri(_keys.VaultUri, $"keys/{_keyName}/{v}"), _credential));
        var result = await signer.SignDataAsync(SignatureAlgorithm.ES256, data, ct);
        return result.Signature;
    }

    public Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct) =>
        _publicKeys.GetAsync(refresh, ct);

    private async Task<string> ReadCurrentKidAsync(CancellationToken ct)
    {
        // Reads the key's public part and properties only; the private key cannot be read at all.
        KeyVaultKey key = await _keys.GetKeyAsync(_keyName, cancellationToken: ct);
        var kid = key.Properties.Version;

        // A new version (a rotation) goes into the published keys before it signs anything, so a ticket never
        // names a kid that /.well-known/jwks.json does not list yet.
        if (!(await _publicKeys.GetAsync(refresh: false, ct)).ContainsKey(kid))
        {
            await _publicKeys.ReadNowAsync(ct); // a failure keeps the keys there were
        }
        return kid;
    }

    private async Task<IReadOnlyDictionary<string, ECParameters>> ReadPublicKeysAsync(CancellationToken ct)
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
    private int _signatures;

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

    /// <summary>How many signatures were made: what Key Vault would bill.</summary>
    public int Signatures => _signatures;

    public Task<string> GetCurrentKidAsync(CancellationToken ct) => Task.FromResult(_current);

    // IEEE P1363 (r‖s) is .NET's default format, and what Key Vault returns and JWS expects.
    public Task<byte[]> SignAsync(string kid, byte[] data, CancellationToken ct)
    {
        Interlocked.Increment(ref _signatures);
        return Task.FromResult(_versions[kid].SignData(data, HashAlgorithmName.SHA256));
    }

    public Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, ECParameters>>(
            _versions.ToDictionary(v => v.Key, v => v.Value.ExportParameters(includePrivateParameters: false)));
}
