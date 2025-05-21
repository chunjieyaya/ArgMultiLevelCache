using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiLevelCache.Implementations;
using MultiLevelCache.Interfaces;
using MultiLevelCache.Options;
using MultiLevelCache.PubSub;
using System;

namespace MultiLevelCache.Extensions
{
    /// <summary>
    /// IServiceCollection扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加多级缓存服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置选项</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddMultiLevelCache(this IServiceCollection services, Action<MultiLevelCacheOptions> configureOptions)
        {
            // 注册配置选项
            services.Configure(configureOptions);

            // 注册多级缓存服务
            services.AddSingleton<IMultiLevelCache>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<MultiLevelCacheOptions>>().Value;
                var cacheManager = new MultiLevelCacheManager(options);
                
                // 添加配置的缓存层级
                foreach (var cacheLevel in options.CacheLevels)
                {
                    cacheManager.AddCache(cacheLevel.Value, cacheLevel.Key);
                }

                return cacheManager;
            });

            return services;
        }

        /// <summary>
        /// 添加Redis事件服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="redisConnectionString">Redis连接字符串</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddRedisEventServices(this IServiceCollection services, string redisConnectionString)
        {
            // 注册Redis事件管理器为单例服务
            services.AddSingleton<RedisEventManager>(sp =>
            {
                // 获取日志服务
                var logger = sp.GetService<ILogger<RedisEventManager>>();
                // 使用日志服务初始化Redis事件管理器
                RedisEventManager.Initialize(redisConnectionString, logger);
                return RedisEventManager.Instance;
            });

            return services;
        }

        //非redis事件服务
        public static IServiceCollection AddEventServices(this IServiceCollection services)
        {
            services.AddSingleton<EventManager>(sp =>
            {
                // 获取日志服务
                var logger = sp.GetService<ILogger<EventManager>>();
                // 使用日志服务初始化Redis事件管理器
                EventManager.Initialize(logger);
                return EventManager.Instance;
            });
            return services;
        }
    }
}