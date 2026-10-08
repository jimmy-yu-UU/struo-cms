using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// SqlSugar's process-wide reflection cache with every key scoped to one provider. SqlSugar keys
/// entity info by type and ConfigId, and row binders by result type, column names and a two-letter
/// prefix of each column's CLR type name, so without the scope a client for a second provider
/// reuses the first provider's resolved column types and binders (SQL Server's int COUNT read
/// through a bigint binder built on MySQL fails to bind).
/// </summary>
internal sealed class ProviderScopedReflectionCache : ICacheService
{
    private static readonly ReflectionInoCacheService Shared = new();

    private readonly string _scope;

    public ProviderScopedReflectionCache(DbType dbType) => _scope = dbType + ":";

    public void Add<V>(string key, V value) => Shared.Add(Scoped(key), value);

    public void Add<V>(string key, V value, int cacheDurationInSeconds) =>
        Shared.Add(Scoped(key), value, cacheDurationInSeconds);

    public bool ContainsKey<V>(string key) => Shared.ContainsKey<V>(Scoped(key));

    public V Get<V>(string key) => Shared.Get<V>(Scoped(key));

    public IEnumerable<string> GetAllKey<V>() =>
        Shared.GetAllKey<V>()
            .Where(k => k.StartsWith(_scope, StringComparison.Ordinal))
            .Select(k => k[_scope.Length..]);

    public void Remove<V>(string key) => Shared.Remove<V>(Scoped(key));

    public V GetOrCreate<V>(string cacheKey, Func<V> create, int cacheDurationInSeconds = int.MaxValue) =>
        Shared.GetOrCreate(Scoped(cacheKey), create, cacheDurationInSeconds);

    private string Scoped(string key) => _scope + key;
}
