namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Owns the Steam WebView session in memory: one read of the store, expiry
/// arithmetic, and the health the Stores screen renders.
///
/// <para><b>It does not renew.</b> Renewal is S6 and lands behind this
/// interface, not beside it. <see cref="GetAsync"/> already returns a session
/// whose access token has died, because the UI has to be able to say so and the
/// <see cref="SteamCredentialSelector"/> already refuses to send one. Until then
/// a signed-in user has roughly a day of access and a one-click re-sign-in, and
/// a keyed user is unaffected.</para>
/// </summary>
public interface ISteamSessionProvider
{
    /// <summary>
    /// The current session, live or dead, or null when none is stored. Dead is
    /// deliberately not null: a caller that cannot distinguish "never signed in"
    /// from "signed in and lapsed" cannot tell the user which one happened,
    /// which is the silent degradation section 4.7's eighth condition forbids.
    /// </summary>
    ValueTask<SteamSession?> GetAsync(CancellationToken ct = default);

    /// <summary>What state the session is in, as one value the UI can switch on.</summary>
    ValueTask<SteamSessionHealth> GetHealthAsync(CancellationToken ct = default);

    /// <summary>Adopts a freshly minted session and persists it. S3's sign-in is the only caller.</summary>
    Task SaveAsync(SteamSession session, CancellationToken ct = default);

    /// <summary>Forgets the session, in memory and on disk. The only path that discards a refresh token.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}
