using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Data;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ArtworkChoiceRepositoryTests
{
    [Fact]
    public async Task All_slots_and_provenance_survive_a_new_connection_factory()
    {
        using var db = new TempDatabase();
        var id = await Work(db);
        var repository = new ArtworkChoiceRepository(db.Factory);
        foreach (var slot in Enum.GetValues<ArtworkSlot>())
            await repository.SetAsync(Choice(id, slot) with
            {
                Creator = "Artist", PageUrl = "https://example.com/art/1",
                SourceUrl = "https://example.com/image.png", CollectionId = "collection-1"
            });

        var reopened = new ArtworkChoiceRepository(new SqliteConnectionFactory(db.DatabasePath, pooling: false));
        var choices = await reopened.GetAllAsync();
        Assert.Equal(3, choices.Count);
        Assert.All(choices, choice =>
        {
            Assert.Equal("winnow://user-art/saved", choice.AssetKey);
            Assert.Equal("provider", choice.SourceId);
            Assert.Equal("saved", choice.AssetId);
            Assert.Equal("Artist", choice.Creator);
            Assert.Equal("https://example.com/art/1", choice.PageUrl);
            Assert.Equal("https://example.com/image.png", choice.SourceUrl);
            Assert.Equal("collection-1", choice.CollectionId);
            Assert.True(choice.Revision > 0);
        });
    }

    [Fact]
    public async Task Manual_wins_over_a_newer_collection_and_reset_affects_only_one_slot()
    {
        using var db = new TempDatabase();
        var id = await Work(db);
        var repository = new ArtworkChoiceRepository(db.Factory);
        await repository.SetAsync(Choice(id, ArtworkSlot.Hero));
        await repository.SetAsync(Choice(id, ArtworkSlot.Hero, "collection") with { Kind = ArtworkChoiceKind.Collection });
        await repository.SetAsync(Choice(id, ArtworkSlot.Icon));

        Assert.Equal("saved", (await repository.GetEffectiveAsync([id], ArtworkSlot.Hero))!.AssetId);
        await repository.ResetAsync([id], ArtworkSlot.Hero);
        Assert.Null(await repository.GetEffectiveAsync([id], ArtworkSlot.Hero));
        Assert.Equal(ArtworkSlot.Icon, Assert.Single(await repository.GetAllAsync()).Slot);
    }

    [Fact]
    public async Task Confirmed_copies_share_latest_manual_choice_until_unlinked_without_copying_rows()
    {
        using var db = new TempDatabase();
        var parent = await Work(db);
        var child = await Work(db);
        var repository = new ArtworkChoiceRepository(db.Factory);
        var links = new IdentityLinkRepository(db.Factory);
        await repository.SetAsync(Choice(parent, ArtworkSlot.Cover, "parent"));
        await repository.SetAsync(Choice(child, ArtworkSlot.Cover, "child"));
        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = parent, ChildWorkIds = [child] });

        var resolution = await links.GetResolutionAsync();
        var all = await repository.GetAllAsync();
        foreach (var id in new[] { parent, child })
        {
            var group = resolution.SameGame.GroupOf(id);
            Assert.Equal("child", (await repository.GetEffectiveAsync(group, ArtworkSlot.Cover))!.AssetId);
            Assert.Equal("child", ArtworkChoices.Effective(all, group, ArtworkSlot.Cover)!.AssetId);
        }

        await links.RetractLinkAsync(child);
        resolution = await links.GetResolutionAsync();
        Assert.Equal("parent", (await repository.GetEffectiveAsync(resolution.SameGame.GroupOf(parent), ArtworkSlot.Cover))!.AssetId);
        Assert.Equal("child", (await repository.GetEffectiveAsync(resolution.SameGame.GroupOf(child), ArtworkSlot.Cover))!.AssetId);
        Assert.Equal(all, await repository.GetAllAsync());
    }

    [Fact]
    public async Task Reset_of_a_group_cannot_reveal_another_members_older_choice()
    {
        using var db = new TempDatabase();
        var parent = await Work(db);
        var child = await Work(db);
        var repository = new ArtworkChoiceRepository(db.Factory);
        await repository.SetAsync(Choice(parent, ArtworkSlot.Hero));
        await repository.SetAsync(Choice(child, ArtworkSlot.Hero));
        await repository.ResetAsync([parent, child], ArtworkSlot.Hero);
        Assert.Empty(await repository.GetAllAsync());
        var revision = await repository.SetAsync(Choice(child, ArtworkSlot.Hero));
        Assert.True(revision > 2);
    }

    [Fact]
    public async Task Undo_preserves_later_edits_and_restores_only_the_written_revision()
    {
        using var db = new TempDatabase();
        var id = await Work(db);
        var repository = new ArtworkChoiceRepository(db.Factory);
        var before = Choice(id, ArtworkSlot.Hero, "before");
        await repository.SetAsync(before);
        var written = Choice(id, ArtworkSlot.Hero, "written");
        written = written with { Revision = await repository.SetAsync(written) };
        Assert.True(await repository.TryRestoreAsync(written, before));
        Assert.False(await repository.TryRestoreAsync(written, before));
        Assert.Equal("before", (await repository.GetEffectiveAsync([id], ArtworkSlot.Hero))!.AssetId);
        written = written with { Revision = await repository.SetAsync(written) };
        await repository.SetAsync(Choice(id, ArtworkSlot.Hero, "later"));
        Assert.False(await repository.TryRestoreAsync(written, before));
        Assert.Equal("later", (await repository.GetEffectiveAsync([id], ArtworkSlot.Hero))!.AssetId);
    }

    [Fact]
    public async Task Failed_replace_rolls_back_its_delete_even_when_ambient_caller_commits()
    {
        using var db = new TempDatabase();
        var id = await Work(db);
        var repository = new ArtworkChoiceRepository(db.Factory);
        await repository.SetAsync(Choice(id, ArtworkSlot.Hero));
        using (var connection = db.Factory.Open())
            connection.Execute("""
                CREATE TRIGGER fail_artwork BEFORE INSERT ON artwork_choices
                WHEN NEW.asset_id = 'failure'
                BEGIN SELECT RAISE(ABORT, 'test failure'); END;
                """);
        using (var unit = db.Factory.Begin())
        {
            await Assert.ThrowsAsync<SqliteException>(() => repository.SetAsync(Choice(id, ArtworkSlot.Hero, "failure")));
            unit.Commit();
        }
        Assert.Equal("saved", (await repository.GetEffectiveAsync([id], ArtworkSlot.Hero))!.AssetId);
    }

    [Fact]
    public async Task Successful_write_and_reset_do_not_commit_an_ambient_transaction()
    {
        using var db = new TempDatabase();
        var id = await Work(db);
        var repository = new ArtworkChoiceRepository(db.Factory);
        await repository.SetAsync(Choice(id, ArtworkSlot.Hero));
        using (db.Factory.Begin())
        {
            await repository.ResetAsync([id], ArtworkSlot.Hero);
            await repository.SetAsync(Choice(id, ArtworkSlot.Icon));
        }
        Assert.Equal(ArtworkSlot.Hero, Assert.Single(await repository.GetAllAsync()).Slot);
    }

    [Fact]
    public async Task Migration_preserves_legacy_user_imports_but_not_automatic_artwork()
    {
        using var db = new TempDatabase();
        var id = await Work(db);
        using (var connection = db.Factory.Open())
            connection.Execute("""
                DROP TABLE artwork_choices;
                DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0044_artwork_choices.sql';
                UPDATE works SET cover_url = 'winnow://user-art/old', background_url = 'https://example.com/automatic' WHERE id = @id;
                INSERT INTO work_field_sources (work_id, field, source, set_at)
                VALUES (@id, 'cover_url', 'user', '2026-09-17 00:00:00'),
                       (@id, 'background_url', 'igdb', '2026-09-17 00:00:00');
                """, new { id });
        db.Initializer.Initialize();
        var choice = Assert.Single(await new ArtworkChoiceRepository(db.Factory).GetAllAsync());
        Assert.Equal(ArtworkSlot.Cover, choice.Slot);
        Assert.Equal(ArtworkChoiceKind.Manual, choice.Kind);
        Assert.Equal("winnow://user-art/old", choice.AssetKey);
        Assert.Equal("user", choice.SourceId);
        await new ArtworkChoiceRepository(db.Factory).ResetAsync([id], ArtworkSlot.Cover);
        var work = await new WorkRepository(db.Factory).GetAsync(id);
        Assert.Null(work!.CoverUrl);
        Assert.Equal("https://example.com/automatic", work.BackgroundUrl);
        var sources = await new WorkFieldSourceRepository(db.Factory).GetSourcesAsync(id);
        Assert.False(sources.ContainsKey("cover_url"));
        Assert.Equal("igdb", sources["background_url"]);
    }

    private static Task<long> Work(TempDatabase db)
        => new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Game" });

    private static ArtworkChoice Choice(long id, ArtworkSlot slot, string asset = "saved") => new()
    {
        WorkId = id, Slot = slot, AssetKey = "winnow://user-art/" + asset,
        SourceId = "provider", AssetId = asset
    };
}
