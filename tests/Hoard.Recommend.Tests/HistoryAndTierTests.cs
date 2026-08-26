using Xunit;

namespace Hoard.Recommend.Tests;

/// <summary>
/// What accrued history changes: tier detection finds evidence where it
/// physically lives (the recently played rows), and the tried-to-like-it
/// bonus separates "40 minutes once" from "40 minutes across six evenings" —
/// the distinction the charter says no storefront can make.
/// </summary>
public class HistoryAndTierTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Snapshot_rises_alone_promote_the_library_to_settling()
    {
        var asOf = RecommendHarness.AsOf;
        // The harness's baseline snapshot sits at AsOf-1d; the rise must be
        // OBSERVED LATER than the baseline, or the series reads backwards.
        var game = await _harness.SeedGameAsync("Getting Snapshots", minutes: 200, lastPlayed: asOf.AddDays(-30));
        await _harness.SeedSnapshotAsync(game, minutes: 260, observedAt: asOf.AddHours(-2));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        Assert.Equal(DataTier.Settling, feed.Tier);
    }

    [Fact]
    public async Task History_on_a_recently_played_game_is_found_even_though_the_feed_ranks_it_low()
    {
        var asOf = RecommendHarness.AsOf;

        // The evidence sits on a game played two days ago — a row the feed
        // deliberately sinks. Tier detection must probe it anyway: probing
        // only the shortlist would examine sixty dormant games and conclude
        // the library has no history while the user racks up sessions.
        var current = await _harness.SeedGameAsync("Currently Playing", minutes: 900, lastPlayed: asOf.AddDays(-2));
        await _harness.SeedSessionAsync(current, asOf.AddDays(-2));
        await _harness.SeedGameAsync("Dormant Candidate", minutes: 300, lastPlayed: asOf.AddYears(-3));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        Assert.Equal(DataTier.Settling, feed.Tier);
    }

    [Fact]
    public async Task Enough_sessions_over_enough_weeks_is_established()
    {
        var asOf = RecommendHarness.AsOf;
        var game = await _harness.SeedGameAsync("The Daily Driver", minutes: 3_000, lastPlayed: asOf.AddDays(-1));

        // 60 sessions across ~9 weeks: past both Tier-2 gates (50 sessions,
        // 56 days of span).
        for (var i = 0; i < 60; i++)
        {
            await _harness.SeedSessionAsync(game, asOf.AddDays(-1 - i));
        }

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        Assert.Equal(DataTier.Established, feed.Tier);
    }

    [Fact]
    public async Task Coming_back_repeatedly_outranks_the_same_minutes_spent_once()
    {
        var asOf = RecommendHarness.AsOf;

        // Identical on every Tier-0 axis: same minutes, same dormancy. One
        // accrued its 240 minutes across five observed rises; the other is a
        // single sitting. The second is someone trying to like it.
        var persistent = await _harness.SeedGameAsync("Tried Five Times", minutes: 140, lastPlayed: asOf.AddYears(-1));
        for (var i = 0; i < 5; i++)
        {
            await _harness.SeedSnapshotAsync(persistent, minutes: 160 + i * 20, observedAt: asOf.AddYears(-1).AddDays(i + 1));
        }

        var oneSitting = await _harness.SeedGameAsync("One Long Evening", minutes: 240, lastPlayed: asOf.AddYears(-1));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        var order = feed.Items.Select(i => i.ReleaseId).ToList();
        Assert.True(order.IndexOf(persistent.ReleaseId) < order.IndexOf(oneSitting.ReleaseId),
            "return episodes must outweigh a small commitment-curve difference");
        Assert.Contains(
            feed.Items.Single(i => i.ReleaseId == persistent.ReleaseId).Signals,
            s => s.Signal == SignalNames.TriedToLikeIt);
    }

    [Fact]
    public async Task The_stale_reason_names_what_was_missed_when_history_has_the_detail()
    {
        var asOf = RecommendHarness.AsOf;
        var game = await _harness.SeedGameAsync("Left Then Patched", minutes: 200, lastPlayed: asOf.AddYears(-2));
        await _harness.SeedMajorUpdateAsync(game, asOf.AddYears(-1), "The Big Rework");
        await _harness.SeedMajorUpdateAsync(game, asOf.AddMonths(-2), "Season Two");

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        var item = feed.Items.Single(i => i.ReleaseId == game.ReleaseId);
        Assert.Contains("2 updates since", item.Reason);
        Assert.Contains("Season Two", item.Reason);
    }
}
