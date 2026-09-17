using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Winnow.App;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Tests;

public sealed class ArtworkBrowserPluginIntegrationTests
{
    [Fact]
    public async Task Packaged_SteamGridDB_browses_all_slots_and_keeps_selected_original_after_disable_and_restart()
    {
        await using var host = await Host.CreateAsync();
        var service = host.Service();
        Assert.Contains(service.Sources, source => source.Id == "plugin:steamgriddb");
        var setup = await service.BrowseAsync(host.WorkId, ArtworkSlot.Hero, "plugin:steamgriddb");
        Assert.Empty(setup.Items);
        Assert.True(setup.CanRetry);
        Assert.Contains("API key", setup.Message);
        host.Context.ApiKey = "fixture-key";

        foreach (var slot in Enum.GetValues<ArtworkSlot>())
        {
            var page = await service.BrowseAsync(host.WorkId, slot, "plugin:steamgriddb");
            var candidate = Assert.Single(page.Items);
            Assert.Equal(slot, candidate.Slot);
            Assert.Equal("Fixture artist", candidate.Creator);
            Assert.StartsWith("https://www.steamgriddb.com/", candidate.PageUrl);
            Assert.NotNull(candidate.ThumbnailKey);
            var cache = host.Provider.GetRequiredService<IMetadataCache>();
            Assert.Equal(candidate.Url, (await cache.GetAsync("plugin-artwork", "steamgriddb:" + candidate.PreviewKey.Id))!.Value.PayloadJson);
            Assert.NotNull(await cache.GetAsync("plugin-artwork", "steamgriddb:" + candidate.ThumbnailKey.Value.Id));
            Assert.NotNull(page.NextCursor);
            var second = await service.BrowseAsync(host.WorkId, slot, "plugin:steamgriddb", page.NextCursor);
            Assert.Null(second.NextCursor);
            Assert.Single(second.Items);
            Assert.True((await service.SaveAsync(host.WorkId, slot, candidate)).Success);
        }

        var choices = await host.Provider.GetRequiredService<IArtworkChoiceRepository>().GetAllAsync();
        Assert.Equal(3, choices.Count);
        Assert.Empty(await host.Provider.GetRequiredService<IIdentityLinkRepository>().GetHistoryAsync());
        host.Context.ApiKey = null;
        await host.Catalog.SetEnabledAsync("steamgriddb", false);
        await host.Catalog.DisposeAsync();
        await using var restarted = new PluginCatalog(host.Provider.GetRequiredService<PluginStorage>(), host.Context);
        await restarted.DiscoverAsync(host.Packages, Path.Combine(host.Root, "absent"));
        Assert.Empty(restarted.GetActive<IArtworkBrowserPlugin>());
        var reopened = host.Service(restarted);
        Assert.DoesNotContain(reopened.Sources, source => source.Id == "plugin:steamgriddb");
        var store = new UserArtStore(host.Provider.GetRequiredService<CoverCacheOptions>());
        foreach (var choice in choices)
        {
            var current = await reopened.GetCurrentAsync(host.WorkId, choice.Slot);
            Assert.True(current!.IsCurrent);
            Assert.Equal("plugin:steamgriddb", current.SourceId);
            Assert.True(store.TryRead(UserArtRef.Token(choice.AssetKey)!, out var retained));
            Assert.Equal(host.Context.ImageBytes, retained);
        }
    }

    [Fact]
    public async Task Host_filters_wrong_kind_offhost_and_malformed_plugin_candidates_before_registration()
    {
        await using var host = await Host.CreateAsync(malformedFixture: true);
        var page = await host.Service().BrowseAsync(host.WorkId, ArtworkSlot.Hero, "plugin:artfixture");
        Assert.Equal("valid", Assert.Single(page.Items).AssetId);
        var metadata = host.Provider.GetRequiredService<IMetadataCache>();
        foreach (var url in new[] { "https://art.example/wrong.png", "https://elsewhere.example/art.png", "https://art.example/oversized.png" })
        {
            var key = PluginArtRef.Key("artfixture", url)!.Value;
            Assert.Null(await metadata.GetAsync("plugin-artwork", "artfixture:" + key.Id));
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly TempDatabase _db = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-plugin-browser-" + Guid.NewGuid().ToString("N"));
        public string Packages { get; private set; } = "";
        public FakeContext Context { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public PluginCatalog Catalog => Provider.GetRequiredService<PluginCatalog>();
        public long WorkId { get; private set; }

        public static async Task<Host> CreateAsync(bool malformedFixture = false)
        {
            var host = new Host();
            Directory.CreateDirectory(host.Root);
            host.Packages = Environment.GetEnvironmentVariable("WINNOW_PLUGIN_PACKAGE_ROOT") ?? Path.Combine(AppContext.BaseDirectory, "plugins");
            if (malformedFixture)
            {
                host.Packages = Path.Combine(host.Root, "packages");
                var directory = Path.Combine(host.Packages, "artfixture");
                Directory.CreateDirectory(directory);
                File.Copy(typeof(MalformedArtworkBrowserPlugin).Assembly.Location, Path.Combine(directory, "Winnow.Tests.dll"));
                await File.WriteAllTextAsync(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(new PluginManifest
                {
                    Id = "artfixture", Name = "Artwork fixture", Version = "1.0.0", EntryAssembly = "Winnow.Tests.dll",
                    EntryType = typeof(MalformedArtworkBrowserPlugin).FullName!, Capabilities = ["artwork"],
                    Network = new() { AllowedHosts = ["art.example"] }
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Program.ConfigureServices(services, new(host.Root, host._db.DatabasePath, DataMigrationOutcome.None));
            services.AddSingleton<ISqliteConnectionFactory>(host._db.Factory);
            services.AddSingleton<IPluginContextFactory>(host.Context);
            services.AddSingleton(provider => new PluginHttpClient(new HttpClient(new ImageHandler(host.Context.ImageBytes)),
                provider.GetRequiredService<PluginHttpPolicies>()));
            host.Provider = services.BuildServiceProvider();
            await host.Catalog.DiscoverAsync(host.Packages, Path.Combine(host.Root, "absent"));
            Assert.Contains(host.Catalog.GetActive<IArtworkBrowserPlugin>(), plugin => plugin.Manifest.Id == (malformedFixture ? "artfixture" : "steamgriddb"));
            host.WorkId = await host.Provider.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = "Fixture game" });
            var releases = host.Provider.GetRequiredService<IReleaseRepository>();
            var releaseId = await releases.InsertAsync(new Release { WorkId = host.WorkId, Name = "Fixture game" });
            await releases.AddExternalIdAsync(new ExternalId { ReleaseId = releaseId, Provider = "steam", ProviderId = "620" });
            return host;
        }

        public ArtworkBrowserService Service(PluginCatalog? catalog = null) => new(
            Provider.GetRequiredService<ArtworkSelectionService>(), Provider.GetRequiredService<IWorkRepository>(),
            Provider.GetRequiredService<IReleaseRepository>(), Provider.GetRequiredService<IWorkImageRepository>(),
            Provider.GetRequiredService<IIgdbClient>(), catalog ?? Catalog, Provider.GetRequiredService<IMetadataCache>(),
            Provider.GetRequiredService<CoverPipeline>(), Provider.GetRequiredService<CoverDiskCache>(), Provider.GetRequiredService<UserArtStore>());

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            _db.Dispose();
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class ImageHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    private sealed class FakeContext : IPluginContextFactory, IPluginContext, IPluginSettings, IPluginSecrets, IPluginCache, IPluginHttp
    {
        private readonly Dictionary<string, PluginCacheEntry> _cache = [];
        public string? ApiKey { get; set; }
        public string PluginId => "steamgriddb";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public byte[] ImageBytes { get; }
        public FakeContext()
        {
            using var bitmap = new SKBitmap(100, 80);
            bitmap.Erase(SKColors.MediumPurple);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            ImageBytes = encoded.ToArray();
        }
        public IPluginContext Create(PluginManifest manifest) => this;
        ValueTask<string?> IPluginSettings.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
        ValueTask IPluginSettings.SetAsync(string key, string? value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        ValueTask<string?> IPluginSecrets.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(ApiKey);
        ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(_cache.GetValueOrDefault(key));
        ValueTask IPluginCache.SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken)
        { _cache[key] = entry; return ValueTask.CompletedTask; }
        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
        {
            var type = request.Url.Contains("/heroes/", StringComparison.Ordinal) ? "hero" : request.Url.Contains("/grids/", StringComparison.Ordinal) ? "grid" : "icon";
            var page = request.Url.EndsWith("page=1", StringComparison.Ordinal) ? 1 : 0;
            var hash = new string(page == 0 ? 'a' : 'b', 32);
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                success = true, page, limit = 1, total = 2,
                data = new[] { new { id = page + 1, width = type == "hero" ? 1920 : type == "grid" ? 600 : 256,
                    height = type == "hero" ? 1080 : type == "grid" ? 900 : 256,
                    url = $"https://cdn2.steamgriddb.com/{type}/{hash}.png",
                    thumb = $"https://cdn2.steamgriddb.com/{type}/thumb/{hash}.png", author = new { name = "Fixture artist" } } }
            });
            return Task.FromResult(new PluginHttpResponse(200, body, new Dictionary<string, string>()));
        }
    }
}

public sealed class MalformedArtworkBrowserPlugin : IArtworkProviderPlugin, IArtworkBrowserPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public IReadOnlyList<PluginArtworkKind> SupportedArtworkKinds => [PluginArtworkKind.Background];
    public Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PluginArtwork>?>([]);
    public Task<PluginArtworkPage> BrowseArtworkAsync(PluginGame game, PluginArtworkKind kind, string? cursor = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new PluginArtworkPage([
            new("valid", "https://art.example/valid.png", 1920, 1080),
            new("wrong", "https://art.example/wrong.png", 600, 900) { Kind = PluginArtworkKind.Cover },
            new("offhost", "https://elsewhere.example/art.png", 1920, 1080),
            new("oversized", "https://art.example/oversized.png", 9000, 1080)
        ]));
}
