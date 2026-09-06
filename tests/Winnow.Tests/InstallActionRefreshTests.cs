using Dapper;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Enrich.Stores;
using Xunit;

namespace Winnow.Tests;

public sealed class InstallActionRefreshTests
{
    [Fact]
    public async Task Reload_updates_open_Epic_actions_without_losing_store_page_or_editor_draft()
    {
        using var db = new TempDatabase();
        using (var lease = db.Factory.Lease())
            await lease.Connection.ExecuteAsync("""
                INSERT INTO works(id,name) VALUES(1,'Moonlighter');
                INSERT INTO releases(id,work_id,name) VALUES(1,1,'Moonlighter');
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'epic','catalog-id');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'epic',0);
                """);
        await new SqliteEpicLaunchKeyStore(db.Factory).SaveAsync([new("catalog-id", "moon-ns", "Eagle")]);
        var cache = new StorefrontCache(db.Factory);
        const string url = "https://store.epicgames.com/p/moonlighter";
        await cache.SaveAsync("epic-namespace:moon-ns",
            """{"data":{"Catalog":{"catalogNs":{"mappings":[{"pageSlug":"moonlighter","pageType":"productHome"}]}}}}""",
            DateTime.UtcNow);
        var owners = new OwnershipRepository(db.Factory);
        var works = new WorkRepository(db.Factory);
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), owners,
            new ReleaseRepository(db.Factory), works, new UpdateEventRepository(db.Factory),
            epicLaunchKeys: new SqliteEpicLaunchKeys(db.Factory), storefrontCache: cache,
            metadataEdits: new WorkMetadataEditService(works, new WorkFieldSourceRepository(db.Factory)));
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.VisibleTiles));
        var details = library.Details!;
        Assert.Equal("Install", details.PrimaryAction!.Label);
        Assert.Equal(url, Assert.Single(details.Links).Uri);
        var editor = details.MetadataEditor!;
        await editor.OpenCommand.ExecuteAsync(null);
        var row = editor.Rows.First();
        row.Draft = "An unsaved edit";
        var notified = false;
        details.PropertyChanged += (_, e) => notified |= string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(details.PrimaryAction);

        await owners.UpsertAsync(new OwnershipUpsert(1, "epic", null, null, "C:\\Games\\Moonlighter", true));
        await library.LoadCommand.ExecuteAsync(null);

        Assert.Same(details, library.Details);
        Assert.Same(editor, details.MetadataEditor);
        Assert.True(editor.IsOpen);
        Assert.Equal("An unsaved edit", row.Draft);
        Assert.True(notified);
        Assert.Equal("Play", details.PrimaryAction!.Label);
        Assert.Equal("Play", Assert.Single(library.VisibleTiles).PrimaryAction!.Label);
        Assert.Equal("C:\\Games\\Moonlighter", details.OpenableFolder);
        Assert.Same(Assert.Single(library.VisibleTiles), details.Tile);
        Assert.Same(details.Tile, library.SelectedTile);
        Assert.True(details.Tile.IsSelected);
        Assert.Equal(url, Assert.Single(details.Links).Uri);

        await owners.UpsertAsync(new OwnershipUpsert(1, "epic", null, null, null, false));
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Install", details.PrimaryAction!.Label);
        Assert.Null(details.OpenableFolder);
        Assert.Equal(url, Assert.Single(details.Links).Uri);
        library.CloseDetailsCommand.Execute(null);
        library.FlipTileCommand.Execute(library.SelectedTile);
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Null(library.Details);
        Assert.Same(Assert.Single(library.VisibleTiles), library.FlippedTile);
        Assert.True(library.FlippedTile!.IsFlipped);
    }
}
