using SqlSugar;

namespace Struo.Infrastructure.Localization;

/// <summary>Dev-only: seeds default languages when the table is empty.</summary>
public static class LanguageSeeder
{
    public static async Task SeedAsync(ISqlSugarClient db)
    {
        if (await db.Queryable<Language>().AnyAsync()) return;
        await db.Insertable(new List<Language>
        {
            new() { Code = "en",    Name = "English",  IsDefault = true,  Enabled = true, Sort = 1 },
            new() { Code = "zh-TW", Name = "繁體中文", IsDefault = false, Enabled = true, Sort = 2 },
        }).ExecuteCommandAsync();
    }
}
