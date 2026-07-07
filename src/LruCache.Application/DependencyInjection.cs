using Microsoft.Extensions.DependencyInjection;

namespace LruCache.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
