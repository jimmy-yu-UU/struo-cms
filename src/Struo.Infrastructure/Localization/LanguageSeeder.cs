using SqlSugar;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Localization;

/// <summary>Invoked by <see cref="Persistence.DataSeeder"/> only when the <see cref="Language"/> table is
/// created during startup (all environments); seeds <see cref="LocalizationOptions.EffectiveLanguages"/>
/// when the table is empty. <c>Sort</c> is the list position, <c>IsDefault</c> marks the entry whose
/// code equals <see cref="LocalizationOptions.DefaultLanguage"/> (case-insensitively).</summary>
public static class LanguageSeeder
{
    public static async Task SeedAsync(ISqlSugarClient db, LocalizationOptions options)
    {
        if (await db.Queryable<Language>().AnyAsync()) return;
        var rows = options.EffectiveLanguages
            .Select((entry, index) => new Language
            {
                Code = entry.Code,
                Name = entry.Name,
                IsDefault = string.Equals(entry.Code, options.DefaultLanguage, StringComparison.OrdinalIgnoreCase),
                Enabled = true,
                Sort = index + 1,
            })
            .ToList();
        await db.Insertable(rows).ExecuteCommandAsync();
    }
}
