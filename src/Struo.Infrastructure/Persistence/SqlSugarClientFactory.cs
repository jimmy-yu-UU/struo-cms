using System.Reflection;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Persistence;

public static class SqlSugarClientFactory
{
    // FieldInterface values whose content is unbounded by nature (see the EntityService hook below).
    private static readonly HashSet<FieldInterface> ContentBearingInterfaces =
    [
        FieldInterface.RichText, FieldInterface.Textarea, FieldInterface.Markdown,
        FieldInterface.Code, FieldInterface.Json
    ];

    // Multi-value [CmsField] interfaces store an array; map them to a JSON column so SqlSugar
    // (de)serializes the List<> automatically (jsonb on Postgres, JSON-in-text elsewhere).
    private static readonly HashSet<FieldInterface> MultiValueInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];

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

                    // Multi-value fields (List<string> / List<TagItem>) always map to a JSON column —
                    // it's the only valid mapping for a List<> property. An explicit
                    // [SugarColumn(IsJson = true)] on the same property is redundant but compatible.
                    //
                    // DataType MUST be widened to `text`: `IsJson` alone leaves the CodeFirst length
                    // unset, and on Postgres that becomes `varchar(1)` (default length 1), so any
                    // serialized JSON longer than one char fails to insert (Npgsql 22001). SQLite
                    // ignores declared length (dynamic typing), which is why this only surfaces on
                    // Postgres — the phase 7g+ live-gate finding. `text` is unbounded on both.
                    var mvField = property.GetCustomAttribute<CmsFieldAttribute>();
                    if (mvField is not null && MultiValueInterfaces.Contains(mvField.Interface))
                    {
                        column.IsJson = true;
                        column.DataType = "text";
                        return;
                    }

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
                        // NullabilityInfoContext is not thread-safe; EntityService can be invoked concurrently while
                        // SqlSugar reflects multiple entities during InitTables, so create a fresh instance per column.
                        var ctx = new NullabilityInfoContext();
                        if (ctx.Create(property).WriteState == NullabilityState.Nullable)
                            column.IsNullable = true;

                        // All DBs: content-bearing [CmsField] interfaces (RichText/Textarea/Markdown/
                        // Code/Json) are unbounded by nature — prose, sanitized HTML, or serialized
                        // structures routinely exceed SqlSugar's default varchar(255) CodeFirst mapping.
                        // A realistic RichText body (a table, a couple of styled paragraphs) trivially
                        // blows past 255 chars and fails on Postgres with 22001 "value too long for
                        // type character varying(255)" (phase7g live gate, Check 1). Widen just these
                        // interfaces to `text`. An explicit [SugarColumn(ColumnDataType = ...)] on the
                        // property always wins over this convention. Plain Text fields keep SqlSugar's
                        // default varchar(255) for now, pending the dedicated MaxLength feature (7g.5).
                        var explicitDataType = property.GetCustomAttribute<SugarColumn>()?.ColumnDataType;
                        if (string.IsNullOrEmpty(explicitDataType))
                        {
                            var cmsField = property.GetCustomAttribute<CmsFieldAttribute>();
                            if (cmsField is not null && ContentBearingInterfaces.Contains(cmsField.Interface))
                            {
                                column.DataType = "text";
                            }
                        }
                    }
                }
            }
        };

        var client = new SqlSugarClient(config);

        AuditAop.Register(client, currentUser);
        return client;
    }
}
