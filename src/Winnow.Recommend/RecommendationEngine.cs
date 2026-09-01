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
    private readonly IReleaseRepository _releases;
    private readonly IOwnershipRepository _ownerships;
    private readonly IPlaytimeSnapshotRepository _snapshots;
    private readonly ISessionRepository _sessions;
    private readonly IUpdateEventRepository _updateEvents;
    private readonly IFacetRepository _facets;
    private readonly ILibraryHistoryStatsRepository? _historyStats;

    public RecommendationEngine(
        ILibraryQueryRepository library,
        IReleaseRepository releases,
        IOwnershipRepository ownerships,
        IPlaytimeSnapshotRepository snapshots,
        ISessionRepository sessions,
        IUpdateEventRepository updateEvents,
        IFacetRepository facets,
        ILibraryHistoryStatsRepository? historyStats = null)
    {
        _library = library;
        _releases = releases;
        _ownerships = ownerships;
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
        int Seed);

    public async Task<RecommendationFeed> GetFeedAsync(
        RecommendationRequest request, CancellationToken ct = default)
    {
        var tuning = request.Tuning;
        var (candidates, bucketRows, seed) = await AssemblePoolAsync(request, ct);
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

        var items = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Facts.ReleaseId)
            .Take(request.MaxResults)
            .Select(s => Present(s, request))
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
        var (candidates, bucketRows, seed) = await AssemblePoolAsync(request, ct);
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

        return new ShelfFeed
        {
            Shelves = ShelfBuilder.Build(definitions, scored, request, request.MaxPerShelf),
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
    internal static Recommendation Present(ScoredCandidate candidate, RecommendationRequest request)
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
            Reason = ReasonBuilder.Build(explanation, request.Tuning),
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

        var bucketRows = await _library.GetOwnershipBucketsAsync(request.Thresholds, ct);
        var identities = (await _releases.GetIdentitiesAsync(ct))
            .ToDictionary(i => i.ReleaseId);
        var ownershipsById = (await _ownerships.GetAllAsync(ct))
            .ToDictionary(o => o.Id);
        var facetSnapshot = await _facets.GetSnapshotAsync(ct);

        // Stores per WORK, over every ownership in the library (not just the
        // candidates): the bought-twice signal is about the work, and the
        // second copy may sit on a row the bucket query filtered from view.
        // This is also why collapsing duplicates costs the signal nothing.
        var storesByWork = new Dictionary<long, HashSet<string>>();
        foreach (var ownership in ownershipsById.Values)
        {
            if (identities.TryGetValue(ownership.ReleaseId, out var identity))
            {
                if (!storesByWork.TryGetValue(identity.WorkId, out var stores))
                {
                    storesByWork[identity.WorkId] = stores = new HashSet<string>(StringComparer.Ordinal);
                }

                stores.Add(ownership.Store);
            }
        }

        var taste = TasteProfile.Build(
            bucketRows, facetSnapshot, request.Thresholds, tuning, request.EndorsedReleaseIds);

        // A verdict is about the GAME, not the row whose card the user
        // happened to click: after a confirmed cross-store merge one work
        // holds two releases, and dismissing the Steam card must not let the
        // GOG copy resurface the same game tomorrow. The stored fact stays
        // the clicked release; this widening to the work is a query,
        // recomputed per request — exactly the derived/truth split.
        var excludedWorks = new HashSet<long>();
        foreach (var releaseId in request.NotInterestedReleaseIds.Concat(request.SnoozedReleaseIds))
        {
            if (identities.TryGetValue(releaseId, out var excluded))
            {
                excludedWorks.Add(excluded.WorkId);
            }
        }

        // ── Candidate assembly and hard exclusions ─────────────────────────
        var candidates = new List<CandidateFacts>(bucketRows.Count);
        foreach (var row in bucketRows)
        {
            if (row.Bucket == LibraryBuckets.Retired)
            {
                // §6.1 precedence made concrete: the 200-hour game never comes
                // back, patches notwithstanding. It still testified to the
                // taste profile above — being finished with a game is the
                // strongest taste evidence there is.
                continue;
            }

            if (request.NotInterestedReleaseIds.Contains(row.ReleaseId)
                || request.SnoozedReleaseIds.Contains(row.ReleaseId))
            {
                continue;
            }

            if (!identities.TryGetValue(row.ReleaseId, out var identity)
                || identity.NameIsProvisional
                || excludedWorks.Contains(identity.WorkId))
            {
                // A tile named "App 1203620" cannot carry an explainable
                // recommendation; enrichment clears the flag and the game
                // joins the pool on the next request.
                continue;
            }

            ownershipsById.TryGetValue(row.OwnershipId, out var ownership);
            var (affinity, facetName) = taste.AffinityFor(row.ReleaseId);

            candidates.Add(new CandidateFacts
            {
                OwnershipId = row.OwnershipId,
                ReleaseId = row.ReleaseId,
                WorkId = identity.WorkId,
                Title = identity.MatchTitle,
                Store = ownership?.Store ?? string.Empty,
                Bucket = row.Bucket,
                PlaytimeMinutes = row.PlaytimeMinutes,
                LastPlayedAt = row.LastPlayedAt,
                Installed = ownership?.Installed ?? false,
                StoreCount = storesByWork.TryGetValue(identity.WorkId, out var stores) ? stores.Count : 1,
                TasteAffinity = affinity,
                TasteFacetName = facetName,
                RecentlySurfaced = request.RecentlySurfacedReleaseIds.Contains(row.ReleaseId),
                ModeMismatch = taste.ClassifyModes(
                    row.ReleaseId, tuning.ModeEvidenceMinGames, tuning.ModeDominanceShare),
                GenreFacetIds = GenreIdsFor(facetSnapshot, row.ReleaseId),
            });
        }

        return new CandidatePool(candidates, bucketRows, seed);
    }

    /// <summary>Genre-kind facet ids for one release — the shelf diversity cap's raw material.</summary>
    private static IReadOnlyList<long> GenreIdsFor(
        Core.Queries.FacetSnapshot snapshot, long releaseId)
    {
        if (!snapshot.ByRelease.TryGetValue(releaseId, out var facets))
        {
            return [];
        }

        List<long>? genres = null;
        foreach (var facetId in facets.FacetIds)
        {
            if (snapshot.ById.TryGetValue(facetId, out var facet)
                && facet.Kind == Core.Queries.FacetKinds.Genre)
            {
                (genres ??= []).Add(facetId);
            }
        }

        return genres is null ? [] : genres;
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
        var enriched = facts with { ReturnEpisodes = await history.EpisodesAsync(facts.OwnershipId, ct) };

        var patched = facts.Bucket == LibraryBuckets.StaleButPatched;
        var maybeDone = RecommendationScorer.HasProbablyDoneShape(
            facts, request.Tuning, request.AsOfUtc);

        if (!patched && !maybeDone)
        {
            return enriched;
        }

        var events = await _updateEvents.GetByReleaseAsync(facts.ReleaseId, ct);

        // Announcements, not build pushes: an announcement has a title a human
        // can read, the count answers "how much did I miss", and — because
        // ISteamNews serves a release's whole history rather than a window —
        // ONE recorded announcement proves Winnow has seen this release's
        // update history and would have recorded anything later. That proof is
        // what licenses the probably-done penalty to claim silence (F15);
        // without it the row simply keeps quiet on the subject.
        var announcements = events
            .Where(e => e.Kind == Core.Domain.UpdateEventKinds.Announcement)
            .OrderBy(e => e.OccurredAt)
            .ToList();

        if (announcements.Count > 0)
        {
            enriched = enriched with { UpdateCoverage = UpdateCoverage.Observed };
        }

        if (patched)
        {
            var since = facts.LastPlayedAt ?? DateTime.MinValue;
            var newer = announcements.Where(e => e.OccurredAt > since).ToList();
            if (newer.Count > 0)
            {
                enriched = enriched with
                {
                    UpdatesSinceLastPlayed = newer.Count,
                    LatestUpdateTitle = newer[^1].Title,
                };
            }
        }

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
            var (episodes, sessions, first, last, hadRise) = await history.ReadAsync(ownershipId, ct);
            _ = episodes;

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

        public async Task<int> EpisodesAsync(long ownershipId, CancellationToken ct)
            => (await ReadAsync(ownershipId, ct)).Episodes;

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
                Math.Max(rises, sessions.Count), sessions.Count, first, last, rises > 0);
            _cache[ownershipId] = history;
            return history;
        }
    }

    private readonly record struct OwnershipHistory(
        int Episodes,
        int SessionCount,
        DateTime? FirstSessionAt,
        DateTime? LastSessionAt,
        bool HadSnapshotRise);
}
