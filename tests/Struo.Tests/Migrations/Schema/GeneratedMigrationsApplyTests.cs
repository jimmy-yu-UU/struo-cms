using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
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
        db.DbMaintenance.IsAnyIndex("ux_file_translations_fk_locale").Should().BeTrue();
        db.DbMaintenance.GetColumnInfosByTableName("schema_probe", false).Select(c => c.DbColumnName.ToLowerInvariant())
            .Should().Contain(["id", "code", "displayname", "note", "body", "tags", "createdat", "stamp", "kind", "price", "amount", "ratio", "flag", "ref", "blob"]);
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
