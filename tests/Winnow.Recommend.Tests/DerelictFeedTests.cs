using Winnow.Core.Identity;
using Winnow.Core.Domain;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

public sealed class DerelictFeedTests
{
    [Fact]
    public async Task Review_obeys_hidden_games_and_account_scope()
    {
        using var harness = new RecommendHarness();
        var mine = await harness.SeedGameAsync("My closed game");
        var theirs = await harness.SeedGameAsync("Their closed game");
        foreach (var game in new[] { mine, theirs })
        {
            await Observe(harness, game, "offline");
            await harness.OwnershipAccounts.UpsertAsync(new OwnershipAccountUpsert(
                game.OwnershipId, game == mine ? "11111" : "22222", 0, null, "steam_web", RecommendHarness.AsOf));
        }

        await harness.Settings.SetAsync(SteamOwnedAccount.RefSettingKey, "11111");
        await harness.Settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        var visible = await harness.Engine.GetShelvesAsync(RecommendHarness.Request());
        Assert.Equal(mine.ReleaseId, Assert.Single(Assert.Single(visible.Shelves).Items).ReleaseId);
        await harness.HiddenGames.HideAsync(mine.WorkId);
        Assert.Empty((await harness.Engine.GetShelvesAsync(RecommendHarness.Request())).Shelves);
    }

    private static async Task<long> Observe(RecommendHarness harness, SeededGame game, string status)
    {
        await harness.Works.ApplyEnrichmentAsync(new WorkEnrichment(game.WorkId, IgdbId: game.WorkId));
        return await harness.Lifecycle.AppendAsync(new LifecycleObservation
        {
            ReleaseId = game.ReleaseId,
            Source = "igdb",
            SourceId = game.WorkId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ObservedAt = DateTime.UtcNow.AddMinutes(-1),
            Signals = new LifecycleSignals { IgdbStatus = status },
        });
    }

    [Fact]
    public async Task Lifecycle_review_is_separate_from_play_scores_and_flat_feed()
    {
        using var harness = new RecommendHarness();
        var playable = await harness.SeedGameAsync("Waiting", installed: true);
        var closed = await harness.SeedGameAsync("Closed", installed: true);
        await Observe(harness, closed, "offline");

        var shelves = await harness.Engine.GetShelvesAsync(RecommendHarness.Request());
        var review = Assert.Single(shelves.Shelves, s => s.Id == ShelfIds.Derelict);
        var item = Assert.Single(review.Items);
        Assert.Equal(closed.ReleaseId, item.ReleaseId);
        Assert.Equal(LibraryBuckets.Derelict, item.Bucket);
        Assert.Equal(GameLifecycleStatus.Offline, item.Explanation.Evidence.Lifecycle!.Status);
        Assert.Contains("confidence", item.Reason);
        Assert.Contains("offline", item.Reason);
        Assert.Equal(0, item.Score);
        Assert.Empty(item.Signals);
        Assert.Equal(1, shelves.CandidateCount);
        Assert.Equal(1, shelves.WorkCount);
        Assert.Equal(1, shelves.HistoryProbeCount);
        Assert.DoesNotContain(shelves.Shelves.Where(s => s.Id != ShelfIds.Derelict).SelectMany(s => s.Items),
            i => i.ReleaseId == closed.ReleaseId);
        var flat = await harness.Engine.GetFeedAsync(RecommendHarness.Request());
        Assert.Equal(playable.ReleaseId, Assert.Single(flat.Items).ReleaseId);
    }

    [Fact]
    public async Task Linked_review_entries_collapse_and_either_copy_feedback_suppresses_them()
    {
        using var harness = new RecommendHarness();
        var steam = await harness.SeedGameAsync("Closed Steam");
        var gog = await harness.SeedGameAsync("Closed GOG", store: "gog");
        await Observe(harness, steam, "offline");
        await Observe(harness, gog, "offline");
        await harness.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = steam.WorkId,
            ChildWorkIds = [gog.WorkId],
        });
        var request = RecommendHarness.Request();
        var initial = await harness.Engine.GetShelvesAsync(request);
        Assert.Single(Assert.Single(initial.Shelves).Items);
        var dismissed = await harness.Engine.GetShelvesAsync(request with { NotInterestedReleaseIds = new HashSet<long> { gog.ReleaseId } });
        Assert.Empty(dismissed.Shelves);
        var snoozed = await harness.Engine.GetShelvesAsync(request with { SnoozedReleaseIds = new HashSet<long> { steam.ReleaseId } });
        Assert.Empty(snoozed.Shelves);
    }

    [Fact]
    public async Task A_linked_game_recommends_its_viable_copy_instead_of_the_offline_primary()
    {
        using var harness = new RecommendHarness();
        var primary = await harness.SeedGameAsync("Closed primary", installed: true);
        var viable = await harness.SeedGameAsync("Viable sibling", store: "gog", installed: true);
        await Observe(harness, primary, "offline");
        await harness.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = primary.WorkId,
            ChildWorkIds = [viable.WorkId],
        });

        var request = RecommendHarness.Request();
        var flat = await harness.Engine.GetFeedAsync(request);
        Assert.Equal(viable.OwnershipId, Assert.Single(flat.Items).OwnershipId);
        var shelves = await harness.Engine.GetShelvesAsync(request);
        Assert.DoesNotContain(shelves.Shelves, s => s.Id == ShelfIds.Derelict);
        Assert.Equal(viable.OwnershipId, Assert.Single(shelves.Shelves.SelectMany(s => s.Items)).OwnershipId);
        var dismissed = await harness.Engine.GetFeedAsync(request with
        {
            NotInterestedReleaseIds = new HashSet<long> { primary.ReleaseId },
        });
        Assert.Empty(dismissed.Items);
    }

    [Fact]
    public async Task Review_reserves_preserve_visible_prefix_and_recent_cards_rotate_behind_unseen()
    {
        using var harness = new RecommendHarness();
        await harness.SeedBatchAsync(async () =>
        {
            // A library-sized pool verifies the separate shelf never consumes history capacity.
            for (var i = 0; i < 1000; i++)
            {
                var game = await harness.SeedGameAsync($"Library entry {i}", store: i < 919 ? "steam" : i < 986 ? "epic" : "gog");
                if (i < 30) await Observe(harness, game, "delisted");
            }
        });
        var request = RecommendHarness.Request() with { MaxPerShelf = 6, VisiblePerShelf = 6 };
        var shallow = Assert.Single((await harness.Engine.GetShelvesAsync(request)).Shelves);
        var deep = Assert.Single((await harness.Engine.GetShelvesAsync(request with { MaxPerShelf = 10 })).Shelves);
        Assert.Equal(shallow.Items.Select(i => i.ReleaseId), deep.Items.Take(6).Select(i => i.ReleaseId));
        Assert.Equal(10, deep.Items.Count);
        var seen = deep.Items.Select(i => i.ReleaseId).ToHashSet();
        var rotated = Assert.Single((await harness.Engine.GetShelvesAsync(request with { RecentlySurfacedReleaseIds = seen })).Shelves);
        Assert.DoesNotContain(rotated.Items, i => seen.Contains(i.ReleaseId));
    }
}
