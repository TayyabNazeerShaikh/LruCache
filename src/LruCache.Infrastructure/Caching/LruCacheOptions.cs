namespace LruCache.Infrastructure.Caching;

public sealed class LruCacheOptions
{
    public const int DefaultCapacity = 100;

    // ASP.NET Core's IOptions<T> system will bind "LruCache:Capacity" from
    // appsettings.json to this property automatically.
    public int Capacity { get; set; } = DefaultCapacity;
}
