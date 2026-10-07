using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Localization;

namespace Struo.Tests.Support.Seeding;

/// <summary>Test setup: seeds <see cref="LocalizationOptions.EffectiveLanguages"/> when the
/// <see cref="Language"/> table is empty. <c>Sort</c> is the list position, <c>IsDefault</c> marks the entry whose
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
