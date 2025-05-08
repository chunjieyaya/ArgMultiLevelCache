using System;
using System.Threading.Tasks;

namespace MultiLevelCache.Interfaces
{
    /// <summary>
    /// 多级缓存管理器接口
    /// </summary>
    public interface IMultiLevelCache
    {
        /// <summary>
        /// 添加缓存层级
        /// </summary>
        /// <param name="cache">缓存实现</param>
        /// <param name="level">缓存级别，数字越小优先级越高</param>
        void AddCache(ICache cache, int level);

        /// <summary>
        /// 获取缓存项，按照缓存级别依次查找
        /// </summary>
        /// <typeparam name="T">缓存项类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="dataFetcher">当缓存中不存在数据时，用于获取数据的委托</param>
        /// <returns>缓存值，如果不存在则通过dataFetcher获取</returns>
        Task<T?> GetAsync<T>(string key, Func<Task<T?>>? dataFetcher = null);

        /// <summary>
        /// 设置缓存项，将值写入所有缓存层
        /// </summary>
        /// <typeparam name="T">缓存项类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="value">缓存值</param>
        /// <param name="expiration">过期时间</param>
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);

        /// <summary>
        /// 从所有缓存层中移除缓存项
        /// </summary>
        /// <param name="key">缓存键</param>
        Task RemoveAsync(string key);

        /// <summary>
        /// 检查缓存项是否存在于任意缓存层中
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>如果存在返回true，否则返回false</returns>
        Task<bool> ExistsAsync(string key);
    }
}