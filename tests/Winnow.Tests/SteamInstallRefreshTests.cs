using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamInstallRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"winnow-steam-refresh-{Guid.NewGuid():N}");
    private readonly TempDatabase _db = new();
    private string ManifestPath => Path.Combine(_root, "steamapps", "appmanifest_620.acf");

    public SteamInstallRefreshTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + _root.Replace("\\", "\\\\") + "\" } }");
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_root, true);
    }

    private void WriteManifest(int state) => File.WriteAllText(ManifestPath,
        $$"""
        "AppState" { "appid" "620" "name" "Portal 2" "StateFlags" "{{state}}" "installdir" "Portal 2" }
        """);

    private LocalLibrarySyncService Sync(SteamLibrarySource source)
    {
        var resolver = new ExternalIdResolver(new WorkRepository(_db.Factory), new ReleaseRepository(_db.Factory),
            new OwnershipRepository(_db.Factory), new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory), _db.Factory, new OwnershipAccountRepository(_db.Factory));
        return new LocalLibrarySyncService(source, SilentStores.Epic(), SilentStores.Gog(), resolver,
            new LibrarySyncGate(), NullLogger<LocalLibrarySyncService>.Instance,
            steamInstallState: new SteamInstallStateRepository(_db.Factory));
    }

    [Fact]
    public async Task Stable_install_and_uninstall_refresh_manifest_only_ownership_without_erasing_history()
    {
        var source = new SteamLibrarySource(steamRoot: _root);
        var sync = Sync(source);
        var published = new List<bool>();
        var ownerships = new OwnershipRepository(_db.Factory);
        using var service = new SteamInstallRefreshService(source.ReadInstallFingerprint,
            ct => sync.SyncAsync(ct), async ct => published.Add(Assert.Single(await ownerships.GetAllAsync(ct)).Installed),
            NullLogger<SteamInstallRefreshService>.Instance);

        WriteManifest(0);
        await service.PollAsync();
        Assert.Empty(await ownerships.GetAllAsync());
        await service.PollAsync();
        Assert.Equal([false], published);
        File.WriteAllText(ManifestPath, "\"AppState\" {");
        await service.PollAsync();
        WriteManifest(4);
        await service.PollAsync();
        Assert.Equal([false], published);
        await service.PollAsync();
        Assert.Equal([false, true], published);
        var installed = Assert.Single(await ownerships.GetAllAsync());
        Assert.NotNull(installed.InstallPath);

        File.Delete(ManifestPath);
        await service.PollAsync();
        Assert.True((await ownerships.GetAsync(installed.Id))!.Installed);
        await service.PollAsync();
        Assert.Equal([false, true, false], published);
        var removed = Assert.Single(await ownerships.GetAllAsync());
        Assert.Equal(installed.Id, removed.Id);
        Assert.Null(removed.InstallPath);
        Assert.NotNull(await new ReleaseRepository(_db.Factory).GetAsync(removed.ReleaseId));
    }

    [Fact]
    public async Task Incomplete_inventory_preserves_installed_state_and_next_complete_scan_reconciles_after_restart()
    {
        WriteManifest(4);
        await Sync(new SteamLibrarySource(steamRoot: _root)).SyncAsync();
        File.Delete(ManifestPath);
        var folders = Path.Combine(_root, "steamapps", "libraryfolders.vdf");
        var validFolders = File.ReadAllText(folders);
        File.WriteAllText(folders, "\"libraryfolders\" { \"0\" { \"path\" \"Z:/missing-winnow-test-drive\" } }");
        var restartedSource = new SteamLibrarySource(steamRoot: _root);
        var restartedSync = Sync(restartedSource);
        Assert.Null(restartedSource.ReadInstallFingerprint());
        await restartedSync.SyncAsync();
        Assert.True(Assert.Single(await new OwnershipRepository(_db.Factory).GetAllAsync()).Installed);
        File.WriteAllText(folders, validFolders);
        await restartedSync.SyncAsync();
        Assert.False(Assert.Single(await new OwnershipRepository(_db.Factory).GetAllAsync()).Installed);
    }

    [Fact]
    public void Steam_management_uses_uninstall_and_never_declares_a_game_launch()
    {
        var action = Assert.IsType<GameLink>(StoreActions.ManagementFor("steam", true, "620", null));
        Assert.Equal("steam://uninstall/620", action.Uri);
        Assert.Equal(GameLinkKind.Uninstall, action.Kind);
        Assert.False(action.StartsGame);
        Assert.Null(StoreActions.ManagementFor("steam", false, "620", null));
        Assert.Null(StoreActions.ManagementFor("steam", null, "620", null));
        Assert.Null(StoreActions.ManagementFor("steam", true, "620?bad", null));
    }

    [Theory]
    [InlineData("epic", null, "com.epicgames.launcher://store/library")]
    [InlineData("gog", "123", "goggalaxy://opengameview/gog_123")]
    public void Other_store_management_is_navigation_not_an_uninstall(string store, string? id, string uri)
    {
        var action = Assert.IsType<GameLink>(StoreActions.ManagementFor(store, true, null, id));
        Assert.StartsWith("Manage in", action.Label);
        Assert.Equal(uri, action.Uri);
        Assert.Equal(GameLinkKind.Link, action.Kind);
        Assert.False(action.StartsGame);
    }
}
