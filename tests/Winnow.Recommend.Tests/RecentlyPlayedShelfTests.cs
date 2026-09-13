using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

public sealed class RecentlyPlayedShelfTests
{
    [Fact]
    public async Task Ten_latest_games_lead_the_feed_without_score_or_feedback_rotation()
    {
        using var harness = new RecommendHarness();
        var played = new List<SeededGame>();
        await harness.SeedBatchAsync(async () =>
        {
            for (var i = 0; i < 300; i++)
            {
                var game = await harness.SeedGameAsync($"Saga: Game {i}",
                    minutes: i < 15 ? 12000 : 0,
                    lastPlayed: i < 15 ? RecommendHarness.AsOf.AddDays(-i) : null,
                    store: i % 3 == 0 ? "gog" : "steam");
                if (i < 15) played.Add(game);
            }
        });
        var request = RecommendHarness.Request() with { MaxPerShelf = 6, VisiblePerShelf = 6 };
        var first = (await harness.Engine.GetShelvesAsync(request)).Shelves[0];
        Assert.Equal(ShelfIds.RecentlyPlayed, first.Id);
        Assert.False(first.SupportsFeedback);
        Assert.Equal(played.Take(10).Select(game => game.ReleaseId), first.Items.Select(item => item.ReleaseId));
        Assert.All(first.Items, item =>
        {
            Assert.Equal(0, item.Score);
            Assert.Empty(item.Signals);
            Assert.Equal(ReasonSignal.LastPlayed, item.Explanation.Primary);
            Assert.StartsWith("Last played on ", item.Reason);
        });
        var all = played.Select(game => game.ReleaseId).ToHashSet();
        var changed = (await harness.Engine.GetShelvesAsync(request with
        {
            MaxPerShelf = 10, ShuffleSeed = 999,
            RecentlySurfacedReleaseIds = all, NotInterestedReleaseIds = all,
            SnoozedReleaseIds = all, EndorsedReleaseIds = all,
        })).Shelves[0];
        Assert.Equal(first.Items.Select(item => item.ReleaseId), changed.Items.Select(item => item.ReleaseId));
    }

    [Fact]
    public async Task Grouped_last_play_orders_one_game_and_preserves_the_viable_action()
    {
        using var harness = new RecommendHarness();
        var kept = await harness.SeedGameAsync("Kept", 40, RecommendHarness.AsOf.AddDays(-5), installed: true);
        var sibling = await harness.SeedGameAsync("Sibling", 30, RecommendHarness.AsOf, store: "gog");
        await harness.Links.LinkAsync(new() { ParentWorkId = kept.WorkId, ChildWorkIds = [sibling.WorkId] });
        await harness.SeedGameAsync("Other", 30, RecommendHarness.AsOf.AddDays(-1));
        var shelf = (await harness.Engine.GetShelvesAsync(RecommendHarness.Request())).Shelves[0];
        Assert.Equal(2, shelf.Items.Count);
        Assert.Equal(kept.WorkId, shelf.Items[0].WorkId);
        Assert.Equal(kept.OwnershipId, shelf.Items[0].OwnershipId);
        Assert.Equal(RecommendHarness.AsOf, shelf.Items[0].Explanation.Evidence.LastPlayedAt);
    }

    [Fact]
    public async Task Unknown_dates_hidden_and_provisional_games_do_not_fill_history()
    {
        using var harness = new RecommendHarness();
        await harness.SeedGameAsync("Unknown date", 400);
        await harness.SeedGameAsync("Never played");
        await harness.SeedGameAsync("App 123", 30, RecommendHarness.AsOf, provisionalName: true);
        var hidden = await harness.SeedGameAsync("Hidden", 30, RecommendHarness.AsOf);
        await harness.HiddenGames.HideAsync(hidden.WorkId);
        var shelves = (await harness.Engine.GetShelvesAsync(RecommendHarness.Request())).Shelves;
        Assert.DoesNotContain(shelves, shelf => shelf.Id == ShelfIds.RecentlyPlayed);
    }

    [Fact]
    public async Task Ties_use_game_identity_and_future_dates_cannot_lead_history()
    {
        using var harness = new RecommendHarness();
        var first = await harness.SeedGameAsync("First", 30, RecommendHarness.AsOf);
        var second = await harness.SeedGameAsync("Second", 30, RecommendHarness.AsOf);
        await harness.SeedGameAsync("Future", 30, RecommendHarness.AsOf.AddDays(1));
        var shelf = (await harness.Engine.GetShelvesAsync(RecommendHarness.Request())).Shelves[0];
        Assert.Equal(new[] { first.WorkId, second.WorkId }, shelf.Items.Select(item => item.WorkId));
    }

    [Fact]
    public async Task Recent_collection_uses_selected_account_play_history()
    {
        using var harness = new RecommendHarness();
        var mine = await harness.SeedGameAsync("Mine", 30, RecommendHarness.AsOf);
        var theirs = await harness.SeedGameAsync("Theirs", 30, RecommendHarness.AsOf);
        await harness.OwnershipAccounts.UpsertAsync(new(mine.OwnershipId, "11111", 30,
            RecommendHarness.AsOf.AddDays(-2), "steam_web", RecommendHarness.AsOf));
        await harness.OwnershipAccounts.UpsertAsync(new(theirs.OwnershipId, "22222", 30,
            RecommendHarness.AsOf, "steam_web", RecommendHarness.AsOf));
        await harness.Settings.SetAsync(SteamOwnedAccount.RefSettingKey, "11111");
        await harness.CompleteSteamInventoryAsync("11111", 1);
        await harness.Settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        var shelf = (await harness.Engine.GetShelvesAsync(RecommendHarness.Request())).Shelves[0];
        Assert.Equal(mine.ReleaseId, Assert.Single(shelf.Items).ReleaseId);
        Assert.Equal(RecommendHarness.AsOf.AddDays(-2), shelf.Items[0].Explanation.Evidence.LastPlayedAt);
    }
}
