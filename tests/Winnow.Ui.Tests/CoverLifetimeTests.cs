using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Covers;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class CoverLifetimeTests
{
    private static readonly CoverKey Key = CoverKey.Steam("42");

    [AvaloniaFact]
    public async Task Direct_cache_callers_share_one_task_factory_under_contention()
    {
        await using var fixture = new Fixture();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await fixture.Cache.GetAsync(Key, 160, CoverLayers.Vivid);
        })).ToArray();
        start.SetResult();
        await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Source.Release.SetResult(Bytes());
        var results = await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(results[0]);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(1, fixture.Source.Calls);
        Assert.Equal(1, fixture.Cache.DecodedCount);
    }

    [AvaloniaFact]
    public async Task Concurrent_same_slot_requests_share_one_decode_and_leases_outlive_cache_shutdown()
    {
        await using var fixture = new Fixture();
        var leases = Enumerable.Range(0, 100).Select(_ => fixture.Pool.Acquire(Key, 160)).ToArray();
        var loads = leases.Select(lease => Task.Run(() => lease.GetAsync())).ToArray();
        await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, fixture.Source.Calls);
        Assert.Equal(1, fixture.Cache.PendingCount);
        fixture.Source.Release.SetResult(Bytes());
        var results = await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(results[0]);
        Assert.All(results, art => Assert.Same(results[0], art));
        Assert.Equal(1, fixture.Source.Calls);
        await fixture.Cache.DisposeAsync();
        Assert.Equal(0, fixture.Disposals);
        Assert.All(leases, lease => Assert.True(lease.TryGetArt(out _)));
        foreach (var lease in leases) lease.Dispose();
        Assert.Equal(1, fixture.Disposals);
        Assert.Equal(0, fixture.Pool.LiveSlots);
    }

    [AvaloniaFact]
    public async Task Admission_is_bounded_and_releasing_scroll_consumers_cancels_abandoned_work()
    {
        await using var fixture = new Fixture(maxPending: 4);
        var leases = Enumerable.Range(0, 80).Select(i => fixture.Pool.Acquire(CoverKey.Steam(i.ToString()), 160)).ToArray();
        var loads = leases.Select(lease => lease.GetAsync()).ToArray();
        await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(4, fixture.Cache.PendingCount);
        Assert.True(fixture.Source.Calls <= 2);
        Assert.All(loads.Skip(4), task => Assert.True(task.IsCompletedSuccessfully));
        foreach (var lease in leases) lease.Dispose();
        await Task.WhenAll(loads.Select(ObserveCancellation)).WaitAsync(TimeSpan.FromSeconds(3));
        await Until(() => fixture.Cache.PendingCount == 0);
        Assert.Equal(0, fixture.Cache.DecodedCount);
        Assert.Equal(0, fixture.Pool.LiveSlots);
        Assert.False(File.Exists(new CoverDiskCache(fixture.Options).NegativePath(Key)));
    }

    [AvaloniaFact]
    public async Task Releasing_one_lease_keeps_the_shared_fetch_until_the_final_lease_leaves()
    {
        await using var fixture = new Fixture();
        var first = fixture.Pool.Acquire(Key, 160);
        var second = fixture.Pool.Acquire(Key, 160);
        var pending = first.GetAsync();
        await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        first.Dispose();
        Assert.False(fixture.Source.Cancelled.Task.IsCompleted);
        second.Dispose();
        await fixture.Source.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await ObserveCancellation(pending);
        await Until(() => fixture.Cache.PendingCount == 0);
        Assert.Equal(0, fixture.Cache.DecodedCount);
    }

    [AvaloniaFact]
    public async Task Shutdown_during_bitmap_conversion_disposes_unpublished_pixels_before_returning()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var fixture = new Fixture(convert: source =>
        {
            var pixels = new Avalonia.Media.Imaging.WriteableBitmap(new(source.Width, source.Height), new(96, 96));
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Fixture conversion was not released.");
            return pixels;
        });
        fixture.Source.Release.SetResult(Bytes());
        var pending = fixture.Cache.GetAsync(Key, 160, CoverLayers.Vivid);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var shutdown = fixture.Cache.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            release.Set();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Null(await pending);
            Assert.Equal(1, fixture.Disposals);
            Assert.Equal(0, fixture.Cache.DecodedCount);
        }
        finally { release.Set(); }
    }

    [AvaloniaFact]
    public async Task Shutdown_drains_an_inflight_source_and_refuses_late_publication_and_new_admissions()
    {
        await using var fixture = new Fixture();
        fixture.Source.IgnoreCancellation = true;
        var pending = fixture.Cache.GetAsync(Key, 160, CoverLayers.Vivid);
        await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var shutdown = fixture.Cache.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        Assert.Null(await fixture.Cache.GetAsync(CoverKey.Steam("43"), 160, CoverLayers.Vivid));
        fixture.Source.Release.SetResult(Bytes());
        await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(await pending);
        Assert.Equal(0, fixture.Cache.PendingCount);
        Assert.Equal(0, fixture.Cache.DecodedCount);
        Assert.Equal(1, fixture.Source.Calls);
        Assert.False(File.Exists(new CoverDiskCache(fixture.Options).SourcePath(Key)));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Desktop_and_fullscreen_covers_recover_after_a_transient_failure(bool fullscreen)
    {
        await using var fixture = new Fixture();
        var tile = TileFixture.Tile(DateTime.UtcNow, coverKey: Key, covers: fixture.Pool);
        Control cover = fullscreen ? new FullscreenCover(tile) : new GameTileView { DataContext = tile };
        cover.Width = 160; cover.Height = 240;
        var window = new Window { Width = 400, Height = 400, Content = cover };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Source.Release.SetException(new HttpRequestException("Temporary fixture outage"));
            await Until(() => fixture.Cache.PendingCount == 0);
            window.Content = null;
            Assert.Equal(0, fixture.Pool.LiveSlots);
            fixture.Source.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Source.Release.SetResult(Bytes());
            window.Content = cover;
            await Until(() => cover.GetVisualDescendants().OfType<Image>().Any(image => image.Source is not null));
            Assert.True(fixture.Source.Calls >= 2);
        }
        finally { window.Close(); }
        Assert.Equal(0, fixture.Pool.LiveSlots);
    }

    [AvaloniaFact]
    public async Task Detail_cover_retries_the_same_display_width_after_null()
    {
        await using var fixture = new Fixture();
        CoverArt? shown = null;
        using var cover = new LeasedCover(fixture.Pool, Key, CoverLayers.Vivid, art => shown = art);
        cover.Request(160);
        await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Source.Release.SetException(new HttpRequestException("Temporary fixture outage"));
        await Until(() => fixture.Cache.PendingCount == 0 && fixture.Pool.LiveSlots == 0);
        Assert.Null(shown);
        fixture.Source.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Source.Release.SetResult(Bytes());
        cover.Request(160);
        await Until(() => shown is not null);
    }

    private static async Task ObserveCancellation(Task<CoverArt?> pending)
    { try { await pending; } catch (OperationCanceledException) { } }

    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "Artwork did not settle within three seconds.");
    }

    private static byte[] Bytes()
    {
        using var bitmap = new SKBitmap(160, 240);
        bitmap.Erase(SKColors.SlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public CoverCacheOptions Options { get; }
        public Source Source { get; } = new();
        public CoverCache Cache { get; }
        public CoverLeasePool Pool { get; }
        public int Disposals;
        public Fixture(int maxPending = 128, Func<SKBitmap, Avalonia.Media.Imaging.Bitmap>? convert = null)
        {
            Options = new() { CacheDirectory = Path.Combine(Path.GetTempPath(), "winnow-art-lifetime-" + Guid.NewGuid().ToString("N")),
                MaxPendingLoads = maxPending, MaxConcurrentDecodes = 2 };
            var pipeline = new CoverPipeline([Source], new CoverDiskCache(Options), Options);
            void Post(Action action) { Interlocked.Increment(ref Disposals); action(); }
            Cache = convert is null ? new(pipeline, Options, post: Post) : new(pipeline, Options, null, Post, convert);
            Pool = new(Cache);
        }
        public async ValueTask DisposeAsync()
        {
            Source.Release.TrySetResult(null);
            await Cache.DisposeAsync();
            var root = Path.GetFullPath(Options.CacheDirectory);
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture directory escaped the temporary directory.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Source : ICoverSource
    {
        public string Name => "fixture";
        public int Calls;
        public bool IgnoreCancellation;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<byte[]?> Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanHandle(CoverKey key) => true;
        public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            try { return await Release.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : ct); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
    }
}
