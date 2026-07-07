namespace LruCache.Application.Abstractions.Caching;

public interface ILruCache<TKey, TValue> where TKey : notnull
{
    /// <summary>How many entries are currently stored.</summary>
    int Count { get; }

    /// <summary>Maximum number of entries before the LRU item is evicted.</summary>
    int Capacity { get; }

    /// <summary>
    /// Tries to retrieve a value by key. Promotes the entry to most-recently-used on hit.
    /// Returns false and sets value to default on miss.
    /// </summary>
    bool TryGet(TKey key, out TValue? value);

    /// <summary>
    /// Inserts or updates a key-value pair. Evicts the least-recently-used entry when
    /// the cache is at capacity.
    /// </summary>
    void Set(TKey key, TValue value);

    /// <summary>Explicitly removes a single entry. Returns true if it existed.</summary>
    bool Remove(TKey key);

    /// <summary>Removes all entries.</summary>
    void Clear();
}
