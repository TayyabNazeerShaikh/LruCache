namespace LruCache.Infrastructure.Caching;

// The payload stored inside each LinkedListNode.
// We must store the Key alongside the Value so that when we evict the
// tail node (the LRU item), we can remove it from the Dictionary too.
internal sealed class LruCacheEntry<TKey, TValue>
{
    internal TKey Key { get; }
    internal TValue Value { get; set; }

    internal LruCacheEntry(TKey key, TValue value)
    {
        Key = key;
        Value = value;
    }
}
