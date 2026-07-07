using LruCache.Api.Contracts.Cache;
using LruCache.Application.Abstractions.Caching;
using Microsoft.AspNetCore.Mvc;

namespace LruCache.Api.Controllers;

[ApiController]
[Route("api/cache")]
public sealed class CacheController : ControllerBase
{
    private readonly ILruCache<string, string> _cache;

    // The DI container injects the singleton LruCache<string,string> registered
    // in Infrastructure.DependencyInjection. The controller never knows the
    // concrete type — it only depends on the interface.
    public CacheController(ILruCache<string, string> cache) => _cache = cache;

    [HttpGet("{key}")]
    public IActionResult Get(string key)
    {
        if (!_cache.TryGet(key, out var value))
            return NotFound();

        return Ok(new CacheResponse(key, value));
    }

    [HttpPut("{key}")]
    public IActionResult Set(string key, [FromBody] SetCacheRequest request)
    {
        _cache.Set(key, request.Value);
        return NoContent();
    }

    [HttpDelete("{key}")]
    public IActionResult Remove(string key)
    {
        if (!_cache.Remove(key))
            return NotFound();

        return NoContent();
    }

    [HttpDelete]
    public IActionResult Clear()
    {
        _cache.Clear();
        return NoContent();
    }

    [HttpGet("stats")]
    public IActionResult Stats() =>
        Ok(new { _cache.Count, _cache.Capacity });
}
