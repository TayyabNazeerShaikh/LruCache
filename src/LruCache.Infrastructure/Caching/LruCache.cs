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

    public int Count => _entries.Count;
    public int Capacity => _capacity;

    internal LruCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        // Pre-size the dictionary to avoid rehashing.
        _entries = new Dictionary<TKey, LinkedListNode<LruCacheEntry<TKey, TValue>>>(capacity);
        _recency = new LinkedList<LruCacheEntry<TKey, TValue>>();
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        if (!_entries.TryGetValue(key, out var node))
        {
            value = default;
            return false;
        }

        // Promote: remove from current position and move to head (MRU).
        // Both operations are O(1) because we hold the node reference.
        _recency.Remove(node);
        _recency.AddFirst(node);

        value = node.Value.Value;
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        if (_entries.TryGetValue(key, out var existingNode))
        {
            // Update in-place and promote to MRU — no eviction needed.
            existingNode.Value.Value = value;
            _recency.Remove(existingNode);
            _recency.AddFirst(existingNode);
            return;
        }

        // At capacity: evict the tail node (LRU item) before inserting.
        if (_entries.Count >= _capacity)
        {
            var lruNode = _recency.Last!;          // tail = LRU
            _entries.Remove(lruNode.Value.Key);    // Key stored in entry — see Step 3
            _recency.RemoveLast();
        }

        // New entry always goes to the head (most recently used).
        var entry = new LruCacheEntry<TKey, TValue>(key, value);
        var newNode = _recency.AddFirst(entry);
        _entries[key] = newNode;
    }
    public bool Remove(TKey key)
    {
        if (!_entries.TryGetValue(key, out var node))
            return false;

        // Always update both structures together — they must stay in sync.
        _entries.Remove(key);
        _recency.Remove(node);
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _recency.Clear();
    }
}
