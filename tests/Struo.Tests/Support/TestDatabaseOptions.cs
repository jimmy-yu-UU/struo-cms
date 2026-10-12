using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.Support;

/// <summary>Builds the configuration a test needs from an <see cref="ITestDatabase"/>.</summary>
internal static class TestDatabaseOptions
{
    /// <summary>Product options for <paramref name="db"/>; the default table prefix applies unless one is given.</summary>
    public static DatabaseOptions Options(this ITestDatabase db, string? tablePrefix = null) =>
        tablePrefix is null
            ? new DatabaseOptions { DbType = db.DbType, ConnectionString = db.ConnectionString }
            : new DatabaseOptions { DbType = db.DbType, ConnectionString = db.ConnectionString, TablePrefix = tablePrefix };

    /// <summary>A raw SqlSugar configuration for <paramref name="db"/>; it never applies a table prefix.</summary>
    public static ConnectionConfig RawConfig(this ITestDatabase db) => new()
    {
        DbType = DbTypeMapper.Map(db.DbType),
        ConnectionString = db.ConnectionString,
        IsAutoCloseConnection = true,
        MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = db.DbType == StruoDbType.SqlServer },
    };
}
