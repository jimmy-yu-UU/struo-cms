using System.Reflection;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Domain.Auditing;
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

    // [CmsField] interfaces whose value is a structured aggregate stored as JSON — map them to a JSON
    // column so SqlSugar (de)serializes the List<>/Dictionary<> automatically (jsonb-in-text). This is
    // the multi-value selects, plus KeyValue, plus Files (List<Guid>), plus
    // Repeater (List<TChild> of [CmsField] sub-props). Json is NOT here — it is a string
    // holding raw JSON text and is widened to `text` by the content-bearing convention below.
    private static readonly HashSet<FieldInterface> JsonColumnInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue, FieldInterface.Files, FieldInterface.Repeater
    ];

    public static ISqlSugarClient Create(
        DatabaseOptions options,
        ICurrentUserAccessor currentUser,
        TranslationSidecarIndexPolicy? sidecarPolicy = null)
    {
        var policy = sidecarPolicy ?? TranslationSidecarIndexPolicy.None;
        var dbType = DbTypeMapper.Map(options.DbType);
        var config = new ConnectionConfig
        {
            ConnectionString = options.ConnectionString,
            DbType = dbType,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) => ApplyColumnConventions(property, column, dbType, policy)
            }
        };

        // GraphQL pins query/mutation roots to DependencyInjectionScope.Request
        // (GraphQlServiceCollectionExtensions), so HotChocolate can run sibling root-field
        // resolvers on separate threads that all share this one request-scoped ISqlSugarClient. A
        // bare SqlSugarClient is not thread-safe for that — concurrent ADO operations on the shared
        // connection intermittently throw ("connection already open") or otherwise fail.
        // SqlSugarScope is SqlSugar's official thread-safe wrapper: it manages a distinct inner
        // SqlSugarClient per logical async flow (AsyncLocal-keyed), and it still implements
        // ISqlSugarClient, so this is a drop-in fix at the factory boundary — DI registration
        // (AddScoped<ISqlSugarClient>) and every call site are unchanged.
        //
        // The soft-delete query filter and the AuditAop stamping must be attached via the ctor's configure
        // action, NOT via `client.QueryFilter.AddTableFilter(...)` / `AuditAop.Register(client, ...)`
        // called on the SqlSugarScope instance after construction. Post-construction attachment on
        // SqlSugarScope only reaches whichever single inner context is current at that moment; every
        // *other* inner context SqlSugarScope creates later (e.g. on another thread/async flow) is a
        // fresh SqlSugarClient that never saw that call, so the floor and audit stamping would
        // silently stop applying under concurrency. The configure-action overload
        // (config, Action<SqlSugarClient>) runs for EVERY inner context SqlSugarScope creates, which
        // is the semantically safe choice — confirmed by the full existing suite (soft-delete,
        // audit-field, revisions-transaction tests) staying green with this shape.
        var client = new SqlSugarScope(config, db =>
        {
            // Soft-delete floor. Every Queryable over an ISoftDeletable entity excludes
            // rows whose DeletedAt is set. Applies to list/get/deep-expansion/cross-relation
            // id-resolution/M2M existence/inbound-Restrict with no per-path code. Reads that need
            // trashed rows (?deleted=only|with, restore, purge) clear this filter per-query (see the
            // repository).
            db.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null);

            AuditAop.Register(db, currentUser);
        });

        return client;
    }

    // The EntityService hook SqlSugar invokes once per reflected property during CodeFirst/InitTables.
    // Conventions run in sequence and several early-return, short-circuiting whatever is below
    // them for that property — the order here is deliberate, not incidental.
    private static void ApplyColumnConventions(PropertyInfo property, EntityColumnInfo column, DbType dbType, TranslationSidecarIndexPolicy policy)
    {
        ApplySqliteIdentityColumnRewrite(column, dbType);

        if (column.IsPrimarykey || column.IsIgnore) return;

        ApplySidecarUniqueGroup(property, column, policy);
        ApplyNullabilityInference(property, column);

        if (ApplyColumnShape(property, column, dbType)) return;

        ApplyBareIsJsonWidening(property, column, dbType);

        if (ApplyJsonColumnInterfaceWidening(property, column, dbType)) return;

        ApplyContentBearingStringWidening(property, column, dbType);
    }

    // SQLite-only: identity PK must be INTEGER (rowid alias) to auto-increment.
    // On other backends the long IsIdentity sidecar PKs must stay bigint, so this
    // rewrite is gated to SQLite — applying it everywhere would downgrade
    // Postgres bigint identity PKs to int4.
    private static void ApplySqliteIdentityColumnRewrite(EntityColumnInfo column, DbType dbType)
    {
        if (dbType == SqlSugar.DbType.Sqlite && column.IsPrimarykey && column.IsIdentity)
        {
            column.DataType = "INTEGER";
        }
    }

    // A6: derive the translation sidecar's (fk, locale) composite unique index from
    // the policy rather than requiring a hand-written [SugarColumn(UniqueGroupNameList)]
    // on every sidecar entity. UIndexGroupNameList is the SqlSugar-internal property
    // name CodeFirst reads for that attribute; the actual index name it emits is
    // generated by SqlSugar from the table/column names, not from this group name, and
    // SchemaGuard detects the resulting index by uniqueness + column coverage, never by
    // name, so the two never need to agree.
    private static void ApplySidecarUniqueGroup(PropertyInfo property, EntityColumnInfo column, TranslationSidecarIndexPolicy policy)
    {
        var uniqueGroup = policy.UniqueGroupFor(property.ReflectedType ?? property.DeclaringType!, property.Name);
        if (uniqueGroup is not null)
        {
            var existing = column.UIndexGroupNameList ?? [];
            column.UIndexGroupNameList = existing.Contains(uniqueGroup, StringComparer.Ordinal)
                ? existing
                : [.. existing, uniqueGroup];
        }
    }

    // Nullability inference runs unconditionally, before any type convention below
    // (including the [ColumnShape] override), so a shaped or JSON-mapped property
    // still gets the same nullable-column treatment a plain property would. This
    // MUST NOT early-return: a type convention further down still needs to run for
    // the same property (e.g. a nullable [CmsField] string that is also
    // content-bearing, or a future nullable [ColumnShape] property).
    private static void ApplyNullabilityInference(PropertyInfo property, EntityColumnInfo column)
    {
        // All DBs: map C# nullable value types (Guid?, int?, DateTime?) to nullable
        // columns in CodeFirst DDL, so inherited Guid? audit actors
        // (CreatedBy/UpdatedBy from AuditableEntity) need no [SugarColumn(IsNullable=true)]
        // in Domain code. Correct on every backend.
        // A nullable value-type property that must map to a NOT NULL column should carry
        // [SugarColumn(IsNullable = false)] to override this hook.
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            column.IsNullable = true;
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
        }
    }

    // Explicit dialect-neutral shape wins over every type convention below. Returns true when the
    // property carried [ColumnShape] and this convention has fully handled the column, so the
    // caller must stop before the JSON-column conventions below.
    private static bool ApplyColumnShape(PropertyInfo property, EntityColumnInfo column, DbType dbType)
    {
        var shape = property.GetCustomAttribute<ColumnShapeAttribute>()?.Shape;
        if (shape is null) return false;

        // Refused, not silently resolved — for any [ColumnShape] value. The JSON-column branch below
        // sets BOTH IsJson and a widened DataType; this early return would keep only the DataType.
        // For ColumnShape.LongText — the only shape that co-occurs with a JsonColumnInterfaces member
        // in this repository — the DataType the two branches compute is identical (both LongText), so
        // the combination's only effect is losing IsJson — and without IsJson SqlSugar never
        // serializes the List<>/Dictionary<> and CodeFirst leaves the length unset, which is
        // varchar(1) on PostgreSQL: every write of a real value fails with 22001. There is nothing a
        // caller could want here, so this is a declaration error, not a precedence question.
        //
        // Deliberately narrowed to JsonColumnInterfaces. A shape combined with a
        // content-bearing interface (RichText/Textarea/Markdown/Code/Json) is legal:
        // both paths compute LongText, the shape simply wins first, and nothing is
        // lost. Pinned by
        // ColumnTypeMapTests.ColumnShape_combined_with_a_content_bearing_CmsField_is_still_allowed.
        var shapedField = property.GetCustomAttribute<CmsFieldAttribute>();
        if (shapedField is not null && JsonColumnInterfaces.Contains(shapedField.Interface))
        {
            throw new InvalidOperationException(
                $"'{property.DeclaringType?.FullName}.{property.Name}' carries both [ColumnShape] and " +
                $"[CmsField(Interface = FieldInterface.{shapedField.Interface})], which maps to a JSON " +
                "column. Remove [ColumnShape] from this property: the JSON-column mapping already " +
                "applies the LongText shape, and it also sets IsJson, which [ColumnShape] alone does not.");
        }

        column.DataType = ColumnTypeMap.For(shape.Value, dbType);
        return true;
    }

    // A property that carries only a bare [SugarColumn(IsJson = true)] — no
    // [CmsField] interface in JsonColumnInterfaces above, no [ColumnShape] — still
    // needs widening, but column.DataType cannot be the test for "nothing has
    // claimed this column yet": SqlSugar's own attribute processing already stamps
    // an IsJson column with its own default length (observed varchar(4000) here on
    // SQLite, varchar(1) on PostgreSQL) before this hook ever runs, so DataType is
    // never actually empty at this point. Reading the property's own [SugarColumn]
    // ColumnDataType instead — the same pattern the content-bearing branch below
    // uses — tells the two cases apart: an unset attribute means SqlSugar's
    // untouched default and gets widened to LongText (otherwise PostgreSQL keeps a
    // short varchar and every real write fails with 22001); a fork's own explicit
    // ColumnDataType is left exactly as SqlSugar already applied it.
    private static void ApplyBareIsJsonWidening(PropertyInfo property, EntityColumnInfo column, DbType dbType)
    {
        if (!column.IsJson) return;

        var explicitJsonDataType = property.GetCustomAttribute<SugarColumn>()?.ColumnDataType;
        if (string.IsNullOrEmpty(explicitJsonDataType))
        {
            column.DataType = ColumnTypeMap.For(ColumnShape.LongText, dbType);
        }
    }

    // Multi-value fields (List<string> / List<TagItem>) always map to a JSON column —
    // it's the only valid mapping for a List<> property. An explicit
    // [SugarColumn(IsJson = true)] on the same property is redundant but compatible.
    //
    // DataType MUST be widened: `IsJson` alone leaves the CodeFirst length
    // unset, and on Postgres that becomes `varchar(1)` (default length 1), so any
    // serialized JSON longer than one char fails to insert (Npgsql 22001). SQLite
    // ignores declared length (dynamic typing), which is why this only surfaces on
    // Postgres — confirmed by running this against a live Postgres database. LongText
    // is unbounded on every backend.
    // Returns true when this convention claimed the column, so the caller must stop before the
    // content-bearing string convention below.
    private static bool ApplyJsonColumnInterfaceWidening(PropertyInfo property, EntityColumnInfo column, DbType dbType)
    {
        var mvField = property.GetCustomAttribute<CmsFieldAttribute>();
        if (mvField is null || !JsonColumnInterfaces.Contains(mvField.Interface)) return false;

        column.IsJson = true;
        column.DataType = ColumnTypeMap.For(ColumnShape.LongText, dbType);
        return true;
    }

    // All DBs: content-bearing [CmsField] interfaces (RichText/Textarea/Markdown/
    // Code/Json) are unbounded by nature — prose, sanitized HTML, or serialized
    // structures routinely exceed SqlSugar's default varchar(255) CodeFirst mapping.
    // A realistic RichText body (a table, a couple of styled paragraphs) trivially
    // blows past 255 chars and fails on Postgres with 22001 "value too long for
    // type character varying(255)" — as verified against a live Postgres
    // database. Widen just these interfaces to the LongText shape. A property with
    // an explicit [ColumnShape] already returned above, before this convention runs;
    // a third-party fork's own [SugarColumn(ColumnDataType = ...)] is also
    // respected here and wins over this convention. Plain Text fields keep
    // SqlSugar's default varchar(255).
    private static void ApplyContentBearingStringWidening(PropertyInfo property, EntityColumnInfo column, DbType dbType)
    {
        if (property.PropertyType != typeof(string)) return;

        var explicitDataType = property.GetCustomAttribute<SugarColumn>()?.ColumnDataType;
        if (!string.IsNullOrEmpty(explicitDataType)) return;

        var cmsField = property.GetCustomAttribute<CmsFieldAttribute>();
        if (cmsField is not null && ContentBearingInterfaces.Contains(cmsField.Interface))
        {
            column.DataType = ColumnTypeMap.For(ColumnShape.LongText, dbType);
        }
    }
}
