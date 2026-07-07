using LruCache.Application.Abstractions.Caching;

namespace LruCache.Infrastructure.Caching;

internal sealed class LruCache<TKey, TValue> : ILruCache<TKey, TValue>
    where TKey : notnull
{
    // Maps every key directly to its node in _recency. O(1) access to any node.
    private readonly Dictionary<TKey, LinkedListNode<LruCacheEntry<TKey, TValue>>> _entries;

    // Doubly-linked list ordered by recency: head = MRU, tail = LRU.
    // LinkedList gives O(1) AddFirst / Remove / RemoveLast when you already
    // hold the node reference (which _entries always provides).
    private readonly LinkedList<LruCacheEntry<TKey, TValue>> _recency;

    private readonly int _capacity;

    // Dedicated lock object. Never lock on `this` — callers could acquire it
    // externally and cause deadlocks we can't predict.
    private readonly object _syncRoot = new();

    public int Count { get { lock (_syncRoot) return _entries.Count; } }
    public int Capacity => _capacity;

    internal LruCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        _entries = new Dictionary<TKey, LinkedListNode<LruCacheEntry<TKey, TValue>>>(capacity);
        _recency = new LinkedList<LruCacheEntry<TKey, TValue>>();
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        // TryGet is NOT a pure read — it promotes the node (writes to _recency).
        // ReaderWriterLockSlim would not help here; a plain lock is correct.
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);

            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_syncRoot)
        {
            if (_entries.TryGetValue(key, out var existingNode))
            {
                existingNode.Value.Value = value;
                _recency.Remove(existingNode);
                _recency.AddFirst(existingNode);
                return;
            }

            if (_entries.Count >= _capacity)
            {
                var lruNode = _recency.Last!;
                _entries.Remove(lruNode.Value.Key);
                _recency.RemoveLast();
            }

            var entry = new LruCacheEntry<TKey, TValue>(key, value);
            var newNode = _recency.AddFirst(entry);
            _entries[key] = newNode;
        }
    }

    public bool Remove(TKey key)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(key, out var node))
                return false;

            _entries.Remove(key);
            _recency.Remove(node);
            return true;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
            _recency.Clear();
        }
    }
}
