using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// A translation sidecar must not hold two rows for the same (foreign key, locale) — that would
/// make overlay reads non-deterministic. The entities declare a composite UNIQUE via
/// <c>[SugarColumn(UniqueGroupNameList = [...])]</c> on the FK + Locale columns, which CodeFirst
/// <c>InitTables</c> materialises as a unique index (the same mechanism the revisions backstop uses).
/// The matching physical DDL for live PostgreSQL is <c>001-core-baseline.sql</c>.
/// </summary>
public sealed class TranslationUniqueConstraintTests
{
    private static (SqliteTestDatabase, ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        return (db, client);
    }

    [Fact]
    public void Article_translation_rejects_duplicate_fk_locale()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(ArticleTranslation));
            var articleId = Guid.NewGuid();

            client.Insertable(new ArticleTranslation { ArticleId = articleId, Locale = "en", Title = "First" })
                .ExecuteCommand();

            var act = () => client.Insertable(
                new ArticleTranslation { ArticleId = articleId, Locale = "en", Title = "Dup" }).ExecuteCommand();

            act.Should().Throw<Exception>();
        }
    }

    [Fact]
    public void File_translation_rejects_duplicate_fk_locale()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(FileTranslation));
            var fileId = Guid.NewGuid();

            client.Insertable(new FileTranslation { FileId = fileId, Locale = "en", Title = "First" })
                .ExecuteCommand();

            var act = () => client.Insertable(
                new FileTranslation { FileId = fileId, Locale = "en", Title = "Dup" }).ExecuteCommand();

            act.Should().Throw<Exception>();
        }
    }

    [Fact]
    public void Article_translation_allows_same_fk_different_locale()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(ArticleTranslation));
            var articleId = Guid.NewGuid();

            client.Insertable(new ArticleTranslation { ArticleId = articleId, Locale = "en", Title = "EN" })
                .ExecuteCommand();

            var act = () => client.Insertable(
                new ArticleTranslation { ArticleId = articleId, Locale = "fr", Title = "FR" }).ExecuteCommand();

            act.Should().NotThrow();
        }
    }
}
