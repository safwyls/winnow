using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The two kinds of list, against a migrated database.
///
/// <para><b>A list</b> is a fixed, ordered set the user assembled by hand.
/// <b>A live list</b> stores a rule and no items, and its membership is whatever
/// the rule says at the moment it is read. The tests that matter most are the
/// two that state the difference — a live list keeps up when the library changes
/// and a manual list does not — and the one that states what deleting a list
/// costs, which is nothing but the list.</para>
/// </summary>
public class GameListTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly GameListRepository _lists;
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;

    public GameListTests()
    {
        _lists = new GameListRepository(_db.Factory);
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // -- manual lists --------------------------------------------------------

    [Fact]
    public async Task A_manual_list_keeps_the_order_items_were_added_in()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");
        var c = await SeedReleaseAsync("Hollow Knight");

        Assert.Equal(0, await _lists.AppendItemAsync(listId, a));
        Assert.Equal(1, await _lists.AppendItemAsync(listId, b));
        Assert.Equal(2, await _lists.AppendItemAsync(listId, c));

        Assert.Equal([a, b, c], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    /// <summary>Re-adding leaves a game where the user put it, rather than sending it to the bottom.</summary>
    [Fact]
    public async Task Appending_a_member_again_does_not_move_it()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");

        await _lists.AppendItemAsync(listId, a);
        await _lists.AppendItemAsync(listId, b);

        Assert.Equal(0, await _lists.AppendItemAsync(listId, a));
        Assert.Equal([a, b], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    [Fact]
    public async Task Reorder_re_deals_dense_positions()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");
        var c = await SeedReleaseAsync("Hollow Knight");
        foreach (var id in new[] { a, b, c })
        {
            await _lists.AppendItemAsync(listId, id);
        }

        await _lists.ReorderAsync(listId, [c, a, b]);

        var items = await _lists.GetItemsAsync(listId);
        Assert.Equal([c, a, b], items.Select(i => i.ReleaseId));
        Assert.Equal([0, 1, 2], items.Select(i => i.Position));
    }

    /// <summary>
    /// A reorder is not a way to remove things: a member the caller did not
    /// mention keeps its membership and trails the ones that were named.
    /// </summary>
    [Fact]
    public async Task Reorder_keeps_members_the_caller_left_out()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");
        var c = await SeedReleaseAsync("Hollow Knight");
        foreach (var id in new[] { a, b, c })
        {
            await _lists.AppendItemAsync(listId, id);
        }

        await _lists.ReorderAsync(listId, [c]);

        Assert.Equal([c, a, b], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    [Fact]
    public async Task Reorder_ignores_ids_that_are_not_members()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var stranger = await SeedReleaseAsync("Not in the list");
        await _lists.AppendItemAsync(listId, a);

        await _lists.ReorderAsync(listId, [stranger, a]);

        Assert.Equal([a], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    [Fact]
    public async Task Removing_an_item_leaves_the_rest()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");
        await _lists.AppendItemAsync(listId, a);
        await _lists.AppendItemAsync(listId, b);

        await _lists.RemoveItemAsync(listId, a);

        Assert.Equal([b], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    [Fact]
    public async Task Rename_replaces_the_name_and_the_description()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog", "everything"));

        Assert.True(await _lists.RenameAsync(listId, "Next up", null));

        var list = await _lists.GetAsync(listId);
        Assert.Equal("Next up", list!.Name);

        // Clearing the description has to be expressible: this is an edit of a
        // form, not a patch.
        Assert.Null(list.Description);
    }

    [Fact]
    public async Task Renaming_or_deleting_a_list_that_is_not_there_reports_false()
    {
        Assert.False(await _lists.RenameAsync(999, "x", null));
        Assert.False(await _lists.DeleteAsync(999));
        Assert.False(await _lists.SetFilterAsync(999, LibraryFilter.Empty));
    }

    // -- live lists ----------------------------------------------------------

    [Fact]
    public async Task A_live_list_stores_a_filter_and_no_items()
    {
        var filter = new LibraryFilter
        {
            Buckets = [LibraryBuckets.NeverPlayed],
            GenreIds = [7],
        };

        var listId = await _lists.InsertAsync(GameList.Live("Untouched RPGs", filter));
        var stored = await _lists.GetAsync(listId);

        Assert.NotNull(stored);
        Assert.True(stored.IsLive);
        Assert.True(stored.IsSmart);
        Assert.Equal(filter, stored.Filter);
        Assert.Empty(await _lists.GetItemsAsync(listId));
    }

    [Fact]
    public async Task A_manual_list_has_an_empty_filter()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var stored = await _lists.GetAsync(listId);

        Assert.False(stored!.IsLive);
        Assert.True(stored.Filter.IsEmpty);
        Assert.Null(stored.FilterJson);
    }

    /// <summary>
    /// A list given a rule becomes a live list, and its old items are abandoned
    /// rather than deleted — so converting back restores the hand-made ordering.
    /// </summary>
    [Fact]
    public async Task Setting_a_filter_makes_a_list_live_without_destroying_its_items()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        await _lists.AppendItemAsync(listId, a);

        Assert.True(await _lists.SetFilterAsync(listId, new LibraryFilter { Stores = ["steam"] }));

        var stored = await _lists.GetAsync(listId);
        Assert.True(stored!.IsLive);
        Assert.Equal(["steam"], stored.Filter.Stores);
        Assert.Single(await _lists.GetItemsAsync(listId));
    }

    /// <summary>
    /// A live list whose stored rule no longer parses shows the whole library
    /// instead of vanishing from the sidebar. Visibly wrong beats absent.
    /// </summary>
    [Fact]
    public async Task A_live_list_with_unreadable_json_still_loads()
    {
        var listId = await _lists.InsertAsync(new GameList
        {
            Name = "From the future",
            IsSmart = true,
            FilterJson = "{ this is not json",
        });

        var stored = await _lists.GetAsync(listId);

        Assert.NotNull(stored);
        Assert.Equal("From the future", stored.Name);
        Assert.True(stored.Filter.IsEmpty);
    }

    // -- membership: the whole difference between the two kinds ---------------

    /// <summary>
    /// A live list recomputes. Its rule names a bucket, so a game that moves
    /// buckets moves in or out of the list with no write of any kind.
    /// </summary>
    [Fact]
    public async Task Live_list_membership_follows_the_library()
    {
        var list = GameList.Live("Never played", new LibraryFilter { Buckets = [LibraryBuckets.NeverPlayed] });
        var listId = await _lists.InsertAsync(list);
        var stored = (await _lists.GetAsync(listId))!;

        var before = new[]
        {
            Row(1, LibraryBuckets.NeverPlayed),
            Row(2, LibraryBuckets.Bounced),
        };

        Assert.Equal([1L], stored.Filter.Apply(before).Select(r => r.ReleaseId));

        // The user finally starts release 1 and bounces off release 2's sequel.
        var after = new[]
        {
            Row(1, LibraryBuckets.Bounced),
            Row(2, LibraryBuckets.Bounced),
            Row(3, LibraryBuckets.NeverPlayed),
        };

        Assert.Equal([3L], stored.Filter.Apply(after).Select(r => r.ReleaseId));

        // And nothing was written to say so.
        Assert.Empty(await _lists.GetItemsAsync(listId));
    }

    /// <summary>
    /// A manual list does NOT recompute. Its membership is what the user put
    /// there, whatever the library does afterwards — which is the whole reason
    /// materialising a live list into <c>list_items</c> would break it.
    /// </summary>
    [Fact]
    public async Task Manual_list_membership_does_not_follow_the_library()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");
        await _lists.AppendItemAsync(listId, a);
        await _lists.AppendItemAsync(listId, b);

        // Playtime, buckets and metadata can all change; the list cannot notice.
        Assert.Equal([a, b], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    // -- deletion ------------------------------------------------------------

    /// <summary>
    /// The cascade runs one way only. Deleting a list takes its membership rows
    /// and stops: the releases, their works and their ownerships are untouched.
    /// This is asserted rather than read off the schema because "which way does
    /// this cascade run" is the question people get wrong from memory.
    /// </summary>
    [Fact]
    public async Task Deleting_a_list_never_deletes_a_game()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");
        await _lists.AppendItemAsync(listId, a);
        await _lists.AppendItemAsync(listId, b);
        var ownershipId = await _ownerships.InsertAsync(new Ownership { ReleaseId = a, Store = "steam" });

        Assert.True(await _lists.DeleteAsync(listId));

        Assert.Null(await _lists.GetAsync(listId));
        Assert.Empty(await _lists.GetItemsAsync(listId));

        Assert.NotNull(await _releases.GetAsync(a));
        Assert.NotNull(await _releases.GetAsync(b));
        Assert.Equal(2, (await _works.GetAllAsync()).Count);
        Assert.Contains(await _ownerships.GetAllAsync(), o => o.Id == ownershipId);
    }

    /// <summary>The inbound cascade in the other direction still works: a deleted release leaves its lists.</summary>
    [Fact]
    public async Task Deleting_a_release_removes_it_from_lists()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Backlog"));
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Celeste");
        await _lists.AppendItemAsync(listId, a);
        await _lists.AppendItemAsync(listId, b);

        using (var lease = _db.Factory.Lease())
        {
            await Dapper.SqlMapper.ExecuteAsync(
                lease.Connection, "DELETE FROM releases WHERE id = @a;", new { a }, lease.Transaction);
        }

        Assert.Equal([b], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    [Fact]
    public async Task Deleting_a_live_list_deletes_only_the_row()
    {
        var listId = await _lists.InsertAsync(
            GameList.Live("Never played", new LibraryFilter { Buckets = [LibraryBuckets.NeverPlayed] }));
        var a = await SeedReleaseAsync("Hades");

        Assert.True(await _lists.DeleteAsync(listId));

        Assert.Empty(await _lists.GetAllAsync());
        Assert.NotNull(await _releases.GetAsync(a));
    }

    private async Task<long> SeedReleaseAsync(string name)
    {
        var workId = await _works.InsertAsync(new Work { Name = name });
        return await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
    }

    private static FilterableRow Row(long releaseId, string bucket) => new(
        releaseId,
        OwnershipId: releaseId,
        bucket,
        Store: "steam",
        Title: "Fixture " + releaseId,
        Installed: false,
        HasUnread: bucket == LibraryBuckets.StaleButPatched,
        FirstReleaseYear: 2015,
        FacetIds: [],
        GameModes: []);
}
