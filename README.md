# ArgMultiLevelCache 多级缓存类库

## 项目简介
ArgMultiLevelCache 是一个灵活的.NET多级缓存类库，支持多种缓存策略的组合使用。通过分层缓存机制，可以有效地平衡性能和资源使用。

## 主要功能
- 多级缓存管理，支持内存缓存和Redis缓存
- 灵活的缓存策略配置
- 基于事件的发布订阅机制，支持本地和分布式事件
- 支持.NET Framework和.NET Core平台

## 使用方法

### 包导入
```csharp
dotnet add package ArgMultiLevelCache
```

### 平台使用说明

#### .NET Core/.NET 5+
在.NET Core和.NET 5+平台中，推荐使用依赖注入方式配置和使用缓存：

```csharp
// 在Startup.cs中配置服务
public void ConfigureServices(IServiceCollection services)
{
    services.AddMultiLevelCache(options =>
    {
        options.DefaultExpiration = TimeSpan.FromMinutes(30);
        options.AddCacheLevel(new MemoryCache(), 1);
        options.AddCacheLevel(new RedisCache("localhost:6379"), 2);
    });
    
    // 添加事件服务
    services.AddEventServices();
    
    // 或者使用Redis事件服务（分布式事件）
    services.AddRedisEventServices("localhost:6379");
}

// 在服务中使用
public class UserService
{
    private readonly IMultiLevelCache _cache;
    private readonly RedisEventManager _eventManager; // 使用Redis事件管理器

    public UserService(IMultiLevelCache cache, RedisEventManager eventManager)
    {
        _cache = cache;
        _eventManager = eventManager;
    }

    public async Task<UserData> GetUserAsync(string userId)
    {
        return await _cache.GetAsync<UserData>(
            $"user:{userId}",
            async () => await _userRepository.GetByIdAsync(userId)
        );
    }
    
    public async Task InvalidateUserCacheAsync(string userId, string reason)
    {
        // 发布缓存失效事件
        await _eventManager.PublishEventAsync(new CacheInvalidatedEventArgs
        {
            Key = $"user:{userId}",
            InvalidatedTime = DateTime.UtcNow,
            Reason = reason
        });
        
        // 直接从缓存中移除
        await _cache.RemoveAsync($"user:{userId}");
    }
}
```

#### .NET Framework
在.NET Framework平台中，推荐使用静态工厂类进行全局访问，确保线程安全：

```csharp
// 在应用程序启动时配置缓存（如Global.asax.cs中）
CacheFactory.Configure(options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(30);
    options.AddCacheLevel(new MemoryCache(), 1);                // 一级缓存：内存缓存
    options.AddCacheLevel(new RedisCache("localhost:6379"), 2); // 二级缓存：Redis缓存
});

// 初始化Redis事件管理器
RedisEventManager.Initialize("localhost:6379");

// 在任意位置使用缓存（线程安全）
public class UserService
{
    public async Task<UserData> GetUserAsync(string userId)
    {
        return await CacheFactory.Default.GetAsync<UserData>(
            $"user:{userId}",
            async () => await GetUserFromDatabaseAsync(userId)
        );
    }

    private async Task<UserData> GetUserFromDatabaseAsync(string userId)
    {
        // 从数据库获取用户数据的实现
        return await Task.FromResult(new UserData());
    }
    
    public async Task InvalidateUserCacheAsync(string userId)
    {
        // 发布缓存失效事件
        await RedisEventManager.Instance.PublishEventAsync(new CacheInvalidatedEventArgs
        {
            Key = $"user:{userId}",
            InvalidatedTime = DateTime.UtcNow,
            Reason = "用户数据更新"
        });
    }
}

// 在WebForm页面中使用
public partial class UserProfile : System.Web.UI.Page
{
    private UserService _userService = new UserService();

    protected async void Page_Load(object sender, EventArgs e)
    {
        if (!IsPostBack)
        {
            await LoadUserDataAsync();
        }
    }

    private async Task LoadUserDataAsync()
    {
        var userId = Request.QueryString["userId"];
        var userData = await _userService.GetUserAsync(userId);
        // 使用userData更新页面控件
    }
}
```

## 事件发布订阅

### 本地事件

本地事件使用 `EventManager` 类进行管理，适用于单实例应用：

```csharp
// 定义事件参数类
public class UserUpdatedEventArgs
{
    public string UserId { get; set; }
    public DateTime UpdateTime { get; set; }
}

// 实现事件观察者
public class UserCacheInvalidator : IEventObserver<UserUpdatedEventArgs>
{
    // 定义观察者执行顺序
    public int Order => 1;
    
    // 实现事件处理方法
    public async Task HandleEventAsync(UserUpdatedEventArgs eventArgs)
    {
        // 处理用户更新事件，例如清除缓存
        Console.WriteLine($"用户 {eventArgs.UserId} 在 {eventArgs.UpdateTime} 被更新");
        await Task.CompletedTask;
    }
}

// 发布事件
await EventManager.Instance.PublishEventAsync(new UserUpdatedEventArgs
{
    UserId = "123",
    UpdateTime = DateTime.UtcNow
});
```

### 分布式事件（Redis）

分布式事件使用 `RedisEventManager` 类进行管理，适用于多实例分布式应用：

```csharp
// 定义事件参数类（与本地事件相同）
public class UserUpdatedEventArgs
{
    public string UserId { get; set; }
    public DateTime UpdateTime { get; set; }
}

// 实现事件观察者（与本地事件相同）
public class UserCacheInvalidator : IEventObserver<UserUpdatedEventArgs>
{
    public int Order => 1;
    
    public async Task HandleEventAsync(UserUpdatedEventArgs eventArgs)
    {
        // 处理用户更新事件
        Console.WriteLine($"用户 {eventArgs.UserId} 在 {eventArgs.UpdateTime} 被更新");
        await Task.CompletedTask;
    }
}

// 发布分布式事件
await RedisEventManager.Instance.PublishEventAsync(new UserUpdatedEventArgs
{
    UserId = "123",
    UpdateTime = DateTime.UtcNow
});
```

### 自动确认机制

`RedisEventManager` 支持事件的自动确认机制，确保事件被正确处理：

```csharp
// 在Startup.cs中配置服务
public void ConfigureServices(IServiceCollection services)
{
    // 添加Redis事件服务，启用自动确认
    services.AddRedisEventServices("localhost:6379", enableAutoAck: true);
}

// 实现支持自动确认的观察者
public class AutoAckObserver : IEventObserver<SomeEventArgs>
{
    public int Order => 1;
    
    public async Task<bool> HandleEventAsync(SomeEventArgs eventArgs)
    {
        try {
            // 处理事件逻辑
            await DoSomethingAsync(eventArgs);
            
            // 返回true表示处理成功，事件将被确认
            return true;
        }
        catch {
            // 返回false表示处理失败，事件不会被确认，将被重新处理
            return false;
        }
    }
}
```

## 高级功能

### 自定义序列化

可以为Redis缓存和事件管理器配置自定义的序列化设置：

```csharp
// 配置自定义序列化设置
var jsonSettings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.All,
    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    NullValueHandling = NullValueHandling.Ignore
};

// 使用自定义序列化设置初始化Redis事件管理器
RedisEventManager.Initialize("localhost:6379", jsonSettings);
```

### 事件重试机制

`RedisEventManager` 支持事件处理失败时的重试机制：

```csharp
// 在Startup.cs中配置服务
public void ConfigureServices(IServiceCollection services)
{
    // 添加Redis事件服务，配置重试策略
    services.AddRedisEventServices(options => {
        options.ConnectionString = "localhost:6379";
        options.EnableAutoAck = true;
        options.MaxRetryCount = 3;
        options.RetryDelayMs = 1000; // 1秒后重试
    });
}
```

## 贡献

欢迎提交问题和贡献代码，请遵循项目的贡献指南。
