using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace MultiLevelCache.PubSub
{
    /// <summary>
    /// 基于Redis的事件管理器，实现分布式事件发布订阅
    /// </summary>
    public class RedisEventManager
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ISubscriber _subscriber;
        private readonly ConcurrentDictionary<string, List<IEventObserver<object>>> _observers = new ConcurrentDictionary<string, List<IEventObserver<object>>>();
        private static readonly Lazy<RedisEventManager> _instance = new Lazy<RedisEventManager>(() => new RedisEventManager());
        private readonly JsonSerializerSettings _jsonSettings;

        /// <summary>
        /// 获取RedisEventManager的单例实例
        /// </summary>
        public static RedisEventManager Instance => _instance.Value;

        private RedisEventManager()
        {
            // 默认连接本地Redis
            _redis = ConnectionMultiplexer.Connect("localhost:6379");
            _subscriber = _redis.GetSubscriber();
            _jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.All // 包含类型信息以便正确反序列化
            };

            LoadObservers();
            SubscribeToRedisChannels();
        }

        /// <summary>
        /// 使用指定的Redis连接字符串初始化RedisEventManager
        /// </summary>
        /// <param name="connectionString">Redis连接字符串</param>
        public static void Initialize(string connectionString)
        {
            if (_instance.IsValueCreated)
            {
                throw new InvalidOperationException("RedisEventManager已经被初始化，无法重新配置");
            }

            // 强制初始化单例实例
            var instance = _instance.Value;
        }

        private void LoadObservers()
        {
            var observerType = typeof(IEventObserver<>);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(p => p.IsClass && !p.IsAbstract && p.GetInterfaces().Any(i => 
                            i.IsGenericType && i.GetGenericTypeDefinition() == observerType));

                    foreach (var type in types)
                    {
                        var interfaceType = type.GetInterfaces().FirstOrDefault(i => 
                            i.IsGenericType && i.GetGenericTypeDefinition() == observerType);
                        
                        if (interfaceType != null)
                        {
                            string groupName = interfaceType.GetGenericArguments()[0].Name;

                            var observerInstance = Activator.CreateInstance(type);
                            if (observerInstance != null)
                            {
                                RegisterObserver(groupName, observerInstance as IEventObserver<object>);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载观察者时出错: {ex.Message}");
                }
            }
        }

        private void SubscribeToRedisChannels()
        {
            // 订阅通用事件通道
            _subscriber.Subscribe("events:all", (channel, message) =>
            {
                try
                {
                    // 解析消息格式: {"GroupName":"xxx", "EventData":"json序列化的事件数据"}
                    var eventMessage = JsonConvert.DeserializeObject<EventMessage>(message);
                    if (eventMessage != null)
                    {
                        // 异步处理事件，但不等待完成
                        _ = ProcessRedisEventAsync(eventMessage.GroupName, eventMessage.EventData);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理Redis事件时出错: {ex.Message}");
                }
            });
        }

        private async Task ProcessRedisEventAsync(string groupName, string eventDataJson)
        {
            if (_observers.TryGetValue(groupName, out var groupObservers))
            {
                // 尝试反序列化事件数据
                try
                {
                    // 使用TypeNameHandling.All可以正确反序列化对象类型
                    var eventData = JsonConvert.DeserializeObject(eventDataJson, _jsonSettings);
                    if (eventData != null)
                    {
                        // 按Order属性排序观察者
                        var sortedObservers = groupObservers.OrderBy(o => o.Order).ToList();

                        foreach (var observer in sortedObservers)
                        {
                            try
                            {
                                // 使用反射调用正确类型的HandleEventAsync方法
                                var observerType = observer.GetType();
                                var interfaceType = observerType.GetInterfaces().FirstOrDefault(i => 
                                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventObserver<>));
                                
                                if (interfaceType != null)
                                {
                                    var method = interfaceType.GetMethod("HandleEventAsync");
                                    if (method != null)
                                    {
                                        var task = (Task)method.Invoke(observer, new[] { eventData });
                                        await task.ConfigureAwait(false);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"观察者处理事件时出错: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"反序列化事件数据时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 通过Redis发布事件
        /// </summary>
        /// <typeparam name="T">事件参数类型</typeparam>
        /// <param name="eventArgs">事件参数</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task PublishEventAsync<T>(T eventArgs) where T : class
        {
            string groupName = typeof(T).Name;
            
            // 本地处理事件
            await ProcessLocalEventAsync(groupName, eventArgs);
            
            // 通过Redis发布事件
            var eventMessage = new EventMessage
            {
                GroupName = groupName,
                EventData = JsonConvert.SerializeObject(eventArgs, _jsonSettings)
            };
            
            string message = JsonConvert.SerializeObject(eventMessage);
            await _subscriber.PublishAsync("events:all", message);
        }

        /// <summary>
        /// 处理本地事件
        /// </summary>
        private async Task ProcessLocalEventAsync<T>(string groupName, T eventArgs) where T : class
        {
            if (_observers.TryGetValue(groupName, out var groupObservers))
            {
                // 按Order属性排序观察者
                var sortedObservers = groupObservers.OrderBy(o => o.Order).ToList();

                foreach (var observer in sortedObservers)
                {
                    try
                    {
                        // 尝试将观察者转换为正确的泛型类型并处理事件
                        if (observer is IEventObserver<T> typedObserver)
                        {
                            await typedObserver.HandleEventAsync(eventArgs);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"本地处理事件时出错: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 注册观察者
        /// </summary>
        /// <typeparam name="T">事件参数类型</typeparam>
        /// <param name="observer">观察者实例</param>
        public void RegisterObserver<T>(IEventObserver<T> observer) where T : class
        {
            var groupName = typeof(T).Name;
            RegisterObserver(groupName, observer as IEventObserver<object>);
        }

        private void RegisterObserver(string groupName, IEventObserver<object> observer)
        {
            if (observer == null) return;

            _observers.AddOrUpdate(groupName, 
                new List<IEventObserver<object>> { observer }, 
                (key, existingList) =>
                {
                    existingList.Add(observer);
                    return existingList;
                });
        }

        /// <summary>
        /// 事件消息包装类，用于Redis发布订阅
        /// </summary>
        private class EventMessage
        {
            public string GroupName { get; set; }
            public string EventData { get; set; }
        }
    }
}