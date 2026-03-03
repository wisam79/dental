using DentalID.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace DentalID.Infrastructure.Services;

/// <summary>
/// Memory cache implementation using Microsoft.Extensions.Caching.Memory
/// </summary>
public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultOptions;

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
        _defaultOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(10))
            .SetAbsoluteExpiration(TimeSpan.FromHours(1))
            .SetPriority(CacheItemPriority.Normal)
            .SetSize(1);
    }

    /// <inheritdoc/>
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        return Task.FromResult(Get<T>(key));
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        Set(key, value, expiration);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Exists(key));
    }

    /// <inheritdoc/>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public T? Get<T>(string key) where T : class
    {
        return _cache.TryGetValue(key, out T? value) ? value : null;
    }

    /// <inheritdoc/>
    public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        var options = _defaultOptions;
        
        if (expiration.HasValue)
        {
            options = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(expiration.Value)
                .SetAbsoluteExpiration(expiration.Value.Add(TimeSpan.FromMinutes(5)))
                .SetPriority(CacheItemPriority.Normal)
                .SetSize(1);
        }

        _cache.Set(key, value, options);
    }

    /// <inheritdoc/>
    public void Remove(string key)
    {
        _cache.Remove(key);
    }

    /// <inheritdoc/>
    public bool Exists(string key)
    {
        return _cache.TryGetValue(key, out _);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        // Bug #22 fix: Use Compact(1.0) to evict 100% of entries
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }
    }
}
