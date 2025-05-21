using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MultiLevelCache.PubSub
{
    /// <summary>
    /// Manages event observers and dispatches events.
    /// </summary>
    public class EventManager
    {
        private readonly ConcurrentDictionary<string, List<object>> _observers = new ConcurrentDictionary<string, List<object>>();
        private static readonly Lazy<EventManager> _instance = new(() => new EventManager());
        private static ILogger<EventManager> _logger;

        /// <summary>
        /// Gets the singleton instance of the EventManager.
        /// </summary>
        public static EventManager Instance => _instance.Value;

        private EventManager()
        {
            LoadObservers();
        }

        /// <summary>
        /// Initializes the EventManager with a logger
        /// </summary>
        public static void Initialize(ILogger<EventManager> logger)
        {
            _logger = logger;
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
                                var method = typeof(EventManager).GetMethod("RegisterObserver");
                                var genericMethod = method.MakeGenericMethod(interfaceType.GetGenericArguments()[0]);
                                genericMethod.Invoke(this, new object[] { observerInstance });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "加载观察者时出错");
                }
            }
        }

        /// <summary>
        /// 向指定组(如果未指定，则为默认组)中的所有观察者发布事件。
        /// </summary>
        /// <param name="eventArgs">事件参数(匿名对象)。</param>
        /// <param name="groupName">事件组名称。默认为"默认"。</param>
        public async Task PublishEventAsync<T>(T eventArgs)
        {
            string groupName = typeof(T).Name;
            if (_observers.TryGetValue(groupName, out var groupObservers))
            {
                // 按 Order 属性排序观察者（用反射获取Order）
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
                        _logger?.LogError(ex, "Error handling event in observer {ObserverName}", observer.GetType().Name);
                    }
                }
            }
        }

        // 使用并发字典进行线程安全的观察者注册
        // 修复类型转换错误
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
    }
}