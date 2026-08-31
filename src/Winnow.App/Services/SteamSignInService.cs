using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.App.Services;

/// <summary>
/// What a completed sign-in attempt leaves behind, with nothing secret in it.
///
/// <para>The type S5's Stores screen binds to. It carries no token and no
/// refresh token by construction rather than by redaction: a view model cannot
/// leak a credential it was never handed. What it does carry is every fact the
/// screen has to be honest about — whether the session can be renewed, whether
/// it survived to disk, and what state it is in.</para>
/// </summary>
/// <param name="Outcome">How the browser session ended.</param>
/// <param name="Detail">A safe one-line reason, fit to show a user.</param>
/// <param name="SteamId">The account that signed in, or null when none did.</param>
/// <param name="ExpiresAt">When the access token dies, read from the token itself.</param>
/// <param name="RefreshTokenCaptured">
/// Whether a refresh token was captured. False means a working session that
/// cannot be renewed: it lasts about a day and unattended syncs will stop when
/// it does, which is the sentence the screen has to say out loud.
/// </param>
/// <param name="Persisted">
/// Whether the session reached the encrypted store. False on a host that cannot
/// encrypt, where the session still works for this run and has to be repeated
/// after a restart.
/// </param>
/// <param name="Health">The state the Stores screen renders.</param>
/// <param name="AccountConfirmed">
/// Whether this sign-in recorded which Steam account is the user's. TASK-55's
/// acceptance criterion 4: the visibility toggle is live the moment the window
/// closes, with no import, no Year in Review call and no waiting.
/// </param>
/// <param name="Pages">
/// The account pages, when the user agreed to that capture in the same session.
/// Null is the ordinary case and is not a failure.
/// </param>
public sealed record SteamSignInReport(
    SteamSignInOutcome Outcome,
    string? Detail,
    string? SteamId,
    DateTimeOffset? ExpiresAt,
    bool RefreshTokenCaptured,
    bool Persisted,
    SteamSessionHealth Health,
    SteamAccountPages? Pages,
    bool AccountConfirmed = false)
{
    /// <summary>Whether a credential came out of this and is now the provider's.</summary>
    public bool SignedIn => Outcome == SteamSignInOutcome.SignedIn;
}

/// <summary>
/// Turns a browser sign-in into a stored Steam session.
///
/// <para><b>The join between S3's mechanism and S2's storage, and it lives here
/// because nowhere else can see both.</b> Winnow.Auth.WebView references Core
/// alone — deliberately, so the browser project stays free of the data layer —
/// and Winnow.Enrich.SteamWeb cannot see a browser. The composition root is the
/// one place that knows about both, so the handover happens here: the session
/// mints, this writes.</para>
///
/// <para>It is the only caller of <see cref="ISteamSessionProvider.SaveAsync"/>,
/// and the write is the moment the S1 credential selector starts seeing a real
/// session instead of nothing.</para>
///
/// <para><b>It also records the account.</b> The token's <c>sub</c> claim names
/// the user's own Steam account, which is the fact the account visibility filter
/// has always needed and which the key path can only learn by spending a Year in
/// Review disclosure call. Writing it here, through the same
/// <see cref="ISteamAccountConfirmation"/> the key path writes through, is
/// TASK-55's acceptance criterion 4 and the reason there is one writer rather
/// than two.</para>
///
/// <para><b>What it will not do.</b> It never logs either token; it never
/// invents a session out of a sign-in that did not produce one; and it never
/// treats a refused sign-in — an identity mismatch above all — as something to
/// retry or to partially keep.</para>
/// </summary>
public sealed class SteamSignInService
{
    private readonly ISteamSignInSession _session;
    private readonly ISteamSessionProvider _sessions;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamSignInService> _log;
    private readonly ISteamAccountConfirmation? _confirmation;

    public SteamSignInService(
        ISteamSignInSession session,
        ISteamSessionProvider sessions,
        TimeProvider clock,
        ILogger<SteamSignInService>? log = null,
        // Optional so a host that composed a sign-in without the settings table
        // still signs in. Absent means the session works and the visibility
        // toggle waits, which is the pre-S4 behaviour rather than a failure.
        ISteamAccountConfirmation? confirmation = null)
    {
        _session = session;
        _sessions = sessions;
        _clock = clock;
        _log = log ?? NullLogger<SteamSignInService>.Instance;
        _confirmation = confirmation;
    }

    /// <summary>Whether an embedded sign-in can run on this machine right now.</summary>
    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
        => _session.IsAvailableAsync(ct);

    /// <summary>The current state of the stored session, for a screen that has not signed in yet.</summary>
    public ValueTask<SteamSessionHealth> GetHealthAsync(CancellationToken ct = default)
        => _sessions.GetHealthAsync(ct);

    /// <summary>
    /// Runs one sign-in and, if it produced a credential, stores it.
    ///
    /// <para>Never throws for an expected failure: a user who closes the window,
    /// a machine with no browser and a refused identity all come back as a report
    /// with a reason.</para>
    /// </summary>
    public async Task<SteamSignInReport> SignInAsync(
        SteamSignInRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _session.SignInAsync(request, ct);

        if (!result.HasSession)
        {
            // Outcome and reason only. The reason is written to be shown to the
            // user and carries no credential; see SteamSignInResult.
            _log.LogInformation(
                "The Steam sign-in did not produce a session ({Outcome}): {Detail}",
                result.Outcome, result.Detail ?? "no reason recorded");

            return new SteamSignInReport(
                result.Outcome,
                result.Detail,
                SteamId: null,
                ExpiresAt: null,
                RefreshTokenCaptured: false,
                Persisted: false,
                await _sessions.GetHealthAsync(ct),
                result.Pages);
        }

        // TryCreate reads the expiry and the account out of the token rather than
        // taking this method's word for either, so a token that does not decode
        // yields no session at all instead of a half-built one whose first
        // request would fail.
        var session = SteamSession.TryCreate(result.AccessToken, result.RefreshToken, _clock.GetUtcNow());

        if (session is null)
        {
            // One way to get here now: the access token did not decode, state an
            // expiry, or name an account. A missing refresh token is no longer
            // one of them — the session record was relaxed precisely because
            // discarding a working day-long token for want of a second secret
            // threw away the sign-in the user had just completed.
            _log.LogWarning(
                "A Steam sign-in produced an access token that could not be read as a session. "
                + "Nothing was written; the Web API key path is unaffected.");

            return new SteamSignInReport(
                result.Outcome,
                "signed in, but the token could not be read as a session, so nothing was stored",
                result.SteamId,
                result.ExpiresAt,
                result.RefreshTokenCaptured,
                Persisted: false,
                await _sessions.GetHealthAsync(ct),
                result.Pages);
        }

        await _sessions.SaveAsync(session, ct);

        // Immediately, from the token's own subject claim, and before the report
        // is built: this is what makes the account visibility toggle live the
        // moment the window closes. No import, no Year in Review call, no wait.
        var confirmed = _confirmation is not null
            && await _confirmation.ConfirmAsync(
                session.SteamId, SteamAccountConfirmationSource.Session, ct);

        var health = await _sessions.GetHealthAsync(ct);

        return new SteamSignInReport(
            result.Outcome,
            session.HasRefreshToken
                ? result.Detail
                // Said out loud rather than degraded into: the session works and
                // has about a day in it, and nothing can renew it silently when
                // it goes. §4.7 condition 8.
                : "signed in, but Steam issued no refresh token, so this session cannot be renewed; "
                    + "it lasts about a day, after which scheduled syncs need a fresh sign-in or an API key",
            session.SteamId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            session.ExpiresAt,
            session.HasRefreshToken,
            Persisted: health != SteamSessionHealth.NotPersisted,
            health,
            result.Pages,
            confirmed);
    }

    /// <summary>
    /// Forgets the session, in memory and on disk. The only path that discards a
    /// refresh token, and the sign-out command S5 binds to.
    ///
    /// <para>Reconciliation runs straight afterwards so the account visibility
    /// filter answers correctly on the next read rather than at the next
    /// backfill pass. It clears a confirmation the session earned, and leaves one
    /// the API key earned alone — the key is still in force, and it is still the
    /// credential that proved the account.</para>
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _sessions.SignOutAsync(ct);

        if (_confirmation is not null)
        {
            await _confirmation.ReconcileAsync(ct);
        }
    }
}
