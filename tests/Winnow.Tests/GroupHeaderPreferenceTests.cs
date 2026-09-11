using Dapper;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class GroupHeaderPreferenceTests
{
    [Fact]
    public async Task Saved_store_changes_header_and_primary_route_but_preserves_metadata_and_identity()
    {
        using var fixture = await GroupHeaderFixture.CreateAsync();
        using var library = fixture.Library();
        using var queue = fixture.Queue(library);
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Sections.SelectMany(section => section.Cards));
        var before = await fixture.Links.GetHistoryAsync();
        await queue.SetGroupHeaderAsync(card, card.HeaderStoreOptions.Single(option => option.Store == "gog"));
        Assert.Equal("GOG title", card.HeaderTitle);
        Assert.Equal(fixture.Steam.Work, card.ParentWorkId);
        var tile = Assert.Single(library.VisibleTiles);
        Assert.Equal("GOG title", tile.Title);
        Assert.Equal("gog", tile.Store);
        Assert.Equal(2011, tile.ReleaseYear);
        Assert.Equal(fixture.Steam.Work, tile.Game.ResolvedWorkId);
        Assert.Equal(before, await fixture.Links.GetHistoryAsync());

        using var reopened = fixture.Queue();
        await reopened.LoadCommand.ExecuteAsync(null);
        var restored = Assert.Single(reopened.Sections.SelectMany(section => section.Cards));
        Assert.Equal("gog", restored.SelectedHeaderStore!.Store);
        Assert.Equal("GOG title", restored.HeaderTitle);
        await fixture.Ownership.UpsertAsync(new OwnershipUpsert(fixture.Gog.Release, "gog", null, null, null, null));
        Assert.Equal("gog", (await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
        await reopened.SetGroupHeaderAsync(restored, restored.HeaderStoreOptions.Single(option => option.Store is null));
        Assert.Equal("Steam title", restored.HeaderTitle);
        Assert.Null((await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
    }

    [Fact]
    public async Task Further_links_inherit_preference_and_undo_retains_original_identity_history()
    {
        using var fixture = await GroupHeaderFixture.CreateAsync();
        await fixture.Preferences.SetAsync(fixture.Steam.Work, "gog");
        var epic = await fixture.AddAsync("Epic title", "epic");
        var act = await fixture.LinkAsync(epic.Work, fixture.Steam.Work);
        Assert.Equal("gog", (await fixture.Preferences.GetAllAsync())[epic.Work]);
        Assert.False(await fixture.Preferences.SetAsync(fixture.Steam.Work, "steam"));
        using var library = fixture.Library();
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Equal("GOG title", Assert.Single(library.VisibleTiles).Title);
        var before = await fixture.Links.GetHistoryAsync();
        await fixture.Preferences.SetAsync(epic.Work, null);
        Assert.Null((await fixture.Preferences.GetAllAsync())[epic.Work]);
        Assert.Equal(before, await fixture.Links.GetHistoryAsync());
        await fixture.Links.RetractActAsync(act);
        Assert.Equal("gog", (await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
        var resolution = (await fixture.Links.GetResolutionAsync()).SameGame;
        Assert.Equal(fixture.Steam.Work, resolution.Resolve(fixture.Gog.Work));
        Assert.Equal(epic.Work, resolution.Resolve(epic.Work));
    }

    [Fact]
    public async Task Unavailable_preference_falls_back_and_can_be_reset_without_losing_the_saved_choice()
    {
        using var fixture = await GroupHeaderFixture.CreateAsync();
        await fixture.Preferences.SetAsync(fixture.Steam.Work, "gog");
        using (var lease = fixture.Db.Factory.Lease())
            await lease.Connection.ExecuteAsync("DELETE FROM ownerships WHERE release_id = @release", new { release = fixture.Gog.Release });
        using var library = fixture.Library();
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Steam title", Assert.Single(library.VisibleTiles).Title);
        using var queue = fixture.Queue();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Sections.SelectMany(section => section.Cards));
        Assert.Equal("GOG (unavailable)", card.SelectedHeaderStore!.Label);
        Assert.Equal("Steam title", card.HeaderTitle);
        Assert.False(await fixture.Preferences.SetAsync(fixture.Steam.Work, "gog"));
        await fixture.Ownership.UpsertAsync(new OwnershipUpsert(fixture.Gog.Release, "gog", null, null, null, null));
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Equal("GOG title", Assert.Single(library.VisibleTiles).Title);
        await queue.SetGroupHeaderAsync(card, card.HeaderStoreOptions.Single(option => option.Store is null));
        Assert.Null((await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
    }

    [Fact]
    public async Task Latest_explicit_choice_wins_when_groups_join_and_pending_queue_default_is_independent()
    {
        using var fixture = await GroupHeaderFixture.CreateAsync();
        var epic = await fixture.AddAsync("Epic title", "epic");
        var manual = await fixture.AddAsync("Manual title", "manual");
        await fixture.LinkAsync(epic.Work, manual.Work);
        await fixture.Preferences.SetAsync(fixture.Steam.Work, "gog");
        await fixture.Preferences.SetAsync(epic.Work, "manual");
        await fixture.LinkAsync(fixture.Steam.Work, epic.Work);
        Assert.Equal("manual", (await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
        using var queue = fixture.Queue();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.SelectPlatformCommand.ExecuteAsync(queue.PlatformOptions.Single(option => option.Store == "steam"));
        Assert.All(queue.Sections.SelectMany(section => section.Cards), card => Assert.Equal("Manual title", card.HeaderTitle));
        Assert.Equal("manual", (await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
    }

    [Fact]
    public async Task Expansion_relations_and_unlinked_works_cannot_receive_group_preferences()
    {
        using var fixture = await GroupHeaderFixture.CreateAsync();
        var expansion = await fixture.AddAsync("Expansion", "steam");
        await fixture.Links.LinkAsync(new IdentityLinkRequest { ParentWorkId = fixture.Steam.Work,
            ChildWorkIds = [expansion.Work], Kind = IdentityLinkKinds.ExpansionOf });
        Assert.False(await fixture.Preferences.SetAsync(expansion.Work, "steam"));
        var single = await fixture.AddAsync("Single", "gog");
        Assert.False(await fixture.Preferences.SetAsync(single.Work, "gog"));
        Assert.False(await fixture.Preferences.SetAsync(fixture.Steam.Work, "missing"));
    }
}
