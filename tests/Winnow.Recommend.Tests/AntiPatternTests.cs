using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The charter's named anti-patterns, one by one: retired games never
/// resurface, the user's verdicts are honoured, correctly-abandoned games are
/// said out loud, and the feed rotates instead of repeating.
/// </summary>
public class AntiPatternTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Retired_never_surfaces_no_matter_how_patched()
    {
        var asOf = RecommendHarness.AsOf;
        var retired = await _harness.SeedGameAsync("The 200 Hour Game", minutes: 12_000, lastPlayed: asOf.AddYears(-4));
        await _harness.SeedMajorUpdateAsync(retired, asOf.AddMonths(-1), "Huge Free DLC");
        await _harness.SeedGameAsync("Ordinary Shelfware");

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        Assert.DoesNotContain(feed.Items, i => i.ReleaseId == retired.ReleaseId);
        Assert.Equal(1, feed.CandidateCount);
    }

    [Fact]
    public async Task Not_interested_and_snoozed_are_hard_exclusions()
    {
        var dismissed = await _harness.SeedGameAsync("Told You No", minutes: 300, lastPlayed: RecommendHarness.AsOf.AddYears(-3));
        var snoozed = await _harness.SeedGameAsync("Not Right Now", minutes: 300, lastPlayed: RecommendHarness.AsOf.AddYears(-3));
        var control = await _harness.SeedGameAsync("Still Eligible", minutes: 300, lastPlayed: RecommendHarness.AsOf.AddYears(-3));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request() with
        {
            NotInterestedReleaseIds = new HashSet<long> { dismissed.ReleaseId },
            SnoozedReleaseIds = new HashSet<long> { snoozed.ReleaseId },
        });

        Assert.Single(feed.Items);
        Assert.Equal(control.ReleaseId, feed.Items[0].ReleaseId);
    }

    [Fact]
    public async Task A_correctly_abandoned_game_is_demoted_and_the_reason_says_you_were_right()
    {
        var asOf = RecommendHarness.AsOf;
        // 41 hours, eight years ago, nothing changed since: the fair-shake
        // case. The seeded announcement is what makes "nothing changed since"
        // a FACT rather than a gap in Winnow's coverage (F15) — without it the
        // model is not entitled to the verdict.
        var done = await _harness.SeedGameAsync("Fair Shake Given", minutes: 2_500, lastPlayed: asOf.AddYears(-8));
        await _harness.SeedUpdateCoverageAsync(done, asOf.AddYears(-9), "1.0 Release Notes");
        // A modest old bounce — the feed's actual target.
        var modest = await _harness.SeedGameAsync("Modest Old Bounce", minutes: 300, lastPlayed: asOf.AddYears(-8));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        var doneItem = Assert.Single(feed.Items, i => i.ReleaseId == done.ReleaseId);
        var modestItem = Assert.Single(feed.Items, i => i.ReleaseId == modest.ReleaseId);

        Assert.True(modestItem.Score > doneItem.Score,
            "a fair-shake-and-left game must rank below a modest bounce of the same age");
        Assert.Contains(doneItem.Signals, s => s.Signal == SignalNames.ProbablyDone && s.Contribution < 0);
        // The verdict leads the sentence: a demoted row with a cheerful reason
        // would be the model lying about its own arithmetic.
        Assert.Equal(ReasonSignal.ProbablyDone, doneItem.Explanation.Primary);
    }

    [Fact]
    public async Task Recently_surfaced_games_rotate_behind_their_unshown_peers()
    {
        var asOf = RecommendHarness.AsOf;
        var shownYesterday = await _harness.SeedGameAsync("Shown Yesterday", minutes: 300, lastPlayed: asOf.AddYears(-3));
        var neverShown = await _harness.SeedGameAsync("Never Shown", minutes: 300, lastPlayed: asOf.AddYears(-3));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request() with
        {
            RecentlySurfacedReleaseIds = new HashSet<long> { shownYesterday.ReleaseId },
        });

        Assert.Equal(neverShown.ReleaseId, feed.Items[0].ReleaseId);
        Assert.Contains(
            feed.Items.Single(i => i.ReleaseId == shownYesterday.ReleaseId).Signals,
            s => s.Signal == SignalNames.RecentlySurfaced);
    }

    [Fact]
    public async Task A_provisionally_named_work_cannot_be_recommended()
    {
        await _harness.SeedGameAsync("App 1203620", provisionalName: true);
        await _harness.SeedGameAsync("Real Title");

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        var item = Assert.Single(feed.Items);
        Assert.Equal("Real Title", item.Title);
    }

    [Fact]
    public async Task One_work_owned_twice_is_one_feed_entry_with_the_bought_twice_signal()
    {
        var game = await _harness.SeedGameAsync("Bought On Two Stores", minutes: 300, lastPlayed: RecommendHarness.AsOf.AddYears(-3));
        await _harness.SeedSecondStoreAsync(game, "gog");

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        var item = Assert.Single(feed.Items);
        Assert.Contains(item.Signals, s => s.Signal == SignalNames.BoughtTwice);
    }

    [Fact]
    public async Task A_different_day_deals_a_different_hand_inside_the_shelfware_pile()
    {
        // Twelve indistinguishable never-opened games: nothing but jitter can
        // order them, and jitter MUST reorder them across seeds — that is the
        // whole defence against "the same five games forever" at Tier 0.
        for (var i = 0; i < 12; i++)
        {
            await _harness.SeedGameAsync($"Shelfware {i:00}");
        }

        var monday = await _harness.Engine.GetFeedAsync(RecommendHarness.Request() with { ShuffleSeed = 1 });
        var tuesday = await _harness.Engine.GetFeedAsync(RecommendHarness.Request() with { ShuffleSeed = 2 });

        Assert.NotEqual(
            monday.Items.Select(i => i.ReleaseId).ToList(),
            tuesday.Items.Select(i => i.ReleaseId).ToList());
    }
}
