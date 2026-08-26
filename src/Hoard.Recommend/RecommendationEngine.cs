using Hoard.Core.Queries;
using Hoard.Core.Repositories;

namespace Hoard.Recommend;

/// <summary>
/// Assembles <see cref="CandidateFacts"/> from the repositories and hands them
/// to <see cref="RecommendationScorer"/>. All the judgement lives in the
/// scorer and the tuning; this class is deliberately just honest plumbing —
/// bulk reads for what every candidate needs, bounded per-row probes for what
/// only the shortlist needs.
///
/// <para><b>Read path.</b> Four bulk reads (buckets, release identities,
/// ownerships, facet snapshot) cover every Tier-0 signal. Snapshot, session
/// and update-event history have per-ownership interfaces only, so they are
/// probed for the shortlist (3× the requested feed, capped by
/// <see cref="RecommendationTuning.HistoryProbeLimit"/>) plus the most
/// recently played rows — which is where longitudinal history physically
/// accrues first, and therefore where tier detection has to look; the probe
/// budget exists so a 1,000-game library costs ~90 small queries per refresh
/// instead of 2,000.</para>
///
/// <para><b>What is inherited rather than reimplemented.</b> The §6.1 bucket
/// query upstream already consolidated demos/betas into their base game,
/// hid non-game entries, correlated update signals into "major update", and
/// applied the never-opened/retired/stale precedence. This engine trusts
/// those rows: recomputing any of it here would be a second definition that
/// could disagree with the library view the user is looking at.</para>
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

    public RecommendationEngine(
        ILibraryQueryRepository library,
        IReleaseRepository releases,
        IOwnershipRepository ownerships,
        IPlaytimeSnapshotRepository snapshots,
        ISessionRepository sessions,
        IUpdateEventRepository updateEvents,
        IFacetRepository facets)
    {
        _library = library;
        _releases = releases;
        _ownerships = ownerships;
        _snapshots = snapshots;
        _sessions = sessions;
        _updateEvents = updateEvents;
        _facets = facets;
    }

    public async Task<RecommendationFeed> GetFeedAsync(
        RecommendationRequest request, CancellationToken ct = default)
    {
        var tuning = request.Tuning;
        var seed = request.ShuffleSeed
            ?? DateOnly.FromDateTime(request.AsOfUtc).DayNumber;

        // ── Bulk reads: everything Tier 0 needs ────────────────────────────
        var bucketRows = await _library.GetOwnershipBucketsAsync(request.Thresholds, ct);
        var identities = (await _releases.GetIdentitiesAsync(ct))
            .ToDictionary(i => i.ReleaseId);
        var ownershipsById = (await _ownerships.GetAllAsync(ct))
            .ToDictionary(o => o.Id);
        var facetSnapshot = await _facets.GetSnapshotAsync(ct);

        // Stores per WORK, over every ownership in the library (not just the
        // candidates): the bought-twice signal is about the work, and the
        // second copy may sit on a row the bucket query filtered from view.
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

        var taste = TasteProfile.Build(bucketRows, facetSnapshot, request.Thresholds);

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
                || identity.NameIsProvisional)
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
            });
        }

        // ── Preliminary rank, then probe the shortlist ─────────────────────
        // The final feed is drawn from the shortlist alone. That is sound
        // because history can only ADD to a row's score (tried-to-like-it is a
        // bonus), so a row outside the top 3× cannot be sitting on enough
        // hidden evidence to reach the top 1× — and the reasons for the rows
        // the user will actually see get the per-release update detail.
        double Preliminary(CandidateFacts facts) => RecommendationScorer.Total(
            RecommendationScorer.Score(facts, request.Thresholds, tuning, request.AsOfUtc, seed));

        var ranked = candidates
            .OrderByDescending(Preliminary)
            .ThenBy(f => f.ReleaseId)
            .ToList();

        var shortlistSize = Math.Min(
            Math.Max(request.MaxResults, request.MaxResults * 3),
            tuning.HistoryProbeLimit);
        var shortlist = ranked.Take(shortlistSize).ToList();

        // Tier detection must look where history actually lives: the most
        // recently played rows, which the feed by design ranks LOWEST. Probing
        // only the shortlist would examine 60 dormant games and conclude no
        // history exists while the user racks up sessions elsewhere.
        var recentProbe = bucketRows
            .Where(r => r.LastPlayedAt is not null)
            .OrderByDescending(r => r.LastPlayedAt)
            .Take(tuning.RecentProbeLimit)
            .Select(r => r.OwnershipId);

        var probe = await ProbeHistoryAsync(
            shortlist.Select(f => f.OwnershipId).Concat(recentProbe).Distinct().ToList(), ct);

        // ── Final scoring over history-enriched facts ──────────────────────
        var scored = new List<(CandidateFacts Facts, IReadOnlyList<SignalContribution> Signals, double Score)>(shortlist.Count);
        foreach (var facts in shortlist)
        {
            var enriched = await EnrichAsync(facts, probe, ct);
            var signals = RecommendationScorer.Score(
                enriched, request.Thresholds, tuning, request.AsOfUtc, seed);
            scored.Add((enriched, signals, RecommendationScorer.Total(signals)));
        }

        // One feed entry per WORK: two ownerships of one game are one
        // recommendation, and the better-scoring copy carries it.
        var items = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Facts.ReleaseId)
            .DistinctBy(s => s.Facts.WorkId)
            .Take(request.MaxResults)
            .Select(s => new Recommendation
            {
                OwnershipId = s.Facts.OwnershipId,
                ReleaseId = s.Facts.ReleaseId,
                WorkId = s.Facts.WorkId,
                Title = s.Facts.Title,
                Store = s.Facts.Store,
                Bucket = s.Facts.Bucket,
                Score = s.Score,
                Reason = ReasonBuilder.Build(s.Facts, s.Signals),
                Signals = s.Signals,
            })
            .ToList();

        return new RecommendationFeed
        {
            Items = items,
            Tier = DetectTier(probe, tuning),
            CandidateCount = candidates.Count,
        };
    }

    /// <summary>
    /// Reads snapshot and session history for the given ownerships, one
    /// ownership at a time — the shape the Core interfaces offer. Bounded by
    /// the caller (shortlist + recent probe), never the whole library.
    /// </summary>
    private async Task<HistoryProbe> ProbeHistoryAsync(
        IReadOnlyList<long> ownershipIds, CancellationToken ct)
    {
        var episodes = new Dictionary<long, int>(ownershipIds.Count);
        var sessionCount = 0;
        DateTime? firstSession = null, lastSession = null;
        var anyMultiSnapshot = false;

        foreach (var ownershipId in ownershipIds)
        {
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

            anyMultiSnapshot |= rises > 0;

            // Sessions are the finer instrument when present; snapshot rises
            // are the coarse fallback. Max, not sum — they are two observations
            // of the same episodes, and adding them would count each twice.
            episodes[ownershipId] = Math.Max(rises, sessions.Count);

            sessionCount += sessions.Count;
            foreach (var session in sessions)
            {
                if (firstSession is null || session.StartedAt < firstSession)
                {
                    firstSession = session.StartedAt;
                }

                if (lastSession is null || session.StartedAt > lastSession)
                {
                    lastSession = session.StartedAt;
                }
            }
        }

        return new HistoryProbe(episodes, anyMultiSnapshot, sessionCount, firstSession, lastSession);
    }

    /// <summary>
    /// Adds the probed history to a shortlist row's facts: return episodes
    /// for scoring, and — for stale rows — the update detail that turns
    /// "it has been updated" into "3 updates since, most recently X" in the
    /// reason. Update events are fetched here, per stale shortlist row only:
    /// scoring already has the fact (bucket membership); this is decoration
    /// for rows the user will actually read.
    /// </summary>
    private async Task<CandidateFacts> EnrichAsync(
        CandidateFacts facts, HistoryProbe probe, CancellationToken ct)
    {
        var enriched = facts;

        if (probe.EpisodesByOwnership.TryGetValue(facts.OwnershipId, out var episodes))
        {
            enriched = enriched with { ReturnEpisodes = episodes };
        }

        if (facts.Bucket == LibraryBuckets.StaleButPatched)
        {
            var events = await _updateEvents.GetByReleaseAsync(facts.ReleaseId, ct);

            // Announcements, not build pushes: an announcement has a title a
            // human can read, and the count answers "how much did I miss",
            // which is the question the reason is answering. The build push
            // already did its job inside the bucket query's correlation.
            var since = facts.LastPlayedAt ?? DateTime.MinValue;
            var announcements = events
                .Where(e => e.Kind == Core.Domain.UpdateEventKinds.Announcement && e.OccurredAt > since)
                .OrderBy(e => e.OccurredAt)
                .ToList();

            if (announcements.Count > 0)
            {
                enriched = enriched with
                {
                    UpdatesSinceLastPlayed = announcements.Count,
                    LatestUpdateTitle = announcements[^1].Title,
                };
            }
        }

        return enriched;
    }

    /// <summary>
    /// Evidence-based tier detection over the probed rows. An approximation by
    /// construction (the probe is a bounded sample), biased toward the
    /// recently played rows on purpose — that is where history accrues first.
    /// </summary>
    private static DataTier DetectTier(HistoryProbe probe, RecommendationTuning tuning)
    {
        if (probe.SessionCount >= tuning.Tier2MinSessions
            && probe.FirstSessionAt is { } first
            && probe.LastSessionAt is { } last
            && (last - first).TotalDays >= tuning.Tier2MinSpanDays)
        {
            return DataTier.Established;
        }

        return probe.SessionCount > 0 || probe.AnyMultiSnapshot
            ? DataTier.Settling
            : DataTier.ColdStart;
    }

    private sealed record HistoryProbe(
        IReadOnlyDictionary<long, int> EpisodesByOwnership,
        bool AnyMultiSnapshot,
        int SessionCount,
        DateTime? FirstSessionAt,
        DateTime? LastSessionAt);
}
