using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using TeeTimeBooking.Api.Tickets;

namespace TeeTimeBooking.Tests;

/// <summary>
/// Verification with Key Vault's caching in front of the keys: a version created after the cache was filled must
/// verify at once, and kids that exist nowhere must not reach the vault more than once per refresh interval.
/// </summary>
public class TicketRotationTests
{
    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public async Task A_ticket_from_a_key_version_newer_than_the_cache_verifies_at_once()
    {
        var vault = new VaultLikeKeys(_clock);
        var tickets = new TicketService(db: null!, vault, _clock); // verification never touches the database
        await vault.GetPublicKeysAsync(refresh: false, default); // cache filled before the rotation
        // Within 30 s of a read no refresh is allowed, so a version that appeared in that window waits for the next
        // one. The app's own signer never hits it: it adds a new version to the keys before signing with it.
        _clock.Advance(TimeSpan.FromMinutes(1));

        var newKid = vault.RotateInVault();
        var (outcome, claims) = await tickets.VerifyAsync(vault.Ticket(newKid), default);

        Assert.Equal(TicketCheck.Valid, outcome);
        Assert.NotNull(claims);
    }

    [Fact]
    public async Task Forged_kids_are_rejected_with_at_most_one_vault_read_per_interval()
    {
        var vault = new VaultLikeKeys(_clock);
        var tickets = new TicketService(db: null!, vault, _clock);
        await vault.GetPublicKeysAsync(refresh: false, default);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var readsBefore = vault.Reads;

        var outcomes = new List<TicketCheck>();
        for (var i = 0; i < 50; i++)
        {
            outcomes.Add((await tickets.VerifyAsync(vault.Ticket($"forged-{i}"), default)).Outcome);
        }

        Assert.All(outcomes, o => Assert.Equal(TicketCheck.UnknownKey, o));
        Assert.Equal(1, vault.Reads - readsBefore);
    }

    /// <summary>Keys "in the vault" and a cache in front of them, as KeyVaultTicketKeys has.</summary>
    private sealed class VaultLikeKeys : ITicketKeys
    {
        private readonly Dictionary<string, ECDsa> _inVault = new();
        private readonly CachedRead<IReadOnlyDictionary<string, ECParameters>> _cache;
        private int _reads;

        public VaultLikeKeys(TimeProvider clock)
        {
            RotateInVault();
            _cache = new(_ =>
            {
                Interlocked.Increment(ref _reads);
                return Task.FromResult<IReadOnlyDictionary<string, ECParameters>>(
                    _inVault.ToDictionary(k => k.Key, k => k.Value.ExportParameters(false)));
            }, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), clock);
        }

        public int Reads => _reads;

        public string RotateInVault()
        {
            var kid = Guid.NewGuid().ToString("N");
            _inVault[kid] = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return kid;
        }

        /// <summary>A ticket signed with that version, or with any key if the version does not exist.</summary>
        public string Ticket(string kid)
        {
            var header = Encode(new { alg = "ES256", typ = "JWT", kid });
            var payload = Encode(new TicketClaims(Guid.NewGuid(), 1, 2, new DateTime(2030, 1, 1, 8, 0, 0), 0));
            var key = _inVault.GetValueOrDefault(kid) ?? ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256);
            return $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
        }

        public Task<IReadOnlyDictionary<string, ECParameters>> GetPublicKeysAsync(bool refresh, CancellationToken ct) =>
            _cache.GetAsync(refresh, ct);

        public Task<string> GetCurrentKidAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<byte[]> SignAsync(string kid, byte[] data, CancellationToken ct) => throw new NotSupportedException();

        private static string Encode<T>(T value) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));
    }
}
