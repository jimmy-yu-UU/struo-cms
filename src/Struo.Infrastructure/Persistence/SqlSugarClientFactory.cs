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
                    // SQLite-only: identity PK must be INTEGER (rowid alias) to auto-increment.
                    // On other backends the long IsIdentity sidecar PKs must stay bigint, so this
                    // rewrite is gated to SQLite — applying it everywhere would downgrade
                    // Postgres bigint identity PKs to int4.
                    if (dbType == SqlSugar.DbType.Sqlite && column.IsPrimarykey && column.IsIdentity)
                    {
                        column.DataType = "INTEGER";
                    }

                    // All DBs: map C# nullable value types (Guid?, int?, DateTime?) to nullable
                    // columns in CodeFirst DDL, so inherited Guid? audit actors
                    // (CreatedBy/UpdatedBy from AuditableEntity) need no [SugarColumn(IsNullable=true)]
                    // in Domain code. Correct on every backend.
                    if (Nullable.GetUnderlyingType(property.PropertyType) is not null && !column.IsPrimarykey)
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
