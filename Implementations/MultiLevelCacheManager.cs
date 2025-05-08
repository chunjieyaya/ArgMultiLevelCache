using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MultiLevelCache.Interfaces;

namespace MultiLevelCache.Implementations
{
    /// <summary>
    /// 多级缓存管理器实现
    /// </summary>
    public class MultiLevelCacheManager : IMultiLevelCache
    {
        private readonly SortedDictionary<int, ICache> _caches;
        private readonly TimeSpan? _defaultExpiration;

        public MultiLevelCacheManager(Options.MultiLevelCacheOptions options)
        {
            _caches = new SortedDictionary<int, ICache>();
            _defaultExpiration = options.DefaultExpiration;
        }

        public void AddCache(ICache cache, int level)
        {
            if (_caches.ContainsKey(level))
            {
                throw new ArgumentException($"缓存级别 {level} 已经存在");
            }
            _caches.Add(level, cache);
        }

        public async Task<T?> GetAsync<T>(string key, Func<Task<T?>>? dataFetcher = null)
        {
            // 按照缓存级别顺序查找数据（数字越小优先级越高）
            foreach (var cache in _caches.OrderBy(x => x.Key))
            {
                var value = await cache.Value.GetAsync<T>(key);
                if (value != null)
                {
                    // 将数据同步到更低优先级的缓存
                    await SyncToLowerLevelCachesAsync(key, value, cache.Key);
                    return value;
                }
            }

            // 如果所有缓存层都没有找到数据，且提供了数据获取方法，则从数据源获取
            if (dataFetcher != null)
            {
                var value = await dataFetcher();
                if (value != null)
                {
                    // 将从数据源获取的数据写入所有缓存层
                    await SetAsync(key, value);
                }
                return value;
            }

            return default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            // 按优先级顺序将数据写入所有缓存层
            foreach (var cache in _caches.OrderBy(x => x.Key))
            {
                // 只有在明确指定过期时间或设置了默认过期时间时才传递过期时间参数
                var actualExpiration = expiration ?? _defaultExpiration;
                await cache.Value.SetAsync(key, value, actualExpiration);
            }
        }

        public async Task RemoveAsync(string key)
        {
            // 按优先级顺序从所有缓存层中移除数据
            foreach (var cache in _caches.OrderBy(x => x.Key))
            {
                await cache.Value.RemoveAsync(key);
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            // 检查是否存在于任意缓存层
            foreach (var cache in _caches)
            {
                if (await cache.Value.ExistsAsync(key))
                {
                    return true;
                }
            }
            return false;
        }

        private async Task SyncToLowerLevelCachesAsync<T>(string key, T value, int currentLevel)
        {
            foreach (var cache in _caches.Where(c => c.Key > currentLevel))
            {
                await cache.Value.SetAsync(key, value);
            }
        }
    }
}