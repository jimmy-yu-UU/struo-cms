using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// DB-5: the plain btree indexes of db/migrations/009-hot-path-indexes.sql are also declared as
/// <c>[SugarIndex]</c> on the owning entities, so a CodeFirst (InitTables) database — dev/test —
/// gets the SAME named indexes the live PG migration creates. Index names match 009 exactly, so the
/// migration's <c>CREATE INDEX IF NOT EXISTS</c> overlaps idempotently with the CodeFirst emission.
/// The two PARTIAL soft-delete indexes (ix_articles_live / ix_categories_live, WHERE deletedat IS NULL)
/// cannot be expressed as attributes and stay migration-only (asserted absent here as documentation).
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

    private static readonly Type[] IndexedEntities =
    [
        typeof(Article), typeof(Category), typeof(ArticleTag), typeof(ArticleTranslation),
        typeof(FileTranslation), typeof(UserRole), typeof(Permission),
    ];

    // Every PLAIN btree index in 009 that maps to an entity property.
    public static TheoryData<string> MappedIndexNames() =>
    [
        "ix_articles_categoryid",
        "ix_categories_parentid",
        "ix_article_tags_articleid",
        "ix_article_tags_tagid",
        "ix_article_translations_fk_locale",
        "ix_file_translations_fk_locale",
        "ix_user_roles_userid",
        "ix_user_roles_roleid",
        "ix_permissions_roleid",
    ];

    private static List<string> IndexNames(ISqlSugarClient client) =>
        client.Ado.SqlQuery<string>("SELECT name FROM sqlite_master WHERE type='index'");

    [Theory]
    [MemberData(nameof(MappedIndexNames))]
    public void InitTables_emits_each_mapped_009_index(string indexName)
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(IndexedEntities);
            IndexNames(client).Should().Contain(indexName);
        }
    }

    [Fact]
    public void InitTables_is_idempotent_for_indexes()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(IndexedEntities);
            // A dev restart re-runs InitTables against the existing schema; re-emitting the same named
            // indexes must not throw ("index already exists").
            var act = () => client.CodeFirst.InitTables(IndexedEntities);
            act.Should().NotThrow();
            IndexNames(client).Should().Contain("ix_articles_categoryid");
        }
    }

    [Fact]
    public void Partial_soft_delete_indexes_stay_migration_only()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(IndexedEntities);
            // Partial indexes (WHERE deletedat IS NULL) are inexpressible as attributes → never emitted
            // by CodeFirst; they exist only via 009 on live PG.
            var names = IndexNames(client);
            names.Should().NotContain("ix_articles_live");
            names.Should().NotContain("ix_categories_live");
        }
    }
}
