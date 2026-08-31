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
    private readonly ILogger<SteamSessionProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SteamSession? _cached;
    private bool _loadedFromStore;

    /// <summary>Latched the first time a hard lapse is observed, so it is diagnosed and logged once rather than per call. S6 also reads it to skip futile renewals.</summary>
    private bool _sessionLapsed;

    public SteamSessionProvider(
        ISteamSessionStore store,
        SteamWebOptions options,
        TimeProvider clock,
        ILogger<SteamSessionProvider>? log = null)
    {
        _store = store;
        _options = options;
        _clock = clock;
        _log = log ?? NullLogger<SteamSessionProvider>.Instance;
    }

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
