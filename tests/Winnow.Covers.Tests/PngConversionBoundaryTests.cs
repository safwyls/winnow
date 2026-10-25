using SkiaSharp;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class PngConversionBoundaryTests
{
    [Fact]
    public async Task One_decode_permit_keeps_source_43_disk_hit_independent_of_source_42_network()
    {
        using var fixture = new Fixture();
        fixture.Source.Hold42 = true;
        fixture.Disk.WriteSource(CoverKey.Steam("43"), Source.Bytes());
        var slow = fixture.Pipeline.GetPngAsync(CoverKey.Steam("42"), 160, fixture.Permit);
        try
        {
            await fixture.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var cached = await fixture.Pipeline.GetPngAsync(CoverKey.Steam("43"), 160, fixture.Permit)
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull(cached);
            Assert.False(slow.IsCompleted);
            Assert.Equal(1, fixture.Source.Requests);
            using var decoded = SKBitmap.Decode(cached);
            Assert.Equal((160, 240), (decoded.Width, decoded.Height));
        }
        finally { fixture.Source.Release.TrySetResult(); }
        Assert.NotNull(await slow.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task One_decode_permit_includes_actual_PNG_encoding_and_disposes_both_transient_bitmaps()
    {
        using var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bitmaps = new List<SKBitmap>();
        byte[] Encode(SKBitmap bitmap)
        {
            bitmaps.Add(bitmap);
            if (bitmaps.Count == 1)
            {
                firstEntered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Release the first encoder.");
            }
            else secondEntered.TrySetResult();
            return CoverPipeline.EncodePng(bitmap);
        }
        var first = Task.Run(() => fixture.Pipeline.GetPngAsync(CoverKey.Steam("42"), 160, fixture.Permit, Encode));
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var second = Task.Run(() => fixture.Pipeline.GetPngAsync(CoverKey.Steam("43"), 160, fixture.Permit, Encode));
            await Assert.ThrowsAsync<TimeoutException>(() => secondEntered.Task.WaitAsync(TimeSpan.FromMilliseconds(300)));
            Assert.Equal(0, fixture.Permit.CurrentCount);
            Assert.False(second.IsCompleted);
            release.Set();
            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.All(results, result =>
            {
                Assert.NotNull(result);
                using var image = SKBitmap.Decode(result);
                Assert.Equal(SKColors.SlateBlue, image.GetPixel(0, 0));
            });
            Assert.Equal(2, bitmaps.Count);
            Assert.All(bitmaps, bitmap => Assert.Equal(IntPtr.Zero, bitmap.Handle));
            Assert.Equal(1, fixture.Permit.CurrentCount);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task Encoder_failure_releases_its_transient_bitmap_and_permit_before_retry()
    {
        using var fixture = new Fixture();
        SKBitmap? owned = null;
        var failed = await fixture.Pipeline.GetPngAsync(CoverKey.Steam("42"), 160, fixture.Permit,
            bitmap => { owned = bitmap; throw new InvalidOperationException("Injected encoder failure."); });
        Assert.Null(failed);
        Assert.NotNull(owned);
        Assert.Equal(IntPtr.Zero, owned.Handle);
        Assert.Equal(1, fixture.Permit.CurrentCount);
        var retry = await fixture.Pipeline.GetPngAsync(CoverKey.Steam("42"), 160, fixture.Permit);
        Assert.NotNull(retry);
        using var result = SKBitmap.Decode(retry);
        Assert.Equal(SKColors.SlateBlue, result.GetPixel(0, 0));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-cover-png-" + Guid.NewGuid().ToString("N"));
        public Source Source { get; } = new();
        public CoverDiskCache Disk { get; }
        public CoverPipeline Pipeline { get; }
        public SemaphoreSlim Permit { get; } = new(1);
        public Fixture()
        {
            var options = new CoverCacheOptions { CacheDirectory = _directory, MaxConcurrentDecodes = 1 };
            Disk = new(options);
            Pipeline = new([Source], Disk, options);
        }
        public void Dispose()
        {
            Source.Release.TrySetResult();
            Pipeline.Dispose(); Permit.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
    private sealed class Source : ICoverSource
    {
        public string Name => "source-PNG-conversion";
        public bool Hold42 { get; set; }
        public int Requests;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanHandle(CoverKey key) => true;
        public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Requests);
            if (Hold42 && key.Id == "42") { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return Bytes();
        }
        public static byte[] Bytes()
        {
            using var bitmap = new SKBitmap(160, 240);
            bitmap.Erase(SKColors.SlateBlue);
            return CoverPipeline.EncodePng(bitmap);
        }
    }
}
