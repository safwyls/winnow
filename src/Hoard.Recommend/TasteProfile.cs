using Hoard.Core.Queries;

namespace Hoard.Recommend;

/// <summary>
/// The playtime-weighted descriptor profile the taste-affinity tiebreaker
/// reads: which genres, themes and tags the user's actual hours concentrate
/// in, built from the games they committed to.
///
/// <para><b>Why √minutes.</b> Raw minutes would let one 350-hour game define
/// the whole profile (the measured library's top game IS 350 hours); a flat
/// count would say a 2-hour bounce testifies as loudly as a 250-hour love
/// affair. The square root keeps order without letting one obsession drown
/// the rest — and, like every shape choice here, it is a tiebreaker's shape,
/// not a learned model.</para>
///
/// <para><b>Why the refund line is the evidence floor.</b> §6.1's argument
/// cuts both ways: under 120 minutes the game was never really played, so it
/// cannot testify about taste either. Retired games clear the floor by
/// definition and carry most of the profile's weight — excluded as
/// candidates, they still say more about what the user loves than anything
/// else in the library.</para>
///
/// <para><b>Which kinds.</b> Genres, themes and Steam tags — the descriptors
/// that say what a game IS. Features ("Steam Cloud"), controller support and
/// game modes describe the wrapper, and "you like games with achievements" is
/// not a taste anyone holds.</para>
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
        RecommendationTuning tuning)
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
            if (row.PlaytimeMinutes < thresholds.BouncedFloorMinutes)
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
            // below the refund line the game cannot testify about anything.
            if (facets.GameModes.Count > 0)
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

    /// <summary>
    /// Whether a candidate's modes clash with how this user demonstrably
    /// plays. Fires only under dominance: at least <paramref name="minGames"/>
    /// committed mode-carrying games, of which at least
    /// <paramref name="dominanceShare"/> sit on one side — and only against a
    /// candidate that is EXCLUSIVELY the other side. A candidate with no mode
    /// facets is unknown, never mismatched.
    /// </summary>
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

    /// <summary>
    /// The candidate's affinity: the strongest single descriptor it shares
    /// with the profile, normalised to [0,1] against the profile's own peak,
    /// plus that descriptor's display name for the explanation. A single
    /// maximum rather than an overlap sum because the explanation has to name
    /// ONE thing ("Survival is where your hours go") — a signal whose
    /// arithmetic is a dot product nobody can narrate would fail the
    /// one-sentence rule.
    ///
    /// <para>(null, null) when there is no evidence on either side — absent
    /// evidence contributes nothing, it is never a zero match.</para>
    /// </summary>
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
