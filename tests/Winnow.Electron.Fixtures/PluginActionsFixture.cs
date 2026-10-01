using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Monitor;
using Winnow.PluginFixture;
using Winnow.PluginSdk;
using Winnow.Plugins;

namespace Winnow.Electron.Fixtures;

/// <summary>The frozen SDK fixture runs through the production loader, importer and action services.</summary>
internal sealed class PluginActionsFixture(PluginCatalog catalog, PluginStorage storage, PluginSyncService sync,
    PluginGameActionService actions, ILibraryQueryRepository library, ISqliteConnectionFactory database,
    LaunchIntents intents, LibraryChangePublisher publisher, PluginActionShellGuard shell)
{
    private GameLink? _capturedPlay;
    private long _capturedOwnership;

    public async Task InitializeAsync(string directory)
    {
        var package = Path.Combine(directory, "plugins", "xbox");
        Directory.CreateDirectory(package);
        File.Copy(typeof(GameActionsFixture).Assembly.Location, Path.Combine(package, "Winnow.PluginFixture.dll"));
        await File.WriteAllTextAsync(Path.Combine(package, "plugin.json"), JsonSerializer.Serialize(new PluginManifest
        {
            Id = "xbox", Name = "Xbox", Version = "1.0.0", EntryAssembly = "Winnow.PluginFixture.dll",
            EntryType = typeof(GameActionsFixture).FullName!,
            Capabilities = [PluginCapabilities.Library, PluginCapabilities.GameActions],
            Settings = [new() { Key = "state", Label = "State" }, new() { Key = "last-action", Label = "Last action" }],
        }));
        await storage.SetEnabledAsync("xbox", true);
        using var connection = database.Open();
        // Observe the unchanged fixture's writes without replacing the production plugin context or storage.
        connection.Execute("""
            CREATE TABLE fixture_plugin_calls(id INTEGER PRIMARY KEY, action TEXT NOT NULL);
            CREATE TRIGGER fixture_plugin_insert AFTER INSERT ON settings
              WHEN NEW.key = 'plugin.4:xbox.setting.11:last-action'
              BEGIN INSERT INTO fixture_plugin_calls(action) VALUES(NEW.value); END;
            CREATE TRIGGER fixture_plugin_update AFTER UPDATE ON settings
              WHEN NEW.key = 'plugin.4:xbox.setting.11:last-action'
              BEGIN INSERT INTO fixture_plugin_calls(action) VALUES(NEW.value); END;
            """);
    }

    public async Task SyncAsync(string state)
    {
        if (state is not ("available" or "provisional" or "unavailable" or "removed"))
            throw new ArgumentException("Unknown plugin fixture observation.");
        await catalog.DiscoveryReady;
        if (!catalog.GetActive<IPluginGameActions>().Any(plugin => plugin.Manifest.Id == "xbox"))
            throw new InvalidOperationException("The real fixture plugin must be loaded before import.");
        await storage.WriteSettingAsync("xbox", "state", state == "available" ? null : state);
        await sync.SyncAsync();
        var snapshot = await ReadAsync();
        var current = await actions.ReadAsync(snapshot, default);
        if (_capturedPlay is null && current.Single().Value.Play is { } play)
        {
            _capturedOwnership = current.Single().Key;
            _capturedPlay = play;
        }
        await publisher.PublishAsync(default);
    }

    public async Task<object> SnapshotAsync()
    {
        var snapshot = await ReadAsync();
        var current = await actions.ReadAsync(snapshot, default);
        using var connection = database.Open();
        return new
        {
            Works = snapshot.Works.Select(work => new { work.Id, work.Name, work.NameIsProvisional }),
            Ownerships = snapshot.Ownerships.Select(entry => new
            {
                entry.Id, entry.ReleaseId, entry.Store, entry.Installed,
                IntentLive = intents.IsLive(entry.Id, DateTime.UtcNow),
            }),
            snapshot.ExternalIds,
            Actions = current.ToDictionary(pair => pair.Key, pair => new
            {
                pair.Value.SourceLabel,
                CanPlay = pair.Value.Play is not null,
                CanOpenStore = pair.Value.Store is not null,
            }),
            LastAction = await storage.ReadSettingAsync("xbox", "last-action"),
            Calls = connection.Query<string>("SELECT action FROM fixture_plugin_calls ORDER BY id").ToArray(),
            Loaded = catalog.GetActive<IPluginGameActions>().Any(plugin => plugin.Manifest.Id == "xbox"),
            ShellAttempts = shell.Attempts.ToArray(),
        };
    }

    public async Task<object> ValidateCapturedAsync(bool afterRemoval)
    {
        var play = _capturedPlay ?? throw new InvalidOperationException("Capture an installed observation first.");
        var snapshot = await ReadAsync();
        if (afterRemoval && snapshot.Ownerships.Single().Installed)
            throw new InvalidOperationException("Stale dispatch checks require the removed observation.");
        return new
        {
            WrongOwnershipAccepted = await actions.ExecuteAsync(_capturedOwnership + 1, play),
            ForgedLinkAccepted = GameLink.Create("Forged", "winnow-plugin://action") is not null,
            StaleAccepted = afterRemoval ? await actions.ExecuteAsync(_capturedOwnership, play) : (bool?)null,
        };
    }

    public async Task UnloadAsync()
    {
        await catalog.DisposeAsync();
        await publisher.PublishAsync(default);
    }

    public async Task SeedSteamAsync()
    {
        using var connection = database.Open();
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
            throw new InvalidOperationException("Seed the isolated Steam case before importing any games.");
        connection.Execute("""
            INSERT INTO works(id,name,sort_name) VALUES(440,'Steam fixture','Steam fixture');
            INSERT INTO releases(id,work_id,name,platform) VALUES(440,440,'Steam fixture','windows');
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(440,440,'steam',1);
            INSERT INTO external_ids(release_id,provider,provider_id) VALUES(440,'steam','440');
            """);
        await publisher.PublishAsync(default);
    }

    private Task<LibrarySnapshot> ReadAsync() => library.GetSnapshotAsync(BucketThresholds.Default);
}

internal sealed class PluginActionShellGuard : IUriDispatcher
{
    public ConcurrentQueue<string> Attempts { get; } = new();
    public Task<bool> OpenAsync(Uri uri)
    {
        Attempts.Enqueue(uri.AbsoluteUri);
        return Task.FromResult(true);
    }
}
