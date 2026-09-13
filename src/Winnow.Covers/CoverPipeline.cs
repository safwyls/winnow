using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Winnow.Covers;

/// <summary>
/// The decoded layers for one cover, at one display width. <see cref="Floor"/>
/// is null when the caller asked for <see cref="CoverLayers.Vivid"/> alone.
/// </summary>
public sealed class CoverBitmaps(SKBitmap vivid, SKBitmap? floor) : IDisposable
{
    public SKBitmap Vivid { get; } = vivid;

    public SKBitmap? Floor { get; } = floor;

    public void Dispose()
    {
        Vivid.Dispose();
        Floor?.Dispose();
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
    private readonly Func<byte[], int, SKBitmap?> _decode;

    // Negative results are remembered in memory as well as on disk so a grid of
    // 616 tiles, most of which will miss, costs no file stat per scroll frame.
    private readonly ConcurrentDictionary<CoverKey, Missing> _knownMissing = new();
    private int _disposed;

    public CoverPipeline(
        IEnumerable<ICoverSource> sources,
        CoverDiskCache disk,
        CoverCacheOptions options,
        ILogger<CoverPipeline>? log = null)
        : this(sources, disk, options, log, CoverImaging.DecodeToWidth)
    {
    }

    internal CoverPipeline(IEnumerable<ICoverSource> sources, CoverDiskCache disk,
        CoverCacheOptions options, ILogger<CoverPipeline>? log, Func<byte[], int, SKBitmap?> decode)
    {
        _sources = sources.ToList();
        _disk = disk;
        _options = options;
        _log = log ?? NullLogger<CoverPipeline>.Instance;
        _fetchGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentFetches));
        _decode = decode;
    }

    /// <summary>
    /// The <see cref="CoverSourceSet"/> identity a negative marker for
    /// <paramref name="key"/> is valid under. Computed per call rather than
    /// cached in the constructor because a source may only learn late whether it
    /// can answer at all — IGDB reports a different identity before and after it
    /// finds credentials, which is what makes "configure IGDB" reopen the
    /// negatives written while it was silent.
    /// </summary>
    public string SourceSetIdFor(CoverKey key) => CoverSourceSet.Identity(_sources, key);

    /// <summary>Whether every source has already declined this key (memory or disk marker).</summary>
    public bool IsKnownMissing(CoverKey key)
    {
        var identity = SourceSetIdFor(key);
        if (_knownMissing.TryGetValue(key, out var missing) && missing.Identity == identity
            && _disk.UtcNow < missing.ExpiresAt)
        {
            return true;
        }

        _knownMissing.TryRemove(key, out _);
        if (!_disk.TryGetMissingUntil(key, identity, out var expiresAt))
        {
            return false;
        }

        _knownMissing[key] = new(identity, expiresAt);
        return true;
    }

    /// <summary>
    /// The layers named by <paramref name="layers"/> at <paramref name="width"/>
    /// pixels, or <see langword="null"/> when no source has art. Never throws for a missing
    /// cover; transport failures are logged and answered as "not yet", so the
    /// caller keeps its placeholder and we retry on the next realization.
    /// <para>The returned bitmaps are the caller's to own and dispose;
    /// de-duplication of concurrent requests belongs to <see cref="CoverCache"/>,
    /// which shares one immutable result rather than one disposable one.</para>
    /// </summary>
    public Task<CoverBitmaps?> GetAsync(
        CoverKey key, int width, CoverLayers layers = CoverLayers.VividAndFloor, CancellationToken ct = default)
        => GetAsync(key, width, layers, null, static bitmaps => bitmaps, ct);

    // The callback consumes the decoded layers while the permit is held, so
    // native pixels and their Avalonia conversion share the same memory bound.
    internal async Task<T?> GetAsync<T>(CoverKey key, int width, CoverLayers layers,
        SemaphoreSlim? decodeGate, Func<CoverBitmaps, T> convert, CancellationToken ct) where T : class
    {
        // Callers are responsible for getting off the UI thread before they get
        // here — CoverCache does it with Task.Run. Task.Yield() would NOT be
        // enough: it resumes on the captured SynchronizationContext, which on
        // the UI thread is Avalonia's dispatcher, so every decode would land
        // back on the thread that has to keep the grid scrolling.
        try
        {
            ct.ThrowIfCancellationRequested();
            if (await WithDecodePermitAsync(() => TryDecodeFromDisk(key, width, layers)).ConfigureAwait(false) is { } cached)
            {
                return cached;
            }

            foreach (var source in _sources)
            {
                if (source.CanHandle(key))
                {
                    await source.RefreshCapabilityAsync(key, ct).ConfigureAwait(false);
                }
            }

            if (IsKnownMissing(key))
            {
                return null;
            }

            var identity = SourceSetIdFor(key);
            // Keep this permit until the fetched bytes have been decoded. This
            // bounds encoded payloads waiting for a decode slot without making
            // disk hits wait for any network request.
            await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var bytes = await FetchAsync(key, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (bytes is null)
                {
                    _disk.MarkMissing(key, identity);
                    if (_disk.TryGetMissingUntil(key, identity, out var expiresAt))
                        _knownMissing[key] = new(identity, expiresAt);
                    return null;
                }

                _knownMissing.TryRemove(key, out _);
                _disk.WriteSource(key, bytes);

                return await WithDecodePermitAsync(() =>
                {
                    // Floor generation allocates transient pixels too and belongs
                    // inside the decode bound, only for callers needing that layer.
                    if (layers == CoverLayers.VividAndFloor) WriteFloorVariant(key, bytes);
                    return Decode(bytes, key, width, layers);
                }).ConfigureAwait(false);
            }
            finally { _fetchGate.Release(); }
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

        async Task<T?> WithDecodePermitAsync(Func<CoverBitmaps?> decode)
        {
            if (decodeGate is not null) await decodeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                var bitmaps = decode();
                return bitmaps is null ? null : convert(bitmaps);
            }
            finally { decodeGate?.Release(); }
        }
    }

    private async Task<byte[]?> FetchAsync(CoverKey key, CancellationToken ct)
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

    private CoverBitmaps? TryDecodeFromDisk(CoverKey key, int width, CoverLayers layers)
    {
        if (!_disk.TryReadSource(key, out var source))
        {
            return null;
        }

        // HasFloor, not TryReadFloor: this is an existence check, and reading
        // the whole variant here only to read it again in Decode was two file
        // reads per cover per display size.
        if (layers == CoverLayers.VividAndFloor && !_disk.HasFloor(key))
        {
            WriteFloorVariant(key, source);
        }

        return Decode(source, key, width, layers);
    }

    /// <summary>
    /// Decodes vivid at display width, and — only when the caller asked for it
    /// — the floor from its stored variant, so the colour-matrix pass is not
    /// repeated on every display size.
    /// </summary>
    private CoverBitmaps? Decode(byte[] source, CoverKey key, int width, CoverLayers layers)
    {
        var vivid = _decode(source, width);
        if (vivid is null)
        {
            return null;
        }

        SKBitmap? floor = null;
        try
        {
            if (layers == CoverLayers.Vivid)
            {
                return new CoverBitmaps(vivid, null);
            }

            if (_disk.TryReadFloor(key, out var floorBytes))
            {
                floor = _decode(floorBytes, width);
            }

            // No stored variant (first run, or a write that lost a race): derive it
            // from the bitmap we already have. Same matrix, same endpoint.
            floor ??= CoverImaging.ApplyFloor(vivid, _options.FloorSaturation, _options.FloorBrightness);
            return new CoverBitmaps(vivid, floor);
        }
        catch
        {
            vivid.Dispose();
            floor?.Dispose();
            throw;
        }
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _fetchGate.Dispose();
    }

    private sealed record Missing(string Identity, DateTimeOffset ExpiresAt);
}
