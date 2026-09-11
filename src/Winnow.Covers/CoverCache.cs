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
public sealed class CoverCache : ICoverCache, IDisposable, IAsyncDisposable
{
    private readonly CoverPipeline _pipeline;
    private readonly ILogger<CoverCache> _log;
    private readonly Action<Action> _post;
    private readonly Func<SKBitmap, Bitmap> _convert;

    private readonly DecodedLru<Slot, CoverArt> _memory;

    /// <summary>
    /// Bounds decodes in flight, so a fast scroll cannot put one JPEG decode
    /// per realized tile on the thread pool at once: each is CPU-bound and
    /// allocates a transient bitmap, and the tiles that scrolled past while the
    /// queue drained are no longer wanted.
    /// </summary>
    private readonly SemaphoreSlim _decodeGate;

    private readonly Lock _gate = new();
    private readonly Dictionary<Slot, Load> _inFlight = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly int _maxPendingLoads;
    private bool _stopping;
    private Task? _shutdown;

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
        : this(pipeline, options, log, post, ToAvalonia)
    {
    }

    internal CoverCache(CoverPipeline pipeline, CoverCacheOptions options, ILogger<CoverCache>? log,
        Action<Action>? post, Func<SKBitmap, Bitmap> convert)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pipeline = pipeline;
        _log = log ?? NullLogger<CoverCache>.Instance;
        _post = post ?? PostToUiThread;
        _convert = convert;
        _memory = new DecodedLru<Slot, CoverArt>(options.MaxDecodedBytes, static art => art.ReleaseHold());
        _decodeGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentDecodes));
        _maxPendingLoads = Math.Max(1, options.MaxPendingLoads);
    }

    /// <summary>Decoded pixel bytes currently held. Diagnostics only.</summary>
    public long DecodedBytes => _memory.Bytes;

    /// <summary>Entries currently cached. Diagnostics and tests only.</summary>
    public int DecodedCount => _memory.Count;

    /// <summary>Running and queued slots. Never exceeds the configured admission limit.</summary>
    public int PendingCount { get { lock (_gate) return _inFlight.Count; } }

    public bool TryGet(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art)
    {
        lock (_gate)
        {
            if (_stopping) { art = null!; return false; }
            return TryGetCore(key, displayWidthPixels, layers, out art);
        }
    }

    private bool TryGetCore(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art)
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
        if (ct.IsCancellationRequested) return Task.FromCanceled<CoverArt?>(ct);
        lock (_gate)
        {
            if (_stopping) return Task.FromResult<CoverArt?>(null);
            if (TryGetCore(key, displayWidthPixels, layers, out var hit))
                return Task.FromResult<CoverArt?>(hit);

            var slot = new Slot(key, CoverImaging.SnapWidth(displayWidthPixels), layers);
            if (!_inFlight.TryGetValue(slot, out var load))
            {
                if (_inFlight.Count >= _maxPendingLoads) return Task.FromResult<CoverArt?>(null);
                load = new Load(CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
                _inFlight.Add(slot, load);
                // Creation and publication share one lock: task factories cannot race.
                load.Task = Task.Run(() => LoadAsync(slot, load), CancellationToken.None);
            }
            if (load.Cancellation.IsCancellationRequested) return Task.FromResult<CoverArt?>(null);
            load.Waiters++;
            return AwaitLoadAsync(load, ct);
        }
    }

    private async Task<CoverArt?> AwaitLoadAsync(Load load, CancellationToken ct)
    {
        try { return await load.Task.WaitAsync(ct).ConfigureAwait(false); }
        finally
        {
            Task? cancellation = null;
            lock (_gate)
            {
                // Mark cancellation before another waiter can join, but let
                // source callbacks run asynchronously outside the cache lock.
                if (--load.Waiters == 0 && !load.Finished) cancellation = load.Cancellation.CancelAsync();
            }
            if (cancellation is not null) await ObserveCancellationAsync(cancellation).ConfigureAwait(false);
        }
    }

    private async Task<CoverArt?> LoadAsync(Slot slot, Load load)
    {
        var ct = load.Cancellation.Token;
        try
        {
            CoverArt art;
            await _decodeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var bitmaps = await _pipeline.GetAsync(slot.Key, slot.Width, slot.Layers, ct).ConfigureAwait(false);
                if (bitmaps is null) return null;
                ct.ThrowIfCancellationRequested();
                Bitmap? vivid = null;
                Bitmap? floor = null;
                try
                {
                    vivid = _convert(bitmaps.Vivid);
                    floor = bitmaps.Floor is null ? null : _convert(bitmaps.Floor);
                    art = new CoverArt(vivid, floor, _post);
                    vivid = floor = null;
                }
                finally { vivid?.Dispose(); floor?.Dispose(); }
            }
            finally
            {
                _decodeGate.Release();
            }

            // Four bytes a pixel, once per layer this slot asked for. The
            // decoded height is read off the bitmap rather than derived from
            // the aspect ratio, because a capsule that is not exactly 2:3 would
            // otherwise be under-declared.
            var layers = slot.Layers == CoverLayers.VividAndFloor ? 2L : 1L;
            lock (_gate)
            {
                if (_stopping || ct.IsCancellationRequested)
                {
                    art.ReleaseHold();
                    return null;
                }
                var cached = _memory.Admit(slot, art, layers * slot.Width * art.Vivid.PixelSize.Height * 4L);
                if (!ReferenceEquals(cached, art)) art.ReleaseHold();
                return cached;
            }
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
            lock (_gate)
            {
                load.Finished = true;
                _inFlight.Remove(slot);
                load.Cancellation.Dispose();
            }
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
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            return new ValueTask(_shutdown ??= Task.Run(DrainAsync));
        }
    }

    private async Task DrainAsync()
    {
        await ObserveCancellationAsync(_lifetime.CancelAsync()).ConfigureAwait(false);
        Task[] pending;
        lock (_gate) pending = _inFlight.Values.Select(load => load.Task).ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false);
        lock (_gate) _memory.Clear();
        _decodeGate.Dispose();
        _pipeline.Dispose();
        _lifetime.Dispose();
    }

    private async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception ex)
        {
            // Source callbacks are outside our control; their failure must not
            // leave decoded art or the remaining loads alive during shutdown.
            _log.LogWarning(ex, "Cover source cancellation callback failed");
        }
    }

    private readonly record struct Slot(CoverKey Key, int Width, CoverLayers Layers);

    private sealed class Load(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task<CoverArt?> Task { get; set; } = null!;
        public int Waiters;
        public bool Finished;
    }
}
