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
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
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

                    // Treat all C# nullable value types (e.g. Guid?, int?, DateTime?)
                    // as nullable columns in CodeFirst DDL. This covers audit actor
                    // fields (Guid? CreatedBy/UpdatedBy) inherited from AuditableEntity
                    // without requiring [SugarColumn(IsNullable=true)] in Domain code.
                    var underlyingType = Nullable.GetUnderlyingType(property.PropertyType);
                    if (underlyingType != null && !column.IsPrimarykey)
                    {
                        column.IsNullable = true;
                    }
                }
            }
        };

        var client = new SqlSugarClient(config);

        AuditAop.Register(client, currentUser);
        return client;
    }
}
