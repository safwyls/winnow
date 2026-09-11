using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Avalonia.Media;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Queries;
using Winnow.Core.Domain;
using Avalonia.VisualTree;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenBackdropTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selection_keeps_ready_landscape_and_ignores_superseded_completion(bool reducedMotion)
    {
        using var first = new RenderTargetBitmap(new PixelSize(16, 9));
        using var next = new RenderTargetBitmap(new PixelSize(16, 9));
        var leases = new DelayedLeases();
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkImageRepository>(new Images()).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell, services) { ReducedMotion = reducedMotion };
        var backdrop = new FullscreenBackdrop(context, TileFixture.Tile(DateTime.UtcNow));
        var window = new Window { Width = 1920, Height = 1080, Content = backdrop };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var image = backdrop.GetVisualDescendants().OfType<Image>().Last();
            var fallback = Assert.Single(backdrop.Children.OfType<ContentControl>());
            Assert.Null(fallback.Content);
            leases.Last.Complete(new CoverArt(first, first));
            await Flush();
            Assert.Same(first, image.Source);
            var held = leases.Last;
            backdrop.Select(TileFixture.Tile(DateTime.UtcNow, workId: 2));
            var obsolete = leases.Last;
            Assert.Same(first, image.Source);
            Assert.False(held.Disposed);
            Assert.Null(fallback.Content);
            backdrop.Select(TileFixture.Tile(DateTime.UtcNow, workId: 3));
            var selected = leases.Last;
            Assert.True(obsolete.Disposed);
            obsolete.Complete(new CoverArt(next, next));
            await Flush();
            Assert.Same(first, image.Source);
            selected.Complete(new CoverArt(next, next));
            await Flush();
            Assert.Same(next, image.Source);
            Assert.Equal(reducedMotion, held.Disposed);
            if (!reducedMotion)
            {
                Assert.InRange(Assert.IsType<Panel>(image.Parent!.Parent).Opacity, 0, .99);
                Assert.Same(first, backdrop.GetVisualDescendants().OfType<Image>().First().Source);
                await Task.Delay(230);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(1, Assert.IsType<Panel>(image.Parent!.Parent).Opacity);
            Assert.True(held.Disposed);
            Assert.Same(selected, leases.Last);
            var finalTile = TileFixture.Tile(DateTime.UtcNow, workId: 4);
            backdrop.Select(finalTile);
            var finalLease = leases.Last;
            backdrop.Select(finalTile);
            Assert.Same(finalLease, leases.Last);
            finalLease.Complete(new CoverArt(first, first));
            await Flush();
            Assert.Equal(reducedMotion, selected.Disposed);
            // Close while both transition layers still own pixels.
            window.Content = null;
            Assert.Null(image.Source);
            Assert.All(leases.All, lease => Assert.True(lease.Disposed));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Failed_landscape_shows_cover_only_after_resolution_and_detach_cancels_pending_art()
    {
        using var pixels = new RenderTargetBitmap(new PixelSize(16, 9));
        var leases = new DelayedLeases();
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkImageRepository>(new Images()).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell, services) { ReducedMotion = true };
        var backdrop = new FullscreenBackdrop(context, TileFixture.Tile(DateTime.UtcNow));
        var window = new Window { Width = 1920, Height = 1080, Content = backdrop };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var fallback = Assert.Single(backdrop.Children.OfType<ContentControl>());
            Assert.Null(fallback.Content);
            leases.Last.Complete(null);
            await Flush();
            Assert.IsType<FullscreenCover>(fallback.Content);
            backdrop.Select(TileFixture.Tile(DateTime.UtcNow, workId: 2));
            var pending = leases.Last;
            window.Content = null;
            Assert.True(pending.Disposed);
            pending.Complete(new CoverArt(pixels, pixels));
            await Flush();
            Assert.Null(backdrop.GetVisualDescendants().OfType<Image>().Last().Source);
            Assert.Null(fallback.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_backdrop_tries_ranked_art_then_screenshot_and_keeps_portrait_separate()
    {
        using var pixels = new RenderTargetBitmap(new PixelSize(16, 9));
        var leases = new DelayedLeases();
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow, coverKey: CoverKey.Igdb("portrait")), "All", [], DateTime.UtcNow,
            covers: leases, images: RankedImages(), backgroundUrl: UserArtRef.Format("override"));
        details.RequestCover(200);
        var portrait = leases.Last;
        details.RequestBackdrop(1920, 1080);
        Assert.Equal(CoverKey.User("override"), leases.Last.Key);
        leases.Last.Complete(null);
        await Flush();
        Assert.Equal(CoverKey.IgdbBackdrop("art"), leases.Last.Key);
        Assert.Equal(1920, leases.Last.Width);
        leases.Last.Complete(null);
        await Flush();
        Assert.Equal(CoverKey.IgdbBackdrop("shot"), leases.Last.Key);
        leases.Last.Complete(new CoverArt(pixels, pixels));
        await Flush();
        Assert.Same(pixels, details.Backdrop);
        Assert.Null(details.Cover);
        details.RequestBackdrop(3840, 2160);
        var upgrade = leases.Last;
        details.Dispose();
        Assert.True(upgrade.Disposed);
        Assert.True(portrait.Disposed);
        upgrade.Complete(new CoverArt(pixels, pixels));
        await Flush();
        Assert.Null(details.Backdrop);
        Assert.All(leases.All, lease => Assert.True(lease.Disposed));
    }

    [AvaloniaFact]
    public async Task Fullscreen_tries_artwork_then_screenshot_before_cover()
    {
        var leases = new DelayedLeases();
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkImageRepository>(new RankedRepository()).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell, services);
        var backdrop = new FullscreenBackdrop(context, TileFixture.Tile(DateTime.UtcNow));
        var window = new Window { Width = 1920, Height = 1080, Content = backdrop };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(CoverKey.IgdbBackdrop("art"), leases.Last.Key);
            leases.Last.Complete(null);
            await Flush();
            Assert.Equal(CoverKey.IgdbBackdrop("shot"), leases.Last.Key);
            Assert.Null(Assert.Single(backdrop.Children.OfType<ContentControl>()).Content);
            leases.Last.Complete(null);
            await Flush();
            Assert.IsType<FullscreenCover>(Assert.Single(backdrop.Children.OfType<ContentControl>()).Content);
        }
        finally { window.Close(); }
        Assert.All(leases.All, lease => Assert.True(lease.Disposed));
    }

    [AvaloniaTheory]
    [InlineData(6000, 1000, 3840)]
    [InlineData(1920, 1080, 1920)]
    public async Task Both_backdrops_size_each_candidate_for_its_source_proportions(int width, int height, int expectedBucket)
    {
        var rows = RankedImages().ToArray();
        rows[0] = rows[0] with { Images = [new() { ImageId = "art", Width = width, Height = height }] };
        var leases = new DelayedLeases();
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "All", [], DateTime.UtcNow,
            covers: leases, images: rows);
        details.RequestBackdrop(1920, 1080);
        Assert.Equal(CoverKey.IgdbBackdrop("art"), leases.Last.Key);
        Assert.Equal(expectedBucket, leases.Last.Width);
        leases.Last.Complete(null);
        await Flush();
        Assert.Equal(CoverKey.IgdbBackdrop("shot"), leases.Last.Key);
        Assert.Equal(1920, leases.Last.Width);
        details.Dispose();

        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkImageRepository>(new RankedRepository(rows)).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell, services);
        var window = new Window { Width = 1920, Height = 1080,
            Content = new FullscreenBackdrop(context, TileFixture.Tile(DateTime.UtcNow)) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(CoverKey.IgdbBackdrop("art"), leases.Last.Key);
            Assert.Equal(expectedBucket, leases.Last.Width);
            leases.Last.Complete(null);
            await Flush();
            Assert.Equal(CoverKey.IgdbBackdrop("shot"), leases.Last.Key);
            Assert.Equal(1920, leases.Last.Width);
        }
        finally { window.Close(); }
        Assert.All(leases.All, lease => Assert.True(lease.Disposed));
    }

    [AvaloniaTheory]
    [InlineData(false, 1920)]
    [InlineData(true, 1920)]
    [InlineData(false, 2520)]
    [InlineData(true, 2520)]
    public async Task Steam_hero_adapts_crop_and_crossfades_independent_geometry(bool cinematic, int width)
    {
        using var hero = new RenderTargetBitmap(new PixelSize(384, 124));
        using var landscape = new RenderTargetBitmap(new PixelSize(160, 90));
        var leases = new DelayedLeases();
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkImageRepository>(new Images()).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell, services);
        var backdrop = new FullscreenBackdrop(context, TileFixture.Tile(DateTime.UtcNow, steamAppId: "42"), cinematic);
        var window = new Window { Width = width, Height = 1080, Content = backdrop };
        var fitted = width >= 2520;
        var heroSize = new Size(width, fitted ? Math.Round(width * 124d / 384) : 1080);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(CoverKey.SteamHero("42"), leases.Last.Key);
            Assert.Equal(fitted ? 2560 : 3840, leases.Last.Width);
            leases.Last.Complete(new CoverArt(hero, hero));
            await Flush();
            var images = backdrop.GetVisualDescendants().OfType<Image>().ToArray();
            var outgoing = images[0];
            var current = images[1];
            var art = Assert.IsType<Panel>(current.Parent);
            var surface = Assert.IsType<Panel>(art.Parent);
            Assert.Equal(heroSize, art.Bounds.Size);
            Assert.Equal(Stretch.UniformToFill, current.Stretch);
            Assert.Equal(0, art.Bounds.Top);
            Assert.Equal(new Size(width, 1080), surface.Bounds.Size);
            Assert.IsType<SolidColorBrush>(surface.Background);
            var gradient = Assert.IsType<LinearGradientBrush>(Assert.Single(art.Children.OfType<Border>()).Background);
            Assert.Equal(fitted ? 1 : cinematic ? .55 : .85,
                gradient.GradientStops.First(stop => stop.Color.A == 255).Offset);
            Assert.Equal(255, gradient.GradientStops[^1].Color.A);

            backdrop.Select(TileFixture.Tile(DateTime.UtcNow, workId: 2));
            leases.Last.Complete(new CoverArt(landscape, landscape));
            await Flush();
            Assert.Equal(new Size(width, 1080), art.Bounds.Size);
            Assert.Equal(heroSize, Assert.IsType<Panel>(outgoing.Parent).Bounds.Size);
            Assert.Same(hero, outgoing.Source);
            Assert.InRange(surface.Opacity, 0, .99);
            await Task.Delay(230); Dispatcher.UIThread.RunJobs();

            backdrop.Select(TileFixture.Tile(DateTime.UtcNow, workId: 3, steamAppId: "43"));
            leases.Last.Complete(new CoverArt(hero, hero));
            await Flush();
            Assert.Equal(heroSize, art.Bounds.Size);
            Assert.Equal(new Size(width, 1080), Assert.IsType<Panel>(outgoing.Parent).Bounds.Size);
            Assert.IsType<SolidColorBrush>(surface.Background);
            Assert.InRange(surface.Opacity, 0, .99);

            window.Width = 3840;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1080, art.Bounds.Height);
            Assert.InRange(art.Bounds.Width, 3344, 3345);
            Assert.InRange(art.Bounds.Left, 247, 248);
            Assert.Equal(0, art.Bounds.Top);
            Assert.Equal(3840, leases.Last.Width);
            gradient = Assert.IsType<LinearGradientBrush>(Assert.Single(art.Children.OfType<Border>()).Background);
            Assert.Equal(.85, gradient.GradientStops[^2].Offset);
            Assert.Equal(0, gradient.GradientStops[^2].Color.A);
            window.Width = 1920;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Size(1920, 1080), art.Bounds.Size);
            Assert.Equal(new Size(1920, 1080), Assert.IsType<Panel>(outgoing.Parent).Bounds.Size);
            gradient = Assert.IsType<LinearGradientBrush>(Assert.Single(art.Children.OfType<Border>()).Background);
            Assert.Equal(cinematic ? .55 : .85, gradient.GradientStops.First(stop => stop.Color.A == 255).Offset);
            window.Content = null;
            Assert.All(leases.All, lease => Assert.True(lease.Disposed));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_hero_tries_IGDB_then_standard_hero_then_cover()
    {
        var leases = new DelayedLeases();
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow,
            steamAppId: "42", coverKey: CoverKey.Steam("42")), "All", [], DateTime.UtcNow,
            covers: leases, images: RankedImages());
        details.RequestBackdrop(1920, 1080);
        var expected = new[] { CoverKey.SteamHero("42"), CoverKey.IgdbBackdrop("art"),
            CoverKey.IgdbBackdrop("shot"), CoverKey.SteamHeroStandard("42"), CoverKey.Steam("42") };
        foreach (var key in expected)
        {
            Assert.Equal(key, leases.Last.Key);
            if (BackdropSelection.IsSteamHero(key)) Assert.Equal(3840, leases.Last.Width);
            leases.Last.Complete(null);
            await Flush();
        }
        Assert.Null(details.Backdrop);
        Assert.All(leases.All, lease => Assert.True(lease.Disposed));
    }

    private static IReadOnlyList<WorkImages> RankedImages() =>
    [
        new() { WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Artwork,
            ImageIds = "art", Images = [new() { ImageId = "art", Width = 1920, Height = 1080 }], ObservedAt = DateTime.UtcNow },
        new() { WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Screenshot,
            ImageIds = "shot", Images = [new() { ImageId = "shot", Width = 3840, Height = 2160 }], ObservedAt = DateTime.UtcNow }
    ];

    private sealed class RankedRepository(IReadOnlyList<WorkImages>? rows = null) : IWorkImageRepository
    {
        public Task UpsertAsync(WorkImages images, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default) => Task.FromResult(rows ?? RankedImages());
    }
    private static async Task Flush()
    {
        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Images : IWorkImageRepository
    {
        public Task UpsertAsync(WorkImages images, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkImages>>([new() { WorkId = workId, Source = ImageSources.Igdb,
                Kind = ImageKinds.Screenshot, ImageIds = $"landscape{workId}", ObservedAt = DateTime.UtcNow }]);
    }

    private sealed class DelayedLeases : ICoverLeases
    {
        public List<Lease> All { get; } = [];
        public Lease Last => All[^1];
        public ICoverLease Acquire(CoverKey key, double displayWidthPixels, CoverLayers layers = CoverLayers.VividAndFloor)
        {
            var lease = new Lease(key, CoverImaging.SnapWidth(displayWidthPixels), layers);
            All.Add(lease); return lease;
        }
        public sealed class Lease(CoverKey key, int width, CoverLayers layers) : ICoverLease
        {
            private readonly TaskCompletionSource<CoverArt?> _completion = new();
            public CoverKey Key => key;
            public int Width => width;
            public CoverLayers Layers => layers;
            public bool Disposed { get; private set; }
            public bool TryGetArt(out CoverArt art) { art = null!; return false; }
            public Task<CoverArt?> GetAsync(CancellationToken ct = default) => _completion.Task;
            public void Complete(CoverArt? art) => _completion.SetResult(art);
            public void Dispose() => Disposed = true;
        }
    }
}
