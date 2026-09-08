namespace Winnow.Covers;

/// <summary>
/// Byte-bounded LRU cache behind <see cref="CoverCache"/>, separated from Avalonia
/// types for testability. Declared bytes are reported via
/// <see cref="GC.AddMemoryPressure(long)"/>, and an evicted value is handed to the
/// <c>onEvicted</c> callback the owner passed in — for decoded art that is what
/// releases the LRU's hold on the pixels, so native memory is freed at eviction
/// rather than at the next gen-2 finalization. Thread-safe.
/// </summary>
internal sealed class DecodedLru<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly long _maxBytes;
    private readonly Action<TValue>? _onEvicted;
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _lru = [];
    private long _bytes;

    /// <param name="maxBytes">Ceiling on declared bytes; see <see cref="CoverCacheOptions.MaxDecodedBytes"/>.</param>
    /// <param name="onEvicted">
    /// Called once per value that leaves the cache, outside the lock, in
    /// eviction order. Never called for a value that is still indexed.
    /// </param>
    public DecodedLru(long maxBytes, Action<TValue>? onEvicted = null)
    {
        _maxBytes = maxBytes;
        _onEvicted = onEvicted;
    }

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
    /// Admits an entry and evicts from the tail until the budget holds, then
    /// returns the value that is now cached under <paramref name="key"/>. That
    /// is <paramref name="value"/> unless the key was already held, in which
    /// case the held value is returned and the caller's value was never
    /// admitted — the caller still owns it and must release it itself. The last
    /// entry is never evicted even if it alone exceeds the budget.
    /// </summary>
    public TValue Admit(TKey key, TValue value, long cost)
    {
        List<TValue>? evicted = null;
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var held))
            {
                return held.Value.Value;
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

                if (_onEvicted is not null)
                {
                    (evicted ??= []).Add(oldest.Value.Value);
                }
            }
        }

        NotifyEvicted(evicted);
        return value;
    }

    /// <summary>Drops all entries, releases all declared memory pressure, and reports each drop as an eviction.</summary>
    public void Clear()
    {
        List<TValue>? evicted = null;
        lock (_gate)
        {
            foreach (var entry in _lru)
            {
                GC.RemoveMemoryPressure(entry.Bytes);
                if (_onEvicted is not null)
                {
                    (evicted ??= []).Add(entry.Value);
                }
            }

            _lru.Clear();
            _index.Clear();
            _bytes = 0;
        }

        NotifyEvicted(evicted);
    }

    // Outside the lock: the callback releases ownership of pixels and may post
    // work to another thread, and nothing it does needs this cache's state.
    private void NotifyEvicted(List<TValue>? evicted)
    {
        if (evicted is null || _onEvicted is null)
        {
            return;
        }

        foreach (var value in evicted)
        {
            _onEvicted(value);
        }
    }

    private sealed record Entry(TKey Key, TValue Value, long Bytes);
}
