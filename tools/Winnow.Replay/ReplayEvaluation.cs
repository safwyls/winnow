using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Recommend;

namespace Winnow.Replay;

public sealed record NamedTuning(string Name, RecommendationTuning Tuning);
public sealed record ReplayOptions
{
    public int K { get; init; } = 10;
    public int OutcomeWindowDays { get; init; } = RecommendationTuning.Default.EndorsementWindowDays;
    public DateTime? OutcomesThroughUtc { get; init; }
    public BucketThresholds Thresholds { get; init; } = BucketThresholds.Default;
    public int ShuffleSeed { get; init; } = 1;
}
public enum OutcomeKind { Positive, Negative, WeakNegative, Unobserved }
public sealed record ReplayOutcome(long WorkId, OutcomeKind Kind, string Evidence);
public sealed record RankedReplayItem(long WorkId, long ReleaseId, double Score, string Reason);
public sealed record TuningEvaluation(string Name, RecommendationTuning Tuning,
    IReadOnlyList<RankedReplayItem> Ranking, int JudgedCount, int PositiveCount,
    double? PrecisionAtK, double? MeanReciprocalRank, double JudgedCoverage);
public sealed record ReplayReport(int Version, DateTime AsOfUtc, DateTime OutcomesThroughUtc,
    string SnapshotSha256, string OutcomesSha256, string ScorerAssemblySha256,
    string EvaluatorAssemblySha256, string DataAssemblySha256, string CoreAssemblySha256,
    int K, int OutcomeWindowDays, int ShuffleSeed, BucketThresholds Thresholds,
    IReadOnlyList<ReplayOutcome> Outcomes, IReadOnlyList<TuningEvaluation> Tunings,
    string MetricPopulation, string Limitations);

public static class ReplayEvaluation
{
    public static async Task<ReplayReport> CompareAsync(string snapshotDirectory, string outcomesDirectory,
        IReadOnlyList<NamedTuning> tunings, ReplayOptions? options = null,
        DateTime? asOfUtc = null, CancellationToken ct = default)
    {
        options ??= new ReplayOptions();
        if (tunings.Count < 2 || tunings.Any(item => item is null || item.Tuning is null || string.IsNullOrWhiteSpace(item.Name))
            || tunings.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != tunings.Count)
            throw new ArgumentException("Provide at least two distinct named tunings.", nameof(tunings));
        if (options.K <= 0 || options.OutcomeWindowDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "K and outcome window must be positive.");
        using var snapshot = SnapshotBundle.Open(snapshotDirectory, asOfUtc);
        using var outcomes = SnapshotBundle.Open(outcomesDirectory);
        var instant = snapshot.Manifest.AsOfUtc;
        var through = options.OutcomesThroughUtc ?? outcomes.Manifest.AsOfUtc;
        if (through.Kind != DateTimeKind.Utc || through <= instant || through > outcomes.Manifest.AsOfUtc)
            throw new ArgumentException("The outcome boundary must follow replay and cannot exceed its captured evidence.");
        ValidateScoringBoundary(snapshot);
        var library = new LibraryQueryRepository(snapshot.Factory);
        var frozenLibrary = await library.GetSnapshotAsync(options.Thresholds, instant, ct);
        var labels = await ReadOutcomesAsync(frozenLibrary, outcomes, instant, through, options.OutcomeWindowDays, ct);
        var byWork = labels.ToDictionary(item => item.WorkId);
        var results = new List<TuningEvaluation>();
        foreach (var tuning in tunings)
        {
            ct.ThrowIfCancellationRequested();
            var engine = new RecommendationEngine(library,
                new PlaytimeSnapshotRepository(snapshot.Factory), new SessionRepository(snapshot.Factory),
                new UpdateEventRepository(snapshot.Factory), new FacetRepository(snapshot.Factory),
                new LibraryHistoryStatsRepository(snapshot.Factory));
            var feedback = await FeedbackSets.LoadAsync(new FeedFeedbackRepository(snapshot.Factory), instant, tuning.Tuning, ct);
            var feed = await engine.GetFeedAsync(feedback.Apply(new RecommendationRequest
            {
                AsOfUtc = instant, Tuning = tuning.Tuning, Thresholds = options.Thresholds,
                ShuffleSeed = options.ShuffleSeed, MaxResults = Math.Max(1, frozenLibrary.Ownerships.Count),
            }), ct);
            // Outcomes never enter a scorer dependency. They select the evaluated population
            // only after the same complete frozen library has been ranked for each tuning.
            var ranked = feed.Items.Select(item => new RankedReplayItem(item.WorkId, item.ReleaseId, item.Score, item.Reason)).ToArray();
            var judged = ranked.Where(item => byWork.TryGetValue(item.WorkId, out var label)
                && label.Kind is OutcomeKind.Positive or OutcomeKind.Negative).ToArray();
            var firstPositive = Array.FindIndex(judged, item => byWork[item.WorkId].Kind == OutcomeKind.Positive);
            results.Add(new TuningEvaluation(tuning.Name, tuning.Tuning, ranked, judged.Length,
                judged.Count(item => byWork[item.WorkId].Kind == OutcomeKind.Positive),
                judged.Length >= options.K ? judged.Take(options.K).Count(item => byWork[item.WorkId].Kind == OutcomeKind.Positive) / (double)options.K : null,
                judged.Length > 0 ? (firstPositive < 0 ? 0 : 1d / (firstPositive + 1)) : null,
                ranked.Length > 0 ? judged.Length / (double)ranked.Length : 0));
        }
        return new ReplayReport(1, instant, through, snapshot.Manifest.DatabaseSha256, outcomes.Manifest.DatabaseSha256,
            SnapshotBundle.Hash(typeof(RecommendationEngine).Assembly.Location),
            SnapshotBundle.Hash(typeof(ReplayEvaluation).Assembly.Location), SnapshotBundle.Hash(typeof(LibraryQueryRepository).Assembly.Location),
            SnapshotBundle.Hash(typeof(LibrarySnapshot).Assembly.Location), options.K, options.OutcomeWindowDays,
            options.ShuffleSeed, options.Thresholds, labels, results,
            "Positive launches and explicit negative verdicts only; unjudged ranks are removed before precision@k and MRR. Precision is null when fewer than k judged games remain.",
            "One captured query per tuning; MRR is that query's reciprocal rank. Observational, exposure-biased evidence cannot establish causal lift or whole-library precision. Weak negatives are excluded: legacy surfacings lack per-row visibility provenance. Same-day impression/action order is unknown. Mutable past library state cannot be reconstructed. Outcomes require a matching unique external identifier; unanchored or conflicting games remain unobserved.");
    }

    private static void ValidateScoringBoundary(ReplayDatabase snapshot)
    {
        // These timestamps describe recorded events/observations, unlike a known future
        // snooze expiry or release year. Reject an inconsistent capture instead of quietly
        // combining future projections with older histories.
        var columns = new Dictionary<string, string[]>
        {
            ["play_records"] = ["observed_at", "last_played_at"],
            ["playtime_snapshots"] = ["observed_at"], ["sessions"] = ["started_at", "ended_at"],
            ["update_events"] = ["occurred_at"], ["lifecycle_observations"] = ["observed_at"],
            ["ownership_accounts"] = ["first_seen_at", "last_seen_at", "last_played_at"],
            ["work_maturity"] = ["observed_at"], ["feed_verdicts"] = ["created_at", "revoked_at"],
            ["identity_links"] = ["applied_at", "retracted_at"], ["hidden_games"] = ["hidden_at", "unhidden_at"],
            ["work_field_sources"] = ["set_at"], ["update_acknowledgements"] = ["created_at", "revoked_at"],
        };
        using var lease = snapshot.Factory.Lease();
        foreach (var (table, fields) in columns)
        foreach (var field in fields)
        {
            var invalid = lease.Connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table} WHERE {field} IS NOT NULL AND (julianday({field}) IS NULL OR julianday({field}) > julianday(@instant));",
                new { instant = snapshot.Manifest.AsOfUtc });
            if (invalid > 0) throw new InvalidDataException($"Capture contains invalid or future scoring evidence in {table}.{field}.");
        }
    }

    private static async Task<IReadOnlyList<ReplayOutcome>> ReadOutcomesAsync(LibrarySnapshot frozen,
        ReplayDatabase outcomes, DateTime asOfUtc, DateTime through, int windowDays, CancellationToken ct)
    {
        var releaseWorks = frozen.Releases.ToDictionary(item => item.Id,
            item => frozen.IdentityResolution.SameGame.Resolve(item.WorkId));
        var anchors = frozen.ExternalIds.Where(item => releaseWorks.ContainsKey(item.ReleaseId))
            .GroupBy(item => (item.Provider, item.ProviderId))
            .ToDictionary(group => group.Key, group => group.Select(item => releaseWorks[item.ReleaseId]).Distinct().Single());
        using var lease = outcomes.Factory.Lease();
        var ids = (await lease.Connection.QueryAsync<ExternalId>(new CommandDefinition(
            "SELECT release_id AS ReleaseId, provider AS Provider, provider_id AS ProviderId FROM external_ids;", cancellationToken: ct))).ToArray();
        var mapping = ids.Where(item => anchors.ContainsKey((item.Provider, item.ProviderId)))
            .GroupBy(item => item.ReleaseId).Select(group => new
            {
                Release = group.Key,
                Works = group.Select(item => anchors[(item.Provider, item.ProviderId)]).Distinct().ToArray(),
            }).Where(item => item.Works.Length == 1).ToDictionary(item => item.Release, item => item.Works[0]);
        var feedback = new FeedFeedbackRepository(outcomes.Factory);
        var firstOutcomeDay = DateOnly.FromDateTime(asOfUtc).AddDays(1);
        var impressions = (await feedback.GetSurfacedSinceAsync(firstOutcomeDay, ct))
            .Where(item => mapping.ContainsKey(item.ReleaseId) && item.SurfacedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) < through)
            .ToArray();
        var verdicts = (await feedback.GetAllVerdictsAsync(ct)).Where(item => item.CreatedAt > asOfUtc && item.CreatedAt <= through).ToArray();
        var launches = (await lease.Connection.QueryAsync<LaunchRow>(new CommandDefinition("""
            SELECT o.release_id AS ReleaseId, s.started_at AS StartedAt
            FROM sessions s JOIN ownerships o ON o.id = s.ownership_id
            WHERE s.attributed_by = 'launch' AND s.started_at > @asOfUtc AND s.started_at <= @through;
            """, new { asOfUtc, through }, cancellationToken: ct))).ToArray();
        var result = new List<ReplayOutcome>();
        foreach (var workId in frozen.Buckets.Select(item => item.ResolvedWorkId).Distinct().Order())
        {
            var observed = impressions.Where(item => mapping[item.ReleaseId] == workId).ToArray();
            var positive = observed.Any(impression => launches.Any(launch => launch.ReleaseId == impression.ReleaseId
                && InWindow(impression, launch.StartedAt, windowDays)));
            var negative = observed.Any(impression => verdicts.Any(verdict => verdict.ReleaseId == impression.ReleaseId
                && verdict.Kind is FeedVerdictKinds.NotInterested or FeedVerdictKinds.Snoozed
                && (verdict.RevokedAt is null || verdict.RevokedAt > through)
                && InWindow(impression, verdict.CreatedAt, windowDays)));
            var matured = observed.Any(item => item.SurfacedOn.AddDays(windowDays + 1)
                .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) <= through);
            var kind = positive && negative ? OutcomeKind.Unobserved : positive ? OutcomeKind.Positive
                : negative ? OutcomeKind.Negative : matured ? OutcomeKind.WeakNegative : OutcomeKind.Unobserved;
            result.Add(new ReplayOutcome(workId, kind, positive && negative ? "Conflicting launch and verdict outcomes; excluded."
                : kind switch
                {
                    OutcomeKind.Positive => "Launch-attributed session after the impression day and inside the outcome window.",
                    OutcomeKind.Negative => "Standing explicit verdict after the impression day and inside the outcome window.",
                    OutcomeKind.WeakNegative => "Matured impression without a qualifying action; visibility provenance is unknown, excluded from metrics.",
                    _ => "No unambiguous, mature, anchored observation.",
                }));
        }
        return result;
    }

    private static bool InWindow(FeedSurfacing impression, DateTime action, int windowDays)
    {
        var days = DateOnly.FromDateTime(action).DayNumber - impression.SurfacedOn.DayNumber;
        return days > 0 && days <= windowDays;
    }

    private sealed class LaunchRow
    {
        public long ReleaseId { get; init; }
        public DateTime StartedAt { get; init; }
    }
}
