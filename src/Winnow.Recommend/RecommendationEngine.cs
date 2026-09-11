using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Recommend;

/// <summary>
/// Assembles <see cref="CandidateFacts"/> from repositories and hands them to
/// <see cref="RecommendationScorer"/>. Bulk reads cover Tier-0 signals; per-game
/// history is read for the score-bound-safe shortlist, and the maturity tier is
/// measured separately over the whole library.
///
/// <para>Those three passes read different rows on purpose. The shortlist is
/// the rows worth explaining, and the tier is a claim about the library, so
/// neither may stand in for the other. <see cref="HistoryReader"/> memoises
/// per-ownership reads for the life of one request, which is what lets the
/// passes overlap without paying twice.</para>
/// </summary>
public sealed class RecommendationEngine : IRecommendationEngine
{
    private readonly ILibraryQueryRepository _library;
    private readonly IPlaytimeSnapshotRepository _snapshots;
    private readonly ISessionRepository _sessions;
    private readonly IUpdateEventRepository _updateEvents;
    private readonly IFacetRepository _facets;
    private readonly ILibraryHistoryStatsRepository? _historyStats;

    public RecommendationEngine(
        ILibraryQueryRepository library,
        IPlaytimeSnapshotRepository snapshots,
        ISessionRepository sessions,
        IUpdateEventRepository updateEvents,
        IFacetRepository facets,
        ILibraryHistoryStatsRepository? historyStats = null)
    {
        _library = library;
        _snapshots = snapshots;
        _sessions = sessions;
        _updateEvents = updateEvents;
        _facets = facets;
        _historyStats = historyStats;
    }

    /// <summary>Everything both entry points share: the assembled pool and the request's derived seed.</summary>
    private sealed record CandidatePool(
        List<CandidateFacts> Candidates,
        IReadOnlyList<Core.Queries.OwnershipBucket> BucketRows,
        int Seed,
        List<Recommendation> Derelict);

    public async Task<RecommendationFeed> GetFeedAsync(
        RecommendationRequest request, CancellationToken ct = default)
    {
        var tuning = request.Tuning;
        var (candidates, bucketRows, seed, _) = await AssemblePoolAsync(request, ct);
        var history = new HistoryReader(_snapshots, _sessions);

        IReadOnlyList<SignalContribution> Score(CandidateFacts facts)
            => RecommendationScorer.Score(facts, request.Thresholds, tuning, request.AsOfUtc, seed);

        // ── Collapse, then rank, then prune ────────────────────────────────
        // Order matters. Two store copies of one game are ONE recommendation,
        // so the second copy must never consume shortlist capacity a distinct
        // work needed (F38) — the collapse happens before any capacity is
        // spent, and keeps the copy with the highest upper bound so it cannot
        // discard the one that would have won.
        var preliminary = candidates
            .Select(facts => Preliminary(facts, Score))
            .ToList();
        var works = ScoreBounds.CollapseByWork(preliminary, tuning);

        var comfort = Math.Min(
            Math.Max(request.MaxResults, request.MaxResults * 3),
            tuning.HistoryProbeLimit);
        var shortlist = ScoreBounds.SafeShortlist(
            works, tuning, request.AsOfUtc, request.MaxResults, comfort);

        // ── Final scoring over history-enriched facts ──────────────────────
        var scored = new List<ScoredCandidate>(shortlist.Count);
        foreach (var candidate in shortlist)
        {
            var enriched = await EnrichAsync(candidate.Facts, request, history, ct);
            var signals = Score(enriched);
            scored.Add(new ScoredCandidate(enriched, signals, RecommendationScorer.Total(signals)));
        }

        // One ledger per flat feed, same as one ledger per shelf. The flat
        // list is what the user reads in a single scroll, so it is the unit
        // over which a phrasing must not repeat.
        var flatLedger = new ShelfReasonLedger(
            ShelfReasonLedger.CapFor(request.MaxResults, tuning));
        var items = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Facts.ReleaseId)
            .Take(request.MaxResults)
            .Select(s => Present(s, request, flatLedger))
            .ToList();

        return new RecommendationFeed
        {
            Items = items,
            Tier = await DetectTierAsync(bucketRows, tuning, history, ct),
            CandidateCount = candidates.Count,
            WorkCount = works.Count,
            HistoryProbeCount = shortlist.Count,
        };
    }

    public async Task<ShelfFeed> GetShelvesAsync(
        RecommendationRequest request, CancellationToken ct = default)
    {
        var tuning = request.Tuning;
        var (candidates, bucketRows, seed, derelict) = await AssemblePoolAsync(request, ct);
        var history = new HistoryReader(_snapshots, _sessions);

        IReadOnlyList<SignalContribution> Score(CandidateFacts facts)
            => RecommendationScorer.Score(facts, request.Thresholds, tuning, request.AsOfUtc, seed);

        // Same order as the flat feed, and for the same reason: each shelf
        // holds one entry per WORK, so a duplicate ownership must not occupy a
        // slot in a shelf's shortlist either.
        var preliminary = candidates
            .Select(facts => Preliminary(facts, Score))
            .ToList();
        var works = ScoreBounds.CollapseByWork(preliminary, tuning);

        var definitions = ShelfBuilder.Definitions(request.Thresholds, tuning);
        var perShelf = Math.Max(request.MaxPerShelf,
            request.MaxPerShelf * Math.Max(1, tuning.ShelfOverfetchFactor));

        // Each shelf keeps its own score-bound-safe slice: a candidate is
        // dropped only when its upper bound cannot reach the lower bound of
        // the shelf's last visible entry, so no history outcome could place it.
        var shortlists = new List<IReadOnlyList<ScoredCandidate>>(definitions.Count);
        foreach (var definition in definitions)
        {
            var eligible = works
                .Where(s => ShelfBuilder.IsEligible(definition, s.Facts, s.Signals))
                .ToList();

            shortlists.Add(ScoreBounds.SafeShortlist(
                eligible, tuning, request.AsOfUtc, request.MaxPerShelf, perShelf));
        }

        var union = ProbeUnion(shortlists, tuning.ShelfProbeLimit);

        var scored = new List<ScoredCandidate>(union.Count);
        foreach (var candidate in union)
        {
            var enriched = await EnrichAsync(candidate.Facts, request, history, ct);
            var signals = Score(enriched);
            scored.Add(new ScoredCandidate(enriched, signals, RecommendationScorer.Total(signals)));
        }

        var shelves = ShelfBuilder.Build(definitions, scored, request, request.MaxPerShelf).ToList();
        if (derelict.Count > 0)
        {
            shelves.Add(new RecommendationShelf
            {
                Id = ShelfIds.Derelict,
                Title = "Derelict",
                Blurb = "Games with evidence of closure, delisting or abandonment; some may still be playable.",
                Items = derelict
                    .OrderBy(item => request.RecentlySurfacedReleaseIds.Contains(item.ReleaseId))
                    .ThenBy(item => RecommendationScorer.JitterValue(seed, item.ReleaseId))
                    .ThenBy(item => item.ReleaseId)
                    .Take(Math.Max(1, request.MaxPerShelf))
                    .ToList(),
            });
        }

        return new ShelfFeed
        {
            Shelves = shelves,
            Tier = await DetectTierAsync(bucketRows, tuning, history, ct),
            CandidateCount = candidates.Count,
            WorkCount = works.Count,
            HistoryProbeCount = union.Count,
        };
    }

    /// <summary>
    /// Interleaves the per-shelf shortlists rank by rank: every shelf's best
    /// candidate is admitted before any shelf's second best, and so on until
    /// the probe limit is reached. Duplicates are dropped by ownership id,
    /// since one ownership can appear on several shelves and is only ever
    /// probed once.
    ///
    /// <para>The previous implementation filled the union shelf by shelf in
    /// claim order and stopped when the budget ran out. Any flat cap applied
    /// in claim order has the same failure mode: it deletes whole later
    /// shelves instead of trimming each shelf's tail. On the real library
    /// (990 candidates, measured 2026-09-01) the first two shelves consumed
    /// the entire budget of 150 and the last three were never scored. Round-
    /// robin makes a binding budget trim every shelf's tail evenly; it
    /// cannot zero out a shelf. Measured: with round-robin, all five shelves
    /// populate even at a budget of 5.</para>
    /// </summary>
    internal static List<ScoredCandidate> ProbeUnion(
        IReadOnlyList<IReadOnlyList<ScoredCandidate>> shortlists, int probeLimit)
    {
        var union = new List<ScoredCandidate>();
        if (probeLimit <= 0 || shortlists.Count == 0)
        {
            return union;
        }

        var seen = new HashSet<long>();
        var depth = 0;
        foreach (var shortlist in shortlists)
        {
            depth = Math.Max(depth, shortlist.Count);
        }

        for (var rank = 0; rank < depth && union.Count < probeLimit; rank++)
        {
            foreach (var shortlist in shortlists)
            {
                if (union.Count >= probeLimit)
                {
                    break;
                }

                if (rank < shortlist.Count && seen.Add(shortlist[rank].Facts.OwnershipId))
                {
                    union.Add(shortlist[rank]);
                }
            }
        }

        return union;
    }

    private static ScoredCandidate Preliminary(
        CandidateFacts facts, Func<CandidateFacts, IReadOnlyList<SignalContribution>> score)
    {
        var signals = score(facts);
        return new ScoredCandidate(facts, signals, RecommendationScorer.Total(signals));
    }

    /// <summary>One scored candidate as the caller sees it — score, structured reason, rendered sentence.</summary>
    internal static Recommendation Present(
        ScoredCandidate candidate,
        RecommendationRequest request,
        ShelfReasonLedger? ledger = null)
    {
        var explanation = RecommendationScorer.Explain(
            candidate.Facts, request.Thresholds, request.Tuning, request.AsOfUtc, candidate.Signals);

        return new Recommendation
        {
            OwnershipId = candidate.Facts.OwnershipId,
            ReleaseId = candidate.Facts.ReleaseId,
            WorkId = candidate.Facts.WorkId,
            Title = candidate.Facts.Title,
            Store = candidate.Facts.Store,
            Bucket = candidate.Facts.Bucket,
            Score = candidate.Score,
            Reason = ReasonBuilder.Build(explanation, request.Tuning, ledger),
            Explanation = explanation,
            Signals = candidate.Signals,
        };
    }

    /// <summary>Bulk reads, taste profile, hard exclusions, and one <see cref="CandidateFacts"/> per surviving ownership.</summary>
    private async Task<CandidatePool> AssemblePoolAsync(
        RecommendationRequest request, CancellationToken ct)
    {
        var tuning = request.Tuning;
        var seed = request.ShuffleSeed
            ?? DateOnly.FromDateTime(request.AsOfUtc).DayNumber;

        var snapshot = await _library.GetSnapshotAsync(request.Thresholds, ct);
        var bucketRows = snapshot.Buckets;
        var facetSnapshot = await _facets.GetSnapshotAsync(ct);
        var games = RecommendationGame.Build(snapshot, facetSnapshot);
        var taste = TasteProfile.Build(games, facetSnapshot, request.Thresholds, tuning, request.EndorsedReleaseIds);
        var excludedWorks = RecommendationGame.ResolveFeedback(snapshot,
            request.NotInterestedReleaseIds.Concat(request.SnoozedReleaseIds));
        var surfacedWorks = RecommendationGame.ResolveFeedback(snapshot, request.RecentlySurfacedReleaseIds);

        var candidates = new List<CandidateFacts>(games.Count);
        var derelict = new List<Recommendation>();
        foreach (var game in games)
        {
            var row = game.Action;
            if (row.Game.Bucket == LibraryBuckets.Retired || game.NameIsProvisional
                || excludedWorks.Contains(game.WorkId)) continue;

            if (row.Game.Bucket == LibraryBuckets.Derelict)
            {
                var explanation = new RecommendationReason
                {
                    Primary = ReasonSignal.Lifecycle,
                    Evidence = new ReasonEvidence
                    {
                        ReleaseId = row.ReleaseId,
                        Title = game.Title,
                        Store = game.Store,
                        Lifecycle = row.Game.Lifecycle,
                        EvidenceReleaseIds = game.ReleaseIds,
                    },
                };
                derelict.Add(new Recommendation
                {
                    OwnershipId = row.OwnershipId, ReleaseId = row.ReleaseId, WorkId = game.WorkId,
                    Title = game.Title, Store = game.Store, Bucket = LibraryBuckets.Derelict,
                    Score = 0, Signals = [], Explanation = explanation,
                    Reason = ReasonBuilder.Build(explanation, tuning),
                });
                continue;
            }

            var (affinity, facetName) = taste.AffinityFor(row.ReleaseId);
            candidates.Add(new CandidateFacts
            {
                OwnershipId = row.OwnershipId,
                ReleaseId = row.ReleaseId,
                WorkId = game.WorkId,
                Title = game.Title,
                Store = game.Store,
                EvidenceOwnershipIds = game.OwnershipIds,
                EvidenceReleaseIds = game.ReleaseIds,
                Bucket = row.Game.Bucket,
                PlaytimeMinutes = row.Game.PlaytimeMinutes,
                LastPlayedAt = row.Game.LastPlayedAt,
                Installed = game.Installed,
                StoreCount = game.StoreCount,
                TasteAffinity = affinity,
                TasteFacetName = facetName,
                RecentlySurfaced = surfacedWorks.Contains(game.WorkId),
                ModeMismatch = taste.ClassifyModes(row.ReleaseId, tuning.ModeEvidenceMinGames, tuning.ModeDominanceShare),
                GenreFacetIds = game.Facets.FacetIds.Where(id => facetSnapshot.ById.TryGetValue(id, out var facet)
                    && facet.Kind == FacetKinds.Genre).ToArray(),
            });
        }
        return new CandidatePool(candidates, bucketRows, seed, derelict);
    }
    /// <summary>
    /// Reads one shortlisted row's own history: return episodes, and — where a
    /// negative claim depends on it — whether Winnow has ever observed this
    /// release's update history at all.
    /// </summary>
    private async Task<CandidateFacts> EnrichAsync(
        CandidateFacts facts,
        RecommendationRequest request,
        HistoryReader history,
        CancellationToken ct)
    {
        var ownershipIds = facts.EvidenceOwnershipIds.Count > 0 ? facts.EvidenceOwnershipIds : [facts.OwnershipId];
        var enriched = facts with { ReturnEpisodes = await history.EpisodesAsync(ownershipIds, ct) };

        var patched = facts.Bucket == LibraryBuckets.StaleButPatched;
        var maybeDone = RecommendationScorer.HasProbablyDoneShape(
            facts, request.Tuning, request.AsOfUtc);

        if (!patched && !maybeDone)
        {
            return enriched;
        }

        var releaseIds = facts.EvidenceReleaseIds.Count > 0 ? facts.EvidenceReleaseIds : [facts.ReleaseId];
        var observed = true;
        var updateCount = 0;
        Core.Domain.UpdateEvent? latestNews = null;
        foreach (var releaseId in releaseIds.Distinct())
        {
            var events = (await _updateEvents.GetByReleaseAsync(releaseId, ct))
                .Where(item => item.OccurredAt <= request.AsOfUtc).ToArray();
            var announcements = events.Where(item => item.Kind == Core.Domain.UpdateEventKinds.Announcement).ToArray();
            observed &= announcements.Length > 0;
            var pushes = UpdateReading.CorrelatedPushes(events, request.Thresholds.UpdateCorrelationWindowDays)
                .Where(push => UpdateReading.SincePlay(push.OccurredAt, facts.LastPlayedAt, facts.PlaytimeMinutes)).ToArray();
            // Storefronts often report the same patch. Match the library's maximum-per-release
            // count rather than adding copies of a game's update history.
            updateCount = Math.Max(updateCount, pushes.Length);
            var news = announcements.Where(item => pushes.Any(push => UpdateReading.Correlates(
                    push.OccurredAt, item.OccurredAt, request.Thresholds.UpdateCorrelationWindowDays)))
                .OrderByDescending(item => item.OccurredAt).ThenBy(item => item.ReleaseId).FirstOrDefault();
            if (news is not null && (latestNews is null || news.OccurredAt > latestNews.OccurredAt)) latestNews = news;
        }
        if (observed) enriched = enriched with { UpdateCoverage = UpdateCoverage.Observed };
        if (patched && updateCount > 0)
            enriched = enriched with
            {
                UpdatesSinceLastPlayed = updateCount,
                LatestUpdateTitle = latestNews?.Title,
                LatestUpdateReleaseId = latestNews?.ReleaseId,
            };

        return enriched;
    }

    /// <summary>
    /// The library's maturity tier, measured over the LIBRARY. The candidate
    /// shortlist is the worst possible sample for this question (it excludes,
    /// by design, exactly the games being played) and the recently-played rows
    /// are the densest in sessions, so neither may stand in for the whole: read
    /// off those two, a user with a hundred sessions spread across a hundred
    /// titles read as cold start.
    ///
    /// <para>Where a global aggregate is available it is used verbatim.
    /// Otherwise a uniform draw over every history-bearing ownership is scaled
    /// back up, and the directly observed count is the floor under the result,
    /// since a count of rows actually read can never exceed the truth.</para>
    /// </summary>
    private async Task<DataTier> DetectTierAsync(
        IReadOnlyList<Core.Queries.OwnershipBucket> bucketRows,
        RecommendationTuning tuning,
        HistoryReader history,
        CancellationToken ct)
    {
        var stats = _historyStats is not null
            ? await _historyStats.GetAsync(ct)
            : await EstimateHistoryAsync(bucketRows, tuning, history, ct);

        if (stats.SessionCount >= tuning.Tier2MinSessions
            && stats.FirstSessionAt is { } first
            && stats.LastSessionAt is { } last
            && (last - first).TotalDays >= tuning.Tier2MinSpanDays)
        {
            return DataTier.Established;
        }

        return stats.SessionCount > 0 || stats.OwnershipsWithSnapshotRises > 0
            ? DataTier.Settling
            : DataTier.ColdStart;
    }

    /// <summary>The sampled fallback for <see cref="DetectTierAsync"/>. Unbiased by construction; see the tuning fields.</summary>
    private static async Task<Core.Queries.LibraryHistoryStats> EstimateHistoryAsync(
        IReadOnlyList<Core.Queries.OwnershipBucket> bucketRows,
        RecommendationTuning tuning,
        HistoryReader history,
        CancellationToken ct)
    {
        // A row with no minutes and no play date cannot hold a session or a
        // snapshot rise — both imply playtime — so excluding it is exact
        // stratification, not a bias. It is also most of a real library.
        var playable = bucketRows
            .Where(r => r.PlaytimeMinutes > 0 || r.LastPlayedAt is not null)
            .ToList();

        if (playable.Count == 0)
        {
            return Core.Queries.LibraryHistoryStats.Empty;
        }

        var sampleSize = Math.Clamp(tuning.TierSampleOwnerships, 1, playable.Count);
        var sample = playable
            .OrderBy(r => RecommendationScorer.JitterValue(tuning.TierSampleSeed, r.OwnershipId))
            .ThenBy(r => r.OwnershipId)
            .Take(sampleSize)
            .ToList();

        var sampleSessions = 0;
        var sampleRises = 0;
        var observedSessions = 0;
        DateTime? firstSession = null, lastSession = null;
        var risesSeen = 0;

        async Task ObserveAsync(long ownershipId, bool inSample)
        {
            var observed = await history.ReadAsync(ownershipId, ct);
            var sessions = observed.SessionCount;
            var first = observed.FirstSessionAt;
            var last = observed.LastSessionAt;
            var hadRise = observed.HadSnapshotRise;

            observedSessions += sessions;
            risesSeen += hadRise ? 1 : 0;
            if (inSample)
            {
                sampleSessions += sessions;
                sampleRises += hadRise ? 1 : 0;
            }

            if (first is { } f && (firstSession is null || f < firstSession))
            {
                firstSession = f;
            }

            if (last is { } l && (lastSession is null || l > lastSession))
            {
                lastSession = l;
            }
        }

        var sampled = new HashSet<long>();
        foreach (var row in sample)
        {
            sampled.Add(row.OwnershipId);
            await ObserveAsync(row.OwnershipId, inSample: true);
        }

        // The most recently played rows are where history physically accrues.
        // They are NOT part of the uniform draw — including them would bias the
        // scaling — but what they hold is directly observed, and a direct
        // observation is a floor the estimate may never fall below.
        foreach (var row in playable
            .Where(r => r.LastPlayedAt is not null && !sampled.Contains(r.OwnershipId))
            .OrderByDescending(r => r.LastPlayedAt)
            .Take(Math.Max(0, tuning.RecentProbeLimit)))
        {
            await ObserveAsync(row.OwnershipId, inSample: false);
        }

        var scale = playable.Count / (double)sampleSize;
        return new Core.Queries.LibraryHistoryStats
        {
            SessionCount = Math.Max(observedSessions, (int)Math.Round(sampleSessions * scale)),
            FirstSessionAt = firstSession,
            LastSessionAt = lastSession,
            OwnershipsWithSnapshotRises = Math.Max(risesSeen, (int)Math.Round(sampleRises * scale)),
            IsEstimate = true,
        };
    }

    /// <summary>
    /// Per-ownership snapshot and session reads, memoised for the life of one
    /// request so the tier pass and the candidate pass never pay twice for the
    /// same row.
    /// </summary>
    private sealed class HistoryReader
    {
        private readonly IPlaytimeSnapshotRepository _snapshots;
        private readonly ISessionRepository _sessions;
        private readonly Dictionary<long, OwnershipHistory> _cache = [];

        public HistoryReader(IPlaytimeSnapshotRepository snapshots, ISessionRepository sessions)
        {
            _snapshots = snapshots;
            _sessions = sessions;
        }

        public async Task<int> EpisodesAsync(IReadOnlyList<long> ownershipIds, CancellationToken ct)
        {
            var histories = new List<OwnershipHistory>();
            foreach (var id in ownershipIds.Distinct()) histories.Add(await ReadAsync(id, ct));
            // Overlapping sessions across copies describe one play episode. Coarse snapshot
            // rises cannot be aligned safely, so retain their largest observed lower bound.
            var episodes = 0;
            DateTime? end = null;
            foreach (var session in histories.SelectMany(item => item.Sessions).OrderBy(item => item.StartedAt))
            {
                if (end is null || session.StartedAt > end) episodes++;
                var nextEnd = session.EndedAt ?? session.StartedAt;
                if (end is null || nextEnd > end) end = nextEnd;
            }
            return Math.Max(episodes, histories.Count == 0 ? 0 : histories.Max(item => item.SnapshotRises));
        }

        public async Task<OwnershipHistory> ReadAsync(long ownershipId, CancellationToken ct)
        {
            if (_cache.TryGetValue(ownershipId, out var cached))
            {
                return cached;
            }

            var snapshots = await _snapshots.GetByOwnershipAsync(ownershipId, ct);
            var sessions = await _sessions.GetByOwnershipAsync(ownershipId, ct);

            // A "rise" is a snapshot whose cumulative minutes exceed the
            // previous reading: at least one play episode happened between the
            // two observations. The first snapshot is a baseline, not a rise —
            // which is exactly why a once-synced library reports zero episodes
            // and the tried-to-like-it signal stays silent at cold start.
            var rises = 0;
            for (var i = 1; i < snapshots.Count; i++)
            {
                if (snapshots[i].PlaytimeMinutes > snapshots[i - 1].PlaytimeMinutes)
                {
                    rises++;
                }
            }

            DateTime? first = null, last = null;
            foreach (var session in sessions)
            {
                if (first is null || session.StartedAt < first)
                {
                    first = session.StartedAt;
                }

                if (last is null || session.StartedAt > last)
                {
                    last = session.StartedAt;
                }
            }

            // Sessions are the finer instrument when present; snapshot rises
            // are the coarse fallback. Max, not sum — they are two observations
            // of the same episodes, and adding them would count each twice.
            var history = new OwnershipHistory(
                Math.Max(rises, sessions.Count), sessions.Count, first, last, rises > 0, rises, sessions);
            _cache[ownershipId] = history;
            return history;
        }
    }

    private readonly record struct OwnershipHistory(
        int Episodes,
        int SessionCount,
        DateTime? FirstSessionAt,
        DateTime? LastSessionAt,
        bool HadSnapshotRise,
        int SnapshotRises,
        IReadOnlyList<Core.Domain.Session> Sessions);
}
