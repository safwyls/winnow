using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
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
                Assert.InRange(image.Opacity, 0, .99);
                Assert.Same(first, backdrop.GetVisualDescendants().OfType<Image>().First().Source);
                await Task.Delay(230);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(1, image.Opacity);
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
