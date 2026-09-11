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

            new ShelfDefinition(
                ShelfIds.WaitingToBeOpened,
                "Waiting to be opened",
                "Already in your library, with no recorded play yet.",
                (facts, _) => facts.PlaytimeMinutes <= 0 && facts.LastPlayedAt is null
                    && !facts.Installed && facts.Bucket != LibraryBuckets.StaleButPatched
                    && (facts.ModeMismatch != ModeMismatch.None
                        || facts.TasteAffinity is not { } affinity || affinity < tuning.OnTasteMinAffinity)),
        ];
    }

    /// <summary>True if the candidate passes the shelf's rule and was not recently played.</summary>
    public static bool IsEligible(
        ShelfDefinition shelf, CandidateFacts facts, IReadOnlyList<SignalContribution> signals)
        => !Fired(signals, SignalNames.RecentlyPlayed) && shelf.Eligible(facts, signals);

    /// <summary>
    /// Fills shelves from the scored pool (must be the union of every shelf's shortlist).
    ///
    /// <para>Two passes over the shelves, not one. Every shelf fills the slice
    /// the caller will SHOW before any shelf fills the reserve behind it, so an
    /// early shelf holding spare cards can never claim a work a later shelf's
    /// visible slice needed. Without that ordering, asking for a deeper shelf
    /// would quietly shrink the feed the reader sees, which is the opposite of
    /// what a reserve is for. When the caller shows everything it asked for
    /// (<see cref="RecommendationRequest.VisiblePerShelf"/> unset) the second
    /// pass has nothing to do and the result is the single pass it always
    /// was.</para>
    /// </summary>
    public static IReadOnlyList<RecommendationShelf> Build(
        IReadOnlyList<ShelfDefinition> definitions,
        IReadOnlyList<ScoredCandidate> scored,
        RecommendationRequest request,
        int maxPerShelf)
    {
        var depth = Math.Max(1, maxPerShelf);
        var shown = Math.Clamp(request.VisiblePerShelf ?? depth, 1, depth);

        var claimedWorks = new HashSet<long>();
        var fills = new List<ShelfFill>(definitions.Count);

        foreach (var definition in definitions)
        {
            var pool = scored
                .Where(s => IsEligible(definition, s.Facts, s.Signals))
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Facts.ReleaseId)
                .ToList();

            fills.Add(new ShelfFill(definition, pool, request, shown));
        }

        foreach (var fill in fills)
        {
            fill.Fill(shown, claimedWorks);
        }

        // The passes decide MEMBERSHIP; the display order is still the
        // scores'. Without this, a relaxation refill appends at the bottom
        // and a strict-pass survivor with a penalty can sit above a stronger
        // item that was merely genre-capped — an order no reason could defend.
        foreach (var fill in fills)
        {
            fill.SortFrom(0);
        }

        if (shown < depth)
        {
            foreach (var fill in fills)
            {
                fill.Fill(depth, claimedWorks);
            }

            // The reserve is ordered among itself, never merged into the
            // visible slice's order: the first `shown` entries are what the
            // caller puts on screen, and a held card outranking one of them is
            // still a held card, not a promotion.
            foreach (var fill in fills)
            {
                fill.SortFrom(shown);
            }
        }

        var shelves = new List<RecommendationShelf>(definitions.Count);
        foreach (var fill in fills)
        {
            if (fill.Items.Count == 0)
            {
                continue;
            }

            shelves.Add(new RecommendationShelf
            {
                Id = fill.Definition.Id,
                Title = fill.Definition.Title,
                Blurb = fill.Definition.Blurb,
                Items = fill.Items,
            });
        }

        return shelves;
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

    /// <summary>
    /// One shelf's fill in progress. The strict pass and the relaxation pass
    /// over the genre-capped skips both keep their position, so the fill can be
    /// resumed at a deeper limit: resuming visits the pool in the order a single
    /// call to that limit would, without having claimed those works before the
    /// other shelves took their own visible slice.
    /// </summary>
    private sealed class ShelfFill
    {
        private readonly List<ScoredCandidate> _pool;
        private readonly RecommendationRequest _request;
        private readonly ShelfReasonLedger _ledger;
        private readonly Dictionary<string, int> _franchiseCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<long, int> _genreCounts = [];
        private readonly List<ScoredCandidate> _genreSkips = [];
        private readonly HashSet<long> _pickedWorks = [];
        private readonly List<Recommendation> _items = [];

        private int _strict;
        private int _relaxed;

        /// <param name="shownCards">
        /// How many of this shelf's items reach the screen. The reason ledger's
        /// variety caps are sized per SURFACE, so they are sized to this rather
        /// than to the depth requested: a caller that asks for twelve in order
        /// to show six must not thereby double how many of those six may cite
        /// the same supporting fact.
        /// </param>
        public ShelfFill(
            ShelfDefinition definition,
            List<ScoredCandidate> pool,
            RecommendationRequest request,
            int shownCards)
        {
            Definition = definition;
            _pool = pool;
            _request = request;

            // The shelf is the deduplication unit, not the whole feed. Two
            // shelves telling different stories may reuse a phrasing invisibly;
            // two cards side by side on one shelf may not.
            _ledger = new ShelfReasonLedger(
                ShelfReasonLedger.CapFor(shownCards, request.Tuning));
        }

        public ShelfDefinition Definition { get; }

        public IReadOnlyList<Recommendation> Items => _items;

        /// <summary>Adds items until the shelf holds <paramref name="limit"/> of them, or the pool runs out.</summary>
        public void Fill(int limit, HashSet<long> claimedWorks)
        {
            while (_items.Count < limit && _strict < _pool.Count)
            {
                var candidate = _pool[_strict++];

                if (claimedWorks.Contains(candidate.Facts.WorkId))
                {
                    continue;
                }

                if (_franchiseCounts.GetValueOrDefault(Franchise.KeyFor(candidate.Facts.Title))
                    >= _request.Tuning.ShelfFranchiseCap)
                {
                    continue;
                }

                var genreCapped = false;
                foreach (var genreId in candidate.Facts.GenreFacetIds)
                {
                    if (_genreCounts.GetValueOrDefault(genreId) >= _request.Tuning.ShelfGenreCap)
                    {
                        genreCapped = true;
                        break;
                    }
                }

                if (genreCapped)
                {
                    _genreSkips.Add(candidate);
                    continue;
                }

                Take(candidate, claimedWorks);
            }

            while (_items.Count < limit && _relaxed < _genreSkips.Count)
            {
                var candidate = _genreSkips[_relaxed++];

                if (_pickedWorks.Contains(candidate.Facts.WorkId)
                    || claimedWorks.Contains(candidate.Facts.WorkId))
                {
                    continue;
                }

                if (_franchiseCounts.GetValueOrDefault(Franchise.KeyFor(candidate.Facts.Title))
                    >= _request.Tuning.ShelfFranchiseCap)
                {
                    continue;
                }

                Take(candidate, claimedWorks);
            }
        }

        /// <summary>Re-orders the items from <paramref name="from"/> onward by score, ties by release id.</summary>
        public void SortFrom(int from)
        {
            if (from >= _items.Count)
            {
                return;
            }

            _items.Sort(from, _items.Count - from, ScoreOrder.Instance);
        }

        private void Take(ScoredCandidate candidate, HashSet<long> claimedWorks)
        {
            var facts = candidate.Facts;
            _items.Add(RecommendationEngine.Present(candidate, _request, _ledger));
            claimedWorks.Add(facts.WorkId);
            _pickedWorks.Add(facts.WorkId);
            var franchise = Franchise.KeyFor(facts.Title);
            _franchiseCounts[franchise] = _franchiseCounts.GetValueOrDefault(franchise) + 1;
            foreach (var genreId in facts.GenreFacetIds)
            {
                _genreCounts[genreId] = _genreCounts.GetValueOrDefault(genreId) + 1;
            }
        }

        private sealed class ScoreOrder : IComparer<Recommendation>
        {
            public static ScoreOrder Instance { get; } = new();

            public int Compare(Recommendation? a, Recommendation? b)
            {
                if (a is null || b is null)
                {
                    return 0;
                }

                var byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : a.ReleaseId.CompareTo(b.ReleaseId);
            }
        }
    }
}
