using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class SchemaCheckerLiveTests
{
    public static TheoryData<string> Backends => LiveBackend.Names();

    [Theory, MemberData(nameof(Backends))]
    public void A_codefirst_framework_schema_has_no_errors_and_a_dropped_unique_index_is_the_only_finding(string backend)
    {
        var (_, dbType, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);

        // The entity-info cache is keyed by prefix, so the prefix is unique per run and per backend.
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = dbType, ConnectionString = conn, TablePrefix = prefix },
            new TestCurrentUserAccessor(Guid.Empty));
        var expected = EntitySchemaReader.ReadAll(db, FrameworkEntityTypes.All);
        try
        {
            db.CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());

            var clean = SchemaChecker.Check(db, dbType, FrameworkEntityTypes.All);
            clean.HasErrors.Should().BeFalse(clean.ToString());

            var revisions = prefix + "revisions";
            db.DbMaintenance.DropIndex("ux_" + revisions + "_item_no", revisions);

            var dropped = SchemaChecker.Check(db, dbType, FrameworkEntityTypes.All);
            dropped.Errors.Should().ContainSingle(f => f.Kind == FindingKind.MissingUniqueIndex && f.Table == revisions,
                dropped.ToString());
            dropped.Errors.Should().HaveCount(1, dropped.ToString());
        }
        finally
        {
            foreach (var table in expected)
                if (db.DbMaintenance.IsAnyTable(table.PhysicalName, false)) db.DbMaintenance.DropTable(table.PhysicalName);
        }
    }
}
