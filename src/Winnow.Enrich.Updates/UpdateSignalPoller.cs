using System.Text;
using Winnow.Core.Domain;
using Winnow.Enrich.Updates.Model;
using Winnow.Enrich.Updates.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Updates;

/// <summary>
/// Polls for update signals (news + build pushes) and writes raw rows into
/// <c>update_events</c>. Skips never-opened/retired games and staggers eligible
/// apps across daily slots. Each source is polled independently. Failures
/// degrade to "no signal this pass", never blocking a user-facing path.
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

    /// <summary>Polls one day's worth of due apps. Idempotent within a day -- schedule state lives in the database.</summary>
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
            // Oldest attempts lead, even when newer attempts failed or are on
            // watch. No title can hold the front of a capped batch forever.
            .OrderBy(candidate => Lookup(states, candidate.AppId)?.LastPolledAt ?? DateTime.MinValue)
            .ThenByDescending(candidate => IsWatching(Lookup(states, candidate.AppId), now))
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

        var failuresBefore = tally.Failures;
        try
        {
            var fetch = await _news.GetLatestPatchNoteAsync(candidate.AppId, ct);
            if (!fetch.ServedFromCache)
                tally.NewsRequests++;

            switch (fetch.Outcome)
            {
                case NewsOutcome.Unavailable:
                    tally.Failures++;
                    break;
                case NewsOutcome.NoFeed:
                    tally.NoFeed++;
                    state = state with { WatchUntil = null };
                    break;
                case NewsOutcome.NoItems:
                    state = state with { WatchUntil = null };
                    break;
                default:
                    var item = fetch.Item!;
                    if (state.IsNewsSince(item.PublishedAt, item.Gid)
                        && (!state.IsBaseline || _options.EmitOnBaseline)
                        && await WriteAsync(candidate, UpdateEventKinds.Announcement,
                            item.PublishedAt, item.Title, item.Url, null, item.RawJson, ct))
                    {
                        tally.AnnouncementsRecorded++;
                    }
                    state = state with { LastNewsGid = item.Gid, LastNewsDate = item.PublishedAt };
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            tally.Failures++;
            _log.LogWarning(ex, "Announcement poll failed for appid {AppId}; build polling continues.", candidate.AppId);
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            // Raw build history is useful even without a feed or a new patch
            // note. The typed client's cache and rate policy bound its cost.
            state = await ConfirmBuildAsync(candidate, state, state.LastNewsDate, now, tally, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            tally.Failures++;
            _log.LogWarning(ex, "Build poll failed for appid {AppId}; other titles continue.", candidate.AppId);
        }

        // Attempts consume a turn even on failure. Keeping their signal marks
        // intact permits retry without letting a broken title monopolize a cap.
        await _state.SetAsync(candidate.AppId,
            state with { RetryPending = tally.Failures > failuresBefore }, now, ct);
    }
    /// <summary>One steamcmd.net call, plus the decision about whether to keep watching.</summary>
    private async Task<UpdatePollState> ConfirmBuildAsync(
        PollCandidate candidate,
        UpdatePollState state,
        DateTime? announcedAt,
        DateTime now,
        Tally tally,
        CancellationToken ct)
    {
        var watchDeadline = announcedAt?.AddDays(_options.CorrelationWindowDays) ?? now;
        var fetch = await _builds.GetPublicBranchAsync(candidate.AppId, ct: ct);
        if (!fetch.ServedFromCache)
        {
            tally.BuildInfoRequests++;
        }

        switch (fetch.Outcome)
        {
            case BuildInfoOutcome.Unavailable:
                tally.Failures++;
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

        if (announcedAt is null)
            return next with { WatchUntil = null };

        // Correlated: the push is within ±CorrelationWindowDays of the
        // announcement, so the bucket query will now find the pair. Nothing left
        // to wait for.
        var separation = (branch.UpdatedAt - announcedAt.Value).Duration();
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

    /// <summary>Whether an app is due today: not yet polled today, and either on the watch list, in today's slot, or overdue.</summary>
    private bool IsDue(string appId, UpdatePollState? state, DateTime now)
    {
        if (state?.LastPolledAt is { } lastPolled)
        {
            if (lastPolled.Date >= now.Date)
            {
                return false;
            }

            if (state.RetryPending || IsWatching(state, now))
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

    /// <summary>Stable slot assignment for an appid via FNV-1a (not string.GetHashCode, which is randomised per process).</summary>
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

    /// <summary>Today's slot, counted in whole days since Unix epoch mod the sweep period.</summary>
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
