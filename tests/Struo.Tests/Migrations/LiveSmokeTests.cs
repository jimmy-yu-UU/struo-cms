using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class LiveSmokeTests
{
    [SugarTable("sqlsugar_smoke_probe")]
    public sealed class SmokeRow
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(Length = 50)] public string Name { get; set; } = "";
    }

    public static TheoryData<string> Backends => LiveBackend.Names("SqlServer", "MySql", "MariaDb");

    [Theory, MemberData(nameof(Backends))]
    public async Task SqlSugar_round_trips_on_the_live_backend(string backend)
    {
        var (_, dbType, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);

        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = dbType, ConnectionString = conn },
            new TestCurrentUserAccessor(Guid.Empty));
        try { db.DbMaintenance.CreateDatabase(); } catch { /* exists or not permitted */ }
        try
        {
            db.CodeFirst.InitTables<SmokeRow>();
            var id = Guid.NewGuid();
            await db.Insertable(new SmokeRow { Id = id, Name = "smoke" }).ExecuteCommandAsync();

            (await db.Queryable<SmokeRow>().FirstAsync(r => r.Id == id)).Name.Should().Be("smoke");
        }
        finally
        {
            db.DbMaintenance.DropTable("sqlsugar_smoke_probe");
        }
    }
}
