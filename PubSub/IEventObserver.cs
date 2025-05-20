using System.Threading.Tasks;

namespace MultiLevelCache.PubSub
{
    /// <summary>
    /// 定义事件观察器的接口。
    /// </summary>
    public interface IEventObserver<T>
    {
        /// <summary>
        /// 获取观察者的执行顺序。
        /// </summary>
        int Order { get; }

        /// <summary>
        /// 异步处理事件。
        /// </summary>
        /// <param name="eventArgs">事件参数，通常是匿名对象。</param>
        /// <returns>表示异步操作的任务。</returns>
        Task HandleEventAsync(T eventArgs);
    }
}