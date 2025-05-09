using System;
using Newtonsoft.Json;
using System.Threading.Tasks;
using MultiLevelCache.Interfaces;
using StackExchange.Redis;

namespace MultiLevelCache.Implementations
{
    /// <summary>
    /// Redis缓存实现
    /// </summary>
    public class RedisCache : ICache
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly JsonSerializerSettings _jsonSettings;

        public RedisCache(string connectionString)
        {
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _db = _redis.GetDatabase();
            _jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var value = await _db.StringGetAsync(key);
            if (value.IsNull)
            {
                return default;
            }

            return JsonConvert.DeserializeObject<T>(value!);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            var jsonValue = JsonConvert.SerializeObject(value, _jsonSettings);
            await _db.StringSetAsync(key, jsonValue, expiration);
        }

        public async Task RemoveAsync(string key)
        {
            await _db.KeyDeleteAsync(key);
        }

        public async Task<bool> ExistsAsync(string key)
        {
            return await _db.KeyExistsAsync(key);
        }

        public void Dispose()
        {
            _redis.Dispose();
        }
    }
}