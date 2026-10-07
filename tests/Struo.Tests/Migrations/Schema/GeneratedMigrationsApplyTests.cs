using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

/// <summary>Applies the checked-in generated migrations to SQLite: the generated sources must run, not just compile.</summary>
public sealed class GeneratedMigrationsApplyTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Generated_migrations_create_their_tables_and_indexes()
    {
        var applied = await ApplyAsync();

        applied.Should().HaveCount(3);
        var db = Open();
        db.DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant())
            .Should().Contain(["schema_probe", "gen_revisions", "gen_file_translations"]);
        db.DbMaintenance.IsAnyIndex("ux_schema_probe_code").Should().BeTrue();
        db.DbMaintenance.IsAnyIndex("ix_schema_probe_name_kind").Should().BeTrue();
        db.DbMaintenance.IsAnyIndex("ux_gen_revisions_item_no").Should().BeTrue();
        db.DbMaintenance.IsAnyIndex("ux_gen_file_translations_fk_locale").Should().BeTrue();
        db.DbMaintenance.GetColumnInfosByTableName("schema_probe", false).Select(c => c.DbColumnName.ToLowerInvariant())
            .Should().Contain(["id", "code", "displayname", "note", "body", "tags", "createdat", "stamp", "kind", "price", "amount", "ratio", "flag", "ref", "blob"]);
    }

    [Fact]
    public async Task Generated_migrations_satisfy_the_schema_checker_for_their_entities()
    {
        await ApplyAsync();
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(FileTranslation)] = TranslationSidecarIndexPolicy.KeyFor(typeof(FileTranslation), "FileId", "Locale")
        });
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString, TablePrefix = "gen_" },
            new TestCurrentUserAccessor(Guid.Empty),
            policy);

        var report = SchemaChecker.Check(db, StruoDbType.Sqlite, [typeof(SchemaProbe), typeof(Revision), typeof(FileTranslation)]);

        report.HasErrors.Should().BeFalse(report.ToString());
    }

    private Task<IReadOnlyList<MigrationInfo>> ApplyAsync() => new MigrationHost(
        new MigrationHostOptions(StruoDbType.Sqlite, _file.ConnectionString, "gen_",
            [typeof(GeneratedMigrationsApplyTests).Assembly], LockTimeoutSeconds: 5)
        { NamespaceFilter = "Struo.Tests.Migrations.Schema.Generated" },
        NullLoggerFactory.Instance).ApplyAsync(default);

    [Fact]
    public async Task Generated_unique_indexes_reject_duplicate_keys()
    {
        await ApplyAsync();
        var db = Open();

        Insert(db, "gen_revisions", Revision(Guid.NewGuid())).Should().Be(1);
        var duplicateRevision = () => Insert(db, "gen_revisions", Revision(Guid.NewGuid()));
        duplicateRevision.Should().Throw<Exception>();

        var fileId = Guid.NewGuid();
        Insert(db, "gen_file_translations", Translation(fileId, "en")).Should().Be(1);
        Insert(db, "gen_file_translations", Translation(fileId, "zh-TW")).Should().Be(1);
        var duplicateTranslation = () => Insert(db, "gen_file_translations", Translation(fileId, "en"));
        duplicateTranslation.Should().Throw<Exception>();
    }

    private static int Insert(SqlSugarClient db, string table, Dictionary<string, object> row) =>
        db.Insertable(row).AS(table).ExecuteCommand();

    private static Dictionary<string, object> Revision(Guid id) => new()
    {
        ["id"] = id,
        ["collectionname"] = "articles",
        ["itemid"] = "1",
        ["revisionnumber"] = 1L,
        ["operation"] = "create",
        ["snapshot"] = "{}",
        ["createdat"] = DateTime.UtcNow
    };

    private static Dictionary<string, object> Translation(Guid fileId, string locale) => new()
    {
        ["fileid"] = fileId,
        ["locale"] = locale
    };

    private SqlSugarClient Open() => new(new ConnectionConfig
        { DbType = SqlSugar.DbType.Sqlite, ConnectionString = _file.ConnectionString, IsAutoCloseConnection = true });
}
