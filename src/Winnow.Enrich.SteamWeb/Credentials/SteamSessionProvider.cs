using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// The in-memory owner of the stored Steam session.
///
/// <para>Concurrency follows <c>EpicTokenProvider</c> deliberately, because the
/// same three problems apply: a live token is read on every enrichment call and
/// must not serialise callers, the store must be read exactly once rather than
/// on every miss, and a dead session must not re-run its own diagnosis on every
/// caller. Hence the unlocked fast path, the <see cref="SemaphoreSlim"/> gate
/// with the check repeated inside it, the loaded-from-store flag, and the lapse
/// latch.</para>
///
/// <para>One difference from Epic, and it is not an oversight: a lapse here does
/// <b>not</b> clear the store. Epic's provider discards a session whose refresh
/// token Epic has rejected, because there is nothing left to recover. A lapsed
/// Steam session is equally unrecoverable, but discarding it would turn
/// <see cref="SteamSessionHealth.Expired"/> (which the UI can explain with a
/// one-click re-sign-in) into
/// <see cref="SteamSessionHealth.NotSignedIn"/>, which looks like a user who
/// never connected. Section 4.7's eighth binding condition makes that
/// distinction load-bearing, so the dead session stays on the books until the
/// user signs out or signs in again.</para>
/// </summary>
public sealed class SteamSessionProvider : ISteamSessionProvider
{
    private readonly ISteamSessionStore _store;
    private readonly SteamWebOptions _options;
    private readonly TimeProvider _clock;
    private readonly ISteamSessionRenewer? _renewer;
    private readonly ILogger<SteamSessionProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SteamSession? _cached;
    private bool _loadedFromStore;

    /// <summary>Latched the first time a hard lapse is observed, so it is diagnosed and logged once rather than per call. S6 also reads it to skip futile renewals.</summary>
    private volatile bool _sessionLapsed;

    public SteamSessionProvider(
        ISteamSessionStore store,
        SteamWebOptions options,
        TimeProvider clock,
        ILogger<SteamSessionProvider>? log = null,
        ISteamSessionRenewer? renewer = null)
    {
        _store = store;
        _options = options;
        _clock = clock;
        _log = log ?? NullLogger<SteamSessionProvider>.Instance;
        _renewer = renewer;
    }

    /// <summary>
    /// How many renewal exchanges this provider actually started. Test hook;
    /// what single-flight is proved against: two concurrent callers must move
    /// this by one, not two.
    /// </summary>
    public int RenewalsStarted { get; private set; }

    public async ValueTask<SteamSession?> GetAsync(CancellationToken ct = default)
    {
        // Fast path outside the lock: a live access token is the overwhelmingly
        // common case and must not serialise callers. Read the field once;
        // re-reading it after the check would be a race with SignOutAsync.
        var cached = _cached;
        if (cached is not null && cached.IsAccessUsable(_clock.GetUtcNow(), _options.SessionExpirySkew))
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (!_loadedFromStore)
            {
                _loadedFromStore = true;
                _cached = await _store.LoadAsync(ct);

                if (_cached is not null)
                {
                    // Neither the account nor either token: the interesting fact
                    // is that a session exists and when it dies.
                    _log.LogDebug(
                        "Reused the stored Steam session; access token expires {ExpiresAt:O}.", _cached.ExpiresAt);
                }
            }

            NoteLapseLocked();
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<SteamSessionHealth> GetHealthAsync(CancellationToken ct = default)
        => Classify(await GetAsync(ct), _clock.GetUtcNow());

    public async Task SaveAsync(SteamSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _gate.WaitAsync(ct);
        try
        {
            _cached = session;
            _loadedFromStore = true;
            _sessionLapsed = false;
            await _store.SaveAsync(session, ct);

            // The account id is deliberately absent: naming it here only puts a
            // real person's Steam id into the log file.
            _log.LogInformation(
                "Signed in to Steam. Access token expires {ExpiresAt:O}; session {Persistence}.",
                session.ExpiresAt,
                _store.CanPersist ? "stored encrypted" : "held in memory for this run only");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _cached = null;

            // Left true: the store has just been emptied, so re-reading it would
            // only confirm that. Epic's provider does the same.
            _loadedFromStore = true;
            _sessionLapsed = false;
            await _store.ClearAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsRenewalDue(SteamSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_renewer is null || _sessionLapsed || !session.HasRefreshToken)
        {
            return false;
        }

        var now = _clock.GetUtcNow();

        // The same arithmetic Classify uses for RenewalDue, so the state the
        // Stores screen shows and the work this provider does cannot disagree.
        return session.IsRefreshUsable(now, _options.SessionExpirySkew)
            && !session.IsAccessUsable(now, _options.SessionExpirySkew + _options.SessionRenewalLeadTime);
    }

    public async Task<SteamSession?> RenewAsync(SteamSession? staleSession, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loadedFromStore)
            {
                _loadedFromStore = true;
                _cached = await _store.LoadAsync(ct);
            }

            if (_cached is not { } current)
            {
                return null;
            }

            // Somebody else already replaced the session this caller was holding.
            // Hand theirs back rather than spending the refresh token again:
            // spending one can invalidate the previous one, so a double spend is
            // a self-inflicted sign-out. EpicTokenProvider.RefreshAsync does the
            // same, for the same reason.
            if (staleSession is not null
                && !string.Equals(current.AccessToken, staleSession.AccessToken, StringComparison.Ordinal))
            {
                return current;
            }

            if (_sessionLapsed)
            {
                // Latched: Steam has already refused this credential once in this
                // process. One rejection per pass, then we stop asking.
                return current;
            }

            if (_renewer is null || !current.HasRefreshToken)
            {
                return current;
            }

            var now = _clock.GetUtcNow();
            if (!current.IsRefreshUsable(now, _options.SessionExpirySkew))
            {
                // Steam told us when this would lapse and it has. Predictable, so
                // it is recorded as its own kind rather than as a rejection.
                return await LapseLockedAsync(current, SteamSessionRenewalFailure.Expired, "it expired", ct);
            }

            RenewalsStarted++;
            var outcome = await _renewer.RenewAsync(current, ct);

            return outcome.Status switch
            {
                SteamRenewalStatus.Renewed => await AdoptLockedAsync(current, outcome, now, ct),
                SteamRenewalStatus.Rejected => await LapseLockedAsync(
                    current, SteamSessionRenewalFailure.Rejected, outcome.Reason, ct),
                SteamRenewalStatus.NotRenewable => current,
                _ => await RecordTransientLockedAsync(current, outcome.Reason, ct),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adopts a renewed access token. Caller must hold <see cref="_gate"/>.
    ///
    /// <para>Three refusals before the token is believed, each of which would
    /// otherwise become a loop or a wrong answer: it must decode and state an
    /// expiry, or there is nothing to store; its subject must be the account
    /// this session belongs to, so a renewal cannot quietly re-point the
    /// library at somebody else; and its audience must set-equal the one
    /// already stored. The audience check is why audience is stored at all: a
    /// token minted for an audience the API will not accept produces a 401,
    /// which triggers a renewal, which mints the same wrong audience again.
    /// Lapsing costs a sign-in; looping costs the refresh token and the
    /// request budget.</para>
    ///
    /// <para>The issuer is deliberately NOT compared: the live evidence
    /// (spike §7.2) records it varying per mint, so matching on it would
    /// reject every renewal.</para>
    ///
    /// <para>The write is one blob through one <see cref="ISteamSessionStore.SaveAsync"/>,
    /// so a rotated refresh token and the access token it came with land
    /// together or not at all.</para>
    /// </summary>
    private async Task<SteamSession> AdoptLockedAsync(
        SteamSession current, SteamRenewalOutcome outcome, DateTimeOffset now, CancellationToken ct)
    {
        var claims = SteamTokenClaims.Read(outcome.AccessToken);

        if (!claims.Readable || claims.ExpiresAt is not { } expiresAt)
        {
            return await RecordTransientLockedAsync(current, "the renewed token did not decode", ct);
        }

        if (claims.SteamId is { } account && account != current.SteamId)
        {
            return await LapseLockedAsync(
                current, SteamSessionRenewalFailure.Rejected, "it named a different account", ct);
        }

        if (!AudienceUnchanged(current.Audience, claims.Audiences))
        {
            return await LapseLockedAsync(
                current, SteamSessionRenewalFailure.Rejected, "its audience changed", ct);
        }

        var renewed = current.WithRenewedAccess(outcome.AccessToken!, expiresAt, now, outcome.RefreshToken);

        _cached = renewed;
        await _store.SaveAsync(renewed, ct);

        // Neither token, and not the account: the facts worth having are that a
        // renewal worked, when the replacement dies, and whether the long-lived
        // secret on disk is now a different one.
        _log.LogInformation(
            "Renewed the Steam session; access token expires {ExpiresAt:O}. Refresh token {Rotation}.",
            renewed.ExpiresAt,
            outcome.RefreshToken is null ? "unchanged" : "replaced by Steam and stored");

        return renewed;
    }

    /// <summary>
    /// The hard lapse. Caller must hold <see cref="_gate"/>. The refresh token
    /// is discarded and the session is latched off for this process, so
    /// nothing tries again until the user signs in. The record itself is kept
    /// so the screen can say the sign-in ended rather than that it never
    /// happened; see <see cref="SteamSession.WithLapsedRefresh"/>.
    /// </summary>
    private async Task<SteamSession> LapseLockedAsync(
        SteamSession current, SteamSessionRenewalFailure kind, string reason, CancellationToken ct)
    {
        _sessionLapsed = true;

        var lapsed = current.WithLapsedRefresh(kind);
        _cached = lapsed;
        await _store.SaveAsync(lapsed, ct);

        _log.LogWarning(
            "Steam would not renew the stored session ({Reason}). The refresh token has been discarded and "
            + "the session cannot be renewed again; the user has to sign in to Steam again. Any access token "
            + "still in hand keeps working until {ExpiresAt:O}, and a configured Web API key and the local "
            + "Steam readers are unaffected.",
            reason,
            lapsed.ExpiresAt);

        return lapsed;
    }

    /// <summary>
    /// A transient failure. Caller must hold <see cref="_gate"/>. Nothing is
    /// cleared and nothing is latched, so the next pass tries again, but the
    /// count is recorded and persisted, which is what moves the health to
    /// <see cref="SteamSessionHealth.RenewalFailing"/> while the access token
    /// is still alive. That is condition 8: the warning has to arrive before
    /// the credential dies, not with it.
    /// </summary>
    private async Task<SteamSession> RecordTransientLockedAsync(
        SteamSession current, string reason, CancellationToken ct)
    {
        var failed = current.WithRenewalFailure(SteamSessionRenewalFailure.Transient);
        _cached = failed;
        await _store.SaveAsync(failed, ct);

        _log.LogWarning(
            "Could not renew the Steam session ({Reason}); this is attempt {Failures} since the last success. "
            + "The session is kept and the next pass tries again. It expires {ExpiresAt:O}.",
            reason,
            failed.RenewalFailures,
            failed.ExpiresAt);

        return failed;
    }

    /// <summary>
    /// Set comparison, order-insensitive and ordinal. An empty stored audience
    /// compares equal to anything: a session recorded before the claim could
    /// be read must not be lapsed for a fact nobody has.
    /// </summary>
    private static bool AudienceUnchanged(IReadOnlyList<string> stored, IReadOnlyList<string> minted)
        => stored.Count == 0
            || (minted.Count > 0 && stored.ToHashSet(StringComparer.Ordinal)
                .SetEquals(minted));

    /// <summary>
    /// Maps a session onto the one value the UI switches on. Pure and total, so
    /// it is testable against a fake clock without a store, and so every caller
    /// gets the same answer from the same facts.
    ///
    /// <para>Order is precedence, and it is chosen rather than incidental. Dead
    /// beats everything, because nothing else is worth saying about a credential
    /// that cannot be sent and cannot be renewed. A recorded failure beats a due
    /// renewal, because "renewal is failing" is the sentence the user has to see
    /// before the token dies. Durability comes last, because a session that is
    /// not persisted still works for this run and saying so is a caveat, not a
    /// fault.</para>
    ///
    /// <para><b>The two kinds of session are distinguished here.</b> A session
    /// holding a refresh token is the renewable kind and passes through
    /// <see cref="SteamSessionHealth.RenewalDue"/> on its way out, which is the
    /// state S6 acts on. A token-only session has no such path, so it reads
    /// <see cref="SteamSessionHealth.Live"/> right up to its expiry and
    /// <see cref="SteamSessionHealth.Expired"/> after it. Calling that one
    /// RenewalDue would tell the user a renewal is owed that nothing can ever
    /// pay, and would hand S6 work it cannot do.</para>
    /// </summary>
    public static SteamSessionHealth Classify(
        SteamSession? session, DateTimeOffset now, TimeSpan? skew = null, TimeSpan? renewalLead = null,
        bool canPersist = true)
    {
        if (session is null)
        {
            return SteamSessionHealth.NotSignedIn;
        }

        var expirySkew = skew ?? SteamCredential.DefaultSkew;
        var lead = renewalLead ?? TimeSpan.FromHours(1);

        var accessUsable = session.IsAccessUsable(now, expirySkew);
        var refreshUsable = session.IsRefreshUsable(now, expirySkew);

        if (!accessUsable && !refreshUsable)
        {
            return SteamSessionHealth.Expired;
        }

        if (session.RenewalFailures > 0)
        {
            return SteamSessionHealth.RenewalFailing;
        }

        // Either already dead with a refresh token that should replace it, or
        // alive but inside the lead window where renewal ought to happen before
        // anyone notices. Both are the same instruction to S6 — and both require
        // a refresh token to act on, which is why the guard is on refreshUsable
        // rather than on the clock alone.
        if (refreshUsable && (!accessUsable || !session.IsAccessUsable(now, expirySkew + lead)))
        {
            return SteamSessionHealth.RenewalDue;
        }

        return canPersist ? SteamSessionHealth.Live : SteamSessionHealth.NotPersisted;
    }

    private SteamSessionHealth Classify(SteamSession? session, DateTimeOffset now)
        => Classify(session, now, _options.SessionExpirySkew, _options.SessionRenewalLeadTime, _store.CanPersist);

    /// <summary>
    /// Latches and logs a hard lapse exactly once. Caller must hold
    /// <see cref="_gate"/>. Nothing is cleared; see the type comment.
    /// </summary>
    private void NoteLapseLocked()
    {
        if (_sessionLapsed || _cached is null)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        if (_cached.IsAccessUsable(now, _options.SessionExpirySkew)
            || _cached.IsRefreshUsable(now, _options.SessionExpirySkew))
        {
            return;
        }

        _sessionLapsed = true;

        // Two different sentences, because they have two different causes and a
        // support question about either is answered by the wrong one. A
        // renewable session that reaches here outlived its refresh token; a
        // token-only session simply reached the end it always had.
        if (_cached.HasRefreshToken)
        {
            _log.LogInformation(
                "The stored Steam session expired on {ExpiresAt:O} and its refresh token lapsed on "
                + "{RefreshExpiresAt:O}. Steam calls that depend on it are skipped until the user signs in "
                + "again; a configured Web API key and the local Steam readers are unaffected.",
                _cached.ExpiresAt,
                _cached.RefreshExpiresAt);
        }
        else
        {
            _log.LogInformation(
                "The stored Steam session expired on {ExpiresAt:O}. It carried no refresh token, so there "
                + "was never a way to renew it silently. Steam calls that depend on it are skipped until the "
                + "user signs in again; a configured Web API key and the local Steam readers are unaffected.",
                _cached.ExpiresAt);
        }
    }
}
