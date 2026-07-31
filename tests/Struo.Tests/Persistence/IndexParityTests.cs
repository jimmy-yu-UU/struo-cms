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
/// Core index parity: every [SugarIndex] declared on a core FrameworkEntityTypes entity must be emitted
/// by CodeFirst (InitTables). There is no longer a separate hand-maintained baseline SQL file to check
/// for parity against — CodeFirst is the only table/index creator now, in every environment and on every
/// backend, so asserting against InitTables directly is the whole check. Declared indexes are discovered
/// by reflecting [SugarIndex] off FrameworkEntityTypes.All rather than hardcoded, so a new core entity's
/// index is covered automatically. Sample (Blog) index parity is the sample's own concern and is not
/// asserted here.
/// (FileTranslation's redundant plain btree was dropped — its (fileid, locale) lookup is served by
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

}
