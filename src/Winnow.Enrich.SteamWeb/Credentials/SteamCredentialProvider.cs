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
        var now = _clock.GetUtcNow();
        var key = SteamCredential.FromApiKey(await _keys.GetAsync(ct));

        // The key drives the unattended 15-minute and 6-hour passes; the
        // session is the fallback for keyless users. When a key is present and
        // this is an unattended pass, the session is not consulted at all: not
        // read, not renewed, not waited on. A renewal that is in flight, slow,
        // or failing therefore cannot delay a scheduler tick, because the tick
        // never reaches the code that would wait for it. This is a structural
        // guarantee rather than a timeout: a timeout would be a promise about
        // how long; this is a promise that it does not happen.
        if (purpose is SteamCredentialPurpose.Unattended
            && key is not null
            && key.IsUsableAt(now, SteamCredential.DefaultSkew))
        {
            return key;
        }

        // Either the user is present and waiting, or there is no key and the
        // session is the only credential this pass will get. Both are cases where
        // waiting out a renewal is the right trade.
        var session = _session is null
            ? null
            : await _session.TryGetAsync(SteamSessionRenewalMode.WhenDue, ct);

        return SteamCredentialSelector.Choose(purpose, key, session, _clock.GetUtcNow());
    }

    public async ValueTask<SteamCredentialInventory> GetInventoryAsync(CancellationToken ct = default)
    {
        var key = SteamCredential.FromApiKey(await _keys.GetAsync(ct));

        // The mode below stops this read starting a renewal. What stops it
        // waiting behind somebody else's is SteamSessionProvider's two-lock
        // split, where the renewal exchange holds its own lock and readers
        // hold a state lock that is never held across the network. Both are
        // needed: a screen that opened a socket to say "a session is
        // registered" and a screen that sat behind three HTTP requests and
        // their backoff to say it are equally the blocking §5.1 forbids.
        var session = _session is null
            ? null
            : await _session.TryGetAsync(SteamSessionRenewalMode.None, ct);

        return new SteamCredentialInventory(
            HasApiKey: key is not null,
            ApiKeySource: key?.Provenance,
            HasSession: session is not null,
            SessionSource: session?.Provenance,
            SessionExpiresAt: session?.ExpiresAt,
            SessionUsable: session?.IsUsableAt(_clock.GetUtcNow(), SteamCredential.DefaultSkew) ?? false,
            SessionAccount: session?.SteamId);
    }

    public ValueTask<SteamCredential?> RenewAfterUnauthorizedAsync(
        SteamCredential rejected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rejected);

        // A rejected API key cannot be replaced by asking again; the same value
        // would come back. Only a session has a renewal path.
        return rejected.Kind is not SteamCredentialKind.SessionToken || _session is null
            ? ValueTask.FromResult<SteamCredential?>(null)
            : _session.RenewAfterUnauthorizedAsync(rejected.Value, ct);
    }

    public void Invalidate() => _keys.Invalidate();
}
