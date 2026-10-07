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
        var host = new MigrationHost(
            new MigrationHostOptions(StruoDbType.Sqlite, _file.ConnectionString, "gen_",
                [typeof(GeneratedMigrationsApplyTests).Assembly], LockTimeoutSeconds: 5)
            { NamespaceFilter = "Struo.Tests.Migrations.Schema.Generated" },
            NullLoggerFactory.Instance);

        var applied = await host.ApplyAsync(default);

        applied.Should().HaveCount(3);
        var db = new SqlSugarClient(new ConnectionConfig
            { DbType = SqlSugar.DbType.Sqlite, ConnectionString = _file.ConnectionString, IsAutoCloseConnection = true });
        db.DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant())
            .Should().Contain(["schema_probe", "gen_revisions", "gen_file_translations"]);
        db.DbMaintenance.IsAnyIndex("ux_schema_probe_code").Should().BeTrue();
        db.DbMaintenance.IsAnyIndex("ix_schema_probe_name_kind").Should().BeTrue();
        db.DbMaintenance.IsAnyIndex("ux_gen_revisions_item_no").Should().BeTrue();
        db.DbMaintenance.IsAnyIndex("ux_file_translations_fk_locale").Should().BeTrue();
        db.DbMaintenance.GetColumnInfosByTableName("schema_probe", false).Select(c => c.DbColumnName.ToLowerInvariant())
            .Should().Contain(["id", "code", "displayname", "note", "body", "tags", "createdat", "stamp", "kind", "price", "flag", "ref", "blob"]);
    }
}
