using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Winnow.Covers;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class CoverConversionBoundaryTests
{
    [AvaloniaFact]
    public async Task A_failed_floor_conversion_releases_the_already_converted_vivid_layer()
    {
        TrackingBitmap? vivid = null;
        var calls = 0;
        await using var fixture = new Fixture(_ => ++calls == 1
            ? vivid = new TrackingBitmap(Bytes())
            : throw new InvalidOperationException("Injected floor conversion failure."));
        Assert.Null(await fixture.Cache.GetAsync(CoverKey.Steam("42"), 160, CoverLayers.VividAndFloor));
        Assert.NotNull(vivid);
        Assert.Equal(1, vivid.Disposals);
        Assert.Equal(0, fixture.Cache.DecodedCount);
        Assert.Equal(0, fixture.Cache.PendingCount);
    }

    [AvaloniaFact]
    public async Task The_decode_limit_includes_transient_bitmap_conversion()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        await using var fixture = new Fixture(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstEntered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("First conversion was not released.");
            }
            else secondEntered.TrySetResult();
            return new TrackingBitmap(Bytes());
        });
        var first = fixture.Cache.GetAsync(CoverKey.Steam("42"), 160, CoverLayers.Vivid);
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var second = fixture.Cache.GetAsync(CoverKey.Steam("43"), 160, CoverLayers.Vivid);
            await Assert.ThrowsAsync<TimeoutException>(() => secondEntered.Task.WaitAsync(TimeSpan.FromMilliseconds(300)));
            Assert.Equal(2, fixture.Cache.PendingCount);
            release.Set();
            Assert.All(await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3)), Assert.NotNull);
            Assert.True(secondEntered.Task.IsCompletedSuccessfully);
        }
        finally { release.Set(); }
    }

    private static byte[] Bytes()
    {
        using var bitmap = new SKBitmap(160, 240);
        bitmap.Erase(SKColors.SlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class TrackingBitmap : Bitmap
    {
        public int Disposals;
        public TrackingBitmap(byte[] bytes) : base(new MemoryStream(bytes)) { }
        public override void Dispose() { Interlocked.Increment(ref Disposals); base.Dispose(); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "winnow-cover-conversion-" + Guid.NewGuid().ToString("N"));
        public CoverCache Cache { get; }
        public Fixture(Func<SKBitmap, Bitmap> convert)
        {
            var options = new CoverCacheOptions { CacheDirectory = _root, MaxConcurrentDecodes = 1 };
            Cache = new CoverCache(new CoverPipeline([new Source()], new CoverDiskCache(options), options),
                options, null, action => action(), convert);
        }
        public async ValueTask DisposeAsync()
        {
            await Cache.DisposeAsync();
            var resolved = Path.GetFullPath(_root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture directory escaped the temporary directory.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class Source : ICoverSource
    {
        public string Name => "conversion-fixture";
        public bool CanHandle(CoverKey key) => true;
        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(Bytes());
    }
}
