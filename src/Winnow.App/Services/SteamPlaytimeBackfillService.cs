using System.Diagnostics;
using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// M5: the one-off import of history Steam already holds and Winnow was never
/// going to observe. Per-month playtime from Steam Replay, and the first-played
/// dates nothing else in Winnow's sources carries.
///
/// <para>A network job, and therefore not part of
/// <see cref="ILocalLibrarySync"/>. It runs once per launch from the startup
/// pipeline, after the remote ownership sync has created the rows it attaches
/// to, and never in front of a user (§5.1).</para>
/// </summary>
public interface ISteamPlaytimeBackfill
{
    /// <inheritdoc cref="SteamPlaytimeBackfillService.BackfillAsync"/>
    Task<SteamPlaytimeBackfillReport> BackfillAsync(CancellationToken ct = default);
}

/// <summary>Knobs for <see cref="SteamPlaytimeBackfillService"/>.</summary>
public sealed class SteamPlaytimeBackfillOptions
{
    /// <summary>
    /// First year to ask Steam Replay about. 2022 is the first year Valve ran
    /// it (published January 2023), so earlier years answer empty and asking
    /// about them is a request spent to learn nothing.
    /// </summary>
    public int FirstYear { get; set; } = 2022;

    /// <summary>
    /// Whether the backfill runs at all. Off mirrors <c>--no-sync</c>: UI work
    /// against a fixed database must not have rows appearing underneath it.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>What one backfill pass did. Every field is a count, never a payload.</summary>
/// <param name="Accounts">Steam accounts the pass looked at.</param>
/// <param name="YearsFetched">Years asked about, cache hits included.</param>
/// <param name="YearsCompleted">Years answered and therefore marked done.</param>
/// <param name="YearsFailed">Years that did not answer and will be retried.</param>
/// <param name="GamesReconstructed">Appids that produced at least one cumulative point.</param>
/// <param name="SnapshotsWritten">New rows in <c>playtime_snapshots</c>. Zero on a re-run.</param>
/// <param name="PlayRecordsWritten">New first-played rows in <c>play_records</c>. Zero on a re-run.</param>
/// <param name="SkippedNoOwnership">
/// Appids Steam reported that no ownership row matches: delisted apps, titles
/// played on a shared account, anything the sync jobs did not create. Counted
/// rather than resolved: this job never creates works, releases or ownerships.
/// </param>
/// <param name="SkippedNoAnchor">Appids with months but no cumulative total to anchor them to.</param>
/// <param name="Clamped">Appids whose backward walk hit zero before the months ran out.</param>
/// <param name="Elapsed">Wall-clock time for the whole pass.</param>
public sealed record SteamPlaytimeBackfillReport(
    int Accounts,
    int YearsFetched,
    int YearsCompleted,
    int YearsFailed,
    int GamesReconstructed,
    int SnapshotsWritten,
    int PlayRecordsWritten,
    int SkippedNoOwnership,
    int SkippedNoAnchor,
    int Clamped,
    TimeSpan Elapsed)
{
    /// <summary>The pass that did nothing: unconfigured, disabled, or no Steam account to ask about.</summary>
    public static SteamPlaytimeBackfillReport Nothing(TimeSpan elapsed)
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, elapsed);

    /// <summary>Whether anything was actually written.</summary>
    public bool WroteAnything => SnapshotsWritten > 0 || PlayRecordsWritten > 0;
}

/// <summary>
/// Implements <see cref="ISteamPlaytimeBackfill"/>.
///
/// <para><b>Why this writes through the repositories and not through
/// <c>ExternalIdResolver</c>.</b> The resolver decides whether to append by
/// comparing a candidate against the NEWEST stored row, and it clamps under
/// <c>PlaytimeView.LowerBound</c> so a source that cannot see the whole total
/// never writes the series backwards. Both behaviours are right for an
/// observation of the present and catastrophic for one of the past: every
/// historical point would compare as "changed" forever, and the clamp would
/// rewrite each one up to today's figure, turning four years of history into
/// four years of today. <c>TryAppendAsync</c> judges each point on its
/// full-fact identity instead, so out-of-order insertion is ordinary and a
/// re-run is a no-op. Pinned by <c>ObservationIdentityTests</c> and
/// <c>CrossJobPlaytimeSeriesTests</c>.</para>
/// </summary>
public sealed class SteamPlaytimeBackfillService : ISteamPlaytimeBackfill
{
    /// <summary>
    /// Settings key holding the completion marker for one (account, year). The
    /// value is the UTC instant the year was imported plus a short summary, a
    /// timestamp rather than a bare flag so a support question about a wrong
    /// series can be answered from the database.
    /// </summary>
    internal const string YearMarkerPrefix = "steam.backfill.yir.";

    /// <summary>
    /// Settings key recording that the configured API key was observed to belong
    /// to this account. See <see cref="ConfirmAccountAsync"/> for why this has to
    /// be persisted rather than recomputed per pass.
    /// </summary>
    internal const string ConfirmedPrefix = "steam.backfill.account.";

    private readonly ISteamHistoryClient _history;
    private readonly IReleaseRepository _releases;
    private readonly IOwnershipRepository _ownerships;
    private readonly IOwnershipAccountRepository _ownershipAccounts;
    private readonly IPlayRecordRepository _playRecords;
    private readonly IPlaytimeSnapshotRepository _snapshots;
    private readonly ISettingsRepository _settings;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly LibrarySyncGate _gate;
    private readonly SteamPlaytimeBackfillOptions _options;
    private readonly TimeProvider _clock;
    private readonly ISteamAccountConfirmation _confirmation;
    private readonly ILogger<SteamPlaytimeBackfillService> _logger;

    public SteamPlaytimeBackfillService(
        ISteamHistoryClient history,
        IReleaseRepository releases,
        IOwnershipRepository ownerships,
        IOwnershipAccountRepository ownershipAccounts,
        IPlayRecordRepository playRecords,
        IPlaytimeSnapshotRepository snapshots,
        ISettingsRepository settings,
        IUnitOfWorkFactory unitOfWork,
        LibrarySyncGate gate,
        SteamPlaytimeBackfillOptions options,
        TimeProvider clock,
        // Required, not optional. The fingerprint is the only thing standing
        // between a freshly pasted key and the previous owner's account
        // identity, and a guard that silently disables itself when a
        // registration is missed is not a guard. AddSteamPlaytimeBackfill
        // already documents AddSteamWebApi() as a prerequisite, and that is
        // what registers this. Read only to build the default confirmation
        // writer below; every fingerprint decision is that writer's.
        ISteamApiKeyProvider apiKeys,
        ILogger<SteamPlaytimeBackfillService> logger,
        // Optional, and the default is the real thing rather than a no-op. The
        // key path is the only caller that predates the shared writer, so an
        // existing construction site that says nothing about confirmation gets
        // exactly the behaviour it always had, over the same settings rows the
        // sign-in path now writes through.
        ISteamAccountConfirmation? confirmation = null)
    {
        _history = history;
        _releases = releases;
        _ownerships = ownerships;
        _ownershipAccounts = ownershipAccounts;
        _playRecords = playRecords;
        _snapshots = snapshots;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _gate = gate;
        _options = options;
        _clock = clock;
        _confirmation = confirmation ?? new SteamAccountConfirmation(settings, apiKeys);
        _logger = logger;
    }

    /// <summary>
    /// Imports every year of Steam Replay not already marked complete, for every
    /// Steam account the library holds ownerships for. Safe to call on every
    /// launch: completed years are never refetched, the current year is
    /// refreshed, and every write is idempotent on its own identity.
    /// </summary>
    public async Task<SteamPlaytimeBackfillReport> BackfillAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_options.Enabled)
        {
            return SteamPlaytimeBackfillReport.Nothing(stopwatch.Elapsed);
        }

        // Before the early return below, deliberately: a key that was REMOVED
        // has to invalidate the confirmation just as surely as one that was
        // replaced, and the unconfigured path returns without ever looking at an
        // account again.
        //
        // The reconciliation is the shared writer's, not this class's, so a
        // sign-out invalidates a session-earned confirmation on exactly the same
        // terms — and a key-earned one survives both, because the credential that
        // earned it is still here.
        await _confirmation.ReconcileAsync(ct);

        // Checked before anything is read, for the same reason the remote
        // ownership sync checks first: "registered and idle" is the common
        // state, and an unconfigured machine must not pay a library-wide
        // ownership read to discover it has nothing to do.
        if (!await _history.IsConfiguredAsync(ct))
        {
            _logger.LogDebug("Steam Web API key not configured; the playtime backfill has nothing to do.");
            return SteamPlaytimeBackfillReport.Nothing(stopwatch.Elapsed);
        }

        var accounts = await SteamAccountsAsync(ct);
        if (accounts.Count == 0)
        {
            _logger.LogDebug("No Steam ownership carries an account id; the playtime backfill is skipped.");
            return SteamPlaytimeBackfillReport.Nothing(stopwatch.Elapsed);
        }

        var totals = new Totals { Accounts = accounts.Count };
        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            await BackfillAccountAsync(account, totals, ct);
        }

        stopwatch.Stop();
        var report = totals.ToReport(stopwatch.Elapsed);

        if (report.YearsFetched > 0)
        {
            _logger.LogInformation(
                "Steam playtime backfill: {Years} years over {Accounts} accounts in {Elapsed:n1}s — "
                + "{Games} games reconstructed, {Snapshots} snapshots, {Records} first-played records, "
                + "{NoOwnership} appids with no ownership, {NoAnchor} with no total, {Clamped} clamped.",
                report.YearsFetched, report.Accounts, report.Elapsed.TotalSeconds,
                report.GamesReconstructed, report.SnapshotsWritten, report.PlayRecordsWritten,
                report.SkippedNoOwnership, report.SkippedNoAnchor, report.Clamped);
        }

        return report;
    }

    /// <summary>
    /// The accounts to ask about, taken from the ownerships the sync jobs
    /// already created rather than from a fresh filesystem scan. The backfill
    /// runs after those jobs, so the table is the cheaper and equally complete
    /// answer, and it keeps this type off the disk entirely.
    /// </summary>
    private async Task<IReadOnlyList<SteamId>> SteamAccountsAsync(CancellationToken ct)
    {
        var accounts = new List<SteamId>();
        var seen = new HashSet<ulong>();

        void Add(string? accountRef)
        {
            if (SteamId.TryParse(accountRef, out var steamId) && seen.Add(steamId.Value))
            {
                accounts.Add(steamId);
            }
        }

        foreach (var ownership in await _ownerships.GetAllAsync(ct))
        {
            if (string.Equals(ownership.Store, ExternalIdProviders.Steam, StringComparison.Ordinal))
            {
                Add(ownership.AccountRef);
            }
        }

        // And the accounts that column cannot name. `ownerships.account_ref`
        // holds the play tuple's winner, so on a shared PC an account that never
        // out-played the other appears in it nowhere — and if the user's own
        // account is that one, this pass would never ask Steam about it, never
        // confirm it, and the visibility toggle would stay disabled forever on
        // exactly the machine the feature exists for. The membership rows name
        // every account each reader saw.
        foreach (var accountRef in await _ownershipAccounts.GetAccountRefsAsync(
                     ExternalIdProviders.Steam, ct))
        {
            Add(accountRef);
        }

        return accounts;
    }

    private async Task BackfillAccountAsync(SteamId steamId, Totals totals, CancellationToken ct)
    {
        var currentYear = _clock.GetUtcNow().UtcDateTime.Year;

        // The current year is always refetched: it is still accruing, and a
        // marker written in March would freeze the series at March. Every
        // earlier year is closed history and is asked about exactly once per
        // install.
        var pending = new List<int>();
        for (var year = _options.FirstYear; year <= currentYear; year++)
        {
            if (year == currentYear || await _settings.GetAsync(YearMarker(steamId, year), ct) is null)
            {
                pending.Add(year);
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        // ── Fetch phase. No lock is held here: the write gate must never sit
        // behind an HTTP timeout, which is the mistake the F04 split was made to
        // stop repeating.
        var months = new Dictionary<string, List<SteamMonthlyPlaytime>>(StringComparer.Ordinal);
        var yearFirstPlayed = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var completed = new List<(int Year, int Games)>();
        var confirmed = await _settings.GetAsync(ConfirmedKey(steamId), ct) is not null;

        // Kept separate from `confirmed`, which a marker written on some earlier
        // launch can satisfy. Only a disclosure THIS PASS proves the key in
        // force belongs to this account, and only that may name the account the
        // visibility filter keeps — otherwise a stale marker would let a
        // stranger's key inherit the previous owner's identity.
        var disclosedThisPass = false;

        foreach (var year in pending)
        {
            ct.ThrowIfCancellationRequested();
            totals.YearsFetched++;

            var review = await _history.GetYearInReviewAsync(steamId, year, ct: ct);

            if (review.AccountMismatch)
            {
                // The configured key belongs to somebody else. Abandoning the
                // whole account is the only safe response: importing would
                // write one person's play history onto another's ownerships,
                // and no marker is written so a corrected key retries cleanly.
                _logger.LogWarning(
                    "Steam Year in Review answered for a different account than {Account}; "
                    + "the playtime backfill is skipped for this account until the API key matches.",
                    steamId.AccountId);
                return;
            }

            if (!review.Answered)
            {
                // Transport failure, distinct from an empty year. No marker, so
                // the next launch asks again.
                totals.YearsFailed++;
                continue;
            }

            if (review.AccountId is not null)
            {
                confirmed = true;
                disclosedThisPass = true;
            }

            foreach (var game in review.Games)
            {
                if (game.Months.Count > 0)
                {
                    Bucket(months, game.AppId).AddRange(game.Months);
                }

                if (game.FirstPlayedUtc is { } first
                    && (!yearFirstPlayed.TryGetValue(game.AppId, out var known) || first < known))
                {
                    yearFirstPlayed[game.AppId] = first;
                }
            }

            completed.Add((year, review.Games.Count));
        }

        // ── The disclosure the ordinary path can no longer reach ─────────────
        //
        // Every year but the current one is asked about exactly once per install,
        // so an account that finished its backfill refetches only the current
        // year — and an uncompiled current-year Replay answers empty, with no
        // account id in it. Nothing else in Winnow discloses which account the
        // key belongs to, so on precisely the accounts that HAVE backfilled, the
        // disclosure could never fire again and the visibility toggle stayed
        // disabled for good. Found live 2026-08-30.
        //
        // The remedy is one extra read, only when it is the only thing standing
        // between the user and a working toggle.
        //
        // ── KEPT, UNCHANGED, AND ON PURPOSE (TASK-55 S4) ────────────────────
        //
        // A signed-in user never needs this: the sign-in writes the account out
        // of the token's own subject claim the moment the window closes, so the
        // reference is already set and NeedsDisclosureRefetchAsync's first
        // condition returns false for one settings read. That makes the refetch a
        // natural no-op for them rather than something switched off for them, and
        // SteamAccountIdentityTests pins that it really is one.
        //
        // For a key-only user it is still the ONLY route to the fact. Nothing
        // else in Winnow discloses which account a key belongs to, so removing
        // this — or gating it on a session that a key-only user will never have —
        // would put TASK-54's bug straight back for exactly the users TASK-54 was
        // written for.
        if (!disclosedThisPass && await NeedsDisclosureRefetchAsync(steamId, ct))
        {
            switch (await DiscloseFromCompletedYearAsync(steamId, totals, ct))
            {
                case DisclosureOutcome.Mismatch:
                    // Same response as the main loop: the key is somebody
                    // else's, so this account is abandoned for the pass.
                    return;

                case DisclosureOutcome.Disclosed:
                    confirmed = true;
                    disclosedThisPass = true;
                    break;

                case DisclosureOutcome.NothingToDiscloseFrom:
                default:
                    // No populated year to ask about, or it did not answer. The
                    // toggle stays disabled and says why; nothing is written.
                    break;
            }
        }

        if (!confirmed)
        {
            // Nothing has ever proved the key belongs to this account.
            // Steam's bare envelope is the same for "no Replay this year"
            // and "not your account", and the anchor endpoint carries no
            // account id to check against. Empty years are recorded as done;
            // nothing is imported.
            await RecordCompletionAsync(steamId, completed, imported: 0, totals, ct);
            _logger.LogDebug(
                "Steam Year in Review disclosed nothing for account {Account}; "
                + "no anchor was fetched and nothing was imported.",
                steamId.AccountId);
            return;
        }

        await ConfirmAccountAsync(steamId, disclosedThisPass, ct);

        // The anchor: cumulative playtime as it stands right now. Everything the
        // reconstruction produces is derived by subtraction from these figures,
        // so without them there is nothing to import even when the months
        // arrived.
        var anchors = await _history.GetLastPlayedTimesAsync(ct: ct);
        if (!anchors.Answered)
        {
            _logger.LogWarning(
                "Steam last-played times did not answer; the year data fetched for account {Account} "
                + "is left unimported and no year is marked complete.",
                steamId.AccountId);
            return;
        }

        // ── Write phase. One transaction for the whole account, under the same
        // gate the resolver passes take, so a backfill and a sync never open
        // concurrent write transactions on SQLite's single writer.
        using var lease = await _gate.EnterAsync(ct).ConfigureAwait(false);
        var written = await WriteAsync(months, yearFirstPlayed, anchors, totals, ct);

        await RecordCompletionAsync(steamId, completed, written, totals, ct);
    }

    private async Task<int> WriteAsync(
        Dictionary<string, List<SteamMonthlyPlaytime>> months,
        Dictionary<string, DateTime> yearFirstPlayed,
        SteamLastPlayedTimes anchors,
        Totals totals,
        CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var anchorsByAppId = anchors.AnchorsByAppId;
        var ownershipCache = new Dictionary<string, long?>(StringComparer.Ordinal);
        var written = 0;

        using var scope = _unitOfWork.Begin();

        foreach (var (appId, appMonths) in months)
        {
            ct.ThrowIfCancellationRequested();

            if (await OwnershipAsync(appId, ownershipCache, ct) is not { } ownershipId)
            {
                totals.SkippedNoOwnership++;
                continue;
            }

            if (!anchorsByAppId.TryGetValue(appId, out var anchorMinutes))
            {
                // Months without a cumulative total. Reconstructing from an
                // assumed baseline is exactly the forward-walk mistake the
                // design rejects, so the game is counted and left alone.
                totals.SkippedNoAnchor++;
                continue;
            }

            var series = PlaytimeSeriesReconstructor.Reconstruct(anchorMinutes, appMonths);
            if (series.Points.Count == 0)
            {
                continue;
            }

            if (series.Clamped)
            {
                totals.Clamped++;
            }

            totals.GamesReconstructed++;
            foreach (var point in series.Points)
            {
                var id = await _snapshots.TryAppendAsync(
                    new PlaytimeSnapshot
                    {
                        OwnershipId = ownershipId,
                        PlaytimeMinutes = point.PlaytimeMinutes,
                        ObservedAt = point.ObservedAt,
                    },
                    ct);

                if (id is not null)
                {
                    totals.SnapshotsWritten++;
                    written++;
                }
            }
        }

        // The first-played dates, from both sources and under separate labels so
        // a row can always be traced back to the endpoint that produced it.
        foreach (var (appId, firstPlayed) in yearFirstPlayed)
        {
            if (await WriteFirstPlayedAsync(
                    appId, firstPlayed, SteamHistorySources.YearInReview, now, ownershipCache, ct))
            {
                totals.PlayRecordsWritten++;
                written++;
            }
        }

        foreach (var game in anchors.Games)
        {
            if (game.FirstPlayedUtc is { } firstPlayed
                && await WriteFirstPlayedAsync(
                    game.AppId, firstPlayed, SteamHistorySources.FirstPlayed, now, ownershipCache, ct))
            {
                totals.PlayRecordsWritten++;
                written++;
            }
        }

        scope.Commit();
        return written;
    }

    /// <summary>
    /// Writes one first-played observation, or declines.
    ///
    /// <para>The record carries the DATE. Its minutes are zero because at the
    /// instant of a first launch the cumulative counter was, to within one
    /// session, zero; guessing any other figure puts a number into the series
    /// no source ever reported.</para>
    ///
    /// <para>Refused when it would become the newest row for the ownership.
    /// <c>LibraryQueryRepository</c>'s <c>latest_play</c> CTE reads the
    /// bucket, the dormancy signal and the displayed playtime off whichever
    /// row sorts newest, so a 2019 record landing on an ownership with no
    /// newer one would make a 900-minute game read as "0 minutes, last played
    /// 2019". The ordinary sync always writes a present-day row, so in
    /// practice this declines only on an ownership the sync has not reached
    /// yet, and the next pass writes it.</para>
    /// </summary>
    private async Task<bool> WriteFirstPlayedAsync(
        string appId,
        DateTime firstPlayed,
        string source,
        DateTime now,
        Dictionary<string, long?> ownershipCache,
        CancellationToken ct)
    {
        if (firstPlayed >= now)
        {
            return false;
        }

        if (await OwnershipAsync(appId, ownershipCache, ct) is not { } ownershipId)
        {
            return false;
        }

        var latest = await _playRecords.GetLatestAsync(ownershipId, ct);
        if (latest is null || latest.ObservedAt <= firstPlayed)
        {
            return false;
        }

        return await _playRecords.TryAppendAsync(
            new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = 0,
                LastPlayedAt = firstPlayed,
                Source = source,
                ObservedAt = firstPlayed,
            },
            ct) is not null;
    }

    /// <summary>
    /// appid to ownership, via <c>external_ids</c> to the release and on to
    /// its Steam ownership. Memoised per pass, including misses: a library
    /// holds hundreds of appids Steam reports that Winnow has no row for,
    /// and each would otherwise be looked up twice.
    ///
    /// <para>Null is the ordinary answer for an appid the sync jobs never
    /// created: a delisted app, a title played on a shared account, a demo
    /// that no longer exists. This job never creates works, releases or
    /// ownerships; that is Resolve's boundary, not enrichment's
    /// (§5.1).</para>
    /// </summary>
    private async Task<long?> OwnershipAsync(
        string appId, Dictionary<string, long?> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(appId, out var cached))
        {
            return cached;
        }

        long? ownershipId = null;
        if (await _releases.FindByExternalIdAsync(ExternalIdProviders.Steam, appId, ct) is { } release)
        {
            foreach (var ownership in await _ownerships.GetByReleaseAsync(release.Id, ct))
            {
                if (string.Equals(ownership.Store, ExternalIdProviders.Steam, StringComparison.Ordinal))
                {
                    ownershipId = ownership.Id;
                    break;
                }
            }
        }

        cache[appId] = ownershipId;
        return ownershipId;
    }

    /// <summary>
    /// Marks the answered years done. Written after the import so a crash
    /// between fetch and write leaves the year pending rather than silently
    /// skipped forever.
    /// </summary>
    private async Task RecordCompletionAsync(
        SteamId steamId,
        IReadOnlyList<(int Year, int Games)> completed,
        int imported,
        Totals totals,
        CancellationToken ct)
    {
        var stamp = _clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

        foreach (var (year, games) in completed)
        {
            await _settings.SetAsync(
                YearMarker(steamId, year),
                string.Create(CultureInfo.InvariantCulture, $"{stamp};games={games};written={imported}"),
                ct);

            totals.YearsCompleted++;
        }
    }

    /// <summary>
    /// Records that Steam confirmed this account owns the configured key.
    ///
    /// <para>Persisted rather than recomputed because after the first pass
    /// only the current year is refetched, and a current year the user has
    /// not played answers empty, leaving a re-run unable to re-derive a
    /// confirmation it had already earned and quietly refusing to import
    /// for the rest of the install's life.</para>
    ///
    /// <para>The same moment also answers a question nothing else in Winnow
    /// can: <b>which of the accounts on this PC is the user's own</b>. Steam
    /// never states it — the app can only observe that a call made with the
    /// configured key answered for a particular account, which is exactly what
    /// has just happened. The account visibility toggle is disabled until this
    /// runs, because a filter that does not know whose library it is keeping
    /// would be hiding games at random.</para>
    /// </summary>
    private async Task ConfirmAccountAsync(
        SteamId steamId, bool disclosedThisPass, CancellationToken ct)
    {
        await _settings.SetAsync(
            ConfirmedKey(steamId),
            _clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ct);

        if (!disclosedThisPass)
        {
            // The marker above was earned on an earlier launch and is enough to
            // let the import proceed. It is NOT enough to name the user's
            // account: a key pasted since then has proved nothing, and the
            // reconciliation that cleared the stored reference must not be
            // undone by a marker predating the key it is about.
            return;
        }

        // Through the shared writer, stamped as key-earned so a later pass can
        // tell whether the confirmation still describes the key in force. The
        // marker above stays here: it is this job's own bookkeeping about which
        // years it has imported, not a statement about whose library this is.
        await _confirmation.ConfirmAsync(steamId, SteamAccountConfirmationSource.WebApiKey, ct);
    }

    /// <summary>What one disclosure refetch established.</summary>
    private enum DisclosureOutcome
    {
        /// <summary>No populated year to ask about, or the request did not answer.</summary>
        NothingToDiscloseFrom = 0,

        /// <summary>Steam named the account. The key in force belongs to it.</summary>
        Disclosed,

        /// <summary>Steam answered for a different account. The key is somebody else's.</summary>
        Mismatch,
    }

    /// <summary>
    /// Whether this pass should spend one read establishing which account the
    /// key belongs to.
    ///
    /// <para>Four conditions, and the point of all four is that this must be a
    /// repair and never a routine cost. It runs only when the ordinary
    /// disclosure did not happen (checked by the caller), only when the answer
    /// is actually missing, only for an account that has already proved itself
    /// once, and only when the key in force is the one that proof was earned
    /// with — or when nothing records which key that was.</para>
    ///
    /// <para>The confirmed marker is what makes this safe to attempt at all. It
    /// says a Year in Review has already answered for this account, so there is
    /// something to re-read; without it there is no reason to think a refetch
    /// would disclose anything the current year did not.</para>
    /// </summary>
    private async Task<bool> NeedsDisclosureRefetchAsync(SteamId steamId, CancellationToken ct)
    {
        if (await _confirmation.GetConfirmedAccountRefAsync(ct) is not null)
        {
            // Already known. This is the state every launch after the repair is
            // in, and it costs one settings read to establish — and it is also
            // the state a signed-in user is in from the moment the sign-in window
            // closes, which is what makes this whole repair free for them.
            return false;
        }

        if (await _settings.GetAsync(ConfirmedKey(steamId), ct) is null)
        {
            return false;
        }

        // Matches a credential in force, or nothing records which credential
        // earned the confirmation. A MISMATCH cannot occur here:
        // ISteamAccountConfirmation.ReconcileAsync runs at the top of the pass
        // and clears both halves, which is what stops a new key inheriting the
        // previous owner's identity. The check is still made rather than assumed,
        // because that ordering is the whole of the guarantee and a future caller
        // could move it.
        var recorded = await _confirmation.GetRecordedFingerprintAsync(ct);

        return recorded is null || await _confirmation.IsInForceAsync(recorded, ct);
    }

    /// <summary>
    /// Re-reads one already-imported year purely for the account id in it.
    ///
    /// <para><b>Nothing is imported.</b> The year's games are read and dropped,
    /// no anchor is fetched and no write transaction is opened, so the pass
    /// writes zero observation rows by construction rather than by relying on
    /// the full-fact identity indexes to swallow a re-import. The completion
    /// markers are not touched either: the year was already done and re-reading
    /// it does not make it more done.</para>
    ///
    /// <para><b>Which year.</b> The completion markers record <c>games=N</c> per
    /// year, so a year that will disclose can be picked rather than guessed:
    /// newest first, populated years only. The current year is excluded because
    /// the loop above has just asked about it and it did not disclose — that is
    /// the whole reason this method is running. Markers an older build wrote
    /// that cannot be parsed are kept as lower-priority candidates so they still
    /// have a path, and the attempts are bounded so a run of unanswered years
    /// cannot turn a repair into a fetch storm.</para>
    ///
    /// <para><b>Cache.</b> The ordinary 6-hour client cache is used when the
    /// stored fingerprint matches the key in force — that is the live case, an
    /// account whose ref went missing while its key never changed. When nothing
    /// records which key the cached bodies were fetched with, the read is forced
    /// fresh: a cached response fetched with a PREVIOUS key would disclose the
    /// previous account and hand back exactly the identity the fingerprint clear
    /// had just removed.</para>
    /// </summary>
    private async Task<DisclosureOutcome> DiscloseFromCompletedYearAsync(
        SteamId steamId, Totals totals, CancellationToken ct)
    {
        var candidates = await DisclosureCandidateYearsAsync(steamId, ct);
        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "No populated Year in Review is recorded for account {Account}, so which account "
                + "the API key belongs to cannot be established; the visibility toggle stays off.",
                steamId.AccountId);
            return DisclosureOutcome.NothingToDiscloseFrom;
        }

        var recorded = await _confirmation.GetRecordedFingerprintAsync(ct);
        var cacheTtl = recorded is null ? TimeSpan.Zero : (TimeSpan?)null;

        foreach (var year in candidates)
        {
            ct.ThrowIfCancellationRequested();
            totals.YearsFetched++;

            var review = await _history.GetYearInReviewAsync(steamId, year, cacheTtl: cacheTtl, ct: ct);

            if (review.AccountMismatch)
            {
                _logger.LogWarning(
                    "Steam Year in Review answered for a different account than {Account}; "
                    + "the playtime backfill is skipped for this account until the API key matches.",
                    steamId.AccountId);
                return DisclosureOutcome.Mismatch;
            }

            if (review.AccountId is not null)
            {
                _logger.LogInformation(
                    "Re-read Steam Year in Review {Year} for account {Account} to establish which "
                    + "account the API key belongs to. Nothing was imported.",
                    year, steamId.AccountId);
                return DisclosureOutcome.Disclosed;
            }

            if (!review.Answered)
            {
                totals.YearsFailed++;
            }
        }

        return DisclosureOutcome.NothingToDiscloseFrom;
    }

    /// <summary>
    /// Years worth re-reading for a disclosure, best first and bounded.
    ///
    /// <para>A marker recording <c>games=0</c> is skipped outright: an empty
    /// Replay is exactly what the current year already answered, and asking
    /// about a second one would spend a request to be told the same nothing.</para>
    /// </summary>
    private async Task<IReadOnlyList<int>> DisclosureCandidateYearsAsync(
        SteamId steamId, CancellationToken ct)
    {
        const int MaxAttempts = 3;

        var populated = new List<int>();
        var unreadable = new List<int>();

        // Newest first: the most recent year the user actually played is the one
        // most likely to still be served from cache, and the least surprising to
        // see in a log.
        for (var year = _clock.GetUtcNow().UtcDateTime.Year - 1; year >= _options.FirstYear; year--)
        {
            if (await _settings.GetAsync(YearMarker(steamId, year), ct) is not { } marker)
            {
                // Not imported, so the ordinary path will fetch it next launch
                // anyway and would disclose then. Nothing to repair here.
                continue;
            }

            switch (GamesRecordedIn(marker))
            {
                case > 0:
                    populated.Add(year);
                    break;
                case null:
                    // A marker from a build that wrote a different shape. It may
                    // still be a populated year; it is simply no longer able to
                    // say so, so it is tried only after the ones that can.
                    unreadable.Add(year);
                    break;
                default:
                    break;
            }
        }

        return [.. populated.Concat(unreadable).Take(MaxAttempts)];
    }

    /// <summary>
    /// The <c>games=N</c> figure out of a completion marker, or null when the
    /// marker does not carry one. See <see cref="RecordCompletionAsync"/> for
    /// the shape: <c>{stamp};games={N};written={M}</c>.
    /// </summary>
    internal static int? GamesRecordedIn(string marker)
    {
        foreach (var field in marker.Split(';', StringSplitOptions.TrimEntries))
        {
            if (field.StartsWith("games=", StringComparison.Ordinal)
                && int.TryParse(
                    field.AsSpan("games=".Length),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var games))
            {
                return games;
            }
        }

        return null;
    }

    /// <summary>
    /// The completion markers this class writes are untouched by
    /// reconciliation. They record that a year was imported for an account,
    /// which stays true whoever's credential is configured now, and re-importing
    /// already-imported history is the waste this class exists to avoid.
    /// </summary>
    private static string YearMarker(SteamId steamId, int year)
        => string.Create(CultureInfo.InvariantCulture, $"{YearMarkerPrefix}{steamId.Value}.{year}");

    private static string ConfirmedKey(SteamId steamId)
        => string.Create(CultureInfo.InvariantCulture, $"{ConfirmedPrefix}{steamId.Value}.confirmed");

    private static List<SteamMonthlyPlaytime> Bucket(
        Dictionary<string, List<SteamMonthlyPlaytime>> months, string appId)
    {
        if (!months.TryGetValue(appId, out var bucket))
        {
            bucket = [];
            months[appId] = bucket;
        }

        return bucket;
    }

    /// <summary>Mutable accumulator for the report; one instance per pass.</summary>
    private sealed class Totals
    {
        public int Accounts { get; init; }

        public int YearsFetched { get; set; }

        /// <summary>Years a completion marker was actually written for.</summary>
        public int YearsCompleted { get; set; }

        public int YearsFailed { get; set; }

        public int GamesReconstructed { get; set; }

        public int SnapshotsWritten { get; set; }

        public int PlayRecordsWritten { get; set; }

        public int SkippedNoOwnership { get; set; }

        public int SkippedNoAnchor { get; set; }

        public int Clamped { get; set; }

        public SteamPlaytimeBackfillReport ToReport(TimeSpan elapsed)
            => new(
                Accounts,
                YearsFetched,
                YearsCompleted,
                YearsFailed,
                GamesReconstructed,
                SnapshotsWritten,
                PlayRecordsWritten,
                SkippedNoOwnership,
                SkippedNoAnchor,
                Clamped,
                elapsed);
    }
}
