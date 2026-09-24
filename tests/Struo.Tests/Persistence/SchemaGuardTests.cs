using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Persistence;

/// <summary>
/// SchemaGuard is a dev-startup fail-fast that asserts the critical constraints the app depends on
/// for correctness actually exist in the connected database. Two kinds are covered: (a) the revisions
/// composite UNIQUE index (backstop against the lost-update race) — always asserted, never
/// caller-supplied; and (b) a UNIQUE (fk, locale) index on each translation sidecar the CALLER passes in
/// as a <see cref="TranslationSidecarDescriptor"/> — SchemaGuard itself carries no table names, so
/// a fork's own sidecars are protected the same way core's file_translations is (Program.cs derives the
/// descriptor list from metadata). The index is declared on Revision as
/// <c>[SugarIndex("ux_{table}_item_no", …, IsUnique)]</c> and emitted under the resolved table name;
/// the guard still detects it by uniqueness + column coverage, never by name.
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

    // Only the file_translations test below needs the sidecar's composite unique derived, so it
    // alone builds its client with the metadata-backed policy Program.cs would pass through DI;
    // every other fact here creates tables by hand or only exercises the revisions backstop.
    private static (SqliteTestDatabase, ISqlSugarClient) NewClientWithPolicy()
    {
        var db = new SqliteTestDatabase();
        var policy = TranslationSidecarIndexPolicy.FromMetadata(
            MetadataScanner.ScanTypes([typeof(File), typeof(MediaFolder)]));
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty), policy);
        return (db, client);
    }

    [Fact]
    public async Task Passes_when_revisions_unique_index_present()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(Revision)); // UniqueGroupNameList -> composite unique index

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, [], default);
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

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, [], default);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("revisions");
        }
    }

    // ── Caller-supplied translation sidecar descriptors ──

    [Fact]
    public async Task Passes_when_a_sidecar_unique_index_is_present()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(Revision));
            client.Ado.ExecuteCommand(
                "CREATE TABLE widget_translations (id integer primary key, widgetid text, locale text)");
            client.Ado.ExecuteCommand(
                "CREATE UNIQUE INDEX ux_widget_translations_fk_locale ON widget_translations (widgetid, locale)");

            var sidecars = new[] { new TranslationSidecarDescriptor("widget_translations", "widgetid", "locale") };
            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, sidecars, default);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task Throws_when_a_sidecar_lacks_the_fk_locale_unique_index()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(Revision)); // valid revisions unique (checked first)
            // widget_translations WITH the lookup key but WITHOUT the unique over (widgetid, locale).
            client.Ado.ExecuteCommand(
                "CREATE TABLE widget_translations (id integer primary key, widgetid text, locale text)");
            client.Ado.ExecuteCommand(
                "CREATE INDEX ix_widget_translations_fk_locale ON widget_translations (widgetid, locale)");

            var sidecars = new[] { new TranslationSidecarDescriptor("widget_translations", "widgetid", "locale") };
            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, sidecars, default);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("widget_translations");
        }
    }

    [Fact]
    public async Task Skips_a_sidecar_table_that_does_not_exist()
    {
        var (db, client) = NewClient();
        using (db)
        {
            // Only revisions exists; the sidecar table is not present -> guard must not require a
            // unique index on a table this database does not have (a fork may not use every sidecar).
            client.CodeFirst.InitTables(typeof(Revision));

            var sidecars = new[] { new TranslationSidecarDescriptor("widget_translations", "widgetid", "locale") };
            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, sidecars, default);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task Passes_for_file_translations_descriptor_resolved_the_way_Program_cs_resolves_it()
    {
        var (db, client) = NewClientWithPolicy();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(Revision));
            client.CodeFirst.InitTables<FileTranslation>(); // policy -> composite unique

            // Same resolution Program.cs performs at the call site: CLR type/property names ->
            // physical table/column names via EntityMaintenance, never a hardcoded literal.
            var sidecar = new TranslationSidecarDescriptor(
                client.EntityMaintenance.GetTableName(typeof(FileTranslation)),
                client.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.FileId), typeof(FileTranslation)),
                client.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.Locale), typeof(FileTranslation)));

            var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, [sidecar], default);
            await act.Should().NotThrowAsync();
        }
    }
}
