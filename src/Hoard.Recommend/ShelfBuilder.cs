using Hoard.Core.Queries;

namespace Hoard.Recommend;

/// <summary>One candidate after scoring — the currency the shelf builder trades in.</summary>
internal sealed record ScoredCandidate(
    CandidateFacts Facts,
    IReadOnlyList<SignalContribution> Signals,
    double Score);

/// <summary>One shelf's identity and membership rule.</summary>
internal sealed record ShelfDefinition(
    string Id,
    string Title,
    string Blurb,
    Func<CandidateFacts, IReadOnlyList<SignalContribution>, bool> Eligible);

/// <summary>
/// Turns one scored candidate pool into themed shelves. Pure — all the reads
/// happened upstream — so every claim below is unit-testable without a
/// database.
///
/// <para><b>Why shelves at all.</b> One ranked list buries every story below
/// the first: the patched comeback, the bounced-but-changed, the on-taste
/// shelfware and the installed-and-waiting all collapse into "a list of
/// games". A shelf is a REASON with items attached, and every shelf here runs
/// entirely on Tier-0 facts — this is the surface that makes day one worth
/// looking at, and history only sharpens it (see
/// docs/recommendation-engine.md §6a).</para>
///
/// <para><b>Claim order = presentation order.</b> A work appears on at most
/// one shelf per feed, claimed by the earliest shelf whose rule it meets —
/// two rails both fronting the same game is the same-five-games failure
/// sideways. The order runs strongest story first: changed-since-you-left,
/// then you-committed-once, then zero-friction, then you-sampled-it, then
/// pure taste. Items a later shelf loses to a claim are items that earned a
/// better sentence.</para>
///
/// <para><b>Diversity is a correctness property.</b> Within a shelf: at most
/// <see cref="RecommendationTuning.ShelfFranchiseCap"/> per franchise (hard —
/// the measured library would otherwise fill a shelf with Infinity Blade) and
/// at most <see cref="RecommendationTuning.ShelfGenreCap"/> sharing one genre
/// (soft — a second pass refills from the skips, because a pool that
/// genuinely is six RPGs should still fill its shelf rather than fake
/// variety it does not have).</para>
/// </summary>
internal static class ShelfBuilder
{
    /// <summary>
    /// The shelf catalogue, in claim order. Every rule here reads Tier-0
    /// facts only — buckets, minutes, install state, facets — which is the
    /// point: the shelf surface must be fully populated on day one.
    /// </summary>
    public static IReadOnlyList<ShelfDefinition> Definitions(
        BucketThresholds thresholds, RecommendationTuning tuning)
    {
        var refund = Math.Max(1, thresholds.BouncedFloorMinutes);

        return
        [
            new ShelfDefinition(
                ShelfIds.PatchedWhileAway,
                "Patched while you were away",
                "Major updates landed after you stopped playing — the game you left isn't the game that's waiting.",
                (facts, _) => facts.Bucket == LibraryBuckets.StaleButPatched),

            new ShelfDefinition(
                ShelfIds.WorthAnotherLook,
                "Worth another look",
                "You committed real hours past the refund line, then drifted off mid-story.",
                (facts, signals) => facts.Bucket == LibraryBuckets.Bounced
                    && !Fired(signals, SignalNames.ProbablyDone)),

            // The two sub-refund shelves exclude the stale bucket on purpose:
            // a patched game that missed the patched shelf's slots waits for
            // another day's rotation there, rather than leaking its (stronger)
            // patch story onto a shelf telling a different one. Two rails
            // fronting the same story is the samey-feed failure at shelf
            // granularity.
            new ShelfDefinition(
                ShelfIds.ReadyToPlay,
                "Installed and waiting",
                "Already on your disk with nothing sunk — zero friction between you and finding out.",
                (facts, _) => facts.Installed
                    && facts.PlaytimeMinutes < refund
                    && facts.Bucket != LibraryBuckets.StaleButPatched),

            new ShelfDefinition(
                ShelfIds.BarelyTouched,
                "Barely gave it a chance",
                $"Under {Phrases.Duration(refund)} in — you opened the door and never walked through.",
                (facts, _) => facts.PlaytimeMinutes >= 1
                    && facts.PlaytimeMinutes < refund
                    && facts.Bucket != LibraryBuckets.StaleButPatched),

            new ShelfDefinition(
                ShelfIds.OnYourTaste,
                "Never opened, right up your alley",
                "Sitting sealed in your library, and it matches where your hours actually go.",
                (facts, _) => facts.PlaytimeMinutes <= 0
                    && facts.LastPlayedAt is null
                    && facts.ModeMismatch == ModeMismatch.None
                    && facts.TasteAffinity is { } affinity
                    && affinity >= tuning.OnTasteMinAffinity),
        ];
    }

    /// <summary>
    /// Whether a candidate belongs on a shelf at all. The one rule shared by
    /// every shelf: a game played inside the fresh window is not forgotten,
    /// and no rail may pretend otherwise — the same fact the scorer's
    /// recently-played penalty encodes, read off the signals rather than
    /// recomputed so the two can never disagree.
    /// </summary>
    public static bool IsEligible(
        ShelfDefinition shelf, CandidateFacts facts, IReadOnlyList<SignalContribution> signals)
        => !Fired(signals, SignalNames.RecentlyPlayed) && shelf.Eligible(facts, signals);

    /// <summary>
    /// Fills the shelves from the scored pool. <paramref name="scored"/> must
    /// already be the union of every shelf's shortlist; entries are re-ranked
    /// per shelf by final score here.
    /// </summary>
    public static IReadOnlyList<RecommendationShelf> Build(
        IReadOnlyList<ShelfDefinition> definitions,
        IReadOnlyList<ScoredCandidate> scored,
        RecommendationTuning tuning,
        int maxPerShelf)
    {
        var claimedWorks = new HashSet<long>();
        var shelves = new List<RecommendationShelf>(definitions.Count);

        foreach (var definition in definitions)
        {
            var pool = scored
                .Where(s => IsEligible(definition, s.Facts, s.Signals))
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Facts.ReleaseId)
                .ToList();

            var items = Fill(pool, claimedWorks, tuning, maxPerShelf);
            if (items.Count > 0)
            {
                shelves.Add(new RecommendationShelf
                {
                    Id = definition.Id,
                    Title = definition.Title,
                    Blurb = definition.Blurb,
                    Items = items,
                });
            }
        }

        return shelves;
    }

    /// <summary>
    /// The two-pass fill. Strict pass: skip anything that would put a second
    /// franchise entry or a genre past its cap on the shelf. Relaxation pass:
    /// refill the remaining slots from the genre-skips in score order — the
    /// genre cap is a preference for variety, not a quota to leave slots
    /// empty over. The franchise cap is never relaxed; see
    /// <see cref="RecommendationTuning.ShelfFranchiseCap"/>.
    /// </summary>
    private static List<Recommendation> Fill(
        List<ScoredCandidate> pool,
        HashSet<long> claimedWorks,
        RecommendationTuning tuning,
        int maxPerShelf)
    {
        var items = new List<Recommendation>(maxPerShelf);
        var franchiseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var genreCounts = new Dictionary<long, int>();
        var genreSkips = new List<ScoredCandidate>();
        var pickedWorks = new HashSet<long>();

        void Take(ScoredCandidate candidate)
        {
            var facts = candidate.Facts;
            items.Add(new Recommendation
            {
                OwnershipId = facts.OwnershipId,
                ReleaseId = facts.ReleaseId,
                WorkId = facts.WorkId,
                Title = facts.Title,
                Store = facts.Store,
                Bucket = facts.Bucket,
                Score = candidate.Score,
                Reason = ReasonBuilder.Build(facts, candidate.Signals),
                Signals = candidate.Signals,
            });
            claimedWorks.Add(facts.WorkId);
            pickedWorks.Add(facts.WorkId);
            var franchise = Franchise.KeyFor(facts.Title);
            franchiseCounts[franchise] = franchiseCounts.GetValueOrDefault(franchise) + 1;
            foreach (var genreId in facts.GenreFacetIds)
            {
                genreCounts[genreId] = genreCounts.GetValueOrDefault(genreId) + 1;
            }
        }

        foreach (var candidate in pool)
        {
            if (items.Count >= maxPerShelf)
            {
                break;
            }

            if (claimedWorks.Contains(candidate.Facts.WorkId))
            {
                continue;
            }

            if (franchiseCounts.GetValueOrDefault(Franchise.KeyFor(candidate.Facts.Title))
                >= tuning.ShelfFranchiseCap)
            {
                continue;
            }

            var genreCapped = false;
            foreach (var genreId in candidate.Facts.GenreFacetIds)
            {
                if (genreCounts.GetValueOrDefault(genreId) >= tuning.ShelfGenreCap)
                {
                    genreCapped = true;
                    break;
                }
            }

            if (genreCapped)
            {
                genreSkips.Add(candidate);
                continue;
            }

            Take(candidate);
        }

        foreach (var candidate in genreSkips)
        {
            if (items.Count >= maxPerShelf)
            {
                break;
            }

            if (pickedWorks.Contains(candidate.Facts.WorkId)
                || claimedWorks.Contains(candidate.Facts.WorkId))
            {
                continue;
            }

            if (franchiseCounts.GetValueOrDefault(Franchise.KeyFor(candidate.Facts.Title))
                >= tuning.ShelfFranchiseCap)
            {
                continue;
            }

            Take(candidate);
        }

        // The passes decide MEMBERSHIP; the display order is still the
        // scores'. Without this, a relaxation refill appends at the bottom
        // and a strict-pass survivor with a penalty can sit above a stronger
        // item that was merely genre-capped — an order no reason could defend.
        items.Sort((a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.ReleaseId.CompareTo(b.ReleaseId);
        });

        return items;
    }

    private static bool Fired(IReadOnlyList<SignalContribution> signals, string name)
    {
        foreach (var signal in signals)
        {
            if (signal.Signal == name)
            {
                return true;
            }
        }

        return false;
    }
}
