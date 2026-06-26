using SqlSugar;
using Struo.Application.Localization;
using Struo.Domain.Localization;

namespace Struo.Infrastructure.Localization;

public sealed class LanguageProvider(ISqlSugarClient db) : ILanguageProvider
{
    private IReadOnlyList<LanguageInfo>? _cache;

    private IReadOnlyList<LanguageInfo> Load() =>
        _cache ??= db.Queryable<Language>().Where(l => l.Enabled).OrderBy(l => l.Sort).ToList()
            .Select(l => new LanguageInfo(l.Code, l.Name, l.IsDefault, l.Enabled, l.Sort)).ToList();

    public IReadOnlyList<LanguageInfo> Enabled() => Load();

    public string DefaultCode() => Load().FirstOrDefault(l => l.IsDefault)?.Code
                                   ?? Load().FirstOrDefault()?.Code ?? "en";

    public bool IsEnabled(string code) =>
        Load().Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    public void Invalidate() => _cache = null;
}
