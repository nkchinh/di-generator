using DIGen;

namespace Sample.Domain;

/// <summary>
/// [RequiredScope] locks the lifetime for all registrations of this interface.
/// Implementations use [Service&lt;T&gt;] to auto-resolve the locked scope.
/// </summary>
[RequiredScope(DiServiceScope.Singleton)]
public interface ILogger
{
    void Log(string message);
}

[Service<ILogger>]
public class ConsoleLogger : ILogger
{
    public void Log(string message) => Console.WriteLine($"[LOG] {message}");
}

[RequiredScope(DiServiceScope.Scoped)]
public interface ICacheService
{
    string? Get(string key);
    void Set(string key, string value);
}

[Service<ICacheService>]
public class InMemoryCacheService : ICacheService
{
    private readonly Dictionary<string, string> _cache = new();

    public string? Get(string key) => _cache.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, string value) => _cache[key] = value;
}
