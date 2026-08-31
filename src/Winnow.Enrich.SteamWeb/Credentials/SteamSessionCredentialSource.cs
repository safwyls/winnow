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

    public async ValueTask<SteamCredential?> TryGetAsync(CancellationToken ct = default)
    {
        if (await _sessions.GetAsync(ct) is not { } session)
        {
            return null;
        }

        return SteamCredential.TryCreateSessionToken(
            session.AccessToken, Name, session.ExpiresAt, session.SteamId);
    }
}
