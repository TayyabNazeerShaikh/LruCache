using LruCache.Infrastructure.Caching;

namespace LruCache.UnitTests;

public sealed class LruCacheTests
{
    // Helper: construct a cache with a small capacity so eviction tests are readable.
    private static LruCache<string, int> CreateCache(int capacity = 3) => new(capacity);

    // ─── TryGet ────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var cache = CreateCache();

        var found = cache.TryGet("x", out var value);

        Assert.False(found);
        Assert.Equal(0, value);     // out-param should be default(int)
    }

    [Fact]
    public void TryGet_ExistingKey_ReturnsTrueAndValue()
    {
        var cache = CreateCache();
        cache.Set("a", 42);

        var found = cache.TryGet("a", out var value);

        Assert.True(found);
        Assert.Equal(42, value);
    }

    // ─── Set ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Set_NewKey_IncreasesCount()
    {
        var cache = CreateCache();

        cache.Set("a", 1);
        cache.Set("b", 2);

        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Set_ExistingKey_UpdatesValueWithoutIncreasingCount()
    {
        var cache = CreateCache();
        cache.Set("a", 1);

        cache.Set("a", 99);

        Assert.Equal(1, cache.Count);         // still one entry
        cache.TryGet("a", out var value);
        Assert.Equal(99, value);
    }

    // ─── Eviction ──────────────────────────────────────────────────────────────

    [Fact]
    public void Set_BeyondCapacity_EvictsLeastRecentlyUsed()
    {
        // Arrange: fill a capacity-2 cache
        var cache = CreateCache(capacity: 2);
        cache.Set("a", 1);   // recency: [a]
        cache.Set("b", 2);   // recency: [b, a]

        // Act: insert a third entry — "a" is LRU victim
        cache.Set("c", 3);   // evicts "a"; recency: [c, b]

        // Assert
        Assert.False(cache.TryGet("a", out _));   // evicted
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void TryGet_PromotesEntry_SoItSurvivesEviction()
    {
        // Arrange
        var cache = CreateCache(capacity: 2);
        cache.Set("a", 1);   // recency: [a]
        cache.Set("b", 2);   // recency: [b, a]

        // Act: access "a" → it gets promoted to MRU → "b" becomes LRU
        cache.TryGet("a", out _);    // recency: [a, b]
        cache.Set("c", 3);           // evicts "b"; recency: [c, a]

        // Assert: "a" survived because it was promoted; "b" was evicted
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Set_UpdateExistingKey_PromotesItSoItSurvivesEviction()
    {
        // Updating an existing key should behave like a TryGet: promote to MRU.
        var cache = CreateCache(capacity: 2);
        cache.Set("a", 1);   // recency: [a]
        cache.Set("b", 2);   // recency: [b, a]

        cache.Set("a", 10);  // update "a" → promote → recency: [a, b]
        cache.Set("c", 3);   // evicts "b"; recency: [c, a]

        Assert.True(cache.TryGet("a", out var val));
        Assert.Equal(10, val);                        // updated value
        Assert.False(cache.TryGet("b", out _));       // evicted
    }

    // ─── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingKey_ReturnsTrueAndDecreasesCount()
    {
        var cache = CreateCache();
        cache.Set("a", 1);

        var removed = cache.Remove("a");

        Assert.True(removed);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void Remove_MissingKey_ReturnsFalse()
    {
        var cache = CreateCache();

        Assert.False(cache.Remove("nonexistent"));
    }

    [Fact]
    public void Remove_ThenSet_SlotIsReusable()
    {
        // After removing, the freed capacity should accept a new entry without eviction.
        var cache = CreateCache(capacity: 1);
        cache.Set("a", 1);
        cache.Remove("a");

        cache.Set("b", 2);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("b", out _));
    }

    // ─── Clear ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllEntriesAndResetsCount()
    {
        var cache = CreateCache();
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Clear_ThenSet_CacheIsFullyUsable()
    {
        var cache = CreateCache(capacity: 2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Clear();

        // Should be able to fill to capacity again with no eviction.
        cache.Set("x", 10);
        cache.Set("y", 20);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("x", out _));
        Assert.True(cache.TryGet("y", out _));
    }

    // ─── Capacity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
    }

    [Fact]
    public void Capacity_AlwaysReturnsValueFromConstructor()
    {
        var cache = new LruCache<string, int>(7);
        Assert.Equal(7, cache.Capacity);
    }
}
