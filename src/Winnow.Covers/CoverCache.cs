using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Winnow.Covers;

/// <summary>
/// The two layers of the dormancy cross-fade
/// (docs/spikes/avalonia-dormancy-rendering.md): the floor variant sits under
/// the vivid art, whose opacity carries the ramp.
/// </summary>
/// <param name="Vivid">Full-saturation cover, decoded at display resolution.</param>
/// <param name="Floor">Saturation 0.22 / brightness 0.60 variant of the same image.</param>
public sealed record CoverArt(Bitmap Vivid, Bitmap Floor);

/// <summary>Cover art for the grid. Never blocks a user-facing path (§5.1).</summary>
public interface ICoverCache
{
    /// <summary>
    /// A cover already decoded and in memory. Synchronous and allocation-free on
    /// the hit path so a recycled tile can repaint in the same layout pass
    /// instead of flashing its placeholder.
    /// </summary>
    bool TryGet(CoverKey key, double displayWidthPixels, out CoverArt art);

    /// <summary>
    /// The cover, fetching and decoding off-thread if needed.
    /// <see langword="null"/> means "no art" — keep the placeholder.
    /// </summary>
    Task<CoverArt?> GetAsync(CoverKey key, double displayWidthPixels, CancellationToken ct = default);
}

/// <summary>
/// Bounded in-memory LRU over <see cref="CoverPipeline"/>.
/// <para><b>Memory bound.</b> Decoded bitmaps are capped by total pixel bytes,
/// not by count, because the same 616 tiles cost 4x more at 2x DPI. The default
/// is 128 MB of BGRA pixels counting both layers: a 148 DIP tile at 1x decodes
/// to 160x240 (154 KB/layer, 307 KB/pair → ~430 covers) and at 2x to 320x480
/// (614 KB/layer, 1.2 MB/pair → ~107 covers). A 1180x760 window realizes roughly
/// 24 tiles, a maximised 4K one under 200, so even the worst case holds several
/// screenfuls of scrollback without eviction thrash — and eviction is cheap
/// anyway, because the disk cache means a re-decode never touches the network.
/// </para>
/// <para>The bound itself lives in <see cref="DecodedLru{TKey,TValue}"/>, which
/// is where the eviction rule and the <see cref="GC.AddMemoryPressure(long)"/>
/// accounting are documented — and, because it is free of Avalonia, where they
/// can be tested without starting a rendering platform.</para>
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
