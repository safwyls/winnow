using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.PluginFixture;
using Winnow.Plugins;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginSyncValidationTests
{
    [Fact]
    public async Task Every_steam_release_contributes_artwork_and_an_unavailable_release_preserves_the_previous_observation()
    {
        await using var host = await Host.CreateAsync(("release-art", typeof(ReleaseArtworkPlugin), [PluginCapabilities.Artwork]));
        using (var connection = host.Database.Factory.Open())
            await connection.ExecuteAsync("""
                INSERT INTO releases(id,work_id,name,platform) VALUES(3,1,'Another release','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES(3,3,'steam',0);
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(3,'steam','3');
                """);
        await host.Sync.SyncAsync();
        var images = host.Provider.GetRequiredService<IWorkImageRepository>();
        var original = Assert.Single(await images.GetForWorkAsync(1));
        Assert.Equal("plugin:release-art", original.Source);
        Assert.Equal("https://art.example/hero-3-old.png", Assert.Single(original.Images).Url);
        Assert.Equal(["1", "3", "2"], (await host.Storage.ReadSettingAsync("release-art", "calls"))!.Split(','));

        await host.Storage.WriteSettingAsync("release-art", "unavailable", "1");
        await host.Storage.WriteSettingAsync("release-art", "revision", "new");
        await host.Sync.SyncAsync();
        var preserved = Assert.Single(await images.GetForWorkAsync(1));
        Assert.Equal(original.ObservedAt, preserved.ObservedAt);
        Assert.Equal(original.Images, preserved.Images);

        await host.Storage.WriteSettingAsync("release-art", "unavailable", null);
        await host.Sync.SyncAsync();
        Assert.Equal("https://art.example/hero-3-new.png", Assert.Single(Assert.Single(await images.GetForWorkAsync(1)).Images).Url);

        await images.UpsertAsync(new()
        {
            WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Artwork,
            ImageIds = "unchanged", Images = [new() { ImageId = "unchanged", Width = 1920, Height = 1080 }], ObservedAt = DateTime.UtcNow,
        });
        await host.Storage.WriteSettingAsync("release-art", "empty", "true");
        await host.Sync.SyncAsync();
        Assert.Equal(ImageSources.Igdb, Assert.Single(await images.GetForWorkAsync(1)).Source);
    }

    [Fact]
    public async Task Null_shapes_and_broken_metadata_do_not_block_other_providers_or_erase_prior_artwork()
    {
        await using var host = await Host.CreateAsync(
            ("a-broken", typeof(BrokenMetadataPlugin), [PluginCapabilities.Metadata]),
            ("b-null", typeof(NullShapeSyncPlugin), [PluginCapabilities.Library, PluginCapabilities.Metadata, PluginCapabilities.Artwork]),
            ("z-good", typeof(FixturePlugin), [PluginCapabilities.Library, PluginCapabilities.Metadata, PluginCapabilities.Artwork]));
        var images = host.Provider.GetRequiredService<IWorkImageRepository>();
        const string previousUrl = "https://art.example/previous.png";
        var previousId = PluginArtRef.Key("b-null", previousUrl)!.Value.Id;
        await images.UpsertAsync(new()
        {
            WorkId = 1, Source = "plugin:b-null", Kind = ImageKinds.Artwork,
            ImageIds = previousId, Images = [new() { ImageId = previousId, Url = previousUrl, Width = 3840, Height = 2160 }],
            ObservedAt = DateTime.UtcNow.AddDays(-1),
        });
        await host.Sync.SyncAsync();

        var releases = host.Provider.GetRequiredService<IReleaseRepository>();
        Assert.NotNull(await releases.FindByExternalIdAsync("plugin:b-null", "nullable-game"));
        Assert.NotNull(await releases.FindByExternalIdAsync("plugin:z-good", "fixture-game"));
        var works = host.Provider.GetRequiredService<IWorkRepository>();
        Assert.Equal("Fixture metadata from z-good", (await works.GetAsync(1))!.Summary);
        Assert.Equal("Fixture metadata from z-good", (await works.GetAsync(2))!.Summary);
        var firstImages = await images.GetForWorkAsync(1);
        Assert.Equal(previousUrl, Assert.Single(Assert.Single(firstImages, row => row.Source == "plugin:b-null").Images).Url);
        Assert.Contains(firstImages, row => row.Source == "plugin:z-good");
        Assert.Contains(await images.GetForWorkAsync(2), row => row.Source == "plugin:z-good");
        var error = Assert.Single(host.Catalog.Plugins, plugin => plugin.Manifest.Id == "a-broken").Error;
        Assert.Equal("The plugin returned data that could not be applied.", error);
        Assert.DoesNotContain("private-key", error!, StringComparison.Ordinal);
    }

    private sealed class Host : IAsyncDisposable
    {
        public TempDatabase Database { get; } = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-sync-validation-" + Guid.NewGuid().ToString("N"));
        public ServiceProvider Provider { get; private set; } = null!;
        public PluginCatalog Catalog => Provider.GetRequiredService<PluginCatalog>();
        public PluginSyncService Sync => Provider.GetRequiredService<PluginSyncService>();
        public PluginStorage Storage => Provider.GetRequiredService<PluginStorage>();

        public static async Task<Host> CreateAsync(params (string Id, Type Type, string[] Capabilities)[] plugins)
        {
            var host = new Host();
            LibraryReadFixtures.Seed(host.Database, 2);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Program.ConfigureServices(services, new(host.Root, host.Database.DatabasePath, DataMigrationOutcome.None));
            services.AddSingleton<ISqliteConnectionFactory>(host.Database.Factory);
            host.Provider = services.BuildServiceProvider();
            var packages = Path.Combine(host.Root, "packages");
            foreach (var plugin in plugins)
            {
                var directory = Path.Combine(packages, plugin.Id);
                Directory.CreateDirectory(directory);
                File.Copy(plugin.Type.Assembly.Location, Path.Combine(directory, "Winnow.PluginFixture.dll"));
                await File.WriteAllTextAsync(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(new PluginManifest
                {
                    Id = plugin.Id, Name = plugin.Id, Version = "1.0.0", EntryAssembly = "Winnow.PluginFixture.dll", EntryType = plugin.Type.FullName!,
                    Capabilities = plugin.Capabilities, Network = new() { AllowedHosts = ["art.example"] },
                    Settings = [new() { Key = "loaded-in-private-context", Label = "Loaded" }, new() { Key = "calls", Label = "Calls" },
                        new() { Key = "unavailable", Label = "Unavailable" }, new() { Key = "revision", Label = "Revision" }, new() { Key = "empty", Label = "Empty" }],
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
            await host.Catalog.DiscoverAsync(packages, Path.Combine(host.Root, "absent"));
            Assert.Equal(plugins.Length, host.Catalog.Plugins.Count);
            Assert.All(host.Catalog.Plugins, plugin => Assert.True(plugin.Loaded, plugin.Error));
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            Database.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var absoluteRoot = Path.GetFullPath(Root);
            if (!absoluteRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The fixture directory escaped the temporary directory.");
            try { Directory.Delete(absoluteRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
