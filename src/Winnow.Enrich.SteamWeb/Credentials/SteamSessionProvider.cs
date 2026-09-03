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

    /// <summary>
    /// Guards in-memory session state. Held only for a store read or write,
    /// microseconds against a local SQLite settings row. Every reader takes
    /// it. Lock order: <see cref="_renewalGate"/> then this, never the
    /// reverse, so the two cannot deadlock.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Single-flight lock for one renewal exchange, and the only lock held
    /// across the network (three HTTP requests plus Polly backoff, potentially
    /// minutes). Readers take <see cref="_gate"/> instead and are never
    /// behind this. Before the split, one lock did both jobs and a reader
    /// whose access token had expired waited behind the whole exchange; that
    /// is reachable (keyless user, expired token, renewal in flight, user
    /// opens the Stores screen) and §5.1 forbids enrichment blocking a
    /// user-facing path.
    /// </summary>
    private readonly SemaphoreSlim _renewalGate = new(1, 1);

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
            await EnsureLoadedLockedAsync(ct);
            NoteLapseLocked();
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the store exactly once. Caller must hold <see cref="_gate"/>, which
    /// is never held across the network, so this wait is bounded by one local
    /// settings read.
    /// </summary>
    private async Task EnsureLoadedLockedAsync(CancellationToken ct)
    {
        if (_loadedFromStore)
        {
            return;
        }

        _loadedFromStore = true;
        _cached = await _store.LoadAsync(ct);

        if (_cached is not null)
        {
            // Neither the account nor either token: the interesting fact is that
            // a session exists and when it dies.
            _log.LogDebug(
                "Reused the stored Steam session; access token expires {ExpiresAt:O}.", _cached.ExpiresAt);
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
        // The single-flight lock, and the only one held across the network.
        // Readers take _gate instead and are never behind this.
        await _renewalGate.WaitAsync(ct);
        try
        {
            if (await ReadCurrentAsync(ct) is not { } current)
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
                return await LapseAsync(current, SteamSessionRenewalFailure.Expired, "it expired", ct);
            }

            RenewalsStarted++;
            var outcome = await _renewer.RenewAsync(current, ct);

            return outcome.Status switch
            {
                SteamRenewalStatus.Renewed => await AdoptAsync(current, outcome, now, ct),
                SteamRenewalStatus.Rejected => await LapseAsync(
                    current, SteamSessionRenewalFailure.Rejected, outcome.Reason, ct),
                SteamRenewalStatus.NotRenewable => current,
                _ => await RecordTransientAsync(current, outcome.Reason, ct),
            };
        }
        finally
        {
            _renewalGate.Release();
        }
    }

    /// <summary>The session as it stands, loading the store once. Takes <see cref="_gate"/> briefly.</summary>
    private async Task<SteamSession?> ReadCurrentAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedLockedAsync(ct);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes the outcome of a renewal, in memory and to the store, under
    /// <see cref="_gate"/>.
    ///
    /// <para>A fresh sign-in can land while an exchange is in flight, precisely
    /// because the exchange deliberately does not hold the state lock. Its
    /// session is newer than anything this renewal knows about, so the outcome
    /// is dropped rather than written over it, and the caller is handed what is
    /// actually current. Returning something other than
    /// <paramref name="next"/> is how a caller knows that happened.</para>
    /// </summary>
    private async Task<SteamSession> ApplyAsync(
        SteamSession current, SteamSession next, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is { } latest
                && !string.Equals(latest.AccessToken, current.AccessToken, StringComparison.Ordinal))
            {
                return latest;
            }

            _cached = next;
            await _store.SaveAsync(next, ct);
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adopts a renewed access token after three guards, and which guard
    /// adopts vs which refuses is the load-bearing decision.
    ///
    /// <para>Refused as transient: a token that does not decode or states no
    /// expiry, because there is nothing to store and an unreadable body is a
    /// shape problem, not a verdict. Refused as a hard lapse: a token whose
    /// subject is a different account. That is the security-critical claim;
    /// adopting it would silently re-point the whole library at somebody
    /// else.</para>
    ///
    /// <para>Adopted, not refused: a changed audience, provided the subject
    /// is unchanged. This was a hard lapse until the full-feature review
    /// caught what that would cost. A hard lapse discards the refresh token
    /// unrecoverably, the sign-in token carries <c>aud ["web:store"]</c>,
    /// and nobody has observed what the <c>pointssummary</c> renewal route
    /// mints; if it differs at all, the first renewal would permanently sign
    /// out every signed-in user on a guess. The new audience is adopted,
    /// stored, and logged at warning level naming both values. The anti-loop
    /// intent the original refusal was written for survives because the
    /// adoption is unconditional: it happens once, the stored audience
    /// becomes the new one, and a token Steam will not actually accept still
    /// fails honestly as a 401 through the reactive path.</para>
    ///
    /// <para>The issuer is deliberately not compared: the live evidence
    /// (spike §7.2) records it varying per mint, so matching on it would
    /// reject every renewal.</para>
    /// </summary>
    private async Task<SteamSession> AdoptAsync(
        SteamSession current, SteamRenewalOutcome outcome, DateTimeOffset now, CancellationToken ct)
    {
        var claims = SteamTokenClaims.Read(outcome.AccessToken);

        if (!claims.Readable || claims.ExpiresAt is not { } expiresAt)
        {
            return await RecordTransientAsync(current, "the renewed token did not decode", ct);
        }

        if (claims.SteamId is { } account && account != current.SteamId)
        {
            return await LapseAsync(
                current, SteamSessionRenewalFailure.Rejected, "it named a different account", ct);
        }

        var audience = current.Audience;

        // An absent audience claim leaves the stored one alone: it is a fact
        // nobody has, not a change.
        if (claims.Audiences.Count > 0 && !AudienceUnchanged(current.Audience, claims.Audiences))
        {
            audience = claims.Audiences;

            _log.LogWarning(
                "The renewed Steam access token carries a different audience: [{Stored}] became [{Minted}]. "
                + "The new audience is adopted and stored rather than treated as a refusal, because nothing "
                + "has ever observed what this renewal route mints and discarding the refresh token on a "
                + "guess would cost the sign-in outright. If Steam will not accept the new token it still "
                + "fails honestly, as a 401.",
                string.Join(", ", current.Audience),
                string.Join(", ", claims.Audiences));
        }

        var renewed = current.WithRenewedAccess(
            outcome.AccessToken!, expiresAt, now, outcome.RefreshToken, audience);

        var applied = await ApplyAsync(current, renewed, ct);
        if (!ReferenceEquals(applied, renewed))
        {
            // A sign-in overtook the exchange. Its session is newer; this one is
            // discarded rather than written over it.
            return applied;
        }

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
    /// The hard lapse. Runs under <see cref="_renewalGate"/> and takes
    /// <see cref="_gate"/> itself through <see cref="ApplyAsync"/>. The
    /// refresh token is discarded and the session is latched off for this
    /// process, so nothing tries again until the user signs in. The record
    /// itself is kept so the screen can say the sign-in ended rather than
    /// that it never happened; see <see cref="SteamSession.WithLapsedRefresh"/>.
    /// </summary>
    private async Task<SteamSession> LapseAsync(
        SteamSession current, SteamSessionRenewalFailure kind, string reason, CancellationToken ct)
    {
        var lapsed = current.WithLapsedRefresh(kind);

        var applied = await ApplyAsync(current, lapsed, ct);
        if (!ReferenceEquals(applied, lapsed))
        {
            // A sign-in overtook the exchange, so there is a working session
            // again. Latching now would switch off a credential the user has
            // just re-earned.
            return applied;
        }

        _sessionLapsed = true;

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
    /// A transient failure. Runs under <see cref="_renewalGate"/> and takes
    /// <see cref="_gate"/> itself through <see cref="ApplyAsync"/>. Nothing
    /// is cleared and nothing is latched, so the next pass tries again, but
    /// the count is recorded and persisted, which is what moves the health
    /// to <see cref="SteamSessionHealth.RenewalFailing"/> while the access
    /// token is still alive. That is condition 8: the warning has to arrive
    /// before the credential dies, not with it.
    /// </summary>
    private async Task<SteamSession> RecordTransientAsync(
        SteamSession current, string reason, CancellationToken ct)
    {
        var failed = current.WithRenewalFailure(SteamSessionRenewalFailure.Transient);

        var applied = await ApplyAsync(current, failed, ct);
        if (!ReferenceEquals(applied, failed))
        {
            return applied;
        }

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
