using FluentMigrator.Runner;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations;

internal static class MigrationDialect
{
    /// <summary>Registers exactly the one processor for the configured backend.</summary>
    public static IMigrationRunnerBuilder Register(IMigrationRunnerBuilder rb, StruoDbType db) => db switch
    {
        StruoDbType.PostgreSQL => rb.AddPostgres(),
        StruoDbType.Sqlite => rb.AddSQLite(),
        StruoDbType.SqlServer => rb.AddSqlServer2016(),
        StruoDbType.MySql => rb.AddMySql8(),
        StruoDbType.Oracle => rb.AddOracle12CManaged(),
        _ => throw new ArgumentOutOfRangeException(nameof(db), db, "Unsupported DbType"),
    };
}
