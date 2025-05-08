using System;
using System.Threading.Tasks;

namespace MultiLevelCache.Interfaces
{
    /// <summary>
    /// 定义缓存接口的基本操作
    /// </summary>
    public interface ICache
    {
        /// <summary>
        /// 获取缓存项
        /// </summary>
        /// <typeparam name="T">缓存项类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <returns>缓存值，如果不存在则返回默认值</returns>
        Task<T?> GetAsync<T>(string key);

        /// <summary>
        /// 设置缓存项
        /// </summary>
        /// <typeparam name="T">缓存项类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="value">缓存值</param>
        /// <param name="expiration">过期时间</param>
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);

        /// <summary>
        /// 移除缓存项
        /// </summary>
        /// <param name="key">缓存键</param>
        Task RemoveAsync(string key);

        /// <summary>
        /// 检查缓存项是否存在
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>如果存在返回true，否则返回false</returns>
        Task<bool> ExistsAsync(string key);
    }
}