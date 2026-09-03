namespace Winnow.Recommend;

/// <summary>
/// Bounds on what probing a candidate's history can still do to its score, and
/// the shortlist rule built from them.
///
/// <para>The shortlist exists because history is read per ownership and the
/// library is thousands of rows. The old rule justified a fixed top slice by
/// claiming history could only ADD to a score, so a row outside the top 3×
/// could not reach the top 1×. Both halves were wrong. History can now also
/// SUBTRACT, since revealing update coverage is what lets the probably-done
/// penalty fire, and even the additive case was unbounded: a row just outside
/// the slice could hold enough hidden return-episode evidence to leapfrog into
/// the results.</para>
/// </summary>
internal static class ScoreBounds
{
    /// <summary>
    /// The most a history probe can still ADD to a candidate's score. Only the
    /// tried-to-like-it signal is hidden from the bulk read, so this is that
    /// weight or nothing. Exactness matters more than tightness here: the zero
    /// cases are what keep the safe shortlist cheap, because never-opened
    /// shelfware is most of a real library and none of it can hold a surprise.
    /// </summary>
    public static double MaxHiddenBonus(CandidateFacts facts, RecommendationTuning tuning)
    {
        // Already probed: nothing is hidden any more.
        if (facts.ReturnEpisodes is not null)
        {
            return 0.0;
        }

        // A game with no minutes and no play date cannot be sitting on unseen
        // play episodes: sessions and snapshot rises both imply playtime, so
        // this is an exact zero rather than a cautious one. It is also what
        // keeps the safe shortlist small on a real library, where the low-
        // scoring bulk is never-opened shelfware.
        if (facts.PlaytimeMinutes <= 0 && facts.LastPlayedAt is null)
        {
            return 0.0;
        }

        return Math.Max(0.0, tuning.WeightTriedToLikeIt);
    }

    /// <summary>
    /// The most a history probe can still SUBTRACT from a candidate's score.
    /// Nonzero only where a probe could reveal update coverage on a row already
    /// shaped like a finished game, because coverage is the one fact that
    /// licenses the probably-done penalty. A probe that can only lower a
    /// candidate is why the shortlist cannot be a fixed top slice.
    /// </summary>
    public static double MaxHiddenPenalty(
        CandidateFacts facts, RecommendationTuning tuning, DateTime asOfUtc)
        => facts.UpdateCoverage == UpdateCoverage.Unknown
            && RecommendationScorer.HasProbablyDoneShape(facts, tuning, asOfUtc)
                ? Math.Max(0.0, tuning.PenaltyProbablyDone)
                : 0.0;

    /// <summary>Highest score this candidate could hold once history is read.</summary>
    public static double Upper(double preliminary, CandidateFacts facts, RecommendationTuning tuning)
        => preliminary + MaxHiddenBonus(facts, tuning);

    /// <summary>Lowest score this candidate could hold once history is read.</summary>
    public static double Lower(
        double preliminary, CandidateFacts facts, RecommendationTuning tuning, DateTime asOfUtc)
        => preliminary - MaxHiddenPenalty(facts, tuning, asOfUtc);

    /// <summary>
    /// Every candidate that could still finish in the top <paramref name="take"/>
    /// once its history is read, ordered by preliminary score. A candidate is
    /// dropped only when its BEST possible final score
    /// (<see cref="Upper"/>) cannot reach the WORST possible final score of the
    /// k-th ranked candidate (<see cref="Lower"/>). That is the whole safety
    /// argument: a dropped row provably could not have placed, whatever its
    /// history turns out to say. Measured on a 200-work library shaped like the
    /// real one, the bound probed 60 works, which is the comfort floor, so
    /// correctness cost nothing.
    /// </summary>
    /// <param name="pool">Preliminary-scored candidates, one per work.</param>
    /// <param name="take">How many results the caller will ultimately show.</param>
    /// <param name="comfortMinimum">
    /// Lower bound on the shortlist size, kept so the rows a user actually sees
    /// get their update detail read even when the safe bound is tighter.
    /// </param>
    public static List<ScoredCandidate> SafeShortlist(
        IReadOnlyList<ScoredCandidate> pool,
        RecommendationTuning tuning,
        DateTime asOfUtc,
        int take,
        int comfortMinimum)
    {
        var ordered = pool
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Facts.ReleaseId)
            .ToList();

        if (ordered.Count == 0)
        {
            return ordered;
        }

        var floor = Math.Max(0, Math.Min(comfortMinimum, ordered.Count));

        // The k-th best guaranteed score. Any candidate that cannot reach it,
        // even with every hidden bonus it could possibly hold, cannot make the
        // cut and is safe to drop.
        var bound = double.NegativeInfinity;
        if (take > 0)
        {
            var lowers = pool
                .Select(c => Lower(c.Score, c.Facts, tuning, asOfUtc))
                .OrderByDescending(x => x)
                .ToList();
            if (lowers.Count >= take)
            {
                bound = lowers[take - 1];
            }
        }

        var kept = new List<ScoredCandidate>(Math.Max(floor, take));
        for (var i = 0; i < ordered.Count; i++)
        {
            var candidate = ordered[i];
            if (i < floor || Upper(candidate.Score, candidate.Facts, tuning) >= bound)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    /// <summary>
    /// One candidate per work, chosen before any capacity is spent. Two store
    /// copies of one game are one recommendation, so collapsing after the
    /// shortlist let a duplicate consume a slot a distinct work needed. The
    /// survivor is the copy with the highest UPPER bound rather than the
    /// highest score, so the collapse cannot discard the copy that would have
    /// won once history was read. The bought-twice signal is untouched by this:
    /// store counts are computed per work over every ownership in the library,
    /// before candidates are assembled.
    /// </summary>
    public static List<ScoredCandidate> CollapseByWork(
        IReadOnlyList<ScoredCandidate> pool, RecommendationTuning tuning)
    {
        var best = new Dictionary<long, ScoredCandidate>(pool.Count);
        foreach (var candidate in pool)
        {
            if (!best.TryGetValue(candidate.Facts.WorkId, out var incumbent)
                || Beats(candidate, incumbent, tuning))
            {
                best[candidate.Facts.WorkId] = candidate;
            }
        }

        return best.Values
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Facts.ReleaseId)
            .ToList();
    }

    /// <summary>Total order over two copies of one work: upper bound, then score, then installed, then id.</summary>
    private static bool Beats(ScoredCandidate a, ScoredCandidate b, RecommendationTuning tuning)
    {
        var byUpper = Upper(a.Score, a.Facts, tuning).CompareTo(Upper(b.Score, b.Facts, tuning));
        if (byUpper != 0)
        {
            return byUpper > 0;
        }

        var byScore = a.Score.CompareTo(b.Score);
        if (byScore != 0)
        {
            return byScore > 0;
        }

        if (a.Facts.Installed != b.Facts.Installed)
        {
            return a.Facts.Installed;
        }

        var byRelease = a.Facts.ReleaseId.CompareTo(b.Facts.ReleaseId);
        return byRelease != 0
            ? byRelease < 0
            : a.Facts.OwnershipId < b.Facts.OwnershipId;
    }
}
