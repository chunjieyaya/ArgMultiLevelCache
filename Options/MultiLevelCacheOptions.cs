using System;
using System.Collections.Generic;
using MultiLevelCache.Interfaces;

namespace MultiLevelCache.Options
{
    /// <summary>
    /// 多级缓存配置选项
    /// </summary>
    public class MultiLevelCacheOptions
    {
        /// <summary>
        /// 默认过期时间
        /// </summary>
        public TimeSpan? DefaultExpiration { get; set; }

        /// <summary>
        /// 缓存层级配置
        /// </summary>
        public Dictionary<int, ICache> CacheLevels { get; set; } = new Dictionary<int, ICache>();

        /// <summary>
        /// 添加缓存层级
        /// </summary>
        /// <param name="cache">缓存实现</param>
        /// <param name="level">缓存级别，数字越小优先级越高</param>
        public void AddCacheLevel(ICache cache, int level)
        {
            if (CacheLevels.ContainsKey(level))
            {
                throw new ArgumentException($"缓存级别 {level} 已经存在");
            }
            CacheLevels.Add(level, cache);
        }
    }
}