using System.IO.Compression;
using System.Text.Json;
using Winnow.PluginFixture;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Plugins.Tests;

public sealed class PluginCatalogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dropped_zip_is_discovered_disabled_and_loads_only_after_enablement_and_restart(bool enclosingFolder)
    {
        using var files = new Packages();
        var archive = files.AddZip("download.ZIP", "fixture", enclosingFolder);
        var state = new State();
        var context = new Context();
        await using (var first = new PluginCatalog(state, context))
        {
            await first.DiscoverAsync(files.Builtin, files.User);
            var plugin = Assert.Single(first.Plugins);
            Assert.Equal("fixture", plugin.Manifest.Id);
            Assert.Equal(Path.Combine(files.User, "fixture"), plugin.DirectoryPath);
            Assert.False(plugin.Enabled);
            Assert.False(plugin.Loaded);
            Assert.Empty(context.Values);
            Assert.Empty(first.Issues);
            Assert.False(File.Exists(archive));
            Assert.Single(Directory.GetFiles(Path.Combine(files.User, ".archives"), "*.zip"));
            await first.SetEnabledAsync("fixture", true);
            Assert.Empty(context.Values);
        }
        await using var restarted = new PluginCatalog(state, context);
        await restarted.DiscoverAsync(files.Builtin, files.User);
        Assert.True(Assert.Single(restarted.Plugins).Loaded);
        Assert.Empty(restarted.Issues);
        Assert.Single(Directory.GetFiles(Path.Combine(files.User, ".archives"), "*.zip"));
    }

    [Fact]
    public async Task Bad_archives_do_not_block_other_imports_and_staging_directories_are_never_loaded()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "builtin");
        files.Add(files.User, ".unpack-interrupted", manifest: Packages.Manifest("unfinished"));
        var good = files.AddZip("b-good.zip", "fixture");
        var bad = Path.Combine(files.User, "a-bad.zip");
        await File.WriteAllTextAsync(bad, "not a ZIP");
        await using var catalog = new PluginCatalog(new State(), new Context());

        await catalog.DiscoverAsync(files.Builtin, files.User);

        Assert.Equal(["builtin", "fixture"], catalog.Plugins.Select(plugin => plugin.Manifest.Id));
        Assert.True(catalog.Plugins[0].Loaded);
        Assert.Equal(bad, Assert.Single(catalog.Issues).DirectoryPath);
        Assert.Equal("not a ZIP", await File.ReadAllTextAsync(bad));
        Assert.False(File.Exists(good));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dropped_zip_cannot_replace_a_plugin_already_discovered_in_another_directory(bool builtin)
    {
        using var files = new Packages();
        files.Add(builtin ? files.Builtin : files.User, "custom-folder", manifest: Packages.Manifest("fixture"));
        var archive = files.AddZip("replacement.zip", "fixture");
        await using var catalog = new PluginCatalog(new State(), new Context());

        await catalog.DiscoverAsync(files.Builtin, files.User);

        Assert.EndsWith("custom-folder", Assert.Single(catalog.Plugins).DirectoryPath);
        Assert.Equal(archive, Assert.Single(catalog.Issues).DirectoryPath);
        Assert.True(File.Exists(archive));
        Assert.False(Directory.Exists(Path.Combine(files.User, "fixture")));
    }

    [Fact]
    public async Task Discovery_creates_the_missing_user_directory_without_creating_the_builtin_directory()
    {
        using var files = new Packages();
        await using var catalog = new PluginCatalog(new State(), new Context());

        await catalog.DiscoverAsync(files.Builtin, files.User);

        Assert.True(Directory.Exists(files.User));
        Assert.False(Directory.Exists(files.Builtin));
        Assert.Empty(catalog.Issues);
    }

    [Fact]
    public async Task An_uncreatable_user_directory_reports_an_issue_and_keeps_builtin_plugins_available()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "fixture");
        File.WriteAllText(files.User, "existing file");
        await using var catalog = new PluginCatalog(new State(), new Context());

        await catalog.DiscoverAsync(files.Builtin, files.User);

        Assert.True(Assert.Single(catalog.Plugins).Loaded);
        Assert.Equal("existing file", File.ReadAllText(files.User));
        Assert.Equal("The plugin directory could not be created or read.", Assert.Single(catalog.Issues).Message);
    }

    [Fact]
    public async Task Background_discovery_does_not_mutate_a_collection_already_being_read_by_the_ui()
    {
        using var files = new Packages();
        files.Add(files.User, "fixture");
        files.Add(files.User, "wrong-api", manifest: Packages.Manifest("wrong-api") with { ApiVersion = 99 });
        await using var catalog = new PluginCatalog(new State(), new Context());
        var priorPlugins = catalog.Plugins;
        var priorIssues = catalog.Issues;
        await catalog.DiscoverAsync(files.Builtin, files.User);
        Assert.Empty(priorPlugins);
        Assert.Empty(priorIssues);
        Assert.Single(catalog.Plugins);
        Assert.Single(catalog.Issues);
    }

    [Fact]
    public async Task Installed_plugin_is_described_without_running_code_until_explicitly_enabled_and_restarted()
    {
        using var files = new Packages();
        files.Add(files.User, "fixture");
        var state = new State();
        var context = new Context();
        await using (var first = new PluginCatalog(state, context))
        {
            await first.DiscoverAsync(files.Builtin, files.User);
            var plugin = Assert.Single(first.Plugins);
            Assert.False(plugin.Enabled);
            Assert.False(plugin.Loaded);
            Assert.Empty(context.Values);
            await first.SetEnabledAsync("fixture", true);
            Assert.True(plugin.RestartRequired);
            Assert.Empty(first.GetActive<IArtworkProviderPlugin>());
        }
        await using var restarted = new PluginCatalog(state, context);
        await restarted.DiscoverAsync(files.Builtin, files.User);
        Assert.True(Assert.Single(restarted.Plugins).Loaded);
        Assert.Equal("True", context.Values["loaded-in-private-context"]);
    }

    [Fact]
    public async Task Builtin_package_loads_in_isolated_context_and_executes_all_four_sdk_capabilities()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "fixture");
        await using var catalog = new PluginCatalog(new State(), new Context());
        await catalog.DiscoverAsync(files.Builtin, files.User);
        var descriptor = Assert.Single(catalog.Plugins);
        Assert.True(descriptor.BuiltIn);
        Assert.True(descriptor.Loaded);
        var game = new PluginGame("host-1", "Fixture game", new Dictionary<string, string> { ["steam"] = "220" });
        var library = await catalog.InvokeAsync<IReadOnlyList<PluginLibraryGame>>(descriptor,
            (plugin, token) => ((ILibrarySourcePlugin)plugin).GetLibraryAsync(token));
        Assert.Equal("fixture-game", Assert.Single(library!).SourceId);
        var metadata = await catalog.InvokeAsync<PluginMetadata>(descriptor,
            (plugin, token) => ((IMetadataProviderPlugin)plugin).GetMetadataAsync(game, token));
        Assert.Equal("Fixture metadata from fixture", metadata!.Summary);
        var artwork = await catalog.InvokeAsync<IReadOnlyList<PluginArtwork>>(descriptor,
            (plugin, token) => ((IArtworkProviderPlugin)plugin).GetArtworkAsync(game, token));
        Assert.Equal(3840, Assert.Single(artwork!).Width);
        var feed = await catalog.InvokeAsync<IReadOnlyList<PluginRecommendation>>(descriptor,
            (plugin, token) => ((IRecommendationFeedPlugin)plugin).GetRecommendationsAsync([game], token));
        Assert.Equal("host-1", Assert.Single(feed!).GameId);
    }

    [Fact]
    public async Task User_collision_cannot_replace_builtin_and_invalid_manifests_never_execute()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "fixture");
        files.Add(files.User, "fixture");
        files.Add(files.User, "wrong-api", manifest: Packages.Manifest("wrong-api") with { ApiVersion = 99 });
        await using var catalog = new PluginCatalog(new State(), new Context());
        await catalog.DiscoverAsync(files.Builtin, files.User);
        Assert.True(Assert.Single(catalog.Plugins).BuiltIn);
        Assert.Equal(2, catalog.Issues.Count);
    }

    [Fact]
    public async Task A_broken_package_does_not_block_other_plugins_or_expose_exception_secrets()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "broken", typeof(ThrowingPlugin));
        files.Add(files.Builtin, "fixture");
        await using var catalog = new PluginCatalog(new State(), new Context());
        await catalog.DiscoverAsync(files.Builtin, files.User);
        var broken = catalog.Plugins.Single(x => x.Manifest.Id == "broken");
        Assert.False(broken.Loaded);
        Assert.DoesNotContain("private-api-key", broken.Error!);
        Assert.Equal("fixture", Assert.Single(catalog.GetActive<IArtworkProviderPlugin>()).Manifest.Id);
    }

    [Fact]
    public async Task Initialization_timeout_isolated_and_undeclared_interfaces_are_not_registered()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "slow", typeof(SlowPlugin));
        files.Add(files.Builtin, "art-only", manifest: Packages.Manifest("art-only") with { Capabilities = [PluginCapabilities.Artwork] });
        await using var catalog = new PluginCatalog(new State(), new Context()) { InitializationTimeout = TimeSpan.FromMilliseconds(100) };
        await catalog.DiscoverAsync(files.Builtin, files.User);
        Assert.NotNull(catalog.Plugins.Single(x => x.Manifest.Id == "slow").Error);
        Assert.Single(catalog.GetActive<IArtworkProviderPlugin>());
        Assert.Empty(catalog.GetActive<ILibrarySourcePlugin>());
    }

    [Fact]
    public async Task Invocation_exception_is_safe_and_retryable_but_timeout_disables_further_calls()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "fixture");
        await using var catalog = new PluginCatalog(new State(), new Context()) { InvocationTimeout = TimeSpan.FromMilliseconds(80) };
        await catalog.DiscoverAsync(files.Builtin, files.User);
        var descriptor = Assert.Single(catalog.Plugins);
        Assert.Null(await catalog.InvokeAsync<PluginMetadata>(descriptor, (_, _) => throw new Exception("private-api-key")));
        Assert.DoesNotContain("private-api-key", descriptor.Error!);
        Assert.True(descriptor.Loaded);
        Assert.NotNull(await catalog.InvokeAsync<PluginMetadata>(descriptor, (_, _) => Task.FromResult<PluginMetadata?>(new())));
        Assert.Null(descriptor.Error);
        var never = new TaskCompletionSource<PluginMetadata?>();
        Assert.Null(await catalog.InvokeAsync<PluginMetadata>(descriptor, (_, _) => never.Task));
        Assert.False(descriptor.Loaded);
        Assert.Empty(catalog.GetActive<IArtworkProviderPlugin>());
        never.SetResult(null);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_leaking_plugin_exception_text()
    {
        using var files = new Packages();
        files.Add(files.Builtin, "fixture");
        await using var catalog = new PluginCatalog(new State(), new Context());
        await catalog.DiscoverAsync(files.Builtin, files.User);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.InvokeAsync<PluginMetadata>(
            Assert.Single(catalog.Plugins), (_, _) => Task.FromResult<PluginMetadata?>(new()), cancellation.Token));
    }

    [Fact]
    public async Task Failed_enable_persistence_preserves_the_previous_setting()
    {
        using var files = new Packages();
        files.Add(files.User, "fixture");
        var state = new State { FailWrites = true };
        await using var catalog = new PluginCatalog(state, new Context());
        await catalog.DiscoverAsync(files.Builtin, files.User);
        await Assert.ThrowsAsync<IOException>(() => catalog.SetEnabledAsync("fixture", true));
        Assert.False(Assert.Single(catalog.Plugins).Enabled);
    }

    private sealed class State : IPluginStateStore
    {
        private readonly Dictionary<string, bool> _enabled = [];
        public bool FailWrites { get; init; }
        public ValueTask<bool?> GetEnabledAsync(string pluginId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_enabled.TryGetValue(pluginId, out var enabled) ? (bool?)enabled : null);
        public ValueTask SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
        {
            if (FailWrites) throw new IOException("Fixture storage failure");
            _enabled[pluginId] = enabled;
            return default;
        }
    }

    private sealed class Context : IPluginContextFactory, IPluginContext, IPluginSettings, IPluginSecrets, IPluginCache, IPluginHttp
    {
        public Dictionary<string, string?> Values { get; } = [];
        public string PluginId { get; private set; } = "";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public IPluginContext Create(PluginManifest manifest) { PluginId = manifest.Id; return this; }
        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult(Values.GetValueOrDefault(key));
        public ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken = default) { Values[key] = value; return default; }
        ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<PluginCacheEntry?>(null);
        public ValueTask SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken = default) => default;
        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Packages : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "winnow-plugin-fixture-" + Guid.NewGuid().ToString("N"));
        public string Builtin => Path.Combine(_root, "builtin");
        public string User => Path.Combine(_root, "user");
        public string AddZip(string name, string id, bool enclosingFolder = false)
        {
            var source = Path.Combine(_root, "source-" + Guid.NewGuid().ToString("N"));
            Add(source, "package", manifest: Manifest(id));
            Directory.CreateDirectory(User);
            var archive = Path.Combine(User, name);
            ZipFile.CreateFromDirectory(enclosingFolder ? source : Path.Combine(source, "package"), archive);
            return archive;
        }
        public void Add(string root, string id, Type? entry = null, PluginManifest? manifest = null)
        {
            var directory = Path.Combine(root, id);
            Directory.CreateDirectory(directory);
            File.Copy(typeof(FixturePlugin).Assembly.Location, Path.Combine(directory, "Winnow.PluginFixture.dll"));
            File.WriteAllText(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(manifest ?? Manifest(id, entry)));
        }
        public static PluginManifest Manifest(string id, Type? entry = null) => new()
        {
            Id = id, Name = id, Version = "1.0.0", EntryAssembly = "Winnow.PluginFixture.dll",
            EntryType = (entry ?? typeof(FixturePlugin)).FullName!,
            Settings = [new() { Key = "loaded-in-private-context", Label = "Fixture context" }],
            Capabilities = entry is null
                ? [PluginCapabilities.Library, PluginCapabilities.Metadata, PluginCapabilities.Artwork, PluginCapabilities.Recommendations]
                : [PluginCapabilities.Artwork],
        };
        public void Dispose()
        {
            // Collectible contexts unload after their last managed reference dies. An async
            // test frame may retain one until after this teardown has returned.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
