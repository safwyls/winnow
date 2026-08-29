using Winnow.Core.Queries;

namespace Winnow.Recommend;

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
/// Turns a scored candidate pool into themed shelves (pure, no IO). Each work appears on
/// at most one shelf, claimed by the first matching definition. Franchise and genre caps
/// enforce diversity within each shelf.
/// </summary>
internal static class ShelfBuilder
{
    /// <summary>The shelf catalogue in claim order. All rules use Tier-0 facts only.</summary>
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

    /// <summary>True if the candidate passes the shelf's rule and was not recently played.</summary>
    public static bool IsEligible(
        ShelfDefinition shelf, CandidateFacts facts, IReadOnlyList<SignalContribution> signals)
        => !Fired(signals, SignalNames.RecentlyPlayed) && shelf.Eligible(facts, signals);

    /// <summary>Fills shelves from the scored pool (must be the union of every shelf's shortlist).</summary>
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

    /// <summary>Two-pass fill: strict pass enforces franchise and genre caps; relaxation pass refills from genre-skips.</summary>
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
