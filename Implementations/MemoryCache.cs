using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MultiLevelCache.Interfaces;

namespace MultiLevelCache.Implementations
{
    /// <summary>
    /// 内存缓存实现
    /// </summary>
    public class MemoryCache : ICache
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cache;

        public MemoryCache()
        {
            _cache = new ConcurrentDictionary<string, CacheItem>();
        }

        public Task<T?> GetAsync<T>(string key)
        {
            if (_cache.TryGetValue(key, out var item) && !IsExpired(item))
            {
                return Task.FromResult((T?)item.Value);
            }
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            var item = new CacheItem
            {
                Value = value,
                ExpirationTime = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null
            };
            _cache.AddOrUpdate(key, item, (_, _) => item);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            _cache.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key)
        {
            return Task.FromResult(_cache.TryGetValue(key, out var item) && !IsExpired(item));
        }

        private bool IsExpired(CacheItem item)
        {
            return item.ExpirationTime.HasValue && item.ExpirationTime.Value < DateTime.UtcNow;
        }

        private class CacheItem
        {
            public object? Value { get; set; }
            public DateTime? ExpirationTime { get; set; }
        }
    }
}