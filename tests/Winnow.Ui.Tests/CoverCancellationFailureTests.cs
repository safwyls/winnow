using Avalonia.Headless.XUnit;
using SkiaSharp;
using Winnow.Covers;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class CoverCancellationFailureTests
{
    [AvaloniaFact]
    public async Task A_throwing_source_callback_does_not_replace_the_last_waiters_cancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "winnow-cover-cancel-" + Guid.NewGuid().ToString("N"));
        var options = new CoverCacheOptions { CacheDirectory = root };
        var source = new ThrowingCancellationSource();
        var pipeline = new CoverPipeline([source], new CoverDiskCache(options), options);
        var cache = new CoverCache(pipeline, options, post: action => action());
        try
        {
            using var cancellation = new CancellationTokenSource();
            var pending = cache.GetAsync(CoverKey.Steam("43"), 160, CoverLayers.Vivid, cancellation.Token);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await cache.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, cache.PendingCount);
        }
        finally
        {
            await cache.DisposeAsync();
            DeleteFixtureDirectory(root);
        }
    }

    [AvaloniaFact]
    public async Task A_throwing_source_cancellation_callback_cannot_skip_shutdown_cleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "winnow-cover-cancel-" + Guid.NewGuid().ToString("N"));
        var options = new CoverCacheOptions { CacheDirectory = root };
        var source = new ThrowingCancellationSource();
        var pipeline = new CoverPipeline([source], new CoverDiskCache(options), options);
        var disposals = 0;
        var cache = new CoverCache(pipeline, options, post: action => { disposals++; action(); });
        try
        {
            Assert.NotNull(await cache.GetAsync(CoverKey.Steam("42"), 160, CoverLayers.Vivid));
            var pending = cache.GetAsync(CoverKey.Steam("43"), 160, CoverLayers.Vivid);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await cache.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Null(await pending);
            Assert.Equal(0, cache.PendingCount);
            Assert.Equal(0, cache.DecodedCount);
            Assert.Equal(1, disposals);
            await cache.DisposeAsync();
        }
        finally
        {
            await cache.DisposeAsync();
            DeleteFixtureDirectory(root);
        }
    }

    private static void DeleteFixtureDirectory(string root)
    {
        var resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture directory escaped the temporary directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    private sealed class ThrowingCancellationSource : ICoverSource
    {
        public string Name => "cancellation-fixture";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanHandle(CoverKey key) => true;
        public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        {
            if (key == CoverKey.Steam("42"))
            {
                using var bitmap = new SKBitmap(160, 240);
                bitmap.Erase(SKColors.SlateBlue);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                return encoded.ToArray();
            }

            using var registration = ct.Register(() => throw new InvalidOperationException("Injected source cancellation failure."));
            Entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        }
    }
}
