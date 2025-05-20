using System;
using Microsoft.Extensions.DependencyInjection;
using MultiLevelCache.PubSub;

namespace MultiLevelCache.Extensions
{
    /// <summary>
    /// Redis事件服务的扩展方法
    /// </summary>
    public static class RedisEventServiceExtensions
    {
        /// <summary>
        /// 添加Redis事件服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="redisConnectionString">Redis连接字符串</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddRedisEventServices(this IServiceCollection services, string redisConnectionString)
        {
            // 初始化Redis事件管理器
            RedisEventManager.Initialize(redisConnectionString);
            
            // 注册Redis事件管理器为单例服务
            services.AddSingleton<RedisEventManager>(sp => RedisEventManager.Instance);
            
            return services;
        }
    }
}