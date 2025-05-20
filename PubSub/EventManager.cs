using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace MultiLevelCache.PubSub
{
    /// <summary>
    /// Manages event observers and dispatches events.
    /// </summary>
    public class EventManager
    {
        private readonly ConcurrentDictionary<string, List<IEventObserver<object>>> _observers = new ConcurrentDictionary<string, List<IEventObserver<object>>>();
        private static readonly Lazy<EventManager> _instance = new(() => new EventManager());

        /// <summary>
        /// Gets the singleton instance of the EventManager.
        /// </summary>
        public static EventManager Instance => _instance.Value;

        private EventManager()
        {
            LoadObservers();
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
                        .Where(p => observerType.IsAssignableFrom(p) && p.IsClass && !p.IsAbstract);

                    foreach (var type in types)
                    {
                        // 分组直接用泛型参数类型名
                        var interfaceType = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == observerType);
                        string groupName = interfaceType?.GetGenericArguments()[0].Name ?? "Default";

                        if (!_observers.ContainsKey(groupName))
                        {
                            _observers[groupName] = new List<IEventObserver<object>>();
                        }

                        var observerInstance = Activator.CreateInstance(type) as IEventObserver<object>;
                        if (observerInstance != null)
                        {
                            _observers[groupName].Add(observerInstance);
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Console.WriteLine($"Error loading types from assembly {assembly.FullName}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing assembly {assembly.FullName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 向指定组(如果未指定，则为默认组)中的所有观察者发布事件。
        /// </summary>
        /// <param name="eventArgs">事件参数(匿名对象)。</param>
        /// <param name="groupName">事件组名称。默认为“默认”。</param>
        public async Task PublishEventAsync<T>(T eventArgs)
        {
            string groupName = typeof(T).Name;
            if (_observers.TryGetValue(groupName, out var groupObservers))
            {
                // 按 Order 属性排序观察者
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
                        else
                        {
                             // 如果转换失败，记录警告或错误
                             Console.WriteLine($"Warning: Observer {observer.GetType().Name} is not compatible with event type {typeof(T).Name}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling event in observer {observer.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        // 使用并发字典进行线程安全的观察者注册
        // 修复类型转换错误
        public void RegisterObserver<T>(IEventObserver<T> observer) where T : class
        {
            var groupName = typeof(T).Name;
            _observers.AddOrUpdate(groupName, new List<IEventObserver<object>> { observer as IEventObserver<object> }, (key, existingList) =>
            {
                existingList.Add(observer as IEventObserver<object>);
                return existingList;
            });
        }
    }
}