using System;
using System.Collections.Generic;
using MultiLevelCache.Interfaces;

namespace MultiLevelCache.Options
{
    public class MultiLevelCacheOptions
    {
        public TimeSpan? DefaultExpiration { get; set; }

        /// <summary>
        /// 缓存层级配置字典
        /// </summary>
        public Dictionary<int, ICache> CacheLevels { get; set; } = new Dictionary<int, ICache>();

        /// <summary>
        /// 添加缓存层级
        /// </summary>
        /// <param name="cache">缓存实现</param>
        /// <param name="level">优先级（数字越小优先级越高）</param>
        public void AddCacheLevel(ICache cache, int level)
        {
            CacheLevels[level] = cache;
        }
    }


}