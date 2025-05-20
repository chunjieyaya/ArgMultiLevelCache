using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MultiLevelCache.Implementations;
using MultiLevelCache.Interfaces;
using MultiLevelCache.Options;
using MultiLevelCache.PubSub; // Added for EventManager

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
        /// Adds event manager services to the specified IServiceCollection.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
        public static IServiceCollection AddEventServices(this IServiceCollection services)
        {
            // Register EventManager as a singleton. 
            // The constructor of EventManager calls LoadObservers, ensuring they are loaded at startup.
            services.AddSingleton<EventManager>(sp => EventManager.Instance);
            return services;
        }
    }
}