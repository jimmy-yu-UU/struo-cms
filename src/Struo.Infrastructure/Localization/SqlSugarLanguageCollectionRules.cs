using SqlSugar;
using Struo.Application.Localization;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Localization;

public sealed class SqlSugarLanguageCollectionRules(ISqlSugarClient db) : ILanguageCollectionRules
{
    public async Task EnsureInvariantsAsync(CancellationToken ct = default)
    {
        // Same scoped client as the repository, so this reads the rows the surrounding transaction wrote.
        // Ordered so the duplicate-code message names the same row on every backend.
        var rows = await db.Queryable<Language>().OrderBy(l => l.Id).ToListAsync(ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicate = rows.FirstOrDefault(row => !seen.Add(row.Code));
        if (duplicate is not null)
            throw new QueryException($"Language code '{duplicate.Code}' already exists.");

        var enabled = rows.Where(r => r.Enabled).ToList();
        if (enabled.Count == 0)
            throw new QueryException("At least one language must remain enabled.");
        if (enabled.Count(r => r.IsDefault) != 1)
            throw new QueryException("Exactly one enabled language must be the default.");
    }
}
