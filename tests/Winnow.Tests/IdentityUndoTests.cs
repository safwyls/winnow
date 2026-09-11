using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Identity;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class IdentityUndoTests
{
    [Theory]
    [InlineData(IdentityLinkKinds.SameGame)]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task Separating_a_reparented_child_does_not_restore_a_nested_link(string kind)
    {
        using var db = Seed();
        var links = new IdentityLinkRepository(db.Factory);
        await Link(links, 2, kind, 1);
        await Link(links, 3, IdentityLinkKinds.SameGame, 2);

        Assert.True(await links.RetractLinkAsync(1));

        var resolution = await links.GetResolutionAsync();
        Assert.False(resolution.IsChild(1));
        Assert.Equal(3, resolution.SameGame.Resolve(2));
        Assert.Single(await links.GetHistoryAsync(), link => link.IsLive);
        AssertDepthOne(db);
    }

    [Theory]
    [InlineData(IdentityLinkKinds.SameGame)]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task Whole_act_undo_preserves_a_later_membership_decision(string kind)
    {
        using var db = Seed();
        var links = new IdentityLinkRepository(db.Factory);
        await Link(links, 3, kind, 1, 2);
        var middle = await Link(links, 4, kind, 1, 2);
        await Link(links, 5, kind, 1);

        Assert.True(await links.RetractActAsync(middle));
        Assert.False(await links.RetractActAsync(middle));

        var live = (await links.GetHistoryAsync()).Where(link => link.IsLive).ToArray();
        Assert.Equal(5, live.Single(link => link.ChildWorkId == 1).ParentWorkId);
        Assert.Equal(3, live.Single(link => link.ChildWorkId == 2).ParentWorkId);
        Assert.All(live, link => Assert.Equal(kind, link.Kind));
        AssertDepthOne(db);
    }

    [Theory]
    [InlineData(IdentityLinkKinds.SameGame)]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task Whole_act_undo_leaves_children_separate_when_the_old_parent_has_moved(string kind)
    {
        using var db = Seed();
        var links = new IdentityLinkRepository(db.Factory);
        await Link(links, 3, kind, 1, 2);
        var middle = await Link(links, 4, kind, 1, 2);
        await Link(links, 5, IdentityLinkKinds.SameGame, 3);

        Assert.True(await links.RetractActAsync(middle));

        var resolution = await links.GetResolutionAsync();
        Assert.False(resolution.IsChild(1));
        Assert.False(resolution.IsChild(2));
        Assert.Equal(5, resolution.SameGame.Resolve(3));
        AssertDepthOne(db);
    }

    [Theory]
    [InlineData(IdentityLinkKinds.SameGame)]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task Whole_act_undo_does_not_revisit_a_previously_separated_member(string kind)
    {
        using var db = Seed();
        var links = new IdentityLinkRepository(db.Factory);
        await Link(links, 3, kind, 1, 2);
        var middle = await Link(links, 4, kind, 1, 2);
        Assert.True(await links.RetractLinkAsync(1));
        var separated = (await links.GetHistoryAsync()).Single(link => link.IsLive && link.ChildWorkId == 1);

        Assert.True(await links.RetractActAsync(middle));

        var live = (await links.GetHistoryAsync()).Where(link => link.IsLive).ToArray();
        Assert.Equal(separated, live.Single(link => link.ChildWorkId == 1));
        Assert.All(live, link => Assert.Equal(3, link.ParentWorkId));
        AssertDepthOne(db);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_failed_restoration_rolls_back_retractions_even_when_an_outer_caller_commits(
        bool wholeAct, bool ambient)
    {
        using var db = Seed();
        var links = new IdentityLinkRepository(db.Factory);
        await Link(links, 3, IdentityLinkKinds.SameGame, 1, 2);
        var middle = await Link(links, 4, IdentityLinkKinds.SameGame, 1, 2);
        var before = await links.GetHistoryAsync();
        var actsBefore = await links.GetActsAsync();
        using (var connection = db.Factory.Open())
        {
            connection.Execute("""
                CREATE TRIGGER reject_restoration BEFORE INSERT ON identity_links
                WHEN NEW.parent_work_id = 3
                BEGIN SELECT RAISE(ABORT, 'injected restoration failure'); END;
                """);
        }

        using (var outer = ambient ? db.Factory.Begin() : null)
        {
            await Assert.ThrowsAsync<SqliteException>(() => wholeAct
                ? links.RetractActAsync(middle)
                : links.RetractLinkAsync(1));
            if (ambient)
            {
                await new SettingsRepository(db.Factory).SetAsync("unrelated", "preserved");
                outer!.Commit();
            }
        }

        Assert.Equal(before, await links.GetHistoryAsync());
        Assert.Equal(actsBefore, await links.GetActsAsync());
        AssertDepthOne(db);
    }

    [Theory]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task An_expansion_proposal_cannot_use_a_base_that_already_has_a_parent(string existingKind)
    {
        using var db = Seed();
        using (var connection = db.Factory.Open())
        {
            connection.Execute("""
                UPDATE works SET igdb_id = id * 100;
                UPDATE works SET igdb_game_type = 'expansion', igdb_parent_id = 100 WHERE id = 2;
                UPDATE works SET igdb_game_type = 'expansion', igdb_parent_id = 200 WHERE id = 3;
                INSERT INTO releases(id,work_id,name) SELECT id,id,name FROM works;
                INSERT INTO ownerships(id,release_id,store) SELECT id,id,'steam' FROM works;
                """);
        }

        var links = new IdentityLinkRepository(db.Factory);
        var scan = new LibraryExpansionScan(new ReleaseRepository(db.Factory), links,
            new ExpansionRefusalRepository(db.Factory));
        Assert.Contains((await scan.ScanAsync()).Groups, group => group.Base.WorkId == 2);

        await Link(links, 1, existingKind, 2);

        Assert.DoesNotContain((await scan.ScanAsync()).Groups, group => group.Base.WorkId == 2);
    }

    [Theory]
    [InlineData(IdentityLinkKinds.SameGame)]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task An_expansion_proposal_cannot_move_a_childs_existing_relationships(string existingKind)
    {
        using var db = Seed();
        using (var connection = db.Factory.Open())
        {
            connection.Execute("""
                UPDATE works SET igdb_id = id * 100;
                UPDATE works SET igdb_game_type = 'expansion', igdb_parent_id = 100 WHERE id = 2;
                INSERT INTO releases(id,work_id,name) SELECT id,id,name FROM works;
                INSERT INTO ownerships(id,release_id,store) SELECT id,id,'steam' FROM works;
                """);
        }

        var links = new IdentityLinkRepository(db.Factory);
        var scan = new LibraryExpansionScan(new ReleaseRepository(db.Factory), links,
            new ExpansionRefusalRepository(db.Factory));
        Assert.Contains((await scan.ScanAsync()).Groups, group => group.Base.WorkId == 1);

        await Link(links, 2, existingKind, 3);

        Assert.DoesNotContain((await scan.ScanAsync()).Groups, group => group.Base.WorkId == 1);
    }

    private static Task<long> Link(IdentityLinkRepository links, long parent, string kind, params long[] children)
        => links.LinkAsync(new IdentityLinkRequest { ParentWorkId = parent, ChildWorkIds = children, Kind = kind });

    private static TempDatabase Seed()
    {
        var db = new TempDatabase();
        using var connection = db.Factory.Open();
        connection.Execute("""
            INSERT INTO works(id,name) VALUES (1,'Game One'),(2,'Game Two'),(3,'Game Three'),
                                             (4,'Game Four'),(5,'Game Five');
            """);
        return db;
    }

    private static void AssertDepthOne(TempDatabase db)
    {
        using var connection = db.Factory.Open();
        Assert.Equal(0, connection.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM identity_links child
            JOIN identity_links parent ON parent.child_work_id = child.parent_work_id
            WHERE child.retracted_at IS NULL AND parent.retracted_at IS NULL;
            """));
    }
}
