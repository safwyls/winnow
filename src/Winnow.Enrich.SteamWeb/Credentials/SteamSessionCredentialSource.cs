namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Adapts the session provider onto the credential seam S1 left open. This one
/// class is the whole of what makes a WebView sign-in a peer of the API key
/// rather than a second Steam integration: everything downstream sees a
/// <see cref="SteamCredential"/> and cannot tell which method produced it.
///
/// <para>An expired session is still handed over. The
/// <see cref="SteamCredentialSelector"/> already refuses to choose one, and the
/// <see cref="SteamCredentialInventory"/> needs to be able to say "a session is
/// registered but is not usable". Returning null would collapse that into
/// "no session" and lose the sentence the user needs.</para>
/// </summary>
public sealed class SteamSessionCredentialSource : ISteamSessionCredentialSource
{
    private readonly ISteamSessionProvider _sessions;

    public SteamSessionCredentialSource(ISteamSessionProvider sessions) => _sessions = sessions;

    /// <summary>Names the method, not the storage: this is what the Stores screen calls the peer connection.</summary>
    public string Name => "steam:webview-sign-in";

    public ValueTask<SteamCredential?> TryGetAsync(CancellationToken ct = default)
        => TryGetAsync(SteamSessionRenewalMode.None, ct);

    public async ValueTask<SteamCredential?> TryGetAsync(
        SteamSessionRenewalMode mode, CancellationToken ct = default)
    {
        if (await _sessions.GetAsync(ct) is not { } session)
        {
            return null;
        }

        // The renewal is asked for only where the caller said it would wait, and
        // only when it is actually owed. IsRenewalDue takes no lock, so the
        // overwhelmingly common case — a live token nowhere near its lead window
        // — costs one comparison and never touches the renewal path at all.
        if (mode is SteamSessionRenewalMode.WhenDue && _sessions.IsRenewalDue(session))
        {
            // A failed renewal falls back to the session it had. That is not
            // stubbornness: "renewal is due" includes a token that is still alive
            // inside its lead window, and refusing to send a working token
            // because its replacement did not arrive would throw away the hour
            // the lead window exists to provide.
            session = await _sessions.RenewAsync(session, ct) ?? session;
        }

        return Credential(session);
    }

    public async ValueTask<SteamCredential?> RenewAfterUnauthorizedAsync(
        string rejectedToken, CancellationToken ct = default)
    {
        if (await _sessions.GetAsync(ct) is not { } session)
        {
            return null;
        }

        // Somebody else already replaced the token that got the 401 — another
        // call site's renewal, or a fresh sign-in. Retry with what they produced
        // rather than spending the refresh token to reach the same place.
        if (!string.Equals(session.AccessToken, rejectedToken, StringComparison.Ordinal))
        {
            return Credential(session);
        }

        var renewed = await _sessions.RenewAsync(session, ct);

        // Null, or the same token back, means the renewal did not produce
        // anything new: a rejection, a transient failure, or a latched session.
        // Either way there is nothing to retry with and the caller gives up for
        // this pass.
        return renewed is null
            || string.Equals(renewed.AccessToken, rejectedToken, StringComparison.Ordinal)
                ? null
                : Credential(renewed);
    }

    private SteamCredential? Credential(SteamSession session)
        => SteamCredential.TryCreateSessionToken(
            session.AccessToken, Name, session.ExpiresAt, session.SteamId);
}
