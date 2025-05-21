using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

namespace MultiLevelCache.PubSub
{
    /// <summary>
    /// 基于Redis的事件管理器，实现分布式事件发布订阅
    /// </summary>
    public class RedisEventManager
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ISubscriber _subscriber;
        private readonly ConcurrentDictionary<string, List<object>> _observers = new();
        private static string _connectionString = null;
        private static RedisEventManager _instance;
        private static readonly object _lock = new();
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly ILogger _logger;
        
        // 重试配置
        private readonly int _maxRetryCount = 3;
        private readonly int _retryDelayMs = 500;
        
        // 事件确认机制
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingConfirmations = new();

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
                        _instance ??= new RedisEventManager(_connectionString);
                    }
                }
                return _instance;
            }
        }

        private RedisEventManager(string connectionString, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return;
                
            _logger = logger ?? NullLogger.Instance;
            
            try
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.AbortOnConnectFail = false; // 连接失败时不中止
                
                _redis = ConnectionMultiplexer.Connect(options);
                _subscriber = _redis.GetSubscriber();
                
                // 优化序列化配置，减少数据传输量
                _jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    TypeNameHandling = TypeNameHandling.Auto, // 只在必要时包含类型信息
                    MaxDepth = 10, // 防止过深的对象图导致序列化问题
                    Formatting = Formatting.None // 不格式化JSON，减少数据量
                };
                
                // 注册Redis连接事件
                _redis.ConnectionRestored += (sender, e) => 
                    _logger.LogInformation("Redis连接已恢复: {EndPoint}", e.EndPoint);
                _redis.ConnectionFailed += (sender, e) => 
                    _logger.LogError("Redis连接失败: {EndPoint}, 原因: {Exception}", e.EndPoint, e.Exception);
                    
                LoadObservers();
                SubscribeToRedisChannels();
                
                _logger.LogInformation("Redis事件管理器已初始化");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化Redis事件管理器时出错");
                throw;
            }
        }

        public static void Initialize(string redisConnectionString, ILogger logger = null)
        {
            lock (_lock)
            {
                _connectionString = redisConnectionString;
                // 下次访问Instance时会用新连接字符串重新创建
                _instance = null;
                
                // 如果提供了logger，直接创建实例
                if (logger != null)
                {
                    _instance = new RedisEventManager(_connectionString, logger);
                }
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
                    _logger.LogError(ex, "加载观察者时出错");
                }
            }
        }

        private void SubscribeToRedisChannels()
        {
            try
            {
                // 订阅事件通道
                _subscriber.Subscribe("rob:events:all", (channel, message) =>
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        // 解析消息格式: {"GroupName":"xxx", "EventData":"json序列化的事件数据", "EventId":"唯一ID"}
                        var eventMessage = JsonConvert.DeserializeObject<EventMessage>(message);
                        if (eventMessage != null)
                        {
                            _logger.LogDebug("收到Redis事件: {GroupName}, EventId: {EventId}", 
                                eventMessage.GroupName, eventMessage.EventId);
                                
                            // 异步处理事件并等待完成
                            Task.Run(async () => 
                            {
                                try 
                                {
                                    await ProcessRedisEventAsync(eventMessage.GroupName, eventMessage.EventData, eventMessage.EventId);
                                    
                                    // 发送确认消息
                                    if (!string.IsNullOrEmpty(eventMessage.EventId))
                                    {
                                        await _subscriber.PublishAsync("rob:events:ack", eventMessage.EventId);
                                        _logger.LogDebug("已发送事件确认: {EventId}", eventMessage.EventId);
                                    }
                                    
                                    sw.Stop();
                                    _logger.LogInformation("处理Redis事件完成: {GroupName}, EventId: {EventId}, 耗时: {ElapsedMs}ms", 
                                        eventMessage.GroupName, eventMessage.EventId, sw.ElapsedMilliseconds);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "处理Redis事件失败: {GroupName}, EventId: {EventId}", 
                                        eventMessage.GroupName, eventMessage.EventId);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "解析Redis事件消息时出错");
                    }
                });
                
                // 订阅确认通道
                _subscriber.Subscribe("rob:events:ack", (channel, message) =>
                {
                    string eventId = message.ToString();
                    if (!string.IsNullOrEmpty(eventId) && _pendingConfirmations.TryRemove(eventId, out var tcs))
                    {
                        tcs.TrySetResult(true);
                        _logger.LogDebug("收到事件确认: {EventId}", eventId);
                    }
                });
                
                _logger.LogInformation("已订阅Redis事件通道");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订阅Redis事件通道时出错");
                throw;
            }
        }

        private async Task ProcessRedisEventAsync(string groupName, string eventDataJson, string eventId = null)
        {
            if (!_observers.TryGetValue(groupName, out var groupObservers) || groupObservers.Count == 0)
            {
                _logger.LogWarning("没有找到事件组的观察者: {GroupName}, EventId: {EventId}", groupName, eventId);
                return;
            }
            
            try
            {
                var eventData = JsonConvert.DeserializeObject(eventDataJson, _jsonSettings);
                if (eventData == null)
                {
                    _logger.LogError("反序列化事件数据失败: {GroupName}, EventId: {EventId}", groupName, eventId);
                    return;
                }
                
                // 按Order属性排序观察者
                var sortedObservers = groupObservers
                    .OrderBy(o => (int)o.GetType().GetProperty("Order").GetValue(o))
                    .ToList();
                
                _logger.LogDebug("开始处理事件: {GroupName}, 观察者数量: {ObserverCount}, EventId: {EventId}", 
                    groupName, sortedObservers.Count, eventId);
                
                foreach (var observer in sortedObservers)
                {
                    var observerType = observer.GetType().Name;
                    var method = observer.GetType().GetMethod("HandleEventAsync");
                    if (method == null)
                    {
                        _logger.LogWarning("观察者未实现HandleEventAsync方法: {ObserverType}", observerType);
                        continue;
                    }
                    
                    // 实现重试机制
                    int retryCount = 0;
                    bool success = false;
                    Exception lastException = null;
                    
                    while (!success && retryCount <= _maxRetryCount)
                    {
                        try
                        {
                            if (retryCount > 0)
                            {
                                _logger.LogWarning("重试处理事件 (第{RetryCount}次): {GroupName}, 观察者: {ObserverType}, EventId: {EventId}", 
                                    retryCount, groupName, observerType, eventId);
                                await Task.Delay(_retryDelayMs * retryCount);
                            }
                            
                            var sw = Stopwatch.StartNew();
                            var task = (Task)method.Invoke(observer, [eventData]);
                            await task.ConfigureAwait(false);
                            sw.Stop();
                            
                            success = true;
                            _logger.LogDebug("观察者处理事件成功: {ObserverType}, 耗时: {ElapsedMs}ms, EventId: {EventId}", 
                                observerType, sw.ElapsedMilliseconds, eventId);
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            retryCount++;
                            
                            if (retryCount > _maxRetryCount)
                            {
                                _logger.LogError(ex, "观察者处理事件失败(已达最大重试次数): {ObserverType}, {GroupName}, EventId: {EventId}", 
                                    observerType, groupName, eventId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理事件时出错: {GroupName}, EventId: {EventId}", groupName, eventId);
                throw;
            }
        }

        /// <summary>
        /// 通过Redis发布事件
        /// </summary>
        /// <typeparam name="T">事件参数类型</typeparam>
        /// <param name="eventArgs">事件参数</param>
        /// <param name="waitForConfirmation">是否等待事件确认</param>
        /// <param name="timeoutMs">确认超时时间(毫秒)</param>
        /// <returns>表示异步操作的任务，如果waitForConfirmation为true，则返回是否成功确认</returns>
        public async Task<bool> PublishEventAsync<T>(T eventArgs, bool waitForConfirmation = false, int timeoutMs = 5000) where T : class
        {
            try
            {
                var sw = Stopwatch.StartNew();
                string groupName = typeof(T).Name;
                string eventId = waitForConfirmation ? Guid.NewGuid().ToString("N") : null;
                
                _logger.LogDebug("开始发布事件: {GroupName}, EventId: {EventId}, WaitForConfirmation: {WaitForConfirmation}", 
                    groupName, eventId, waitForConfirmation);
                
                // 本地处理事件
                await ProcessLocalEventAsync(groupName, eventArgs);
                
                // 如果需要确认，创建等待任务
                TaskCompletionSource<bool> confirmationTask = null;
                if (waitForConfirmation && !string.IsNullOrEmpty(eventId))
                {
                    confirmationTask = new TaskCompletionSource<bool>();
                    _pendingConfirmations[eventId] = confirmationTask;
                }
                
                // 通过Redis发布事件
                var eventMessage = new EventMessage
                {
                    GroupName = groupName,
                    EventData = JsonConvert.SerializeObject(eventArgs, _jsonSettings),
                    EventId = eventId
                };
                
                string message = JsonConvert.SerializeObject(eventMessage);
                await _subscriber.PublishAsync("rob:events:all", message);
                
                // 等待确认
                bool confirmed = true;
                if (waitForConfirmation && confirmationTask != null)
                {
                    using var cts = new CancellationTokenSource(timeoutMs);
                    cts.Token.Register(() => confirmationTask.TrySetResult(false));
                    
                    confirmed = await confirmationTask.Task;
                    if (!confirmed)
                    {
                        _logger.LogWarning("事件确认超时: {GroupName}, EventId: {EventId}, 超时: {TimeoutMs}ms", 
                            groupName, eventId, timeoutMs);
                    }
                    
                    // 清理
                    _pendingConfirmations.TryRemove(eventId, out _);
                }
                
                sw.Stop();
                _logger.LogInformation("事件发布完成: {GroupName}, EventId: {EventId}, 耗时: {ElapsedMs}ms, 确认状态: {Confirmed}", 
                    groupName, eventId, sw.ElapsedMilliseconds, confirmed);
                    
                return confirmed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布事件时出错: {EventType}", typeof(T).Name);
                throw;
            }
        }

        /// <summary>
        /// 处理本地事件
        /// </summary>
        private async Task ProcessLocalEventAsync<T>(string groupName, T eventArgs) where T : class
        {
            if (!_observers.TryGetValue(groupName, out var groupObservers) || groupObservers.Count == 0)
            {
                _logger.LogDebug("本地没有找到事件组的观察者: {GroupName}", groupName);
                return;
            }
            
            try
            {
                var sw = Stopwatch.StartNew();
                // 按Order属性排序观察者
                var sortedObservers = groupObservers
                    .OrderBy(o => (int)o.GetType().GetProperty("Order").GetValue(o))
                    .ToList();

                _logger.LogDebug("开始处理本地事件: {GroupName}, 观察者数量: {ObserverCount}", 
                    groupName, sortedObservers.Count);
                    
                foreach (var observer in sortedObservers)
                {
                    var observerType = observer.GetType().Name;
                    try
                    {
                        // 用反射调用HandleEventAsync
                        var method = observer.GetType().GetMethod("HandleEventAsync");
                        if (method != null)
                        {
                            var innerSw = Stopwatch.StartNew();
                            var task = (Task)method.Invoke(observer, new object[] { eventArgs });
                            await task.ConfigureAwait(false);
                            innerSw.Stop();
                            
                            _logger.LogDebug("本地观察者处理事件成功: {ObserverType}, 耗时: {ElapsedMs}ms", 
                                observerType, innerSw.ElapsedMilliseconds);
                        }
                        else
                        {
                            _logger.LogWarning("本地观察者未实现HandleEventAsync方法: {ObserverType}", observerType);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "本地观察者处理事件时出错: {ObserverType}, {GroupName}", 
                            observerType, groupName);
                    }
                }
                
                sw.Stop();
                _logger.LogDebug("本地事件处理完成: {GroupName}, 耗时: {ElapsedMs}ms", 
                    groupName, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理本地事件时出错: {GroupName}", groupName);
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
                [observer],
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
            /// <summary>
            /// 事件组名称
            /// </summary>
            public string GroupName { get; set; }
            
            /// <summary>
            /// 序列化后的事件数据
            /// </summary>
            public string EventData { get; set; }
            
            /// <summary>
            /// 事件唯一标识，用于确认机制
            /// </summary>
            public string EventId { get; set; }
        }
    }
}