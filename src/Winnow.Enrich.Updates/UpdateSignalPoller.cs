using System.Text;
using Winnow.Core.Domain;
using Winnow.Enrich.Updates.Model;
using Winnow.Enrich.Updates.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Updates;

/// <summary>
/// M2's update-signal poller: the thing that finally makes "Patched since"
/// non-zero.
///
/// <para>It writes both of §4.5's raw signals into <c>update_events</c> and
/// stops there. It never decides what a "major update" is — that correlation
/// lives in <c>LibraryQueryRepository</c>'s bucket query, at read time, precisely
/// so the heuristic can be retuned without re-fetching. Feeding it correct raw
/// rows is this class's entire job.</para>
///
/// <para><b>The schedule is the design.</b> A naive full poll of a 616-game
/// library is 1,232 requests and ~20 minutes; the spike's three rules bring that
/// to roughly 63 requests a day:</para>
///
/// <list type="number">
/// <item><b>Eliminate.</b> Never-opened games can never show the badge
/// (design-system §5.2), and retired ones are outranked in the bucket query, so
/// neither is polled. On a typical large Steam library that is ~40% of it gone
/// before a single request. See <see cref="IPollCandidateSource"/>.</item>
/// <item><b>Cascade.</b> Announcements are rare; depot pushes are constant — Dota
/// 2 and Elden Ring both carry fresh pushes with no patch note behind them. So
/// the cheap keyless Valve signal sweeps wide, and the ~12 KB volunteer service
/// is touched only when a new patch note actually appears. Because
/// <c>timeupdated</c> is a persistent point-in-time value, that one call usually
/// also sees a build that landed days <i>earlier</i>, resolving the pair
/// immediately.</item>
/// <item><b>Stagger.</b> Each app is pinned to one of
/// <see cref="UpdateSignalOptions.SweepPeriodDays"/> slots by a stable hash of
/// its appid, so a day's batch is about a seventh of the eligible set. Up to a
/// week of detection latency is irrelevant for a badge about a game last played
/// six months ago.</item>
/// </list>
///
/// <para>Nothing here may block a user-facing path (§5.1). Every failure mode —
/// a dead network, a 403, steamcmd.net going dark as §4.5 saw it do — degrades
/// to "no signal this pass" and leaves the app due next time.</para>
/// </summary>
public sealed class UpdateSignalPoller
{
    private readonly ISteamNewsClient _news;
    private readonly IBuildInfoClient _builds;
    private readonly IPollCandidateSource _candidates;
    private readonly IUpdatePollStateStore _state;
    private readonly IUpdateEventWriter _events;
    private readonly UpdateSignalOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<UpdateSignalPoller> _log;

    public UpdateSignalPoller(
        ISteamNewsClient news,
        IBuildInfoClient builds,
        IPollCandidateSource candidates,
        IUpdatePollStateStore state,
        IUpdateEventWriter events,
        UpdateSignalOptions options,
        TimeProvider clock,
        ILogger<UpdateSignalPoller> log)
    {
        _news = news;
        _builds = builds;
        _candidates = candidates;
        _state = state;
        _events = events;
        _options = options;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Polls one day's worth of due apps and returns what it did.
    ///
    /// <para>Safe and cheap to call more than once a day: an app polled today is
    /// not due again today, so a second call in the same session is a no-op that
    /// costs one query and no requests. That, rather than a timer this class
    /// owns, is what makes the schedule survive restarts — all the state lives
    /// in the database, so "call this once per background session" is a correct
    /// integration whether the app runs daily or twice an hour.</para>
    /// </summary>
    public async Task<UpdatePollReport> PollDueBatchAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        var eligible = await _candidates.GetEligibleAsync(_options.RetiredFloorMinutes, ct);
        if (eligible.Count == 0)
        {
            return new UpdatePollReport();
        }

        var states = await _state.GetManyAsync(eligible.Select(c => c.AppId), ct);

        var due = eligible
            .Where(candidate => IsDue(candidate.AppId, Lookup(states, candidate.AppId), now))
            // Watch-list apps first — they are mid-correlation and a window is
            // closing on them — then longest-unpolled. Never-polled apps sort to
            // the front of the second group, so a truncated batch spends its
            // budget on the apps with the least information, not the most.
            .OrderByDescending(candidate => IsWatching(Lookup(states, candidate.AppId), now))
            .ThenBy(candidate => Lookup(states, candidate.AppId)?.LastPolledAt ?? DateTime.MinValue)
            .ThenBy(candidate => candidate.ReleaseId)
            .ToArray();

        var batch = due.Take(Math.Max(1, _options.MaxAppsPerBatch)).ToArray();

        var report = new Tally { Eligible = eligible.Count, Due = due.Length };

        foreach (var candidate in batch)
        {
            ct.ThrowIfCancellationRequested();
            await PollOneAsync(candidate, Lookup(states, candidate.AppId), now, report, ct);
        }

        if (due.Length > batch.Length)
        {
            _log.LogInformation(
                "Update poll capped at {Cap} of {Due} due apps; the remaining {Remaining} lead the next batch.",
                batch.Length, due.Length, due.Length - batch.Length);
        }

        _log.LogInformation(
            "Update poll: {Polled}/{Eligible} apps, {NewsRequests} news + {BuildRequests} build requests, "
            + "{Announcements} announcements + {Pushes} build pushes recorded, "
            + "{NoFeed} without a feed, {Watching} on watch, {Failures} unanswered.",
            report.Polled, report.Eligible, report.NewsRequests, report.BuildInfoRequests,
            report.AnnouncementsRecorded, report.BuildPushesRecorded,
            report.NoFeed, report.Watching, report.Failures);

        return report.ToReport();
    }

    // ── One app ─────────────────────────────────────────────────────────────

    private async Task PollOneAsync(
        PollCandidate candidate, UpdatePollState? known, DateTime now, Tally tally, CancellationToken ct)
    {
        var state = known ?? new UpdatePollState();
        tally.Polled++;

        var fetch = await _news.GetLatestPatchNoteAsync(candidate.AppId, ct);

        // Counts wire traffic, not method calls: a live no-feed negative is
        // answered from cache and costs nothing, which is the entire point of
        // caching it.
        if (!fetch.ServedFromCache)
        {
            tally.NewsRequests++;
        }

        switch (fetch.Outcome)
        {
            case NewsOutcome.Unavailable:
                // Nothing learned. Deliberately NOT stamping last-polled: the app
                // stays due so a transient outage does not cost it a whole sweep
                // period of invisibility.
                tally.Failures++;
                return;

            case NewsOutcome.NoFeed:
                // A fact about this appid, already cached by the client for
                // NoNewsFeedRetryAfter. Stamped as polled so the stagger stops
                // scheduling it every slot; the cache is what makes it free, and
                // the stamp is what keeps it out of the ordering.
                tally.NoFeed++;
                await _state.SetAsync(candidate.AppId, state with { WatchUntil = null }, now, ct);
                return;

            case NewsOutcome.NoItems:
                // The app has a feed and nothing in it is tagged patchnotes.
                // A real answer: stamp it and move on.
                await _state.SetAsync(candidate.AppId, state with { WatchUntil = null }, now, ct);
                return;
        }

        var item = fetch.Item!;

        var isNews = state.IsNewsSince(item.PublishedAt, item.Gid);
        var isBaseline = state.IsBaseline;

        var next = state with { LastNewsGid = item.Gid, LastNewsDate = item.PublishedAt };

        if (!isNews)
        {
            // Same newest item as last time. This is the overwhelmingly common
            // outcome — one cheap request, no writes, no cascade — and it is
            // what the whole cost model rests on.
            //
            // The watch list is still honoured below, because "no new
            // announcement" is exactly the state an app sits in while waiting
            // for the build that its last announcement promised.
            next = await ResolveWatchAsync(candidate, next, now, tally, ct);
            await _state.SetAsync(candidate.AppId, next, now, ct);
            return;
        }

        if (!isBaseline || _options.EmitOnBaseline)
        {
            if (await WriteAsync(candidate, UpdateEventKinds.Announcement, item.PublishedAt, item.Title, item.Url, buildId: null, item.RawJson, ct))
            {
                tally.AnnouncementsRecorded++;
            }
        }

        // Cascade. The gate is deliberately narrow: `timeupdated` is the app's
        // LATEST push, so against a patch note from 2019 it cannot correlate no
        // matter what it says, and the call would spend the volunteer service's
        // bandwidth to confirm a foregone "no".
        var age = now - item.PublishedAt;
        if (age > TimeSpan.FromDays(_options.CascadeMaxAnnouncementAgeDays))
        {
            _log.LogDebug(
                "Appid {AppId}: newest patch note is {Age:N0} days old — too old to correlate; skipping steamcmd.net.",
                candidate.AppId, age.TotalDays);
            await _state.SetAsync(candidate.AppId, next with { WatchUntil = null }, now, ct);
            return;
        }

        next = await ConfirmBuildAsync(candidate, next, item.PublishedAt, now, tally, ct);
        await _state.SetAsync(candidate.AppId, next, now, ct);
    }

    /// <summary>
    /// The daily re-check for an app whose announcement is still waiting on its
    /// build push — the Stardew Valley case, where the build landed two days
    /// after the post.
    /// </summary>
    private async Task<UpdatePollState> ResolveWatchAsync(
        PollCandidate candidate, UpdatePollState state, DateTime now, Tally tally, CancellationToken ct)
    {
        if (!IsWatching(state, now) || state.LastNewsDate is not { } announcedAt)
        {
            return state with { WatchUntil = null };
        }

        return await ConfirmBuildAsync(candidate, state, announcedAt, now, tally, ct);
    }

    /// <summary>
    /// One steamcmd.net call, plus the decision about whether to keep watching.
    /// </summary>
    private async Task<UpdatePollState> ConfirmBuildAsync(
        PollCandidate candidate,
        UpdatePollState state,
        DateTime announcedAt,
        DateTime now,
        Tally tally,
        CancellationToken ct)
    {
        var watchDeadline = announcedAt.AddDays(_options.CorrelationWindowDays);
        var fetch = await _builds.GetPublicBranchAsync(candidate.AppId, ct: ct);
        if (!fetch.ServedFromCache)
        {
            tally.BuildInfoRequests++;
        }

        switch (fetch.Outcome)
        {
            case BuildInfoOutcome.Unavailable:
                // §4.5 watched this service go dark. Degrade to "no build
                // signal" and keep watching until the window closes, so an
                // outage does not silently drop a correlation that was about to
                // complete.
                return KeepWatching(state, watchDeadline, now, tally);

            case BuildInfoOutcome.NoData:
                // The service answered and has nothing for this appid — a
                // delisted or never-mirrored app. No amount of re-asking changes
                // that, so stop watching.
                return state with { WatchUntil = null };
        }

        var branch = fetch.Branch!;

        // Written whether or not it correlates. §4.5 is explicit that BOTH raw
        // signals are stored so the heuristic can be retuned without re-fetching,
        // and pitfall 4 is about how the signal is READ, not whether it is kept.
        // Suppressing uncorrelated pushes here would hard-code today's window
        // into the data and make widening it later require a full re-fetch.
        if (state.LastBuildTimeUpdated != branch.UpdatedAt)
        {
            if (await WriteAsync(candidate, UpdateEventKinds.BuildPush, branch.UpdatedAt, title: null, url: null, branch.BuildId, branch.RawJson, ct))
            {
                tally.BuildPushesRecorded++;
            }
        }

        var next = state with { LastBuildTimeUpdated = branch.UpdatedAt };

        // Correlated: the push is within ±CorrelationWindowDays of the
        // announcement, so the bucket query will now find the pair. Nothing left
        // to wait for.
        var separation = (branch.UpdatedAt - announcedAt).Duration();
        if (separation <= TimeSpan.FromDays(_options.CorrelationWindowDays))
        {
            _log.LogDebug(
                "Appid {AppId}: build push and announcement correlate ({Separation:N1} days apart).",
                candidate.AppId, separation.TotalDays);
            return next with { WatchUntil = null };
        }

        // The newest push predates the announcement by more than the window: the
        // promised build has not landed yet. Keep looking until the window
        // closes — this is the case a single pass cannot resolve.
        if (branch.UpdatedAt < announcedAt)
        {
            return KeepWatching(next, watchDeadline, now, tally);
        }

        // A push newer than the announcement by more than the window. Dota 2 and
        // Elden Ring both look like this; the pair will never correlate and
        // waiting changes nothing.
        return next with { WatchUntil = null };
    }

    private UpdatePollState KeepWatching(
        UpdatePollState state, DateTime deadline, DateTime now, Tally tally)
    {
        if (deadline <= now)
        {
            return state with { WatchUntil = null };
        }

        tally.Watching++;
        return state with { WatchUntil = deadline };
    }

    private async Task<bool> WriteAsync(
        PollCandidate candidate,
        string kind,
        DateTime occurredAt,
        string? title,
        string? url,
        string? buildId,
        string? rawJson,
        CancellationToken ct)
        => await _events.UpsertAsync(
            new UpdateEvent
            {
                ReleaseId = candidate.ReleaseId,
                Kind = kind,
                BuildId = buildId,
                // Truncated to whole seconds because that is the resolution the
                // schema stores and the identity index compares. Rounding here
                // rather than letting the driver do it keeps the value this code
                // reasons about identical to the one on disk.
                OccurredAt = new DateTime(
                    occurredAt.Ticks - (occurredAt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
                Title = title,
                Url = url,
                RawJson = rawJson,
            },
            ct);

    // ── Schedule ────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether an app is due today. Four rules, in order:
    ///
    /// <list type="bullet">
    /// <item>Polled already today → not due. This is what makes
    /// <c>PollDueBatchAsync</c> safe to call repeatedly, and what makes the
    /// stagger survive a restart: the answer comes from the database, not from
    /// anything held in memory.</item>
    /// <item>On the watch list with the window still open → due daily.</item>
    /// <item>Its slot matches today → due.</item>
    /// <item>Overdue by <see cref="UpdateSignalOptions.CatchUpAfter"/> → due,
    /// whatever its slot. Covers a machine switched off through its slot day and
    /// a batch truncated by the cap.</item>
    /// </list>
    /// </summary>
    private bool IsDue(string appId, UpdatePollState? state, DateTime now)
    {
        if (state?.LastPolledAt is { } lastPolled)
        {
            if (lastPolled.Date >= now.Date)
            {
                return false;
            }

            if (IsWatching(state, now))
            {
                return true;
            }

            if (now - lastPolled >= _options.CatchUpAfter)
            {
                return true;
            }
        }

        return Slot(appId, _options.SweepPeriodDays) == TodaySlot(now, _options.SweepPeriodDays);
    }

    private static bool IsWatching(UpdatePollState? state, DateTime now)
        => state?.WatchUntil is { } until && until > now;

    /// <summary>
    /// Which of the sweep's slots an appid belongs to.
    ///
    /// <para>FNV-1a over the appid's bytes, not <see cref="string.GetHashCode()"/>:
    /// .NET randomises string hashing per process, so the built-in hash would
    /// reshuffle every app into a different slot on every launch and destroy the
    /// staggering it is supposed to provide.</para>
    /// </summary>
    public static int Slot(string appId, int sweepPeriodDays)
    {
        var period = Math.Max(1, sweepPeriodDays);

        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(appId))
        {
            hash ^= b;
            hash *= prime;
        }

        return (int)(hash % (uint)period);
    }

    /// <summary>
    /// Today's slot, counted in whole days since the Unix epoch so it advances
    /// once per UTC day on every machine and never depends on when the app
    /// happens to be running.
    /// </summary>
    public static int TodaySlot(DateTime now, int sweepPeriodDays)
    {
        var period = Math.Max(1, sweepPeriodDays);
        var days = (now.Date - DateTime.UnixEpoch.Date).Days;
        return (int)(((days % period) + period) % period);
    }

    private static UpdatePollState? Lookup(IReadOnlyDictionary<string, UpdatePollState> states, string appId)
        => states.TryGetValue(appId, out var state) ? state : null;

    /// <summary>Mutable accumulator behind the immutable <see cref="UpdatePollReport"/>.</summary>
    private sealed class Tally
    {
        public int Eligible { get; init; }

        public int Due { get; init; }

        public int Polled { get; set; }

        public int NewsRequests { get; set; }

        public int BuildInfoRequests { get; set; }

        public int AnnouncementsRecorded { get; set; }

        public int BuildPushesRecorded { get; set; }

        public int NoFeed { get; set; }

        public int Watching { get; set; }

        public int Failures { get; set; }

        public UpdatePollReport ToReport() => new()
        {
            Eligible = Eligible,
            Due = Due,
            Polled = Polled,
            NewsRequests = NewsRequests,
            BuildInfoRequests = BuildInfoRequests,
            AnnouncementsRecorded = AnnouncementsRecorded,
            BuildPushesRecorded = BuildPushesRecorded,
            NoFeed = NoFeed,
            Watching = Watching,
            Failures = Failures,
        };
    }
}
