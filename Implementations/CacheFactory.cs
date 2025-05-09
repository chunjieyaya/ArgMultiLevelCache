using System;
using MultiLevelCache.Interfaces;
using MultiLevelCache.Options;

namespace MultiLevelCache.Implementations
{
    /// <summary>
    /// 缓存工厂类，提供全局静态访问点
    /// </summary>
    public static class CacheFactory
    {
        private static readonly Lazy<MultiLevelCacheManager> Instance = new Lazy<MultiLevelCacheManager>(
            () => new MultiLevelCacheManager(new MultiLevelCacheOptions
            {
                DefaultExpiration = TimeSpan.FromMinutes(30)
            }),
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 获取缓存管理器实例
        /// </summary>
        public static IMultiLevelCache Default => Instance.Value;

        /// <summary>
        /// 初始化默认缓存配置
        /// </summary>
        /// <param name="configure">配置回调方法</param>
        public static void Configure(Action<MultiLevelCacheOptions> configure)
        {
            if (Instance.IsValueCreated)
            {
                throw new InvalidOperationException("缓存管理器已经被初始化，无法重新配置");
            }

            var options = new MultiLevelCacheOptions
            {
                DefaultExpiration = TimeSpan.FromMinutes(30)
            };
            configure(options);

            // 强制初始化实例
            var manager = Instance.Value;

            // 配置缓存层级
            if (options.CacheLevels != null)
            {
                foreach (var level in options.CacheLevels)
                {
                    manager.AddCache(level.Value, level.Key);
                }
            }
        }
    }
}