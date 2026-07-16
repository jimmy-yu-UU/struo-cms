using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// DB-5: SchemaGuard is a dev-startup fail-fast that asserts the critical constraints the app depends on
/// for correctness actually exist in the connected database — the one that matters is the revisions
/// composite UNIQUE index (DB-4 backstop against the lost-update race). If it is missing, the guard
/// throws instead of letting the app run with a silent correctness gap. The index NAME differs by
/// backend/creation-path (PG migration = ux_revisions_item_no; SQLite CodeFirst =
/// Index_revisions_..._Unique), so the guard detects it by uniqueness + column coverage, not by name.
/// </summary>
public sealed class SchemaGuardTests
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
    public async Task Passes_when_revisions_unique_index_present()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(Revision)); // UniqueGroupNameList -> composite unique index

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, default);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task Throws_when_revisions_table_lacks_the_composite_unique_index()
    {
        var (db, client) = NewClient();
        using (db)
        {
            // A revisions table that has a PK (its own unique index on id) but NOT the composite unique
            // over (collectionname, itemid, revisionnumber) — simulating a DB where 010 never ran.
            client.Ado.ExecuteCommand(
                "CREATE TABLE revisions (id text primary key, collectionname text, " +
                "itemid text, revisionnumber integer, operation text, snapshot text, createdat text)");

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, default);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("revisions");
        }
    }

    // ── DB-10: the guard also asserts a UNIQUE (fk, locale) on each translation sidecar that EXISTS ──

    [Fact]
    public async Task Passes_when_translation_unique_indexes_present()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(Revision));
            client.CodeFirst.InitTables(typeof(Struo.Sample.Blog.ArticleTranslation)); // UniqueGroupNameList
            client.CodeFirst.InitTables(typeof(Struo.Infrastructure.Files.FileTranslation));

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, default);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task Throws_when_article_translations_lacks_the_fk_locale_unique_index()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(Revision)); // valid revisions unique (checked first)
            // article_translations WITH the lookup key but WITHOUT the unique over (articleid, locale) —
            // simulating a DB where 011 never ran.
            client.Ado.ExecuteCommand(
                "CREATE TABLE article_translations (id integer primary key, articleid text, " +
                "locale text, title text)");
            client.Ado.ExecuteCommand(
                "CREATE INDEX ix_article_translations_fk_locale ON article_translations (articleid, locale)");

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, default);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("article_translations");
        }
    }

    [Fact]
    public async Task Skips_a_translation_table_that_does_not_exist()
    {
        var (db, client) = NewClient();
        using (db)
        {
            // Only revisions exists; neither translation sidecar is present -> guard must not require
            // a unique index on a table the database does not have.
            client.CodeFirst.InitTables(typeof(Revision));

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, default);
            await act.Should().NotThrowAsync();
        }
    }
}
