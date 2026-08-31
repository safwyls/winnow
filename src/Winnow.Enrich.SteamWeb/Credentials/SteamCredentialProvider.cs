namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Composes the existing key chain with an optional session source through the
/// pure <see cref="SteamCredentialSelector"/>. Holds no cache of its own:
/// <see cref="ChainedSteamApiKeyProvider"/> already memoises because key lookup
/// happens on every enrichment call and the settings table would otherwise be
/// read each time. A second cache here would be a second thing to invalidate
/// and a second way to serve a stale answer, so
/// <see cref="Invalidate"/> simply delegates. The clock is injected as
/// <see cref="TimeProvider"/> so expiry is testable.
/// </summary>
public sealed class SteamCredentialProvider : ISteamCredentialProvider
{
    private readonly ISteamApiKeyProvider _keys;
    private readonly ISteamSessionCredentialSource? _session;
    private readonly TimeProvider _clock;

    public SteamCredentialProvider(
        ISteamApiKeyProvider keys,
        TimeProvider clock,
        ISteamSessionCredentialSource? session = null)
    {
        _keys = keys;
        _clock = clock;
        _session = session;
    }

    public async ValueTask<SteamCredential?> GetAsync(
        SteamCredentialPurpose purpose, CancellationToken ct = default)
    {
        var key = SteamCredential.FromApiKey(await _keys.GetAsync(ct));
        var session = _session is null ? null : await _session.TryGetAsync(ct);

        return SteamCredentialSelector.Choose(purpose, key, session, _clock.GetUtcNow());
    }

    public async ValueTask<SteamCredentialInventory> GetInventoryAsync(CancellationToken ct = default)
    {
        var key = SteamCredential.FromApiKey(await _keys.GetAsync(ct));
        var session = _session is null ? null : await _session.TryGetAsync(ct);

        return new SteamCredentialInventory(
            HasApiKey: key is not null,
            ApiKeySource: key?.Provenance,
            HasSession: session is not null,
            SessionSource: session?.Provenance,
            SessionExpiresAt: session?.ExpiresAt,
            SessionUsable: session?.IsUsableAt(_clock.GetUtcNow(), SteamCredential.DefaultSkew) ?? false,
            SessionAccount: session?.SteamId);
    }

    public void Invalidate() => _keys.Invalidate();
}
