using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

public sealed class LanguageSeederTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;

    public LanguageSeederTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Language>();
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Seeds_the_configured_languages_in_order_with_the_configured_default()
    {
        var options = new LocalizationOptions
        {
            Languages = [new() { Code = "ja", Name = "日本語" }, new() { Code = "ko", Name = "한국어" }, new() { Code = "de", Name = "Deutsch" }],
            DefaultLanguage = "KO",
        };

        await LanguageSeeder.SeedAsync(_db, options);

        var rows = await _db.Queryable<Language>().OrderBy(l => l.Sort).ToListAsync();
        rows.Select(l => l.Code).Should().Equal("ja", "ko", "de");
        rows.Select(l => l.Sort).Should().Equal(1, 2, 3);
        rows.Select(l => l.Name).Should().Equal("日本語", "한국어", "Deutsch");
        rows.Single(l => l.IsDefault).Code.Should().Be("ko");
        rows.Should().OnlyContain(l => l.Enabled);
    }

    [Fact]
    public async Task Does_nothing_when_the_table_already_has_rows()
    {
        await _db.Insertable(new Language { Code = "fr", Name = "Français", IsDefault = true, Enabled = true, Sort = 1 }).ExecuteCommandAsync();

        await LanguageSeeder.SeedAsync(_db, TestLocalization.Default);

        (await _db.Queryable<Language>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Shipped_defaults_seed_en_as_default_and_zh_TW_second()
    {
        await LanguageSeeder.SeedAsync(_db, new LocalizationOptions());

        var rows = await _db.Queryable<Language>().OrderBy(l => l.Sort).ToListAsync();
        rows.Select(l => l.Code).Should().Equal("en", "zh-TW");
        rows.Single(l => l.IsDefault).Code.Should().Be("en");
    }
}
