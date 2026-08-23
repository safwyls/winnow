using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Hoard.Covers;

/// <summary>The vivid/floor pair for one cover, at one display width.</summary>
public sealed class CoverBitmaps(SKBitmap vivid, SKBitmap floor) : IDisposable
{
    public SKBitmap Vivid { get; } = vivid;

    public SKBitmap Floor { get; } = floor;

    public void Dispose()
    {
        Vivid.Dispose();
        Floor.Dispose();
    }
}

/// <summary>
/// Fetch → disk → decode, with no Avalonia in sight. Everything that can be
/// tested without a rendering platform lives here; <see cref="CoverCache"/> only
/// adds the bitmap conversion and the memory bound on top.
/// </summary>
public sealed class CoverPipeline : IDisposable
{
    private readonly IReadOnlyList<ICoverSource> _sources;
    private readonly CoverDiskCache _disk;
    private readonly CoverCacheOptions _options;
    private readonly ILogger<CoverPipeline> _log;
    private readonly SemaphoreSlim _fetchGate;

    // Negative results are remembered in memory as well as on disk so a grid of
    // 616 tiles, most of which will miss, costs no file stat per scroll frame.
    private readonly ConcurrentDictionary<CoverKey, bool> _knownMissing = new();

    public CoverPipeline(
        IEnumerable<ICoverSource> sources,
        CoverDiskCache disk,
        CoverCacheOptions options,
        ILogger<CoverPipeline>? log = null)
    {
        _sources = sources.ToList();
        _disk = disk;
        _options = options;
        _log = log ?? NullLogger<CoverPipeline>.Instance;
        _fetchGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentFetches));
    }

    /// <summary>Whether every source has already declined this key (memory or disk marker).</summary>
    public bool IsKnownMissing(CoverKey key)
    {
        if (_knownMissing.ContainsKey(key))
        {
            return true;
        }

        if (!_disk.IsKnownMissing(key))
        {
            return false;
        }

        _knownMissing[key] = true;
        return true;
    }

    /// <summary>
    /// The vivid/floor pair at <paramref name="width"/> pixels, or
    /// <see langword="null"/> when no source has art. Never throws for a missing
    /// cover; transport failures are logged and answered as "not yet", so the
    /// caller keeps its placeholder and we retry on the next realization.
    /// <para>The returned bitmaps are the caller's to own and dispose;
    /// de-duplication of concurrent requests belongs to <see cref="CoverCache"/>,
    /// which shares one immutable result rather than one disposable one.</para>
    /// </summary>
    public async Task<CoverBitmaps?> GetAsync(CoverKey key, int width, CancellationToken ct = default)
    {
        // Callers are responsible for getting off the UI thread before they get
        // here — CoverCache does it with Task.Run. Task.Yield() would NOT be
        // enough: it resumes on the captured SynchronizationContext, which on
        // the UI thread is Avalonia's dispatcher, so every decode would land
        // back on the thread that has to keep the grid scrolling.
        if (IsKnownMissing(key))
        {
            return null;
        }

        try
        {
            if (TryDecodeFromDisk(key, width) is { } cached)
            {
                return cached;
            }

            var bytes = await FetchAsync(key, ct).ConfigureAwait(false);
            if (bytes is null)
            {
                _knownMissing[key] = true;
                _disk.MarkMissing(key);
                return null;
            }

            _disk.WriteSource(key, bytes);
            WriteFloorVariant(key, bytes);
            return Decode(bytes, key, width);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            // A transport hiccup must not be cached as "no art" — leave the key
            // clean so the next realization tries again.
            _log.LogWarning(ex, "Cover fetch failed for {Key}", key);
            return null;
        }
    }

    private async Task<byte[]?> FetchAsync(CoverKey key, CancellationToken ct)
    {
        await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var source in _sources)
            {
                if (!source.CanHandle(key))
                {
                    continue;
                }

                var bytes = await source.TryFetchAsync(key, ct).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    return bytes;
                }
            }

            return null;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private CoverBitmaps? TryDecodeFromDisk(CoverKey key, int width)
    {
        if (!_disk.TryReadSource(key, out var source))
        {
            return null;
        }

        if (!_disk.TryReadFloor(key, out _))
        {
            WriteFloorVariant(key, source);
        }

        return Decode(source, key, width);
    }

    /// <summary>
    /// Decodes vivid at display width, and the floor from its stored variant so
    /// the colour-matrix pass is not repeated on every display size.
    /// </summary>
    private CoverBitmaps? Decode(byte[] source, CoverKey key, int width)
    {
        var vivid = CoverImaging.DecodeToWidth(source, width);
        if (vivid is null)
        {
            return null;
        }

        SKBitmap? floor = null;
        if (_disk.TryReadFloor(key, out var floorBytes))
        {
            floor = CoverImaging.DecodeToWidth(floorBytes, width);
        }

        // No stored variant (first run, or a write that lost a race): derive it
        // from the bitmap we already have. Same matrix, same endpoint.
        floor ??= CoverImaging.ApplyFloor(vivid, _options.FloorSaturation, _options.FloorBrightness);
        return new CoverBitmaps(vivid, floor);
    }

    private void WriteFloorVariant(CoverKey key, byte[] source)
    {
        using var scaled = CoverImaging.DecodeToWidth(source, _options.DiskVariantWidth);
        if (scaled is null)
        {
            return;
        }

        using var floor = CoverImaging.ApplyFloor(scaled, _options.FloorSaturation, _options.FloorBrightness);
        _disk.WriteFloor(key, CoverImaging.EncodeJpeg(floor, _options.DiskVariantQuality));
    }

    public void Dispose() => _fetchGate.Dispose();
}
