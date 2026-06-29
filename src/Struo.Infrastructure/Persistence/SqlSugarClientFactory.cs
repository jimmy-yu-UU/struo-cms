using System.Reflection;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

public static class SqlSugarClientFactory
{
    private static readonly NullabilityInfoContext NullabilityCtx = new();

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

                    if (column.IsPrimarykey || column.IsIgnore) return;

                    // All DBs: map C# nullable value types (Guid?, int?, DateTime?) to nullable
                    // columns in CodeFirst DDL, so inherited Guid? audit actors
                    // (CreatedBy/UpdatedBy from AuditableEntity) need no [SugarColumn(IsNullable=true)]
                    // in Domain code. Correct on every backend.
                    // A nullable value-type property that must map to a NOT NULL column should carry
                    // [SugarColumn(IsNullable = false)] to override this hook.
                    if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
                    {
                        column.IsNullable = true;
                        return;
                    }

                    // NRT: map string? (nullable reference type) to nullable columns.
                    // SeoTranslation.SeoTitle / SeoMetaDescription declare string? and live in Domain
                    // (no [SugarColumn] allowed there). NullabilityInfoContext reads the compiler-emitted
                    // nullable annotations so we respect the intent without adding SqlSugar to Domain.
                    if (property.PropertyType == typeof(string))
                    {
                        var info = NullabilityCtx.Create(property);
                        if (info.WriteState == NullabilityState.Nullable)
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
