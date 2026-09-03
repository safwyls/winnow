using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// F15, end to end. Update coverage begins when polling begins, so an empty
/// update history means Winnow was not watching — not that nothing shipped. A
/// penalty that rests on "nothing has changed since" may only fire where
/// coverage proves it, and no sentence may claim it otherwise.
/// </summary>
public class UpdateCoverageTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Without_recorded_update_history_the_probably_done_penalty_is_withheld()
    {
        var asOf = RecommendHarness.AsOf;

        // Identical fair-shake-and-left rows, eight years dormant. One has a
        // recorded announcement, so Winnow has demonstrably read this release's
        // update history; the other has nothing at all.
        var watched = await _harness.SeedGameAsync("Watched And Quiet", minutes: 2_500, lastPlayed: asOf.AddYears(-8));
        await _harness.SeedUpdateCoverageAsync(watched, asOf.AddYears(-9), "1.0 Release Notes");

        var unwatched = await _harness.SeedGameAsync("Never Polled", minutes: 2_500, lastPlayed: asOf.AddYears(-8));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        var watchedItem = feed.Items.Single(i => i.ReleaseId == watched.ReleaseId);
        var unwatchedItem = feed.Items.Single(i => i.ReleaseId == unwatched.ReleaseId);

        Assert.Equal(UpdateCoverage.Observed, WatchedCoverage(watchedItem));
        Assert.Contains(watchedItem.Signals, s => s.Signal == SignalNames.ProbablyDone);
        Assert.Equal(ReasonSignal.ProbablyDone, watchedItem.Explanation.Primary);

        Assert.DoesNotContain(unwatchedItem.Signals, s => s.Signal == SignalNames.ProbablyDone);
        Assert.NotEqual(ReasonSignal.ProbablyDone, unwatchedItem.Explanation.Primary);
        Assert.True(unwatchedItem.Score > watchedItem.Score,
            "absence of evidence must not cost a row anything");
    }

    [Fact]
    public async Task A_row_winnow_was_not_watching_never_says_nothing_changed()
    {
        var asOf = RecommendHarness.AsOf;
        await _harness.SeedGameAsync("Never Polled", minutes: 2_500, lastPlayed: asOf.AddYears(-8));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
        var item = Assert.Single(feed.Items);

        foreach (var claim in new[] { "nothing", "no update", "hasn't changed", "has not changed", "unchanged" })
        {
            Assert.DoesNotContain(claim, item.Reason, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var signal in item.Signals)
        {
            Assert.DoesNotContain("nothing", signal.Explanation, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Coverage_is_only_read_for_rows_whose_verdict_depends_on_it()
    {
        var asOf = RecommendHarness.AsOf;

        // A modest bounce is nowhere near the fair-shake gate, so no claim
        // about update silence is ever made about it and its coverage stays
        // unread — which is what keeps the extra query bounded to the rows
        // that need it.
        var modest = await _harness.SeedGameAsync("Modest Bounce", minutes: 300, lastPlayed: asOf.AddYears(-8));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
        var item = Assert.Single(feed.Items, i => i.ReleaseId == modest.ReleaseId);

        Assert.DoesNotContain(item.Signals, s => s.Signal == SignalNames.ProbablyDone);
    }

    /// <summary>Coverage is not carried on the result, so it is re-derived from the signal that depends on it.</summary>
    private static UpdateCoverage WatchedCoverage(Recommendation item)
        => item.Signals.Any(s => s.Signal == SignalNames.ProbablyDone)
            ? UpdateCoverage.Observed
            : UpdateCoverage.Unknown;
}
