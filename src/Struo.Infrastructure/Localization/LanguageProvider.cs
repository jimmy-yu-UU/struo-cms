using SqlSugar;
using Struo.Application.Localization;
using Struo.Domain.Localization;

namespace Struo.Infrastructure.Localization;

public sealed class LanguageProvider(ISqlSugarClient db) : ILanguageProvider
{
    // CS-7: Lazy (ExecutionAndPublication) makes the per-scope load atomic — concurrent GraphQL
    // resolvers sharing this scoped instance run the query at most once instead of racing on `??=`.
    private Lazy<IReadOnlyList<LanguageInfo>> _cache = CreateCache(db);

    private static Lazy<IReadOnlyList<LanguageInfo>> CreateCache(ISqlSugarClient db) =>
        new(() => db.Queryable<Language>().Where(l => l.Enabled).OrderBy(l => l.Sort).ToList()
                .Select(l => new LanguageInfo(l.Code, l.Name, l.IsDefault, l.Enabled, l.Sort)).ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<LanguageInfo> Enabled() => _cache.Value;

    public string DefaultCode() => Enabled().FirstOrDefault(l => l.IsDefault)?.Code
                                   ?? Enabled().FirstOrDefault()?.Code ?? "en";

    public bool IsEnabled(string code) =>
        Enabled().Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    // Swap in a fresh Lazy rather than mutating the old value (immutability rule): callers still
    // holding a reference to the previous snapshot keep observing it unchanged.
    public void Invalidate() => _cache = CreateCache(db);
}
