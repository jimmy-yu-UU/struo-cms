using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Core index parity: every [SugarIndex] declared on a core FrameworkEntityTypes entity must be BOTH
/// emitted by CodeFirst (InitTables) in dev/test AND captured in db/migrations/001-core-baseline.sql for
/// production — previously this test only checked the first half (InitTables on SQLite) and never opened
/// the baseline file, so a declared index dropped from the baseline would go unnoticed. Declared indexes
/// are discovered by reflecting [SugarIndex] off FrameworkEntityTypes.All rather than hardcoded, so a new
/// core entity's index is covered automatically. Sample (Blog) index parity is the sample's own concern
/// and is not asserted here.
/// (DB-16: FileTranslation's redundant plain btree was dropped — its (fileid, locale) lookup is served by
/// the composite UNIQUE index — so FileTranslation no longer contributes a mapped plain btree here.)
/// </summary>
public sealed class IndexParityTests
{
    private static (SqliteTestDatabase, ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        return (db, client);
    }

    // Derived, not hardcoded: every core entity carrying at least one [SugarIndex] attribute, together
    // with the index names it declares. A new core entity that adds [SugarIndex] is picked up here with
    // no test edit required.
    private static readonly IReadOnlyList<Type> CoreIndexedEntities = FrameworkEntityTypes.All
        .Where(t => t.GetCustomAttributes<SugarIndexAttribute>().Any())
        .ToArray();

    public static TheoryData<string> CoreMappedIndexNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in CoreIndexedEntities
                     .SelectMany(t => t.GetCustomAttributes<SugarIndexAttribute>())
                     .Select(a => a.IndexName))
            data.Add(name);
        return data;
    }

    private static List<string> IndexNames(ISqlSugarClient client) =>
        client.Ado.SqlQuery<string>("SELECT name FROM sqlite_master WHERE type='index'");

    [Theory]
    [MemberData(nameof(CoreMappedIndexNames))]
    public void InitTables_emits_each_core_mapped_index(string indexName)
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(CoreIndexedEntities.ToArray());
            IndexNames(client).Should().Contain(indexName);
        }
    }

    [Fact]
    public void InitTables_is_idempotent_for_core_indexes()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(CoreIndexedEntities.ToArray());
            var act = () => client.CodeFirst.InitTables(CoreIndexedEntities.ToArray());
            act.Should().NotThrow();

            var allDeclaredIndexNames = CoreIndexedEntities
                .SelectMany(t => t.GetCustomAttributes<SugarIndexAttribute>())
                .Select(a => a.IndexName);
            IndexNames(client).Should().Contain(allDeclaredIndexNames);
        }
    }

    [Theory]
    [MemberData(nameof(CoreMappedIndexNames))]
    public void Baseline_captures_each_core_mapped_index(string indexName)
    {
        var sql = BaselineSql();
        sql.Should().Contain($"CREATE INDEX IF NOT EXISTS {indexName}",
            $"db/migrations/001-core-baseline.sql must create index '{indexName}' for production");
    }

    private static string BaselineSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "migrations")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repo's db/migrations directory must be locatable from the test host");
        return File.ReadAllText(Path.Combine(dir!.FullName, "db", "migrations", "001-core-baseline.sql"));
    }
}
