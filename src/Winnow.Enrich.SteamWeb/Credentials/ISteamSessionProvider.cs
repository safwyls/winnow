namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Owns the Steam WebView session in memory: one read of the store, expiry
/// arithmetic, and the health the Stores screen renders.
///
/// <para>Renewal (S6) lives behind this interface rather than beside it.
/// <see cref="GetAsync"/> still never renews and never blocks on one: it is
/// what the Stores screen and the credential inventory read, and both must
/// answer instantly and must be able to see a dead session in order to say
/// so. Renewal is asked for explicitly, by <see cref="RenewAsync"/>, and
/// only where a caller has decided it is willing to wait.</para>
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

    /// <summary>Forgets the whole session, in memory and on disk. The hard-lapse path also discards the refresh token, but keeps the record; sign-out discards everything.</summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether this session is worth renewing right now: it has a refresh
    /// token, that token has not lapsed, this process has not already latched
    /// the session off, a renewer is registered, and the access token is
    /// inside its renewal lead or already gone. Cheap, pure of IO, and takes
    /// no lock, so a caller can ask before deciding whether to wait on
    /// <see cref="RenewAsync"/>.
    /// </summary>
    bool IsRenewalDue(SteamSession session);

    /// <summary>
    /// Spends the refresh token for a fresh access token, single-flight.
    ///
    /// <para><paramref name="staleSession"/> is the session the caller was
    /// holding when it decided a renewal was needed. If somebody else has
    /// already replaced it, that replacement is handed back and no request is
    /// sent. That is not politeness: spending a refresh token can invalidate
    /// the previous one, so a double spend is a self-inflicted sign-out. Same
    /// contract as <c>IEpicTokenProvider.RefreshAsync</c>, for the same
    /// reason.</para>
    ///
    /// <para>Returns the session that is now current, renewed, unchanged, or
    /// lapsed, or null when there is none. Never throws for an expected
    /// failure.</para>
    /// </summary>
    Task<SteamSession?> RenewAsync(SteamSession? staleSession, CancellationToken ct = default);
}
