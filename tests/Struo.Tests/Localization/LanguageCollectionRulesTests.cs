using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

public sealed class LanguageCollectionRulesTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarLanguageCollectionRules _rules;

    public LanguageCollectionRulesTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(_db, TestLocalization.Default).GetAwaiter().GetResult();
        _rules = new SqlSugarLanguageCollectionRules(_db);
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Seeded_state_satisfies_the_invariants()
    {
        await _rules.Invoking(r => r.EnsureInvariantsAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Duplicate_code_differing_only_in_case_is_rejected()
    {
        await _db.Insertable(new Language { Code = "EN", Name = "dup", Enabled = false, Sort = 9 }).ExecuteCommandAsync();
        var act = () => _rules.EnsureInvariantsAsync();
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("Language code 'EN' already exists.");
    }

    [Fact]
    public async Task Disabling_every_language_is_rejected()
    {
        await _db.Updateable<Language>().SetColumns(l => l.Enabled == false).Where(l => true).ExecuteCommandAsync();
        var act = () => _rules.EnsureInvariantsAsync();
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("At least one language must remain enabled.");
    }

    [Fact]
    public async Task Zero_enabled_defaults_is_rejected()
    {
        await _db.Updateable<Language>().SetColumns(l => l.IsDefault == false).Where(l => true).ExecuteCommandAsync();
        var act = () => _rules.EnsureInvariantsAsync();
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("Exactly one enabled language must be the default.");
    }

    [Fact]
    public async Task Two_enabled_defaults_is_rejected()
    {
        await _db.Updateable<Language>().SetColumns(l => l.IsDefault == true).Where(l => true).ExecuteCommandAsync();
        var act = () => _rules.EnsureInvariantsAsync();
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("Exactly one enabled language must be the default.");
    }

    [Fact]
    public async Task A_disabled_default_does_not_count()
    {
        // en is default; disable it -> zh-TW enabled but no enabled default.
        await _db.Updateable<Language>().SetColumns(l => l.Enabled == false).Where(l => l.Code == "en").ExecuteCommandAsync();
        var act = () => _rules.EnsureInvariantsAsync();
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("Exactly one enabled language must be the default.");
    }
}
