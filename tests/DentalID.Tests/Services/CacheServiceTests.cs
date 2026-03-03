using System;
using System.Threading.Tasks;
using DentalID.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace DentalID.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CacheService"/> (in-memory cache wrapper).
/// Uses a real <see cref="MemoryCache"/> instance to avoid mocking internals.
/// </summary>
public class CacheServiceTests : IDisposable
{
    private readonly MemoryCache _memoryCache;
    private readonly CacheService _service;

    public CacheServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _service = new CacheService(_memoryCache);
    }

    public void Dispose() => _memoryCache.Dispose();

    // ────── Synchronous API ────────────────────────────────────────────────

    [Fact]
    public void Set_Get_ShouldRoundtrip_SyncApi()
    {
        var value = new CachePayload { Name = "Test", Value = 42 };
        _service.Set("key1", value);

        var result = _service.Get<CachePayload>("key1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Get_NonExistentKey_ShouldReturnNull()
    {
        var result = _service.Get<CachePayload>("missing_key");
        result.Should().BeNull();
    }

    [Fact]
    public void Exists_ShouldReturnTrue_WhenKeyPresent()
    {
        _service.Set("exists_key", new CachePayload());
        _service.Exists("exists_key").Should().BeTrue();
    }

    [Fact]
    public void Exists_ShouldReturnFalse_WhenKeyAbsent()
    {
        _service.Exists("absent_key").Should().BeFalse();
    }

    [Fact]
    public void Remove_ShouldDeleteEntry()
    {
        _service.Set("remove_key", new CachePayload { Name = "ToRemove" });
        _service.Exists("remove_key").Should().BeTrue();

        _service.Remove("remove_key");

        _service.Exists("remove_key").Should().BeFalse();
    }

    [Fact]
    public void Remove_NonExistentKey_ShouldNotThrow()
    {
        // Act (should complete without exception)
        var act = () => _service.Remove("ghost_key");
        act.Should().NotThrow();
    }

    [Fact]
    public void Clear_ShouldEvictAllEntries()
    {
        _service.Set("k1", new CachePayload { Name = "A" });
        _service.Set("k2", new CachePayload { Name = "B" });
        _service.Set("k3", new CachePayload { Name = "C" });

        _service.Clear();

        _service.Exists("k1").Should().BeFalse();
        _service.Exists("k2").Should().BeFalse();
        _service.Exists("k3").Should().BeFalse();
    }

    [Fact]
    public void Set_WithCustomExpiration_ShouldStoreEntry()
    {
        // Store with a 30-minute custom TTL
        _service.Set("custom_ttl_key", new CachePayload { Name = "custom" }, TimeSpan.FromMinutes(30));

        _service.Exists("custom_ttl_key").Should().BeTrue();
        _service.Get<CachePayload>("custom_ttl_key")!.Name.Should().Be("custom");
    }

    [Fact]
    public void Set_OverwritesExistingKey()
    {
        _service.Set("overwrite_key", new CachePayload { Name = "original" });
        _service.Set("overwrite_key", new CachePayload { Name = "updated" });

        var result = _service.Get<CachePayload>("overwrite_key");
        result!.Name.Should().Be("updated");
    }

    // ────── Asynchronous API ───────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_GetAsync_ShouldRoundtrip()
    {
        var payload = new CachePayload { Name = "async_test", Value = 99 };
        await _service.SetAsync("async_key", payload);

        var result = await _service.GetAsync<CachePayload>("async_key");

        result.Should().NotBeNull();
        result!.Name.Should().Be("async_test");
        result.Value.Should().Be(99);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenKeyPresent()
    {
        await _service.SetAsync("exists_async", new CachePayload());
        var exists = await _service.ExistsAsync("exists_async");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteEntry()
    {
        await _service.SetAsync("remove_async", new CachePayload { Name = "bye" });
        await _service.RemoveAsync("remove_async");

        var exists = await _service.ExistsAsync("remove_async");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_ShouldEvictAllEntries()
    {
        await _service.SetAsync("ca1", new CachePayload());
        await _service.SetAsync("ca2", new CachePayload());

        await _service.ClearAsync();

        (await _service.ExistsAsync("ca1")).Should().BeFalse();
        (await _service.ExistsAsync("ca2")).Should().BeFalse();
    }

    // ────── Helper ──────────────────────────────────────────────────────────

    private sealed class CachePayload
    {
        public string Name { get; init; } = string.Empty;
        public int Value { get; init; }
    }
}
