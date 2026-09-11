using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

public sealed class ResolvedGameEvidenceTests
{
    [Fact]
    public async Task Installed_sibling_supplies_action_while_the_kept_title_and_game_identity_remain()
    {
        using var harness = new RecommendHarness();
        var parent = await harness.SeedGameAsync("Kept title");
        var sibling = await harness.SeedGameAsync("Other store title", store: "epic", installed: true);
        await harness.Links.LinkAsync(new() { ParentWorkId = parent.WorkId, ChildWorkIds = [sibling.WorkId] });
        var shelf = Assert.Single((await harness.Engine.GetShelvesAsync(RecommendHarness.Request())).Shelves);
        Assert.Equal(ShelfIds.ReadyToPlay, shelf.Id);
        var item = Assert.Single(shelf.Items);
        Assert.Equal(sibling.OwnershipId, item.OwnershipId);
        Assert.Equal(sibling.ReleaseId, item.ReleaseId);
        Assert.Equal(parent.WorkId, item.WorkId);
        Assert.Equal("Kept title", item.Title);
        Assert.Equal(2, item.Explanation.Evidence.StoreCount);
        Assert.Equal(new[] { parent.ReleaseId, sibling.ReleaseId }.Order(), item.Explanation.Evidence.EvidenceReleaseIds.Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hidden_sibling_feedback_stays_effective_across_account_visibility_and_unlink(bool snoozed)
    {
        using var harness = new RecommendHarness();
        var parent = await harness.SeedGameAsync("Visible Epic", store: "epic");
        var child = await harness.SeedGameAsync("Hidden Steam");
        await harness.Links.LinkAsync(new() { ParentWorkId = parent.WorkId, ChildWorkIds = [child.WorkId] });
        await harness.OwnershipAccounts.UpsertAsync(new(child.OwnershipId, "22222", 0, null, "steam_web", RecommendHarness.AsOf));
        await harness.Settings.SetAsync(SteamOwnedAccount.RefSettingKey, "11111");
        await harness.Settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        var request = RecommendHarness.Request() with
        {
            NotInterestedReleaseIds = snoozed ? [] : new HashSet<long> { child.ReleaseId },
            SnoozedReleaseIds = snoozed ? new HashSet<long> { child.ReleaseId } : [],
        };
        Assert.Single((await harness.Engine.GetShelvesAsync(RecommendHarness.Request())).Shelves);
        Assert.Empty((await harness.Engine.GetShelvesAsync(request)).Shelves);
        await harness.Settings.SetAsync(AccountScope.SettingKey, AccountScope.All);
        Assert.Empty((await harness.Engine.GetFeedAsync(request)).Items);
        await harness.Links.RetractLinkAsync(child.WorkId);
        Assert.Equal(parent.ReleaseId, Assert.Single((await harness.Engine.GetFeedAsync(request)).Items).ReleaseId);
    }

    [Fact]
    public async Task Group_history_counts_distinct_play_episodes_and_preserves_patch_provenance()
    {
        using var harness = new RecommendHarness();
        var now = RecommendHarness.AsOf;
        var parent = await harness.SeedGameAsync("Kept", 300, now.AddYears(-2));
        var child = await harness.SeedGameAsync("Sibling", 300, now.AddYears(-2), store: "epic");
        await harness.Links.LinkAsync(new() { ParentWorkId = parent.WorkId, ChildWorkIds = [child.WorkId] });
        await harness.SeedSessionAsync(parent, now.AddYears(-2));
        await harness.SeedSessionAsync(child, now.AddYears(-2).AddMinutes(5));
        await harness.SeedSessionAsync(child, now.AddYears(-2).AddDays(1));
        await harness.SeedMajorUpdateAsync(child, now.AddDays(-10), "Sibling update");
        await harness.SeedMajorUpdateAsync(parent, now.AddDays(-10), "Duplicate storefront update");
        var evidence = Assert.Single((await harness.Engine.GetFeedAsync(RecommendHarness.Request())).Items).Explanation.Evidence;
        Assert.Equal(2, evidence.ReturnEpisodes);
        Assert.Equal(1, evidence.UpdatesSinceLastPlayed);
        Assert.Contains(evidence.LatestUpdateReleaseId, new long?[] { parent.ReleaseId, child.ReleaseId });
        Assert.NotNull(evidence.LatestUpdateTitle);
    }

    [Fact]
    public async Task Facets_on_the_sibling_supply_taste_and_duplicate_store_observations_do_not_reweight_it()
    {
        using var harness = new RecommendHarness();
        var anchor = await harness.SeedGameAsync("Played anchor", 3000, RecommendHarness.AsOf.AddYears(-1));
        await harness.SeedGenreAsync(anchor, "Survival");
        var parent = await harness.SeedGameAsync("Kept");
        var child = await harness.SeedGameAsync("Sibling", store: "epic");
        await harness.SeedGenreAsync(child, "Survival");
        await harness.Links.LinkAsync(new() { ParentWorkId = parent.WorkId, ChildWorkIds = [child.WorkId] });
        var before = Assert.Single((await harness.Engine.GetFeedAsync(RecommendHarness.Request())).Items, item => item.WorkId == parent.WorkId);
        Assert.Equal(1, before.Explanation.Evidence.TasteAffinity);
        await harness.SeedSecondStoreAsync(anchor, "gog");
        var after = Assert.Single((await harness.Engine.GetFeedAsync(RecommendHarness.Request())).Items, item => item.WorkId == parent.WorkId);
        Assert.Equal(before.Score, after.Score);
        Assert.Equal(before.Explanation.Evidence.TasteFacetName, after.Explanation.Evidence.TasteFacetName);
    }

    [Fact]
    public async Task A_cold_uninstalled_library_has_honest_shelves_and_exclusions_still_win()
    {
        using var harness = new RecommendHarness();
        var eligible = await harness.SeedGameAsync("No recorded play");
        await harness.SeedGameAsync("App 1234", provisionalName: true);
        await harness.SeedGameAsync("Finished", minutes: 12000);
        var request = RecommendHarness.Request();
        var feed = await harness.Engine.GetShelvesAsync(request);
        var shelf = Assert.Single(feed.Shelves);
        Assert.Equal(DataTier.ColdStart, feed.Tier);
        Assert.Equal(ShelfIds.WaitingToBeOpened, shelf.Id);
        var item = Assert.Single(shelf.Items);
        Assert.Equal(eligible.ReleaseId, item.ReleaseId);
        Assert.Equal(0, item.Explanation.Evidence.PlaytimeMinutes);
        Assert.Null(item.Explanation.Evidence.TasteAffinity);
        Assert.Empty((await harness.Engine.GetShelvesAsync(request with
        { NotInterestedReleaseIds = new HashSet<long> { eligible.ReleaseId } })).Shelves);
    }
}
