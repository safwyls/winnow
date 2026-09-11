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
using Winnow.Data.Repositories;
using Winnow.PluginFixture;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginSyncIntegrationTests
{
    [Fact]
    public async Task Shipped_steamgriddb_package_loads_through_the_public_sdk_without_an_app_reference()
    {
        using var database = new TempDatabase();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(services, new(database.DatabasePath + "-plugin-data", database.DatabasePath, DataMigrationOutcome.None));
        services.AddSingleton<Winnow.Data.ISqliteConnectionFactory>(database.Factory);
        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<PluginCatalog>();
        var packages = Environment.GetEnvironmentVariable("WINNOW_PLUGIN_PACKAGE_ROOT") ?? Path.Combine(AppContext.BaseDirectory, "plugins");
        await catalog.DiscoverAsync(packages, database.DatabasePath + "-absent");
        var plugin = Assert.Single(catalog.GetActive<IArtworkProviderPlugin>(), p => p.Manifest.Id == "steamgriddb");
        Assert.Null(plugin.Error);
        Assert.Contains(plugin.Manifest.Settings, s => s.Key == "apikey" && s.Secret);
        Assert.DoesNotContain(typeof(Program).Assembly.GetReferencedAssemblies(), a => a.Name?.Contains("SteamGridDb", StringComparison.Ordinal) == true);
        Assert.Null(await catalog.InvokeAsync(plugin, (instance, ct) => ((IArtworkProviderPlugin)instance)
            .GetArtworkAsync(new("fixture", "Game", new Dictionary<string, string> { ["steam"] = "220" }), ct)));
    }

    [Fact]
    public async Task Separate_assembly_imports_library_and_applies_scoped_metadata_and_artwork()
    {
        using var database = new TempDatabase();
        LibraryReadFixtures.Seed(database, 2);
        var root = Path.Combine(Path.GetTempPath(), "winnow-plugin-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "packages", "fixture"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(services, new(root, database.DatabasePath, DataMigrationOutcome.None));
        services.AddSingleton<Winnow.Data.ISqliteConnectionFactory>(database.Factory);
        await using var provider = services.BuildServiceProvider();
        var directory = Path.Combine(root, "packages", "fixture");
        File.Copy(typeof(FixturePlugin).Assembly.Location, Path.Combine(directory, "Winnow.PluginFixture.dll"));
        await File.WriteAllTextAsync(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(new PluginManifest
        {
            Id = "fixture", Name = "Fixture", Version = "1.0.0", EntryAssembly = "Winnow.PluginFixture.dll",
            EntryType = typeof(FixturePlugin).FullName!, Capabilities = ["library", "metadata", "artwork"],
            Settings = [new() { Key = "loaded-in-private-context", Label = "Loaded" }],
            Network = new() { AllowedHosts = ["art.example"] }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var catalog = provider.GetRequiredService<PluginCatalog>();
        await catalog.DiscoverAsync(Path.Combine(root, "packages"), Path.Combine(root, "absent"));
        Assert.True(Assert.Single(catalog.Plugins).Loaded);
        var sync = provider.GetRequiredService<PluginSyncService>();
        await sync.SyncAsync();
        var release = await provider.GetRequiredService<IReleaseRepository>().FindByExternalIdAsync("plugin:fixture", "fixture-game");
        Assert.NotNull(release);
        Assert.Equal(release.Id, (await provider.GetRequiredService<IReleaseRepository>().FindByExternalIdAsync("steam", "220"))!.Id);
        Assert.Equal("Fixture metadata from fixture", (await provider.GetRequiredService<IWorkRepository>().GetAsync(release.WorkId))!.Summary);
        Assert.Equal("plugin:fixture", (await provider.GetRequiredService<IWorkFieldSourceRepository>().GetSourcesAsync(release.WorkId))[WorkFields.Summary]);
        var images = provider.GetRequiredService<IWorkImageRepository>();
        var before = Assert.Single(await images.GetForWorkAsync(release.WorkId));
        Assert.Equal("plugin:fixture", before.Source);
        Assert.Single(BackdropSelection.Candidates(null, [before], sourceOrder: ["plugin:fixture"]), key => PluginArtRef.PluginId(key) == "fixture");
        await sync.SyncAsync();
        Assert.Equal(before.ObservedAt, Assert.Single(await images.GetForWorkAsync(release.WorkId)).ObservedAt);
        Assert.Equal(3, (await provider.GetRequiredService<IOwnershipRepository>().GetAllAsync()).Count);
        await provider.GetRequiredService<IWorkFieldSourceRepository>().SetFieldAsync(release.WorkId, WorkFields.Summary, "My summary");
        await sync.ApplyMetadataAsync("fixture", release.WorkId, new() { Summary = "Replacement", Tags = ["Custom tag"] }, default);
        Assert.Equal("My summary", (await provider.GetRequiredService<IWorkRepository>().GetAsync(release.WorkId))!.Summary);
        await new FacetRepository(database.Factory).SetWorkFacetsAsync(release.WorkId, [new(FacetKinds.Genre, "Built-in genre")]);
        var facets = await new FacetRepository(database.Factory).GetSnapshotAsync();
        Assert.Contains(facets.Facets, f => f.Name == "Custom tag");
        Assert.Contains(facets.Facets, f => f.Name == "Built-in genre");
        await sync.ApplyArtworkAsync(catalog.Plugins[0], release.WorkId, [new("bad", "https://elsewhere.example/art.png", 3840, 2160)], default);
        Assert.Single(await images.GetForWorkAsync(release.WorkId));
        await sync.ApplyArtworkAsync(catalog.Plugins[0], release.WorkId, [], default);
        Assert.Empty(await images.GetForWorkAsync(release.WorkId));
        await catalog.DisposeAsync();
        // Collectible assemblies can remain mapped until a later collection on Windows.
        try { Directory.Delete(root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Plugin_facets_refresh_only_their_source_and_leave_builtin_assignments()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var plugins = new PluginFacetRepository(db.Factory);
        var builtin = new FacetRepository(db.Factory);
        await builtin.SetWorkFacetsAsync(1, [new(FacetKinds.Genre, "RPG")]);
        await plugins.SetAsync(1, "plugin:first", [new(FacetKinds.Tag, "One")]);
        await plugins.SetAsync(1, "plugin:second", [new(FacetKinds.Tag, "Two")]);
        await plugins.SetAsync(1, "plugin:first", []);
        var result = await builtin.GetSnapshotAsync();
        var names = result.ByRelease[1].FacetIds.Select(id => result.ById[id].Name);
        Assert.Equal(["RPG", "Two"], names.Order().ToArray());
    }
}
