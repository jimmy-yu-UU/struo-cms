using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

public static class SqlSugarClientFactory
{
    public static ISqlSugarClient Create(DatabaseOptions options, ICurrentUserAccessor currentUser)
    {
        var dbType = DbTypeMapper.Map(options.DbType);
        var config = new ConnectionConfig
        {
            ConnectionString = options.ConnectionString,
            DbType = dbType,
            IsAutoCloseConnection = true
        };

        if (dbType == SqlSugar.DbType.Sqlite)
        {
            config.ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    // SQLite only auto-increments an INTEGER (rowid alias).
                    // Rewrite identity PK columns so CodeFirst emits INTEGER
                    // instead of BIGINT, keeping the entity field as long.
                    if (column.IsPrimarykey && column.IsIdentity)
                    {
                        column.DataType = "INTEGER";
                    }
                }
            };
        }

        var client = new SqlSugarClient(config);

        AuditAop.Register(client, currentUser);
        return client;
    }
}
