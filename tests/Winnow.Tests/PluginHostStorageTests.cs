using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginHostStorageTests
{
    private const string HeroUrl = "https://cdn2.steamgriddb.com/hero/0123456789abcdef0123456789abcdef.png";
    private const string LegacySecretKey = "steamgriddb.api_key.protected";

    [Fact]
    public async Task Contexts_isolate_settings_cache_and_secret_fields_in_the_same_database()
    {
        using var host = new Host();
        var first = host.Contexts.Create(Manifest("first"));
        var second = host.Contexts.Create(Manifest("second"));
        await first.Settings.SetAsync("label", "First value");
        await second.Settings.SetAsync("label", "Second value");
        Assert.Equal("First value", await first.Settings.GetAsync("label"));
        Assert.Equal("Second value", await second.Settings.GetAsync("label"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await first.Settings.GetAsync("apikey"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await first.Settings.SetAsync("apikey", "plaintext"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await first.Secrets.GetAsync("label"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await first.Settings.GetAsync("undeclared"));

        var expired = new PluginCacheEntry(Encoding.UTF8.GetBytes("cached response"), DateTimeOffset.UtcNow.AddDays(-1));
        await first.Cache.SetAsync("same-cache-key", expired);
        var cached = await first.Cache.GetAsync("same-cache-key");
        Assert.Equal(expired.Payload, cached!.Payload);
        Assert.Equal(expired.ExpiresAt, cached.ExpiresAt);
        Assert.Null(await second.Cache.GetAsync("same-cache-key"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await first.Cache.SetAsync("oversized",
            new(new byte[2 * 1024 * 1024 + 1], DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task Dotted_plugin_ids_and_setting_keys_cannot_alias_another_plugins_namespace()
    {
        using var host = new Host();
        var nested = host.Contexts.Create(Manifest("a.setting.b") with
        { Settings = [new() { Key = "c", Label = "First setting" }] });
        var plain = host.Contexts.Create(Manifest("a") with
        { Settings = [new() { Key = "b.setting.c", Label = "Second setting" }] });
        await nested.Settings.SetAsync("c", "Nested ID");
        await plain.Settings.SetAsync("b.setting.c", "Dotted key");
        Assert.Equal("Nested ID", await nested.Settings.GetAsync("c"));
        Assert.Equal("Dotted key", await plain.Settings.GetAsync("b.setting.c"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Payload\":null,\"ExpiresAt\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{broken")]
    public async Task Corrupt_cache_envelopes_are_misses(string json)
    {
        using var host = new Host();
        await host.Cache.SetAsync("plugin:first", "corrupt", json, DateTime.UtcNow);
        Assert.Null(await host.Storage.ReadCacheAsync("first", "corrupt"));
    }

    [Fact]
    public async Task Saved_credentials_are_protected_scoped_and_never_reloaded_into_settings_snapshots()
    {
        using var host = new Host();
        if (!OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.Storage.WriteSecretAsync("first", "apikey", "fixture-private-key"));
            return;
        }
        host.AddManifest(Manifest("first"));
        await using var catalog = host.Catalog();
        await catalog.DiscoverAsync(host.BuiltinDirectory, host.UserDirectory);
        var backend = new PluginSettingsBackend(catalog, host.Storage, host.UserDirectory);
        await backend.SaveAsync("first", new Dictionary<string, string> { ["apikey"] = "fixture-private-key" });
        Assert.Equal("fixture-private-key", await host.Storage.ReadSecretAsync("first", "apikey"));
        Assert.Null(await host.Storage.ReadSecretAsync("second", "apikey"));
        using (var connection = host.Database.Factory.Open())
        {
            var values = await connection.QueryAsync<string>("SELECT value FROM settings WHERE value IS NOT NULL;");
            Assert.DoesNotContain(values, value => value.Contains("fixture-private-key", StringComparison.Ordinal));
        }
        var snapshot = Assert.Single(await backend.LoadAsync());
        var secret = snapshot.Settings.Single(field => field.IsSecret);
        Assert.True(secret.HasStoredSecret);
        Assert.Null(secret.Value);
        await backend.RemoveSecretAsync("first", "apikey");
        Assert.Null(await host.Storage.ReadSecretAsync("first", "apikey"));
        Assert.False(await host.Storage.HasStoredSecretAsync("first", "apikey"));
    }

    [Fact]
    public async Task Removing_saved_credentials_preserves_an_explicit_configuration_fallback()
    {
        using var host = new Host();
        host.Configuration["Plugins:first:apikey"] = "fixture-config-key";
        Assert.Equal("fixture-config-key", await host.Contexts.Create(Manifest("first")).Secrets.GetAsync("apikey"));
        if (!OperatingSystem.IsWindows()) return;
        await host.Storage.WriteSecretAsync("first", "apikey", "fixture-stored-key");
        Assert.Equal("fixture-stored-key", await host.Storage.ReadSecretAsync("first", "apikey"));
        await host.Storage.RemoveSecretAsync("first", "apikey");
        Assert.Equal("fixture-config-key", await host.Storage.ReadSecretAsync("first", "apikey"));
    }

    [Fact]
    public async Task Backend_prevalidates_all_fields_and_exposes_invalid_packages_without_executing_them()
    {
        using var host = new Host();
        host.AddManifest(Manifest("first"));
        host.AddManifest(Manifest("incompatible") with { ApiVersion = 99 });
        await using var catalog = host.Catalog();
        await catalog.DiscoverAsync(host.BuiltinDirectory, host.UserDirectory);
        var backend = new PluginSettingsBackend(catalog, host.Storage, host.UserDirectory);
        await host.Storage.WriteSettingAsync("first", "label", "Before");
        await Assert.ThrowsAsync<ArgumentException>(() => backend.SaveAsync("first", new Dictionary<string, string>
        { ["label"] = "Must not persist", ["undeclared"] = "Invalid" }));
        Assert.Equal("Before", await host.Storage.ReadSettingAsync("first", "label"));
        var snapshots = await backend.LoadAsync();
        Assert.Equal(2, snapshots.Count);
        var valid = snapshots.Single(x => x.Id == "first");
        Assert.False(valid.Enabled);
        Assert.False(valid.IsLoaded);
        Assert.True(valid.CanConfigure);
        Assert.False(snapshots.Single(x => x.Id != "first").CanConfigure);
        await backend.SetEnabledAsync("first", true);
        Assert.True((await backend.LoadAsync()).Single(x => x.Id == "first").RestartRequired);
        Assert.False(Assert.Single(catalog.Plugins).Loaded);
    }

    [Fact]
    public async Task Legacy_migration_preserves_artwork_bytes_metadata_age_and_igdb_observations_without_network()
    {
        using var host = new Host();
        LibraryReadFixtures.Seed(host.Database, 2);
        var observed = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var old = new WorkImages
        {
            WorkId = 1, Source = ImageSources.SteamGridDb, Kind = ImageKinds.Artwork, ImageIds = "7", ObservedAt = observed,
            Images = [new() { ImageId = "7", Url = HeroUrl, Width = 3840, Height = 1240, Animated = false, ImageType = "hero" }],
        };
        var igdb = old with { Source = ImageSources.Igdb, ImageIds = "igdbart", Images = [new() { ImageId = "igdbart" }] };
        await host.Images.UpsertAsync(old);
        await host.Images.UpsertAsync(igdb);
        const string payload = "{\"version\":1,\"images\":[]}";
        await host.Cache.SetAsync("steamgriddb", "heroes:steam:1", payload, observed);
        var oldKey = SteamGridDbHeroUrl.Key(HeroUrl)!.Value;
        var newKey = PluginArtRef.Key("steamgriddb", HeroUrl)!.Value;
        byte[] bytes = [1, 3, 5, 7];
        host.Disk.WriteSource(oldKey, bytes);
        await host.Migration().RunAsync();
        var rows = await host.Images.GetForWorkAsync(1);
        Assert.DoesNotContain(rows, row => row.Source == ImageSources.SteamGridDb);
        var migrated = rows.Single(row => row.Source == "plugin:steamgriddb");
        Assert.Equal(old.Images, migrated.Images);
        Assert.Equal(observed, migrated.ObservedAt);
        Assert.Equal(igdb.Images, rows.Single(row => row.Source == ImageSources.Igdb).Images);
        Assert.True(host.Disk.TryReadSource(newKey, out var migratedBytes));
        Assert.Equal(bytes, migratedBytes);
        Assert.True(host.Disk.TryReadSource(oldKey, out _));
        var cache = await host.Storage.ReadCacheAsync("steamgriddb", "heroes:steam:1");
        Assert.Equal(payload, Encoding.UTF8.GetString(cache!.Payload));
        Assert.Equal(new DateTimeOffset(observed).AddDays(30), cache.ExpiresAt);
        Assert.NotNull(await host.Cache.GetAsync("plugin-artwork", "steamgriddb:" + newKey.Id));
        Assert.Equal("true", await host.Settings.GetAsync("plugins.steamgriddb.migrated.v1"));
        await host.Migration().RunAsync();
        Assert.Equal(2, (await host.Images.GetForWorkAsync(1)).Count);
        Assert.Equal(observed, (await host.Images.GetForWorkAsync(1)).Single(row => row.Source == "plugin:steamgriddb").ObservedAt);
    }

    [Fact]
    public async Task Legacy_secret_is_reprotected_and_configuration_alias_remains_available()
    {
        using var host = new Host();
        host.Configuration["SteamGridDb:ApiKey"] = "fixture-config-key";
        if (OperatingSystem.IsWindows())
            await host.Settings.SetAsync(LegacySecretKey, ProtectLegacy("fixture-legacy-key"));
        await host.Migration().RunAsync();
        Assert.Equal("fixture-config-key", host.Configuration["Plugins:steamgriddb:apikey"]);
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("fixture-legacy-key", await host.Storage.ReadSecretAsync("steamgriddb", "apikey"));
        Assert.Null(await host.Settings.GetAsync(LegacySecretKey));
        await host.Storage.RemoveSecretAsync("steamgriddb", "apikey");
        Assert.Equal("fixture-config-key", await host.Storage.ReadSecretAsync("steamgriddb", "apikey"));
    }

    [Fact]
    public async Task Legacy_secret_survives_unreadable_protection_and_does_not_replace_a_valid_new_key()
    {
        using var host = new Host();
        await host.Settings.SetAsync(LegacySecretKey, "not-valid-protected-data");
        await host.Migration().RunAsync();
        Assert.Equal("not-valid-protected-data", await host.Settings.GetAsync(LegacySecretKey));
        if (!OperatingSystem.IsWindows()) return;
        await host.Settings.SetAsync(LegacySecretKey, ProtectLegacy("fixture-old-key"));
        await host.Storage.WriteSecretAsync("steamgriddb", "apikey", "fixture-new-key");
        await host.Migration().RunAsync();
        Assert.Equal("fixture-new-key", await host.Storage.ReadSecretAsync("steamgriddb", "apikey"));
        Assert.Null(await host.Settings.GetAsync(LegacySecretKey));
    }

    [Fact]
    public async Task Corrupt_new_secret_does_not_destroy_a_usable_legacy_key()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        await host.Settings.SetAsync(LegacySecretKey, ProtectLegacy("fixture-legacy-key"));
        await host.Storage.WriteSecretAsync("steamgriddb", "apikey", "fixture-new-key");
        using (var connection = host.Database.Factory.Open())
            await connection.ExecuteAsync("UPDATE settings SET value='corrupt' WHERE key LIKE 'plugin.%';");
        await host.Migration().RunAsync();
        Assert.Equal("fixture-legacy-key", await host.Storage.ReadSecretAsync("steamgriddb", "apikey"));
    }

    [Fact]
    public async Task Source_preferences_translate_legacy_steamgriddb_position_and_include_new_provider_groups()
    {
        using var host = new Host();
        await host.Settings.SetAsync(ArtworkPreferences.SettingKey, "igdb,steamgriddb,steam");
        var preferences = new ArtworkPreferences(host.Settings);
        preferences.ConfigureSources([new("plugin:steamgriddb", "SteamGridDB"), new("plugin:other", "Other artwork")]);
        await preferences.LoadAsync();
        Assert.Equal(["igdb", "plugin:steamgriddb", "steam", "plugin:other"], preferences.SourceOrder);
        await preferences.SaveAsync(["plugin:other", "igdb", "plugin:steamgriddb", "steam"]);
        var restarted = new ArtworkPreferences(host.Settings);
        restarted.ConfigureSources([new("plugin:steamgriddb", "SteamGridDB"), new("plugin:other", "Other artwork")]);
        await restarted.LoadAsync();
        Assert.Equal(preferences.SourceOrder, restarted.SourceOrder);
    }

    private static PluginManifest Manifest(string id) => new()
    {
        Id = id, Name = id, Version = "1.0.0", EntryAssembly = "NotLoaded.dll", EntryType = "Fixture.Plugin",
        Capabilities = [PluginCapabilities.Artwork], Settings =
        [new() { Key = "label", Label = "Label" }, new() { Key = "apikey", Label = "API key", Secret = true, Required = true }],
    };

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ProtectLegacy(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),
        Encoding.UTF8.GetBytes("Winnow.SteamGridDb.ApiKey.v1"), DataProtectionScope.CurrentUser));

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "winnow-plugin-host-" + Guid.NewGuid().ToString("N"));
        public TempDatabase Database { get; } = new();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().AddInMemoryCollection().Build();
        public SqliteSettingsStore Settings { get; }
        public SqliteMetadataCache Cache { get; }
        public PluginStorage Storage { get; }
        public PluginContextFactory Contexts { get; }
        public WorkImageRepository Images { get; }
        public CoverDiskCache Disk { get; }
        public string BuiltinDirectory => Path.Combine(_root, "builtin");
        public string UserDirectory => Path.Combine(_root, "user");
        public Host()
        {
            Settings = new(Database.Factory);
            Cache = new(Database.Factory);
            Storage = new(Settings, Cache, Configuration);
            Images = new(Database.Factory);
            Disk = new(new CoverCacheOptions { CacheDirectory = Path.Combine(_root, "covers") });
            var services = new ServiceCollection();
            services.AddPluginHttp();
            _services = services.BuildServiceProvider();
            Contexts = new(Storage, _services.GetRequiredService<PluginHttpClient>());
        }
        public PluginCatalog Catalog() => new(Storage, Contexts);
        public LegacySteamGridDbPluginMigration Migration() => new(Database.Factory, Storage, Settings, Configuration, Cache, Images, Disk);
        public void AddManifest(PluginManifest manifest)
        {
            var directory = Path.Combine(UserDirectory, manifest.Id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(manifest));
        }
        public void Dispose()
        {
            _services.Dispose();
            Database.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
