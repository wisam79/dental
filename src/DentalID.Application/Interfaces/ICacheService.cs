namespace DentalID.Application.Interfaces;

/// <summary>
/// Interface for caching service
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Get cached item or default
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Set cached item with optional expiration
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Remove cached item
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all cached items
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cached item or default (synchronous)
    /// </summary>
    T? Get<T>(string key) where T : class;

    /// <summary>
    /// Set cached item with optional expiration (synchronous)
    /// </summary>
    void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class;

    /// <summary>
    /// Remove cached item (synchronous)
    /// </summary>
    void Remove(string key);

    /// <summary>
    /// Check if key exists (synchronous)
    /// </summary>
    bool Exists(string key);

    /// <summary>
    /// Clear all cached items (synchronous)
    /// </summary>
    void Clear();
}
