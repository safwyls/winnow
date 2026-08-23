namespace Hoard.Covers;

/// <summary>
/// The byte-bounded LRU behind <see cref="CoverCache"/>, with the Avalonia
/// bitmaps factored out behind <typeparamref name="TValue"/>.
///
/// <para><b>Why it is a type of its own.</b> The eviction rule is the whole
/// memory safety story for a 3,000-tile grid at 2x DPI, and inside
/// <see cref="CoverCache"/> it was unreachable from a test: nothing can be
/// admitted without first constructing an Avalonia <c>WriteableBitmap</c>, and
/// that needs a rendering platform the test projects deliberately do not start.
/// Splitting the bookkeeping from the decoding costs one indirection and makes
/// the part that can silently leak a whole library's worth of native memory
/// something a test can drive directly.</para>
///
/// <para><b>Evicted values are dropped, not disposed.</b> A tile scrolled just
/// off screen may still be mid-render with one bound, and disposing a live
/// bitmap tears down the Skia image underneath it. The GC reclaims them once
/// the last tile lets go — but the pixels live in native memory the collector
/// cannot see, so admissions and evictions declare their cost with
/// <see cref="GC.AddMemoryPressure(long)"/>. Without that, sweeping a large
/// library repeatedly drifts upward: the managed handles are tiny and nothing
/// tells the GC that each one is holding a third of a megabyte.</para>
///
/// <para>Thread-safe. Every operation takes one lock; the grid calls
/// <see cref="TryGet"/> from the UI thread during layout and
/// <see cref="Admit"/> from decode tasks.</para>
/// </summary>
internal sealed class DecodedLru<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly long _maxBytes;
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _lru = [];
    private long _bytes;

    public DecodedLru(long maxBytes) => _maxBytes = maxBytes;

    /// <summary>Bytes currently declared. Diagnostics, and what the eviction rule reads.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>Entries currently held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _index.Count;
            }
        }
    }

    /// <summary>A hit promotes to most-recently-used; a miss changes nothing.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    /// <summary>
    /// Admits an entry and evicts from the tail until the budget holds.
    ///
    /// <para>Re-admitting a key already held is a no-op rather than a replace:
    /// two decodes of the same slot race only when the in-flight de-duplication
    /// upstream lost, and the value already in the cache is as good as the one
    /// arriving — swapping it would only invalidate whatever is already bound to
    /// it.</para>
    ///
    /// <para>The last entry is never evicted, even when it alone exceeds the
    /// budget. An empty cache in front of a tile that is asking for art is a
    /// guaranteed re-decode on every single realization; holding one oversized
    /// cover is bounded and finite.</para>
    /// </summary>
    public void Admit(TKey key, TValue value, long cost)
    {
        lock (_gate)
        {
            if (_index.ContainsKey(key))
            {
                return;
            }

            var node = _lru.AddFirst(new Entry(key, value, cost));
            _index[key] = node;
            _bytes += cost;
            GC.AddMemoryPressure(cost);

            while (_bytes > _maxBytes && _lru.Count > 1)
            {
                var oldest = _lru.Last!;
                _lru.RemoveLast();
                _index.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
                GC.RemoveMemoryPressure(oldest.Value.Bytes);
            }
        }
    }

    /// <summary>
    /// Drops everything and hands back every byte of declared pressure. Balanced
    /// with <see cref="Admit"/>: leaving pressure behind on an unbalanced
    /// <c>AddMemoryPressure</c> teaches the GC that memory is in use for the
    /// rest of the process's life.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _lru)
            {
                GC.RemoveMemoryPressure(entry.Bytes);
            }

            _lru.Clear();
            _index.Clear();
            _bytes = 0;
        }
    }

    private sealed record Entry(TKey Key, TValue Value, long Bytes);
}
