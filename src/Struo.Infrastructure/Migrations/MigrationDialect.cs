using FluentMigrator.Runner;
using FluentMigrator.Runner.Generators.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        StruoDbType.MySql => AddMySql(rb),
        StruoDbType.Oracle => rb.AddOracle12CManaged(),
        _ => throw new ArgumentOutOfRangeException(nameof(db), db, "Unsupported DbType"),
    };

    private static IMigrationRunnerBuilder AddMySql(IMigrationRunnerBuilder rb)
    {
        rb.AddMySql8();
        rb.Services.Replace(ServiceDescriptor.Scoped<IMySqlTypeMap>(_ => new Utf8Mb4MySqlTypeMap()));
        return rb;
    }
}
