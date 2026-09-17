using SkiaSharp;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Tests;

public sealed class ArtworkBrowserServiceTests
{
    [Fact]
    public async Task Current_cover_uses_live_library_projection_but_never_replaces_a_saved_choice()
    {
        await using var host = new Host();
        var work = await host.Work();
        var release = await host.Releases.InsertAsync(new Release { WorkId = work, Name = "Game" });
        await host.Releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = "620" });
        Assert.Equal(CoverKey.Steam("620"), (await host.Service.GetCurrentAsync(work, ArtworkSlot.Cover))!.PreviewKey);
        CoverKey? projected = CoverKey.Igdb("preferred");
        var displayed = new DisplayedArtworkBrowserService(host.Service, () => projected);
        Assert.Equal(projected, (await displayed.GetCurrentAsync(work, ArtworkSlot.Cover))!.PreviewKey);

        Assert.True((await displayed.SaveAsync(work, ArtworkSlot.Cover,
            new ArtworkCandidate("igdb", "IGDB", "manual", ArtworkSlot.Cover, new CoverKey("test", "valid")))).Success);
        var saved = Assert.Single(await host.Choices.GetAllAsync());
        var chosenKey = CoverKey.User(UserArtRef.Token(saved.AssetKey)!);
        projected = CoverKey.Igdb("newprojection");
        Assert.Equal(chosenKey, (await displayed.GetCurrentAsync(work, ArtworkSlot.Cover))!.PreviewKey);
        Assert.True((await displayed.ResetAsync(work, ArtworkSlot.Cover)).Success);
        Assert.Equal(projected, (await displayed.GetCurrentAsync(work, ArtworkSlot.Cover))!.PreviewKey);
        projected = CoverKey.Steam("440");
        Assert.Equal(projected, (await displayed.GetCurrentAsync(work, ArtworkSlot.Cover))!.PreviewKey);
    }

    [Theory]
    [InlineData(ArtworkSlot.Hero)]
    [InlineData(ArtworkSlot.Cover)]
    [InlineData(ArtworkSlot.Icon)]
    public async Task Saving_retains_original_bytes_and_provenance_until_explicit_reset(ArtworkSlot slot)
    {
        await using var host = new Host();
        var work = await host.Work();
        var candidate = new ArtworkCandidate("igdb", "IGDB", "asset", slot, new CoverKey("test", "valid"))
        { Creator = "Artist", PageUrl = "https://example.com/art", Url = "https://example.com/image.png" };
        Assert.True((await host.Service.SaveAsync(work, slot, candidate)).Success);
        var saved = Assert.Single(await host.Choices.GetAllAsync());
        Assert.Equal("Artist", saved.Creator);
        Assert.Equal("https://example.com/art", saved.PageUrl);
        Assert.Equal("https://example.com/image.png", saved.SourceUrl);
        Assert.True(new UserArtStore(host.Options).TryRead(UserArtRef.Token(saved.AssetKey)!, out var retained));
        Assert.Equal(host.ImageBytes, retained);
        var current = await host.CreateService().GetCurrentAsync(work, slot);
        Assert.True(current!.IsCurrent);
        Assert.Equal("user", current.PreviewKey.Provider);
        Assert.Equal("igdb", current.SourceId);
        Assert.Empty(await host.Links.GetHistoryAsync());

        var failed = await host.Service.SaveAsync(work, slot, candidate with { PreviewKey = new CoverKey("missing", "none") });
        Assert.False(failed.Success);
        Assert.Equal(saved, Assert.Single(await host.Choices.GetAllAsync()));
        Assert.True((await host.Service.ResetAsync(work, slot)).Success);
        Assert.Empty(await host.Choices.GetAllAsync());
    }

    [Theory]
    [InlineData(ArtworkSlot.Hero)]
    [InlineData(ArtworkSlot.Cover)]
    [InlineData(ArtworkSlot.Icon)]
    public async Task Imports_validate_decode_and_preserve_saved_choice_when_input_is_invalid(ArtworkSlot slot)
    {
        await using var host = new Host();
        var work = await host.Work();
        var path = Path.Combine(host.Root, "import.png");
        await File.WriteAllBytesAsync(path, host.ImageBytes);
        Assert.True((await host.Service.ImportFileAsync(work, slot, path)).Success);
        var saved = Assert.Single(await host.Choices.GetAllAsync());
        File.Delete(path);
        Assert.NotNull(await host.CreateService().GetCurrentAsync(work, slot));
        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        Assert.False((await host.Service.ImportFileAsync(work, slot, path)).Success);
        Assert.False((await host.Service.ImportUrlAsync(work, slot, "not a URL")).Success);
        Assert.Equal(saved, Assert.Single(await host.Choices.GetAllAsync()));
        Assert.Empty(await host.Links.GetHistoryAsync());
    }

    [Fact]
    public async Task Igdb_offers_cover_and_paged_landscapes_but_never_invents_icons()
    {
        await using var host = new Host();
        var work = await host.Work(igdbId: 10);
        host.Igdb.Games = [new IgdbGame(10, "Game", "https://images.igdb.com/igdb/image/upload/t_cover_big/co123.jpg", null, null, [], [], [])
        { ArtworkImageIds = Enumerable.Range(0, 45).Select(i => "ar" + i).ToArray() }];
        var cover = Assert.Single((await host.Service.BrowseAsync(work, ArtworkSlot.Cover, "igdb")).Items);
        Assert.Equal(CoverKey.Igdb("co123"), cover.PreviewKey);
        var hero = await host.Service.BrowseAsync(work, ArtworkSlot.Hero, "igdb");
        Assert.Equal(40, hero.Items.Count);
        Assert.Equal("40", hero.NextCursor);
        Assert.All(hero.Items, item => Assert.Equal(CoverProviders.IgdbBackdrop, item.PreviewKey.Provider));
        Assert.Equal(5, (await host.Service.BrowseAsync(work, ArtworkSlot.Hero, "igdb", hero.NextCursor)).Items.Count);
        var icons = await host.Service.BrowseAsync(work, ArtworkSlot.Icon, "igdb");
        Assert.Empty(icons.Items);
        Assert.Contains("does not provide", icons.Message);
        Assert.Empty(await host.Choices.GetAllAsync());
    }

    [Fact]
    public async Task Igdb_game_cache_cannot_reintroduce_unsuitable_hero_images_filtered_from_observations()
    {
        await using var host = new Host();
        var work = await host.Work(igdbId: 10);
        GameImage[] images =
        [
            new() { ImageId = "landscape", Width = 1920, Height = 1080 },
            new() { ImageId = "portrait", Width = 600, Height = 900 },
            new() { ImageId = "square", Width = 600, Height = 600 },
            new() { ImageId = "animated", Animated = true },
            new() { ImageId = "transparent", AlphaChannel = true }
        ];
        await host.Images.UpsertAsync(new WorkImages
        {
            WorkId = work, Source = ImageSources.Igdb, Kind = ImageKinds.Artwork,
            ImageIds = string.Join(',', images.Select(i => i.ImageId)), Images = images,
            ObservedAt = DateTime.UtcNow
        });
        host.Igdb.Games = [new IgdbGame(10, "Game", null, null, null, [], [], [])
        {
            ArtworkImageIds = images.Select(i => i.ImageId).ToArray(), ArtworkImages = images
        }];
        var candidate = Assert.Single((await host.Service.BrowseAsync(work, ArtworkSlot.Hero, "igdb")).Items);
        Assert.Equal("landscape", candidate.AssetId);
        Assert.Equal(1920, candidate.Width);
        Assert.Equal(1080, candidate.Height);
    }

    [Fact]
    public async Task Steam_candidates_use_source_isolated_keys_and_browsing_never_changes_identity()
    {
        await using var host = new Host();
        var work = await host.Work();
        var release = await host.Releases.InsertAsync(new Release { WorkId = work, Name = "Game" });
        await host.Releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = "620" });
        foreach (var slot in Enum.GetValues<ArtworkSlot>())
        {
            var page = await host.Service.BrowseAsync(work, slot, "steam");
            Assert.Equal(slot == ArtworkSlot.Hero ? 2 : 1, page.Items.Count);
            Assert.All(page.Items, candidate => Assert.StartsWith("artwork-steam-", candidate.PreviewKey.Provider));
        }
        Assert.Empty(await host.Links.GetHistoryAsync());
        Assert.Empty(await host.Choices.GetAllAsync());
        Assert.Equal("620", Assert.Single(await host.Releases.GetExternalIdsAsync(release)).ProviderId);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly PluginCatalog _plugins = new(new PluginState(), new Contexts());
        private readonly CoverPipeline _pipeline;
        private readonly CoverDiskCache _disk;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-browser-test-" + Guid.NewGuid().ToString("N"));
        public CoverCacheOptions Options { get; }
        public byte[] ImageBytes { get; }
        public ArtworkChoiceRepository Choices { get; }
        public IdentityLinkRepository Links { get; }
        public ReleaseRepository Releases { get; }
        public WorkImageRepository Images { get; }
        public FakeIgdb Igdb { get; } = new();
        public ArtworkBrowserService Service { get; }
        public Host()
        {
            Directory.CreateDirectory(Root);
            Options = new CoverCacheOptions { CacheDirectory = Path.Combine(Root, "covers") };
            using var bitmap = new SKBitmap(100, 80);
            bitmap.Erase(SKColors.Coral);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            ImageBytes = data.ToArray();
            _disk = new CoverDiskCache(Options);
            _pipeline = new CoverPipeline([new Images(ImageBytes)], _disk, Options);
            Choices = new ArtworkChoiceRepository(_db.Factory);
            Links = new IdentityLinkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Images = new WorkImageRepository(_db.Factory);
            Service = CreateService();
        }
        public ArtworkBrowserService CreateService() => new(new ArtworkSelectionService(Choices, Links),
            new WorkRepository(_db.Factory), Releases, Images, Igdb, _plugins,
            new SqliteMetadataCache(_db.Factory), _pipeline, _disk, new UserArtStore(Options));
        public Task<long> Work(long? igdbId = null) => new WorkRepository(_db.Factory).InsertAsync(new Work { Name = "Game", IgdbId = igdbId });
        public async ValueTask DisposeAsync()
        {
            _pipeline.Dispose();
            await _plugins.DisposeAsync();
            _db.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Images(byte[] bytes) : ICoverSource
    {
        public string Name => "test";
        public bool CanHandle(CoverKey key) => key.Provider == "test";
        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default) => Task.FromResult<byte[]?>(bytes);
    }
    private sealed class PluginState : IPluginStateStore
    {
        public ValueTask<bool?> GetEnabledAsync(string pluginId, CancellationToken cancellationToken = default) => ValueTask.FromResult<bool?>(false);
        public ValueTask SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class Contexts : IPluginContextFactory
    {
        public IPluginContext Create(PluginManifest manifest) => throw new NotSupportedException();
    }
    private sealed class FakeIgdb : IIgdbClient
    {
        public IReadOnlyList<IgdbGame> Games { get; set; } = [];
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => Task.FromResult(Games);
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(int externalGameSourceId, IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
