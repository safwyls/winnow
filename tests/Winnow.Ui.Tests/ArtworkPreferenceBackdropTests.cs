using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ArtworkPreferenceBackdropTests
{
    [AvaloniaFact]
    public async Task Fullscreen_unowned_group_root_background_precedes_owned_child_heroes()
    {
        using var database = new TempDatabase();
        var works = new WorkRepository(database.Factory);
        var root = await works.InsertAsync(new Work { Name = "Group root", BackgroundUrl = UserArtRef.Format("rootsaved") });
        var child = await works.InsertAsync(new Work { Name = "Owned Steam copy", BackgroundUrl = UserArtRef.Format("childsaved") });
        var release = await new ReleaseRepository(database.Factory).InsertAsync(new Release { WorkId = child, Name = "Owned Steam copy" });
        var ownership = await new OwnershipRepository(database.Factory).InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        const string url = "https://cdn2.steamgriddb.com/hero/61ba87bf4177f576150389d84d14bb01.png";
        var images = new WorkImageRepository(database.Factory);
        await images.UpsertAsync(new WorkImages
        {
            WorkId = child, Source = ImageSources.SteamGridDb, Kind = ImageKinds.Artwork,
            ImageIds = "50584", ObservedAt = DateTime.UtcNow,
            Images = [new() { ImageId = "50584", Width = 1920, Height = 620, Url = url }],
        });
        var tile = TileFixture.Tile(DateTime.UtcNow,
            [TileEntry.For(ownership, release, child, "steam", 0, null, steamAppId: "42")], root, LibraryBuckets.NeverPlayed);
        Assert.NotEqual(tile.Game.ResolvedWorkId, tile.Primary.WorkId);
        var leases = new Leases();
        using var pixels = new RenderTargetBitmap(new PixelSize(32, 10));
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkRepository>(works).AddSingleton<IWorkImageRepository>(images).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell, services) { ReducedMotion = true };
        var backdrop = new FullscreenBackdrop(context, tile);
        var window = new Window { Width = 1920, Height = 1080, Content = backdrop };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await Flush();
            Assert.Equal(CoverKey.User("rootsaved"), leases.Last.Key);
            leases.Last.Complete(null);
            await Flush();
            Assert.Equal(CoverKey.SteamHero("42"), leases.Last.Key);
            leases.Last.Complete(null);
            await Flush();
            Assert.Equal(SteamGridDbHeroUrl.Key(url), leases.Last.Key);
            leases.Last.Complete(pixels);
            await Flush();
            Assert.Same(pixels, backdrop.GetVisualDescendants().OfType<Image>().Last().Source);
            Assert.DoesNotContain(leases.All, lease => lease.Key == CoverKey.User("childsaved"));
            window.Content = null;
            Assert.All(leases.All, lease => Assert.True(lease.Disposed));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_live_order_retains_pixels_until_replacement_and_unsubscribes_on_dispose()
    {
        var preferences = new ArtworkPreferences(new Store());
        var leases = new Leases();
        using var oldArt = new RenderTargetBitmap(new PixelSize(32, 10));
        using var nextArt = new RenderTargetBitmap(new PixelSize(32, 18));
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow, steamAppId: "42"),
            "All", [], DateTime.UtcNow, covers: leases, images: Rows, artworkPreferences: preferences);
        details.RequestBackdrop(1920, 1080);
        Assert.Equal(CoverKey.SteamHero("42"), leases.Last.Key);
        var previous = leases.Last;
        previous.Complete(oldArt);
        await Flush();
        await preferences.SaveAsync([ArtworkPreferences.Igdb, ArtworkPreferences.Steam, ArtworkPreferences.SteamGridDb]);
        await Flush();
        Assert.Equal(CoverKey.IgdbBackdrop("art"), leases.Last.Key);
        Assert.Same(oldArt, details.Backdrop);
        Assert.False(previous.Disposed);
        leases.Last.Complete(nextArt);
        await Flush();
        Assert.Same(nextArt, details.Backdrop);
        Assert.True(previous.Disposed);
        details.Dispose();
        var requests = leases.All.Count;
        await preferences.SaveAsync([ArtworkPreferences.Steam, ArtworkPreferences.Igdb, ArtworkPreferences.SteamGridDb]);
        await Flush();
        Assert.Equal(requests, leases.All.Count);
        Assert.All(leases.All, lease => Assert.True(lease.Disposed));
    }

    [AvaloniaFact]
    public async Task Fullscreen_live_order_retains_pixels_and_detach_releases_subscriptions()
    {
        var preferences = new ArtworkPreferences(new Store());
        var leases = new Leases();
        using var oldArt = new RenderTargetBitmap(new PixelSize(32, 10));
        using var nextArt = new RenderTargetBitmap(new PixelSize(32, 18));
        using var services = new ServiceCollection().AddSingleton(preferences).AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkImageRepository>(new Images()).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell, services) { ReducedMotion = true };
        var backdrop = new FullscreenBackdrop(context, TileFixture.Tile(DateTime.UtcNow, steamAppId: "42"));
        var window = new Window { Width = 1920, Height = 1080, Content = backdrop };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var image = backdrop.GetVisualDescendants().OfType<Image>().Last();
            var previous = leases.Last;
            Assert.Equal(CoverKey.SteamHero("42"), previous.Key);
            previous.Complete(oldArt);
            await Flush();
            await preferences.SaveAsync([ArtworkPreferences.Igdb, ArtworkPreferences.SteamGridDb, ArtworkPreferences.Steam]);
            await Flush();
            Assert.Equal(CoverKey.IgdbBackdrop("art"), leases.Last.Key);
            Assert.Same(oldArt, image.Source);
            Assert.False(previous.Disposed);
            leases.Last.Complete(nextArt);
            await Flush();
            Assert.Same(nextArt, image.Source);
            Assert.True(previous.Disposed);
            window.Content = null;
            var requests = leases.All.Count;
            await preferences.SaveAsync([ArtworkPreferences.Steam, ArtworkPreferences.Igdb, ArtworkPreferences.SteamGridDb]);
            await Flush();
            Assert.Equal(requests, leases.All.Count);
            Assert.All(leases.All, lease => Assert.True(lease.Disposed));
        }
        finally { window.Close(); }
    }

    private static IReadOnlyList<WorkImages> Rows => [new()
    {
        WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Artwork, ImageIds = "art",
        Images = [new() { ImageId = "art", Width = 3840, Height = 2160 }], ObservedAt = DateTime.UtcNow,
    }];
    private static async Task Flush() { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
    private sealed class Images : IWorkImageRepository
    {
        public Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default) => Task.FromResult(Rows);
        public Task UpsertAsync(WorkImages images, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Store : ISettingsStore
    {
        private string? _value;
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_value);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) { _value = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Leases : ICoverLeases
    {
        public List<Lease> All { get; } = [];
        public Lease Last => All[^1];
        public ICoverLease Acquire(CoverKey key, double width, CoverLayers layers = CoverLayers.VividAndFloor)
        {
            var lease = new Lease(key, CoverImaging.SnapWidth(width), layers);
            All.Add(lease); return lease;
        }
        public sealed class Lease(CoverKey key, int width, CoverLayers layers) : ICoverLease
        {
            private readonly TaskCompletionSource<CoverArt?> _result = new();
            public CoverKey Key => key;
            public int Width => width;
            public CoverLayers Layers => layers;
            public bool Disposed { get; private set; }
            public bool TryGetArt(out CoverArt art) { art = null!; return false; }
            public Task<CoverArt?> GetAsync(CancellationToken ct = default) => _result.Task;
            public void Complete(Bitmap? bitmap) => _result.SetResult(bitmap is null ? null : new CoverArt(bitmap, bitmap));
            public void Dispose() => Disposed = true;
        }
    }
}
