namespace Winnow.Covers;

/// <summary>One consumer's claim on the art for a (<see cref="CoverKey"/>, snapped width bucket) pair.</summary>
public interface ICoverLease : IDisposable
{
    CoverKey Key { get; }

    /// <summary>The snapped bucket this lease is for — <see cref="CoverImaging.SnapWidth"/>, never the raw pixel request.</summary>
    int Width { get; }

    bool TryGetArt(out CoverArt art);

    Task<CoverArt?> GetAsync(CancellationToken ct = default);
}

/// <summary>Hands out reference-counted leases over the cover cache, one per consumer.</summary>
public interface ICoverLeases
{
    ICoverLease Acquire(CoverKey key, double displayWidthPixels);
}

/// <summary>
/// Reference-counted leases keyed by (cover, width bucket) over
/// <see cref="ICoverCache"/>. While at least one lease on a slot is held the
/// pool keeps the decoded <see cref="CoverArt"/>; when the last lease is
/// disposed the pool drops it and the memory LRU behind the cache is the
/// only owner of decoded pixels again.
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

    public ICoverLease Acquire(CoverKey key, double displayWidthPixels)
    {
        var slot = new Slot(key, CoverImaging.SnapWidth(displayWidthPixels));
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

        if (_cache.TryGet(slot.Key, slot.Width, out var cached))
        {
            Hold(entry, cached);
            art = cached;
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
            shared = entry.Load ??= _cache.GetAsync(slot.Key, slot.Width, CancellationToken.None);
        }

        return AwaitShared(shared, entry, ct);
    }

    private async Task<CoverArt?> AwaitShared(Task<CoverArt?> shared, Entry entry, CancellationToken ct)
    {
        var art = await shared.WaitAsync(ct).ConfigureAwait(false);
        if (art is not null)
        {
            Hold(entry, art);
        }

        return art;
    }

    private void Hold(Entry entry, CoverArt art)
    {
        lock (_gate)
        {
            // Count == 0 means the last consumer let go while the load ran; the
            // memory LRU is the owner again and this slot keeps nothing alive.
            if (entry.Count > 0)
            {
                entry.Art ??= art;
            }
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

            entry.Art = null;
            entry.Load = null;
        }
    }

    private readonly record struct Slot(CoverKey Key, int Width);

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
