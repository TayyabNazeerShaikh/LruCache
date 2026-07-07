using LruCache.Application.Abstractions.Caching;
using LruCache.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LruCache.Infrastructure;

public static class DependencyInjection
{
    // Call this from Program.cs to wire up the cache.
    // Singleton: one shared LruCache instance for the whole app — thread safety is essential.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<ILruCache<string, string>, LruCache<string, string>>();
        return services;
    }
}
