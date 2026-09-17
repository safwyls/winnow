using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Monitor;
using Winnow.PluginFixture;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginGameActionIntegrationTests
{
    [Fact]
    public async Task Resource_identifier_fallback_is_promoted_when_the_real_title_becomes_available()
    {
        await using var host = await Host.CreateAsync();
        await host.Storage.WriteSettingAsync("xbox", "state", "provisional");
        await host.Sync.SyncAsync();
        var initial = Assert.Single((await host.SnapshotAsync()).Works);
        Assert.True(initial.NameIsProvisional);
        await host.Storage.WriteSettingAsync("xbox", "state", null);
        await host.Sync.SyncAsync();
        var promoted = Assert.Single((await host.SnapshotAsync()).Works);
        Assert.Equal(initial.Id, promoted.Id);
        Assert.Equal("Xbox fixture", promoted.Name);
        Assert.False(promoted.NameIsProvisional);
    }

    [Fact]
    public async Task Imported_actions_survive_offline_refresh_and_route_through_the_active_provider_with_launch_attribution()
    {
        await using var host = await Host.CreateAsync();
        await host.Sync.SyncAsync();
        var snapshot = await host.SnapshotAsync();
        var ownership = Assert.Single(snapshot.Ownerships);
        var entry = Assert.Single(await host.Actions.ReadAsync(snapshot, default)).Value;
        Assert.Equal("Played history — not proof of ownership", entry.SourceLabel);
        Assert.NotNull(entry.Play);
        Assert.NotNull(entry.Store);
        Assert.Equal("Xbox", StoreNaming.Label(ownership.Store));
        var intents = new LaunchIntents();
        var launcher = new GameLaunchService(new RejectDispatcher(), intents, pluginActions: host.Actions);
        Assert.Equal(LaunchDispatch.HandedOff, await launcher.LaunchAsync(ownership.Id, entry.Play));
        Assert.True(intents.IsLive(ownership.Id, DateTime.UtcNow));
        Assert.Equal("stable-package:Play", await host.Storage.ReadSettingAsync("xbox", "last-action"));
        Assert.Equal(LaunchDispatch.AlreadyRunning, await launcher.LaunchAsync(ownership.Id, entry.Play));
        var router = new GameLinkRouter(new RejectDispatcher(), host.Provider.GetRequiredService<ISettingsRepository>(),
            new NoClients(), pluginActions: host.Actions);
        Assert.True((await router.OpenAsync(entry.Store, "Xbox fixture")).Opened);
        Assert.Equal("stable-package:OpenStore", await host.Storage.ReadSettingAsync("xbox", "last-action"));
        Assert.False(await host.Actions.ExecuteAsync(ownership.Id + 1, entry.Play));
        Assert.Null(GameLink.Create("Forged", "winnow-plugin://action"));

        await host.Storage.WriteSettingAsync("xbox", "state", "unavailable");
        await host.Sync.SyncAsync();
        Assert.NotNull(Assert.Single(await host.Actions.ReadAsync(await host.SnapshotAsync(), default)).Value.Play);
        using var library = host.Provider.GetRequiredService<LibraryViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        var tile = Assert.Single(library.AllTiles);
        Assert.Equal(entry.SourceLabel, tile.Primary.LibrarySourceLabel);
        Assert.NotNull(tile.PrimaryAction);
    }

    [Fact]
    public async Task Uninstall_removes_play_and_stale_buttons_cannot_bypass_the_latest_observation()
    {
        await using var host = await Host.CreateAsync();
        await host.Sync.SyncAsync();
        var before = await host.SnapshotAsync();
        var ownership = Assert.Single(before.Ownerships);
        var previous = Assert.Single(await host.Actions.ReadAsync(before, default)).Value;
        await host.Storage.WriteSettingAsync("xbox", "state", "removed");
        await host.Sync.SyncAsync();
        var now = Assert.Single(await host.Actions.ReadAsync(await host.SnapshotAsync(), default)).Value;
        Assert.Null(now.Play);
        Assert.NotNull(now.Store);
        Assert.False(await host.Actions.ExecuteAsync(ownership.Id, previous.Play!));
        await host.Catalog.DisposeAsync();
        var unloaded = Assert.Single(await host.Actions.ReadAsync(await host.SnapshotAsync(), default)).Value;
        Assert.Null(unloaded.Play);
        Assert.Null(unloaded.Store);
        Assert.Equal(previous.SourceLabel, unloaded.SourceLabel);
    }

    private sealed class RejectDispatcher : IUriDispatcher
    {
        public Task<bool> OpenAsync(Uri uri) => throw new InvalidOperationException("Plugin actions must not reach the shell URI dispatcher.");
    }
    private sealed class NoClients : IStoreClientAvailability { public bool IsAvailable(string scheme) => false; }

    private sealed class Host : IAsyncDisposable
    {
        public TempDatabase Database { get; } = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-plugin-actions-" + Guid.NewGuid().ToString("N"));
        public ServiceProvider Provider { get; private set; } = null!;
        public PluginCatalog Catalog => Provider.GetRequiredService<PluginCatalog>();
        public PluginSyncService Sync => Provider.GetRequiredService<PluginSyncService>();
        public PluginStorage Storage => Provider.GetRequiredService<PluginStorage>();
        public PluginGameActionService Actions => Provider.GetRequiredService<PluginGameActionService>();
        public Task<LibrarySnapshot> SnapshotAsync() => Provider.GetRequiredService<ILibraryQueryRepository>().GetSnapshotAsync(BucketThresholds.Default);

        public static async Task<Host> CreateAsync()
        {
            var host = new Host();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Program.ConfigureServices(services, new(host.Root, host.Database.DatabasePath, DataMigrationOutcome.None));
            services.AddSingleton<ISqliteConnectionFactory>(host.Database.Factory);
            host.Provider = services.BuildServiceProvider();
            var directory = Path.Combine(host.Root, "packages", "xbox");
            Directory.CreateDirectory(directory);
            File.Copy(typeof(GameActionsFixture).Assembly.Location, Path.Combine(directory, "Winnow.PluginFixture.dll"));
            await File.WriteAllTextAsync(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(new PluginManifest
            {
                Id = "xbox", Name = "Xbox", Version = "1.0.0", EntryAssembly = "Winnow.PluginFixture.dll",
                EntryType = typeof(GameActionsFixture).FullName!, Capabilities = [PluginCapabilities.Library, PluginCapabilities.GameActions],
                Settings = [new() { Key = "state", Label = "State" }, new() { Key = "last-action", Label = "Last action" }],
            }));
            await host.Catalog.DiscoverAsync(Path.GetDirectoryName(directory)!, Path.Combine(host.Root, "user"));
            Assert.True(Assert.Single(host.Catalog.Plugins).Loaded);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            Database.Dispose();
            var absoluteRoot = Path.GetFullPath(Root);
            if (!absoluteRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Temporary path escaped its root.");
            try { Directory.Delete(absoluteRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
