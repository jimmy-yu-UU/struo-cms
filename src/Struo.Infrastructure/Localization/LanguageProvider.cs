using SqlSugar;
using Struo.Application.Localization;
using Struo.Domain.Localization;

namespace Struo.Infrastructure.Localization;

public sealed class LanguageProvider(ISqlSugarClient db) : ILanguageProvider
{
    private readonly object _gate = new();
    private Task<IReadOnlyList<LanguageInfo>>? _load;

    // One load per scope: concurrent callers share the same Task. A failed or cancelled load is
    // discarded so the next access retries; Invalidate swaps in a fresh load without touching the
    // snapshot earlier callers already hold.
    public Task<IReadOnlyList<LanguageInfo>> GetEnabledAsync()
    {
        lock (_gate)
        {
            if (_load is null || _load.IsFaulted || _load.IsCanceled)
                _load = LoadAsync(db);
            return _load;
        }
    }

    private static async Task<IReadOnlyList<LanguageInfo>> LoadAsync(ISqlSugarClient db)
    {
        var rows = await db.Queryable<Language>().Where(l => l.Enabled).OrderBy(l => l.Sort).ToListAsync();
        return rows.Select(l => new LanguageInfo(l.Code, l.Name, l.IsDefault, l.Enabled, l.Sort)).ToList();
    }

    public async Task<string> DefaultCodeAsync()
    {
        var enabled = await GetEnabledAsync();
        return enabled.FirstOrDefault(l => l.IsDefault)?.Code
            ?? (enabled.Count > 0 ? enabled[0].Code : null)
            ?? throw new InvalidOperationException(
                "There is no enabled language. Enable at least one row in System > Language (the Language collection).");
    }

    public async Task<bool> IsEnabledAsync(string code) => await CanonicalizeAsync(code) is not null;

    public async Task<string?> CanonicalizeAsync(string code) =>
        (await GetEnabledAsync())
        .FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code;

    public void Invalidate()
    {
        lock (_gate) _load = null;
    }
}
