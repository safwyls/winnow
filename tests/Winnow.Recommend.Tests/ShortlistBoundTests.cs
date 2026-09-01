using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// What the shortlist is allowed to throw away. F32: pruning must be
/// score-bound safe, because probing a candidate's history CHANGES its score
/// and a fixed top slice can therefore drop a winner. F38: two store copies of
/// one game are one recommendation, so the duplicate must never consume
/// shortlist capacity a distinct work needed.
/// </summary>
public class ShortlistBoundTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_candidate_outside_the_old_shortlist_still_wins_when_its_hidden_history_says_so()
    {
        var asOf = RecommendHarness.AsOf;

        // MaxResults 3 makes the old shortlist exactly nine rows. Nine filler
        // bounces occupy every one of them on preliminary score alone.
        for (var i = 0; i < 9; i++)
        {
            await _harness.SeedGameAsync($"Filler {i:00}", minutes: 300, lastPlayed: asOf.AddYears(-2));
        }

        // The tenth row: more minutes, so a LOWER commitment value and a
        // preliminary score below all nine — but five observed snapshot rises
        // the preliminary pass cannot see. Once probed, tried-to-like-it
        // carries it past every filler. Under a fixed top-9 slice it was never
        // probed at all and never appeared.
        var gem = await _harness.SeedGameAsync("Hidden Gem", minutes: 1_500, lastPlayed: asOf.AddYears(-2));
        for (var i = 0; i < 5; i++)
        {
            await _harness.SeedSnapshotAsync(gem, minutes: 1_520 + i * 20, observedAt: asOf.AddDays(-30 + i));
        }

        var request = RecommendHarness.Request(maxResults: 3);
        var feed = await _harness.Engine.GetFeedAsync(request);

        // The premise: it really is outside the top nine before probing.
        Assert.True(await PreliminaryRankAsync(request, gem.ReleaseId) >= 9,
            "the fixture must actually put the gem outside the old shortlist");

        var item = Assert.Single(feed.Items, i => i.ReleaseId == gem.ReleaseId);
        Assert.Contains(item.Signals, s => s.Signal == SignalNames.TriedToLikeIt);
        Assert.All(
            feed.Items.Where(i => i.ReleaseId != gem.ReleaseId),
            other => Assert.True(item.Score > other.Score,
                "the leapfrog candidate must actually beat the rows that displaced it"));
    }

    [Fact]
    public void The_safe_bound_keeps_only_candidates_that_could_still_reach_the_cut()
    {
        // The rule in isolation: a row is dropped only when the BEST it could
        // become cannot reach the WORST the k-th row could become. Never-opened
        // rows can hide nothing (no minutes means no episodes), so they are the
        // ones the bound is entitled to be strict about.
        var tuning = RecommendationTuning.Default;
        var contender = Candidate(id: 1, score: 0.30, minutes: 300);
        var shelfware = Candidate(id: 2, score: 0.05, minutes: 0);

        Assert.Equal(tuning.WeightTriedToLikeIt,
            ScoreBounds.MaxHiddenBonus(contender.Facts, tuning), precision: 12);
        Assert.Equal(0.0, ScoreBounds.MaxHiddenBonus(shelfware.Facts, tuning), precision: 12);

        var pool = new List<ScoredCandidate> { contender, shelfware };
        for (var i = 3; i < 40; i++)
        {
            pool.Add(Candidate(id: i, score: 0.28, minutes: 0));
        }

        var kept = ScoreBounds.SafeShortlist(pool, tuning, RecommendHarness.AsOf, take: 5, comfortMinimum: 0);

        Assert.Contains(kept, c => c.Facts.ReleaseId == 1);
        Assert.DoesNotContain(kept, c => c.Facts.ReleaseId == 2);
    }

    [Fact]
    public async Task A_twice_owned_game_does_not_consume_capacity_a_distinct_work_needed()
    {
        var asOf = RecommendHarness.AsOf;

        // Three games owned on two stores each: six ownership rows, three
        // works. Collapsing AFTER the shortlist let those six rows fill six
        // slots; collapsing first spends three.
        for (var i = 0; i < 3; i++)
        {
            var twice = await _harness.SeedGameAsync(
                $"Bought Twice {i:00}", minutes: 300, lastPlayed: asOf.AddYears(-2));
            await _harness.SeedSecondStoreAsync(twice, "gog");
        }

        var single = await _harness.SeedGameAsync("Owned Once", minutes: 320, lastPlayed: asOf.AddYears(-2));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request(maxResults: 2));

        Assert.Equal(7, feed.CandidateCount);
        Assert.Equal(4, feed.WorkCount);
        Assert.Equal(4, feed.HistoryProbeCount);
        Assert.Equal(feed.WorkCount, feed.HistoryProbeCount);

        // The signals the collapse must not cost: the bought-twice fact, and a
        // real store on the surviving copy.
        var twiceItem = feed.Items.First(i => i.Title.StartsWith("Bought Twice", StringComparison.Ordinal));
        Assert.Contains(twiceItem.Signals, s => s.Signal == SignalNames.BoughtTwice);
        Assert.False(string.IsNullOrEmpty(twiceItem.Store));

        // And with room for all four, every distinct work surfaces: the second
        // copies never took a slot from one.
        var roomForAll = await _harness.Engine.GetFeedAsync(RecommendHarness.Request(maxResults: 4));
        Assert.Equal(4, roomForAll.Items.Count);
        Assert.Equal(4, roomForAll.Items.Select(i => i.WorkId).Distinct().Count());
        Assert.Contains(roomForAll.Items, i => i.ReleaseId == single.ReleaseId);
    }

    [Fact]
    public async Task The_safe_bound_does_not_blow_the_probe_budget_on_a_library_shaped_like_the_real_one()
    {
        var asOf = RecommendHarness.AsOf;
        var random = new Random(7);

        // The measured shape: mostly never-opened, a long tail of old bounces,
        // a handful of patched comebacks. Correctness cannot be traded for
        // query count, so the bound is what it is — but on a real distribution
        // it has to stay cheap, and never-opened rows (which can hide nothing)
        // are what keeps it that way.
        for (var i = 0; i < 120; i++)
        {
            await _harness.SeedGameAsync($"Sealed {i:000}");
        }

        for (var i = 0; i < 60; i++)
        {
            await _harness.SeedGameAsync(
                $"Bounce {i:000}",
                minutes: 130 + random.Next(4_000),
                lastPlayed: asOf.AddDays(-400 - random.Next(2_500)));
        }

        for (var i = 0; i < 20; i++)
        {
            var patched = await _harness.SeedGameAsync(
                $"Patched {i:000}", minutes: 200 + random.Next(1_000),
                lastPlayed: asOf.AddYears(-3));
            await _harness.SeedMajorUpdateAsync(patched, asOf.AddMonths(-2), $"Update {i}");
        }

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request(maxResults: 20));

        Assert.Equal(200, feed.CandidateCount);
        Assert.Equal(200, feed.WorkCount);
        Assert.True(feed.HistoryProbeCount <= 100,
            $"the safe shortlist probed {feed.HistoryProbeCount} of 200 works");
        Assert.True(feed.HistoryProbeCount >= 20, "and it must still cover what the user will see");
    }

    /// <summary>Where a release sits when the pool is ranked with no history read at all.</summary>
    private async Task<int> PreliminaryRankAsync(RecommendationRequest request, long releaseId)
    {
        var feed = await _harness.Engine.GetFeedAsync(request with
        {
            MaxResults = 100,
            Tuning = request.Tuning with { WeightTriedToLikeIt = 0 },
        });

        return feed.Items.Select(i => i.ReleaseId).ToList().IndexOf(releaseId);
    }

    private static ScoredCandidate Candidate(long id, double score, long minutes)
    {
        var facts = new CandidateFacts
        {
            OwnershipId = id,
            ReleaseId = id,
            WorkId = id,
            Title = $"Fixture {id}",
            Store = "steam",
            Bucket = minutes > 0 ? LibraryBuckets.Bounced : LibraryBuckets.NeverPlayed,
            PlaytimeMinutes = minutes,
            LastPlayedAt = minutes > 0 ? RecommendHarness.AsOf.AddYears(-2) : null,
        };

        return new ScoredCandidate(facts, [], score);
    }
}
