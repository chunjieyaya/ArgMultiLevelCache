# MultiLevelCache 多级缓存类库

## 项目简介
MultiLevelCache 是一个灵活的.NET多级缓存类库，支持多种缓存策略的组合使用。通过分层缓存机制，可以有效地平衡性能和资源使用。

## 主要特性
- 支持多级缓存策略，自动按优先级查找和同步数据
- 灵活的缓存层级配置，支持自定义缓存实现
- 内置内存缓存和Redis缓存实现
- 异步操作支持，提供完整的异步API
- 支持数据源回退和自动缓存填充
- 可扩展的接口设计，易于集成和扩展

## 工作原理
1. **多级缓存查询**：按照缓存级别优先级（数字越小优先级越高）依次查找数据
2. **自动同步机制**：当在高优先级缓存中找到数据时，自动同步到低优先级缓存
3. **数据源回退**：当所有缓存层都未命中时，支持通过委托从数据源获取数据
4. **统一过期策略**：支持为不同缓存层设置统一的过期时间

## 使用方法

### 依赖注入配置
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
}

// 在服务中使用
public class UserService
{
    private readonly IMultiLevelCache _cache;

    public UserService(IMultiLevelCache cache)
    {
        _cache = cache;
    }

    public async Task<UserData> GetUserAsync(string userId)
    {
        return await _cache.GetAsync<UserData>(
            $"user:{userId}",
            async () => await _userRepository.GetByIdAsync(userId)
        );
    }
}
```

### 基本用法
```csharp
// 创建多级缓存管理器
var options = new MultiLevelCacheOptions
{
    DefaultExpiration = TimeSpan.FromMinutes(30)
};
var cacheManager = new MultiLevelCacheManager(options);

// 添加内存缓存作为一级缓存
var memoryCache = new MemoryCache();
cacheManager.AddCache(memoryCache, 1);

// 使用缓存
await cacheManager.SetAsync("key", "value", TimeSpan.FromMinutes(10));
var value = await cacheManager.GetAsync<string>("key");
```

### Redis缓存配置示例
```csharp
// 创建Redis缓存实例
var redisCache = new RedisCache("localhost:6379");

// 添加到缓存管理器
cacheManager.AddCache(redisCache, 2);  // 作为二级缓存

// 使用Redis缓存
await cacheManager.SetAsync("user:profile", userProfile, TimeSpan.FromHours(1));
var profile = await cacheManager.GetAsync<UserProfile>("user:profile");
```

### 多级缓存配置示例
```csharp
// 创建多级缓存管理器
var options = new MultiLevelCacheOptions
{
    DefaultExpiration = TimeSpan.FromHours(1)
};
var cacheManager = new MultiLevelCacheManager(options);

// 配置多级缓存（数字越小优先级越高）
cacheManager.AddCache(new MemoryCache(), 1);        // 一级缓存：内存缓存
cacheManager.AddCache(new RedisCache("localhost:6379"), 2);  // 二级缓存：Redis缓存

// 使用数据源回退
var userData = await cacheManager.GetAsync<UserData>(
    "user:1",
    async () => await userRepository.GetByIdAsync("1")
);
```

## 扩展开发
要实现自定义缓存提供者，只需实现`ICache`接口：

```csharp
public class CustomCache : ICache
{
    public Task<T?> GetAsync<T>(string key) { ... }
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) { ... }
    public Task RemoveAsync(string key) { ... }
    public Task<bool> ExistsAsync(string key) { ... }
}
```

## 性能优化建议

### 缓存层级优化
- 将热点数据放在内存缓存（一级缓存）中，减少网络请求
- 根据数据访问频率和重要性合理分配缓存层级
- 避免过多的缓存层级，一般2-3层即可满足大多数需求

### 数据访问优化
- 使用数据源回退机制，避免缓存未命中时的性能损失
```csharp
// 示例：带有数据源回退的缓存访问
var data = await _cache.GetAsync<UserData>(
    key,
    async () => await _repository.GetDataAsync() // 仅在缓存未命中时执行
);
```

### 内存管理
- 为内存缓存设置合理的容量限制，避免内存溢出
- 使用滑动过期时间处理热点数据
- 定期清理过期数据，避免内存泄漏

## 最佳实践

### 缓存键设计
- 使用冒号分隔的命名方式，便于管理和查找
```csharp
// 推荐的键命名方式
const string userProfileKey = "user:profile:{0}";
const string userSettingsKey = "user:settings:{0}";
const string userFriendsKey = "user:friends:{0}";

// 使用示例
var key = string.Format(userProfileKey, userId);
```

### 缓存过期策略
- 使用适当的过期时间，避免数据过期风暴
- 对不同类型的数据采用不同的过期策略
```csharp
// 不同数据类型的过期时间示例
var shortTermExpiration = TimeSpan.FromMinutes(5);  // 适用于变化频繁的数据
var mediumTermExpiration = TimeSpan.FromHours(1);   // 适用于一般数据
var longTermExpiration = TimeSpan.FromDays(1);      // 适用于相对稳定的数据
```

### 异常处理
- 实现降级策略，在缓存故障时保证系统可用性
- 使用Try-Catch处理缓存操作异常
```csharp
public async Task<T?> GetWithFallbackAsync<T>(string key)
{
    try
    {
        return await _cache.GetAsync<T>(key);
    }
    catch (Exception ex)
    {
        // 记录异常并降级到数据源
        _logger.LogError(ex, "Cache access failed");
        return await _repository.GetDataAsync<T>();
    }
}
```

### 缓存预热
- 系统启动时预加载重要数据
- 使用异步方式进行缓存预热，避免阻塞
```csharp
public async Task WarmUpCacheAsync()
{
    var tasks = new List<Task>();
    foreach (var key in _importantKeys)
    {
        tasks.Add(_cache.GetAsync<T>(
            key,
            async () => await _repository.GetDataAsync(key)
        ));
    }
    await Task.WhenAll(tasks);
}
```

## 注意事项
- 缓存级别数字越小，优先级越高（1级优先于2级）
- 避免缓存穿透：对空值也进行缓存，设置较短的过期时间
- 避免缓存雪崩：为过期时间添加随机偏移
- 在分布式环境中注意缓存一致性问题
- 定期监控缓存命中率，根据实际情况调整缓存策略