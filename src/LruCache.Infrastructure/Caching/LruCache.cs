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

    public bool TryGet(TKey key, out TValue? value) => throw new NotImplementedException();
    public void Set(TKey key, TValue value) => throw new NotImplementedException();
    public bool Remove(TKey key) => throw new NotImplementedException();
    public void Clear() => throw new NotImplementedException();
}
