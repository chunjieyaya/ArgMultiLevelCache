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
        private readonly ConcurrentDictionary<string, List<object>> _observers = new ConcurrentDictionary<string, List<object>>();
        private static string _connectionString = null;
        private static RedisEventManager _instance;
        private static readonly object _lock = new object();
        private readonly JsonSerializerSettings _jsonSettings;

        /// <summary>
        /// 获取RedisEventManager的单例实例
        /// </summary>
        public static RedisEventManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new RedisEventManager(_connectionString);
                        }
                    }
                }
                return _instance;
            }
        }

        private RedisEventManager(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return;
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _subscriber = _redis.GetSubscriber();
            _jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.All
            };
            LoadObservers();
            SubscribeToRedisChannels();
        }

        public static void Initialize(string redisConnectionString)
        {
            lock (_lock)
            {
                _connectionString = redisConnectionString;
                // 下次访问Instance时会用新连接字符串重新创建
                _instance = null; 
            }
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
                            var observerInstance = Activator.CreateInstance(type);
                            if (observerInstance != null)
                            {
                                // 反射调用 RegisterObserver<T>，保证分组正确
                                var method = typeof(RedisEventManager).GetMethod("RegisterObserver");
                                var genericMethod = method.MakeGenericMethod(interfaceType.GetGenericArguments()[0]);
                                genericMethod.Invoke(this, new object[] { observerInstance });
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
            _subscriber.Subscribe("rob:events:all", (channel, message) =>
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
                try
                {
                    var eventData = JsonConvert.DeserializeObject(eventDataJson, _jsonSettings);
                    if (eventData != null)
                    {
                        var sortedObservers = groupObservers.OrderBy(o => (int)o.GetType().GetProperty("Order").GetValue(o)).ToList();
                        foreach (var observer in sortedObservers)
                        {
                            try
                            {
                                var method = observer.GetType().GetMethod("HandleEventAsync");
                                if (method != null)
                                {
                                    var task = (Task)method.Invoke(observer, new object[] { eventData });
                                    await task.ConfigureAwait(false);
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
            await _subscriber.PublishAsync("rob:events:all", message);
        }

        /// <summary>
        /// 处理本地事件
        /// </summary>
        private async Task ProcessLocalEventAsync<T>(string groupName, T eventArgs) where T : class
        {
            if (_observers.TryGetValue(groupName, out var groupObservers))
            {
                // 按Order属性排序观察者（用反射获取Order）
                var sortedObservers = groupObservers.OrderBy(o => (int)o.GetType().GetProperty("Order").GetValue(o)).ToList();

                foreach (var observer in sortedObservers)
                {
                    try
                    {
                        // 用反射调用HandleEventAsync
                        var method = observer.GetType().GetMethod("HandleEventAsync");
                        if (method != null)
                        {
                            var task = (Task)method.Invoke(observer, new object[] { eventArgs });
                            await task.ConfigureAwait(false);
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
            _observers.AddOrUpdate(groupName,
                new List<object> { observer },
                (key, existingList) =>
                {
                    if (!existingList.Any(o => o.GetType() == observer.GetType()))
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