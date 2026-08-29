using Winnow.Core.Queries;

namespace Winnow.Recommend;

/// <summary>
/// Playtime-weighted descriptor profile for the taste-affinity tiebreaker. Weights facets
/// by sqrt(minutes) from committed games (above the refund line) plus endorsed releases.
/// Only genre, theme and tag facets count; generic facets above a prevalence ceiling are excluded.
/// </summary>
internal sealed class TasteProfile
{
    private readonly Dictionary<long, double> _weightByFacet;
    private readonly FacetSnapshot _snapshot;
    private readonly double _maxWeight;
    private readonly int _committedWithModes;
    private readonly int _committedSinglePlayer;

    private TasteProfile(
        Dictionary<long, double> weightByFacet,
        FacetSnapshot snapshot,
        int committedWithModes,
        int committedSinglePlayer)
    {
        _weightByFacet = weightByFacet;
        _snapshot = snapshot;
        _maxWeight = weightByFacet.Count == 0 ? 0 : weightByFacet.Values.Max();
        _committedWithModes = committedWithModes;
        _committedSinglePlayer = committedSinglePlayer;
    }

    public static TasteProfile Build(
        IReadOnlyList<OwnershipBucket> bucketRows,
        FacetSnapshot snapshot,
        BucketThresholds thresholds,
        RecommendationTuning tuning,
        IReadOnlySet<long>? endorsedReleaseIds = null)
    {
        // ── The prevalence cut ─────────────────────────────────────────────
        // A descriptor carried by a quarter of the library describes the
        // LIBRARY, not the user. Measured on the real data, "Action" sits on
        // roughly two-thirds of releases, and with it in play the affinity
        // metric saturated: 266 of 427 never-opened rows scored a perfect
        // match, which is a metric measuring nothing. Cutting facets above
        // the prevalence ceiling turns the profile's peaks from
        // Action/Adventure/Singleplayer into Survival/Sandbox/Crafting — the
        // things this user actually, distinctively plays. The absolute floor
        // protects small libraries (and test fixtures), where three carriers
        // of one genre is coincidence, not genericity.
        var totalWithFacets = snapshot.Releases.Count;
        var genericAt = Math.Max(
            tuning.TasteFacetPrevalenceFloor,
            (int)Math.Ceiling(tuning.TasteFacetMaxPrevalence * totalWithFacets));
        var carriers = new Dictionary<long, int>();
        foreach (var release in snapshot.Releases)
        {
            foreach (var facetId in release.FacetIds)
            {
                carriers[facetId] = carriers.GetValueOrDefault(facetId) + 1;
            }
        }

        var weights = new Dictionary<long, double>();
        var committedWithModes = 0;
        var committedSinglePlayer = 0;
        foreach (var row in bucketRows)
        {
            // ── The evidence floor, and the one exception feedback earns ───
            // The refund line stays the floor: under 120 minutes a game was
            // never really played and cannot testify. The exception is a
            // release the user launched OFF THE FEED (an endorsement): they
            // answered the pitch with their time, and silencing that answer
            // would waste the loop's best positive signal. It still testifies
            // with only the √minutes it actually has — the same currency as
            // everything else — so three feed-driven launches (√40 ≈ 6 each)
            // cannot outvote one committed game (√6000 ≈ 77), which is the
            // arithmetic that stops a handful of clicks from steering the
            // profile. This is the endorsement's ONLY scoring effect.
            var committed = row.PlaytimeMinutes >= thresholds.BouncedFloorMinutes;
            var endorsed = endorsedReleaseIds?.Contains(row.ReleaseId) ?? false;
            if (!committed && !endorsed)
            {
                continue;
            }

            if (!snapshot.ByRelease.TryGetValue(row.ReleaseId, out var facets))
            {
                continue;
            }

            var weight = Math.Sqrt(row.PlaytimeMinutes);
            foreach (var facetId in facets.FacetIds)
            {
                if (IsTasteKind(snapshot, facetId)
                    && carriers.GetValueOrDefault(facetId) < genericAt)
                {
                    weights[facetId] = weights.GetValueOrDefault(facetId) + weight;
                }
            }

            // The mode tally behind ClassifyModes. Game COUNTS, not minutes:
            // the sentence the signal ships is "nearly everything you play is
            // single-player", and a count is that sentence's arithmetic —
            // minutes-weighting would let one MMO bender reclassify a solo
            // player. Committed games only, same floor as the taste weights:
            // below the refund line the game cannot testify about anything —
            // and an ENDORSED sub-refund row stays out of this tally too: the
            // endorsement exception above is scoped to taste weights only,
            // because reclassifying how the user plays needs commitment, not
            // one answered pitch.
            if (committed && facets.GameModes.Count > 0)
            {
                committedWithModes++;
                if (facets.GameModes.Contains(GameModes.SinglePlayer))
                {
                    committedSinglePlayer++;
                }
            }
        }

        return new TasteProfile(weights, snapshot, committedWithModes, committedSinglePlayer);
    }

    /// <summary>Whether a candidate's modes clash with the user's dominant play mode. Returns <see cref="ModeMismatch.None"/> when evidence is insufficient.</summary>
    public ModeMismatch ClassifyModes(
        long releaseId, int minGames, double dominanceShare)
    {
        if (_committedWithModes < minGames
            || !_snapshot.ByRelease.TryGetValue(releaseId, out var facets)
            || facets.GameModes.Count == 0)
        {
            return ModeMismatch.None;
        }

        var singleShare = _committedSinglePlayer / (double)_committedWithModes;
        var modes = facets.GameModes;
        var hasSingle = modes.Contains(GameModes.SinglePlayer);

        // "Online-only" is deliberately the competitive trio, not co-op or
        // split-screen: couch co-op beside a solo library is a maybe, an
        // MMO beside one is a mistake.
        var onlineOnly = !hasSingle
            && (modes.Contains(GameModes.Multiplayer)
                || modes.Contains(GameModes.Mmo)
                || modes.Contains(GameModes.BattleRoyale));

        if (onlineOnly && singleShare >= dominanceShare)
        {
            return ModeMismatch.OnlineOnlyForSoloPlayer;
        }

        var soloOnly = hasSingle && modes.Count == 1;
        if (soloOnly && 1 - singleShare >= dominanceShare)
        {
            return ModeMismatch.SoloOnlyForOnlinePlayer;
        }

        return ModeMismatch.None;
    }

    /// <summary>Returns the candidate's strongest shared facet normalised to [0,1], or (null, null) if no evidence.</summary>
    public (double? Affinity, string? FacetName) AffinityFor(long releaseId)
    {
        if (_maxWeight <= 0 || !_snapshot.ByRelease.TryGetValue(releaseId, out var facets))
        {
            return (null, null);
        }

        var best = 0.0;
        long bestFacet = -1;
        foreach (var facetId in facets.FacetIds)
        {
            if (_weightByFacet.TryGetValue(facetId, out var weight) && weight > best)
            {
                best = weight;
                bestFacet = facetId;
            }
        }

        if (bestFacet < 0)
        {
            return (null, null);
        }

        var name = _snapshot.ById.TryGetValue(bestFacet, out var facet) ? facet.Name : null;
        return (best / _maxWeight, name);
    }

    private static bool IsTasteKind(FacetSnapshot snapshot, long facetId)
        => snapshot.ById.TryGetValue(facetId, out var facet)
           && facet.Kind is FacetKinds.Genre or FacetKinds.Theme or FacetKinds.Tag;
}
