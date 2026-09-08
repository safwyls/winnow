using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Winnow.Covers;

/// <summary>Cover art for the grid. Never blocks a user-facing path (§5.1).</summary>
public interface ICoverCache
{
    /// <summary>Synchronous lookup for a cover already decoded and in memory.</summary>
    bool TryGet(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art);

    /// <summary>The cover, fetching and decoding off-thread if needed. Null means no art available.</summary>
    Task<CoverArt?> GetAsync(
        CoverKey key, double displayWidthPixels, CoverLayers layers, CancellationToken ct = default);
}

/// <summary>
/// Bounded in-memory LRU over <see cref="CoverPipeline"/>. Capped by total pixel
/// bytes (not count) via <see cref="DecodedLru{TKey,TValue}"/>, and the first
/// holder of every decoded <see cref="CoverArt"/>: eviction releases that hold,
/// which disposes the layers unless a lease still holds them
/// (<see cref="CoverLeasePool"/>).
///
/// <para>Slots are keyed by cover, snapped width bucket <em>and</em>
/// <see cref="CoverLayers"/>, so a vivid-only surface and a two-layer surface
/// cannot overwrite each other's entry. A vivid-only request checks the
/// two-layer slot as well before it decodes anything, so a wall tile and the
/// detail modal at the same width share one decode.</para>
/// </summary>
public sealed class CoverCache : ICoverCache, IDisposable
{
    private readonly CoverPipeline _pipeline;
    private readonly ILogger<CoverCache> _log;
    private readonly Action<Action> _post;

    private readonly DecodedLru<Slot, CoverArt> _memory;

    /// <summary>
    /// Bounds decodes in flight, so a fast scroll cannot put one JPEG decode
    /// per realized tile on the thread pool at once: each is CPU-bound and
    /// allocates a transient bitmap, and the tiles that scrolled past while the
    /// queue drained are no longer wanted.
    /// </summary>
    private readonly SemaphoreSlim _decodeGate;

    private readonly ConcurrentDictionary<Slot, Task<CoverArt?>> _inFlight = new();

    /// <param name="post">
    /// How the disposal of evicted art reaches the UI thread; see
    /// <see cref="CoverArt"/> for why it must. Defaults to the Avalonia
    /// dispatcher at background priority, so a dispose lands after the consumer
    /// that let the art go has cleared its binding.
    /// </param>
    public CoverCache(
        CoverPipeline pipeline,
        CoverCacheOptions options,
        ILogger<CoverCache>? log = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pipeline = pipeline;
        _log = log ?? NullLogger<CoverCache>.Instance;
        _post = post ?? PostToUiThread;
        _memory = new DecodedLru<Slot, CoverArt>(options.MaxDecodedBytes, static art => art.ReleaseHold());
        _decodeGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentDecodes));
    }

    /// <summary>Decoded pixel bytes currently held. Diagnostics only.</summary>
    public long DecodedBytes => _memory.Bytes;

    /// <summary>Entries currently cached. Diagnostics and tests only.</summary>
    public int DecodedCount => _memory.Count;

    public bool TryGet(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art)
    {
        var width = CoverImaging.SnapWidth(displayWidthPixels);
        if (_memory.TryGet(new Slot(key, width, layers), out art))
        {
            return true;
        }

        // A cached pair answers a vivid-only request: its floor layer is pixels
        // this caller will not draw, never pixels it is missing. The reverse is
        // not true, so a two-layer request never settles for a vivid-only entry.
        return layers == CoverLayers.Vivid
            && _memory.TryGet(new Slot(key, width, CoverLayers.VividAndFloor), out art);
    }

    public Task<CoverArt?> GetAsync(
        CoverKey key, double displayWidthPixels, CoverLayers layers, CancellationToken ct = default)
    {
        if (TryGet(key, displayWidthPixels, layers, out var hit))
        {
            return Task.FromResult<CoverArt?>(hit);
        }

        var slot = new Slot(key, CoverImaging.SnapWidth(displayWidthPixels), layers);

        // Task.Run, not a bare async call: this is invoked from the UI thread as
        // a tile realizes, and everything downstream (file IO, JPEG decode, the
        // colour-matrix pass) must stay off it (§5.1, §5.4).
        return _inFlight.GetOrAdd(slot, s => Task.Run(() => LoadAsync(s, ct), CancellationToken.None));
    }

    private async Task<CoverArt?> LoadAsync(Slot slot, CancellationToken ct)
    {
        try
        {
            CoverBitmaps? bitmaps;
            await _decodeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bitmaps = await _pipeline.GetAsync(slot.Key, slot.Width, slot.Layers, ct).ConfigureAwait(false);
            }
            finally
            {
                _decodeGate.Release();
            }

            if (bitmaps is null)
            {
                return null;
            }

            CoverArt art;
            using (bitmaps)
            {
                art = new CoverArt(
                    ToAvalonia(bitmaps.Vivid),
                    bitmaps.Floor is null ? null : ToAvalonia(bitmaps.Floor),
                    _post);
            }

            // Four bytes a pixel, once per layer this slot asked for. The
            // decoded height is read off the bitmap rather than derived from
            // the aspect ratio, because a capsule that is not exactly 2:3 would
            // otherwise be under-declared.
            var layers = slot.Layers == CoverLayers.VividAndFloor ? 2L : 1L;
            var cached = _memory.Admit(slot, art, layers * slot.Width * art.Vivid.PixelSize.Height * 4L);

            // Another decode of the same slot beat this one into the cache. The
            // cached art is the one every caller shares, so drop the
            // duplicate's pixels here rather than leave them to a finalizer.
            if (!ReferenceEquals(cached, art))
            {
                art.ReleaseHold();
            }

            return cached;
        }
        catch (OperationCanceledException)
        {
            return null;
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

    private static void PostToUiThread(Action action)
        => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);

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
        // Clear reports every entry as an eviction, so this releases the LRU's
        // hold on all of them. Art a lease still holds survives until that
        // lease is disposed, which is the rule eviction follows too.
        _memory.Clear();
        _decodeGate.Dispose();
        _pipeline.Dispose();
    }

    private readonly record struct Slot(CoverKey Key, int Width, CoverLayers Layers);
}
