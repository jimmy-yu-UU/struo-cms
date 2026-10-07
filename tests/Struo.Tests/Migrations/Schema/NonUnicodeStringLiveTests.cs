using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class NonUnicodeStringLiveTests
{
    [Fact]
    public void A_CodeFirst_table_created_without_nvarchar_on_SQL_Server_is_a_warning_not_an_error()
    {
        var (_, dbType, connection) = LiveBackend.Get("SqlServer");
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var db = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbTypeMapper.Map(dbType),
            ConnectionString = conn,
            IsAutoCloseConnection = true,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = false }
        });
        const string table = "chk_probe";
        try
        {
            if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
            db.CodeFirst.InitTables(typeof(CheckProbe));

            var report = SchemaChecker.Check(db, dbType, [typeof(CheckProbe)]);

            report.HasErrors.Should().BeFalse(report.ToString());
            report.Warnings.Should().Contain(f => f.Kind == FindingKind.NonUnicodeString && f.Column == "title",
                report.ToString());
        }
        finally
        {
            if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
        }
    }
}
