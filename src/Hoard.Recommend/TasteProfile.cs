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

    private TasteProfile(Dictionary<long, double> weightByFacet, FacetSnapshot snapshot)
    {
        _weightByFacet = weightByFacet;
        _snapshot = snapshot;
        _maxWeight = weightByFacet.Count == 0 ? 0 : weightByFacet.Values.Max();
    }

    public static TasteProfile Build(
        IReadOnlyList<OwnershipBucket> bucketRows,
        FacetSnapshot snapshot,
        BucketThresholds thresholds)
    {
        var weights = new Dictionary<long, double>();
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
                if (IsTasteKind(snapshot, facetId))
                {
                    weights[facetId] = weights.GetValueOrDefault(facetId) + weight;
                }
            }
        }

        return new TasteProfile(weights, snapshot);
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
