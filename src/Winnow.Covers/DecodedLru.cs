namespace Winnow.Covers;

/// <summary>
/// Byte-bounded LRU cache behind <see cref="CoverCache"/>, separated from Avalonia
/// types for testability. Evicted values are dropped (not disposed) and tracked via
/// <see cref="GC.AddMemoryPressure(long)"/>. Thread-safe.
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
    /// Admits an entry and evicts from the tail until the budget holds. Re-admitting
    /// a held key is a no-op. The last entry is never evicted even if it alone exceeds the budget.
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

    /// <summary>Drops all entries and releases all declared memory pressure.</summary>
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
