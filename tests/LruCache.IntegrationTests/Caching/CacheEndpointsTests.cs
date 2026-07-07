using System.Net;
using System.Net.Http.Json;
using LruCache.Api.Contracts.Cache;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LruCache.IntegrationTests.Caching;

// IClassFixture<WebApplicationFactory<Program>> boots the real ASP.NET Core app
// in memory — full pipeline: routing, DI, middleware, serialization.
public sealed class CacheEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CacheEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_NonExistentKey_Returns404()
    {
        var response = await _client.GetAsync("/api/cache/missing-key");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ThenGet_ReturnsStoredValue()
    {
        await _client.PutAsJsonAsync("/api/cache/greeting", new SetCacheRequest("hello"));

        var result = await _client.GetFromJsonAsync<CacheResponse>("/api/cache/greeting");

        Assert.Equal("greeting", result!.Key);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task Put_Twice_OverwritesValue()
    {
        await _client.PutAsJsonAsync("/api/cache/name", new SetCacheRequest("alice"));
        await _client.PutAsJsonAsync("/api/cache/name", new SetCacheRequest("bob"));

        var result = await _client.GetFromJsonAsync<CacheResponse>("/api/cache/name");

        Assert.Equal("bob", result!.Value);
    }

    [Fact]
    public async Task Delete_ExistingKey_Returns204_ThenGet_Returns404()
    {
        await _client.PutAsJsonAsync("/api/cache/temp", new SetCacheRequest("data"));

        var deleteResponse = await _client.DeleteAsync("/api/cache/temp");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync("/api/cache/temp");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentKey_Returns404()
    {
        var response = await _client.DeleteAsync("/api/cache/ghost");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAll_ClearsCache()
    {
        await _client.PutAsJsonAsync("/api/cache/k1", new SetCacheRequest("v1"));
        await _client.PutAsJsonAsync("/api/cache/k2", new SetCacheRequest("v2"));

        var clearResponse = await _client.DeleteAsync("/api/cache");
        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cache/k1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cache/k2")).StatusCode);
    }

    [Fact]
    public async Task Stats_ReturnsCountAndCapacity()
    {
        var response = await _client.GetAsync("/api/cache/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("count", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capacity", body, StringComparison.OrdinalIgnoreCase);
    }
}
