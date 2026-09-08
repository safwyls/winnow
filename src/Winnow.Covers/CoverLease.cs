namespace Winnow.Covers;

/// <summary>
/// One consumer's claim on the art for a (<see cref="CoverKey"/>, snapped width
/// bucket, <see cref="CoverLayers"/>) slot. While the lease is alive the art it
/// resolved to is not disposed, whether or not the memory cache still holds it,
/// so a consumer may draw what <see cref="TryGetArt"/> hands back for exactly
/// as long as it holds the lease — and must clear the art from every binding
/// before it disposes the lease.
/// </summary>
public interface ICoverLease : IDisposable
{
    CoverKey Key { get; }

    /// <summary>The snapped bucket this lease is for — <see cref="CoverImaging.SnapWidth"/>, never the raw pixel request.</summary>
    int Width { get; }

    /// <summary>The layers this lease asked the cache for.</summary>
    CoverLayers Layers { get; }

    bool TryGetArt(out CoverArt art);

    Task<CoverArt?> GetAsync(CancellationToken ct = default);
}

/// <summary>Hands out reference-counted leases over the cover cache, one per consumer.</summary>
public interface ICoverLeases
{
    /// <param name="layers">
    /// <see cref="CoverLayers.Vivid"/> for a surface that draws the art at full
    /// saturation, <see cref="CoverLayers.VividAndFloor"/> for one that stacks
    /// the dormancy cross-fade's two layers.
    /// </param>
    ICoverLease Acquire(
        CoverKey key, double displayWidthPixels, CoverLayers layers = CoverLayers.VividAndFloor);
}

/// <summary>
/// Reference-counted leases keyed by (cover, width bucket, layers) over
/// <see cref="ICoverCache"/>. While at least one lease on a slot is held the
/// pool keeps the decoded <see cref="CoverArt"/> and one hold on its pixels;
/// when the last lease is disposed the pool releases both, and the memory LRU
/// behind the cache is the only owner again — or, if the art had already been
/// evicted, nothing is and the layers are disposed.
///
/// <para>That is the whole of the disposal rule: the LRU disposes what it
/// evicts, unless a lease says a surface is still drawing it. A consumer that
/// keeps decoded art without a lease breaks the rule, which is why every
/// surface — wall tile, list row, feed card, merge row, detail modal,
/// screenshot strip, lightbox, IGDB candidate row, metadata preview — asks
/// through a lease.</para>
/// </summary>
public sealed class CoverLeasePool : ICoverLeases
{
    private readonly ICoverCache _cache;
    private readonly Lock _gate = new();
    private readonly Dictionary<Slot, Entry> _entries = [];

    public CoverLeasePool(ICoverCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <summary>Slots with at least one live lease. Diagnostics and tests only.</summary>
    public int LiveSlots
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public ICoverLease Acquire(
        CoverKey key, double displayWidthPixels, CoverLayers layers = CoverLayers.VividAndFloor)
    {
        var slot = new Slot(key, CoverImaging.SnapWidth(displayWidthPixels), layers);
        lock (_gate)
        {
            if (!_entries.TryGetValue(slot, out var entry))
            {
                entry = new Entry();
                _entries[slot] = entry;
            }

            entry.Count++;
            return new Handle(this, slot, entry);
        }
    }

    private bool TryGetArt(Slot slot, Entry entry, out CoverArt art)
    {
        lock (_gate)
        {
            if (entry.Art is { } held)
            {
                art = held;
                return true;
            }
        }

        if (_cache.TryGet(slot.Key, slot.Width, slot.Layers, out var cached) && Hold(entry, cached))
        {
            art = entry.Art ?? cached;
            return true;
        }

        art = null!;
        return false;
    }

    private Task<CoverArt?> GetAsync(Slot slot, Entry entry, CancellationToken ct)
    {
        Task<CoverArt?> shared;
        lock (_gate)
        {
            if (entry.Art is { } held)
            {
                return Task.FromResult<CoverArt?>(held);
            }

            // CancellationToken.None on purpose: the load is shared by every
            // lease on this slot, so one consumer walking away must not cancel
            // it for the others. Per-consumer cancellation is the WaitAsync below.
            shared = entry.Load ??= _cache.GetAsync(
                slot.Key, slot.Width, slot.Layers, CancellationToken.None);
        }

        return AwaitShared(slot, shared, entry, ct);
    }

    private async Task<CoverArt?> AwaitShared(
        Slot slot, Task<CoverArt?> shared, Entry entry, CancellationToken ct)
    {
        // Two attempts, not a loop. The cache can evict and dispose a decode
        // between handing it over and this hold, and asking again decodes it
        // afresh; a second failure means the whole budget is under this one
        // slot's cost, and answering "no art yet" leaves the placeholder up
        // rather than spinning on a decode that cannot be kept.
        for (var attempt = 0; ; attempt++)
        {
            var art = await shared.WaitAsync(ct).ConfigureAwait(false);
            if (art is null || Hold(entry, art))
            {
                return art;
            }

            if (attempt > 0)
            {
                return null;
            }

            lock (_gate)
            {
                if (entry.Count == 0)
                {
                    return null;
                }

                shared = entry.Load = _cache.GetAsync(
                    slot.Key, slot.Width, slot.Layers, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Takes this slot's hold on the pixels, so nothing disposes them while a
    /// lease is out. False means the caller must not draw the art: either the
    /// last lease went while the load ran, and the LRU owns it again, or the LRU
    /// had already evicted and disposed it.
    /// </summary>
    private bool Hold(Entry entry, CoverArt art)
    {
        lock (_gate)
        {
            // Count == 0 means the last consumer let go while the load ran; the
            // memory LRU is the owner again and this slot keeps nothing alive.
            if (entry.Count == 0)
            {
                return false;
            }

            if (entry.Art is not null)
            {
                return true;
            }

            if (!art.TryHold())
            {
                return false;
            }

            entry.Art = art;
            return true;
        }
    }

    private void Release(Slot slot, Entry entry)
    {
        lock (_gate)
        {
            if (--entry.Count > 0)
            {
                return;
            }

            if (_entries.TryGetValue(slot, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(slot);
            }

            // Releasing this slot's hold is what lets art the LRU has already
            // evicted be disposed. CoverArt posts the dispose to the UI thread,
            // so it lands after the consumer's binding has let go.
            entry.Art?.ReleaseHold();
            entry.Art = null;
            entry.Load = null;
        }
    }

    private readonly record struct Slot(CoverKey Key, int Width, CoverLayers Layers);

    private sealed class Entry
    {
        public int Count;
        public CoverArt? Art;
        public Task<CoverArt?>? Load;
    }

    private sealed class Handle : ICoverLease
    {
        private readonly CoverLeasePool _pool;
        private readonly Slot _slot;
        private readonly Entry _entry;
        private bool _disposed;

        public Handle(CoverLeasePool pool, Slot slot, Entry entry)
        {
            _pool = pool;
            _slot = slot;
            _entry = entry;
        }

        public CoverKey Key => _slot.Key;

        public int Width => _slot.Width;

        public CoverLayers Layers => _slot.Layers;

        public bool TryGetArt(out CoverArt art)
        {
            if (_disposed)
            {
                art = null!;
                return false;
            }

            return _pool.TryGetArt(_slot, _entry, out art);
        }

        public Task<CoverArt?> GetAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pool.GetAsync(_slot, _entry, ct);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pool.Release(_slot, _entry);
        }
    }
}
