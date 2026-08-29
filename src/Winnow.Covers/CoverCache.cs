using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Winnow.Covers;

/// <summary>The two layers of the dormancy cross-fade: vivid art and desaturated floor variant.</summary>
public sealed record CoverArt(Bitmap Vivid, Bitmap Floor);

/// <summary>Cover art for the grid. Never blocks a user-facing path (§5.1).</summary>
public interface ICoverCache
{
    /// <summary>Synchronous lookup for a cover already decoded and in memory.</summary>
    bool TryGet(CoverKey key, double displayWidthPixels, out CoverArt art);

    /// <summary>The cover, fetching and decoding off-thread if needed. Null means no art available.</summary>
    Task<CoverArt?> GetAsync(CoverKey key, double displayWidthPixels, CancellationToken ct = default);
}

/// <summary>
/// Bounded in-memory LRU over <see cref="CoverPipeline"/>. Capped by total pixel
/// bytes (not count) via <see cref="DecodedLru{TKey,TValue}"/>.
/// </summary>
public sealed class CoverCache : ICoverCache, IDisposable
{
    private readonly CoverPipeline _pipeline;
    private readonly ILogger<CoverCache> _log;

    private readonly DecodedLru<Slot, CoverArt> _memory;

    private readonly ConcurrentDictionary<Slot, Task<CoverArt?>> _inFlight = new();

    public CoverCache(CoverPipeline pipeline, CoverCacheOptions options, ILogger<CoverCache>? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pipeline = pipeline;
        _log = log ?? NullLogger<CoverCache>.Instance;
        _memory = new DecodedLru<Slot, CoverArt>(options.MaxDecodedBytes);
    }

    /// <summary>Decoded pixel bytes currently held. Diagnostics only.</summary>
    public long DecodedBytes => _memory.Bytes;

    public bool TryGet(CoverKey key, double displayWidthPixels, out CoverArt art)
        => _memory.TryGet(new Slot(key, CoverImaging.SnapWidth(displayWidthPixels)), out art);

    public Task<CoverArt?> GetAsync(CoverKey key, double displayWidthPixels, CancellationToken ct = default)
    {
        if (TryGet(key, displayWidthPixels, out var hit))
        {
            return Task.FromResult<CoverArt?>(hit);
        }

        if (_pipeline.IsKnownMissing(key))
        {
            return Task.FromResult<CoverArt?>(null);
        }

        var slot = new Slot(key, CoverImaging.SnapWidth(displayWidthPixels));

        // Task.Run, not a bare async call: this is invoked from the UI thread as
        // a tile realizes, and everything downstream (file IO, JPEG decode, the
        // colour-matrix pass) must stay off it (§5.1, §5.4).
        return _inFlight.GetOrAdd(slot, s => Task.Run(() => LoadAsync(s, ct), CancellationToken.None));
    }

    private async Task<CoverArt?> LoadAsync(Slot slot, CancellationToken ct)
    {
        try
        {
            using var bitmaps = await _pipeline.GetAsync(slot.Key, slot.Width, ct).ConfigureAwait(false);
            if (bitmaps is null)
            {
                return null;
            }

            var art = new CoverArt(ToAvalonia(bitmaps.Vivid), ToAvalonia(bitmaps.Floor));

            // Both layers, four bytes a pixel. The decoded height is read off
            // the bitmap rather than derived from the aspect ratio, because a
            // capsule that is not exactly 2:3 would otherwise be under-declared.
            _memory.Admit(slot, art, 2L * slot.Width * art.Vivid.PixelSize.Height * 4L);
            return art;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cover decode failed for {Key}", slot.Key);
            return null;
        }
        finally
        {
            _inFlight.TryRemove(slot, out _);
        }
    }

    private static unsafe Bitmap ToAvalonia(SKBitmap source)
    {
        var target = new WriteableBitmap(
            new PixelSize(source.Width, source.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var frame = target.Lock();
        var sourceStride = source.RowBytes;
        var targetStride = frame.RowBytes;
        var run = (uint)Math.Min(sourceStride, targetStride);
        var sourcePixels = (byte*)source.GetPixels();
        var targetPixels = (byte*)frame.Address;

        for (var y = 0; y < source.Height; y++)
        {
            Buffer.MemoryCopy(
                sourcePixels + ((long)y * sourceStride),
                targetPixels + ((long)y * targetStride),
                targetStride,
                run);
        }

        return target;
    }

    public void Dispose()
    {
        _memory.Clear();
        _pipeline.Dispose();
    }

    private readonly record struct Slot(CoverKey Key, int Width);
}
