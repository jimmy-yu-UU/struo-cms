using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Core index parity: the plain btree indexes declared as [SugarIndex] on core framework entities are
/// emitted by CodeFirst (InitTables) in dev/test AND captured in db/migrations/001-core-baseline.sql for
/// production. Sample (Blog) index parity is the sample's own concern and no longer asserted here.
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

    private static readonly Type[] CoreIndexedEntities =
    [
        typeof(FileTranslation), typeof(UserRole), typeof(Permission),
    ];

    public static TheoryData<string> CoreMappedIndexNames() =>
    [
        "ix_file_translations_fk_locale",
        "ix_user_roles_userid",
        "ix_user_roles_roleid",
        "ix_permissions_roleid",
    ];

    private static List<string> IndexNames(ISqlSugarClient client) =>
        client.Ado.SqlQuery<string>("SELECT name FROM sqlite_master WHERE type='index'");

    [Theory]
    [MemberData(nameof(CoreMappedIndexNames))]
    public void InitTables_emits_each_core_mapped_index(string indexName)
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(CoreIndexedEntities);
            IndexNames(client).Should().Contain(indexName);
        }
    }

    [Fact]
    public void InitTables_is_idempotent_for_core_indexes()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(CoreIndexedEntities);
            var act = () => client.CodeFirst.InitTables(CoreIndexedEntities);
            act.Should().NotThrow();
            IndexNames(client).Should().Contain("ix_user_roles_userid");
        }
    }
}
