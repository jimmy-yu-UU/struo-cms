// src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs
using System.Reflection;
using System.Text;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Localization;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

public sealed class SqlSugarItemRepository(
    ISqlSugarClient db,
    IEntityRegistry registry,
    IRelationshipGraph graph,
    IMetadataProvider metadata,
    StruoQueryOptions options) : IItemRepository
{
    // Cached generic method definitions — resolved once at class load, pinned by parameter-type signature.
    // Each private helper is async and returns a KNOWN Task<T> so the dispatcher can cast before awaiting.

    private static readonly MethodInfo RunQueryAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(RunQueryAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(List<IConditionalModel>), typeof(string), typeof(int), typeof(int), typeof(CancellationToken)])!;

    private static readonly MethodInfo GetByIdGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(GetByIdGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object)])!;

    private static readonly MethodInfo CreateGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(CreateGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object)])!;

    private static readonly MethodInfo UpdateGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(UpdateGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(CancellationToken)])!;

    private static readonly MethodInfo DeleteGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(DeleteGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(CancellationToken)])!;

    private static readonly MethodInfo WhereInGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(WhereInGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(IReadOnlyList<object>), typeof(CancellationToken)])!;

    private static readonly MethodInfo QueryIdsGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(QueryIdsGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(List<IConditionalModel>), typeof(string), typeof(CancellationToken)])!;

    private static readonly MethodInfo SyncM2MGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(SyncM2MGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(object), typeof(IReadOnlyList<object>), typeof(CancellationToken)])!;

    private static readonly MethodInfo LoadTranslationsGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(LoadTranslationsGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(IReadOnlyList<object>), typeof(string), typeof(string), typeof(CancellationToken)])!;

    private static readonly MethodInfo QueryTranslationParentIdsGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(QueryTranslationParentIdsGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(List<IConditionalModel>), typeof(CancellationToken)])!;

    private static readonly MethodInfo SyncTranslationsGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(SyncTranslationsGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(object),
             typeof(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>), typeof(CancellationToken)])!;

    public async Task<QueryResult> QueryAsync(string collection, QueryModel query,
        IReadOnlyList<string> searchableFields, string? queryLocale = null, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var collMeta = metadata.GetCollection(collection);
        var translatableFields = collMeta?.Translation?.Fields ?? [];

        // Split searchable fields: non-translatable go into the normal LIKE OR group;
        // translatable ones are resolved to parent id sets via the translation sidecar.
        var nonTranslatableSearchable = searchableFields
            .Where(f => !translatableFields.Any(t => string.Equals(t, f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var conditionals = ConditionalModelTranslator.Translate(query.Filter, query.Search, nonTranslatableSearchable, d, db);

        // Translatable search: union parent ids from each translatable searchable field at the query locale.
        if (!string.IsNullOrWhiteSpace(query.Search) && queryLocale is not null)
        {
            var translatableSearchable = searchableFields
                .Where(f => translatableFields.Any(t => string.Equals(t, f, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (translatableSearchable.Count > 0 && collMeta?.Translation is not null)
            {
                var tm = collMeta.Translation;
                var allParentIds = new HashSet<string>();
                foreach (var field in translatableSearchable)
                {
                    var fieldCondition = new ComparisonFilter(field, QueryOperator.Contains, query.Search);
                    var parentIds = await QueryTranslationParentIdsAsync(
                        tm.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty,
                        queryLocale, fieldCondition, ct);
                    foreach (var pid in parentIds)
                        allParentIds.Add(pid?.ToString() ?? "");
                }
                allParentIds.Remove("");

                if (allParentIds.Count > 0)
                {
                    // Combine with existing conditionals using OR (non-translatable OR translatable parent id match).
                    var idColumn = db.EntityMaintenance.GetDbColumnName(d.IdProperty, d.EntityType);
                    var translationIdModel = new ConditionalModel
                    {
                        FieldName = idColumn,
                        ConditionalType = SqlSugar.ConditionalType.In,
                        FieldValue = string.Join(",", allParentIds)
                    };

                    if (nonTranslatableSearchable.Count > 0)
                    {
                        // Both non-translatable LIKE group and translatable id-IN need to be OR'd together.
                        // ConditionalModelTranslator.Translate emits the LIKE group as a ConditionalCollections
                        // appended last.  We assert this explicitly rather than relying on position alone:
                        // if the last element is NOT a ConditionalCollections, the LIKE group is missing
                        // (e.g. future translator change) and we fall back to a simple append rather than
                        // silently turning the translatable match into an AND by injecting into the wrong group.
                        var lastConditional = conditionals.Count > 0 ? conditionals[^1] : null;
                        if (lastConditional is SqlSugar.ConditionalCollections likeCollection)
                        {
                            // Verified: last element is the LIKE ConditionalCollections — safe to OR in.
                            likeCollection.ConditionalList.Add(
                                new KeyValuePair<SqlSugar.WhereType, SqlSugar.ConditionalModel>(
                                    SqlSugar.WhereType.Or, translationIdModel));
                        }
                        else
                        {
                            // Fallback: no LIKE group found — append as a standalone AND condition.
                            // This preserves correctness (rows matching the translatable term are still
                            // returned) at the cost of not OR-ing with any non-translatable LIKE results,
                            // which is safe because there are no non-translatable LIKE conditions here.
                            conditionals.Add(translationIdModel);
                        }
                    }
                    else
                    {
                        // No non-translatable searchable fields — just add the IN condition.
                        conditionals.Add(translationIdModel);
                    }
                }
            }
        }

        var orderBy = BuildOrderBy(query.Sort, d, collection, queryLocale);
        var method = RunQueryAsyncDef.MakeGenericMethod(d.EntityType);
        return await (Task<QueryResult>)method.Invoke(this, [conditionals, orderBy, query.Limit, query.Offset, ct])!;
    }

    private async Task<QueryResult> RunQueryAsync<T>(
        List<IConditionalModel> conditionals, string? orderBy, int limit, int offset, CancellationToken ct)
        where T : class, new()
    {
        var pageNumber = (offset / Math.Max(1, limit)) + 1;
        var total = new RefAsync<int>();
        var queryable = db.Queryable<T>().Where(conditionals);
        if (!string.IsNullOrWhiteSpace(orderBy)) queryable = queryable.OrderBy(orderBy);
        var rows = await queryable.ToPageListAsync(pageNumber, limit, total, ct);
        return new QueryResult(rows.Cast<object>().ToList(), total.Value);
    }

    public async Task<object?> GetByIdAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var method = GetByIdGenericAsyncDef.MakeGenericMethod(d.EntityType);
        return await (Task<object?>)method.Invoke(this, [ConvertId(id, d)])!;
    }

    private async Task<object?> GetByIdGenericAsync<T>(object id) where T : class, new() =>
        await db.Queryable<T>().InSingleAsync(id);

    public async Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var pk = d.EntityType.GetProperty(d.IdProperty)!;
        if (pk.PropertyType == typeof(Guid) && pk.GetValue(entity) is Guid cur && cur == Guid.Empty)
            pk.SetValue(entity, Guid.CreateVersion7());
        var method = CreateGenericAsyncDef.MakeGenericMethod(d.EntityType);
        return await (Task<object>)method.Invoke(this, [entity])!;
    }

    private async Task<object> CreateGenericAsync<T>(object entity) where T : class, new() =>
        (await db.Insertable((T)entity).ExecuteReturnEntityAsync())!;

    public async Task<object?> UpdateAsync(string collection, string id, object entity,
        CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var existing = await GetByIdAsync(collection, id, ct);
        if (existing is null) return null;

        // Clone so the caller's object is never mutated.
        var clone = CloneEntity(entity, d.EntityType);
        d.EntityType.GetProperty(d.IdProperty)!.SetValue(clone, ConvertId(id, d));

        var method = UpdateGenericAsyncDef.MakeGenericMethod(d.EntityType);
        await (Task)method.Invoke(this, [clone, ct])!;
        return await GetByIdAsync(collection, id, ct);
    }

    private async Task UpdateGenericAsync<T>(object entity, CancellationToken ct) where T : class, new() =>
        await db.Updateable((T)entity).ExecuteCommandAsync(ct);

    public async Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var existing = await GetByIdAsync(collection, id, ct);
        if (existing is null) return false;

        var method = DeleteGenericAsyncDef.MakeGenericMethod(d.EntityType);
        await (Task)method.Invoke(this, [ConvertId(id, d), ct])!;
        return true;
    }

    private async Task DeleteGenericAsync<T>(object id, CancellationToken ct) where T : class, new() =>
        await db.Deleteable<T>().In(id).ExecuteCommandAsync(ct);

    public Task<IReadOnlyList<object>> QueryWhereInAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        // Map camelCase field name -> CLR property name (fall back to the raw name, e.g. "id").
        var clrProperty = d.FieldToProperty.TryGetValue(property, out var p) ? p : property;
        return QueryEntityWhereInAsync(d.EntityType, clrProperty, values, ct);
    }

    public async Task<IReadOnlyList<object>> QueryEntityWhereInAsync(
        Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        // Empty value set -> never emit an "IN ()"; return empty.
        if (values.Count == 0) return [];

        var column = db.EntityMaintenance.GetDbColumnName(propertyName, entityType);
        var method = WhereInGenericAsyncDef.MakeGenericMethod(entityType);
        return await (Task<IReadOnlyList<object>>)method.Invoke(this, [column, values, ct])!;
    }

    private async Task<IReadOnlyList<object>> WhereInGenericAsync<T>(
        string column, IReadOnlyList<object> values, CancellationToken ct) where T : class, new()
    {
        // Use a ConditionalModel (ConditionalType.In) rather than the typed .In(string, ...)
        // overload: SqlSugar's In(string, FieldType[]) is value-typed/array-bound and brittle
        // across heterogeneous CLR id types. The comma-joined value form matches how the
        // Phase-2 ConditionalModelTranslator emits IN clauses.
        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = column,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", values.Select(v => v?.ToString()))
            }
        };
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<object>> QueryIdsAsync(
        string collection, FilterNode leafCondition, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var conditionals = ConditionalModelTranslator.Translate(leafCondition, null, [], d, db);
        var method = QueryIdsGenericAsyncDef.MakeGenericMethod(d.EntityType);
        return await (Task<IReadOnlyList<object>>)method.Invoke(this, [conditionals, d.IdProperty, ct])!;
    }

    private async Task<IReadOnlyList<object>> QueryIdsGenericAsync<T>(
        List<IConditionalModel> conditionals, string idProperty, CancellationToken ct) where T : class, new()
    {
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        var pi = typeof(T).GetProperty(idProperty)!;
        return rows.Select(r => pi.GetValue(r)!).ToList();
    }

    public async Task SyncManyToManyAsync(
        Type junctionType,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<object> targetIds,
        CancellationToken ct = default)
    {
        var parentColumn = db.EntityMaintenance.GetDbColumnName(parentFkProperty, junctionType);
        var method = SyncM2MGenericAsyncDef.MakeGenericMethod(junctionType);
        await (Task)method.Invoke(this, [parentColumn, parentFkProperty, targetFkProperty, sortProperty, parentId, targetIds, ct])!;
    }

    private async Task SyncM2MGenericAsync<T>(
        string parentColumn,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<object> targetIds,
        CancellationToken ct) where T : class, new()
    {
        // ConditionalType.In (not Equal): Equal binds FieldValue as text -> "bigint = text" 42883 on PostgreSQL. In is the Postgres-safe primitive used elsewhere in this class.
        var deleteConditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = parentColumn,
                ConditionalType = ConditionalType.In,
                FieldValue = parentId.ToString()
            }
        };

        // Build new rows before opening the transaction so reflection work stays outside the tx.
        var type = typeof(T);
        var parentProp = type.GetProperty(parentFkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var targetProp = type.GetProperty(targetFkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var sortProp   = sortProperty is null ? null
            : type.GetProperty(sortProperty, BindingFlags.Public | BindingFlags.Instance)!;

        var rows = new List<T>(targetIds.Count);
        for (var i = 0; i < targetIds.Count; i++)
        {
            var row = new T();
            parentProp.SetValue(row, IdCoercion.Coerce(parentId,    parentProp.PropertyType));
            targetProp.SetValue(row, IdCoercion.Coerce(targetIds[i], targetProp.PropertyType));
            // Use Convert.ChangeType so the sort index (int) is coerced to whatever numeric
            // type the sort column declares (e.g. int, long, short).
            sortProp?.SetValue(row, Convert.ChangeType(i, sortProp.PropertyType));
            rows.Add(row);
        }

        // Delete + insert in a single transaction so a failed insert never leaves the parent
        // with zero junction rows.
        try
        {
            await db.Ado.BeginTranAsync();
            await db.Deleteable<T>().Where(deleteConditionals).ExecuteCommandAsync(ct);
            if (rows.Count > 0)
                await db.Insertable(rows).ExecuteCommandAsync(ct);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<object>> LoadTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<object> parentIds,
        string? locale,
        CancellationToken ct = default)
    {
        if (parentIds.Count == 0) return [];

        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        var localeColumn = db.EntityMaintenance.GetDbColumnName(localeProperty, translationType);
        var method = LoadTranslationsGenericAsyncDef.MakeGenericMethod(translationType);
        return await (Task<IReadOnlyList<object>>)method.Invoke(
            this, [fkColumn, parentIds, localeColumn, locale, ct])!;
    }

    private async Task<IReadOnlyList<object>> LoadTranslationsGenericAsync<T>(
        string fkColumn, IReadOnlyList<object> parentIds, string localeColumn, string? locale,
        CancellationToken ct) where T : class, new()
    {
        // ConditionalType.In on the FK keeps "bigint IN (...)" Postgres-safe (no text coercion).
        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = fkColumn,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", parentIds.Select(v => v?.ToString()))
            }
        };
        if (locale is not null)
        {
            conditionals.Add(new ConditionalModel
            {
                FieldName = localeColumn,
                ConditionalType = ConditionalType.Equal,
                FieldValue = locale
            });
        }
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<object>> QueryTranslationParentIdsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        string locale,
        FilterNode fieldCondition,
        CancellationToken ct = default)
    {
        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        var localeColumn = db.EntityMaintenance.GetDbColumnName(localeProperty, translationType);

        // Build a fake EntityDescriptor for the translation type so ConditionalModelTranslator
        // can map camelCase field names to DB columns.
        var translationDescriptor = BuildTranslationDescriptor(translationType);

        // Locale equality filter (AND'd with the field condition below).
        var localeConditional = new ConditionalModel
        {
            FieldName = localeColumn,
            ConditionalType = ConditionalType.Equal,
            FieldValue = locale
        };

        // Field condition translated via the translation entity's column map.
        var fieldConditionals = ConditionalModelTranslator.Translate(fieldCondition, null, [], translationDescriptor, db);

        // Combine: locale AND field.  SqlSugar AND's consecutive IConditionalModel items.
        var conditionals = new List<IConditionalModel> { localeConditional };
        conditionals.AddRange(fieldConditionals);

        var method = QueryTranslationParentIdsGenericAsyncDef.MakeGenericMethod(translationType);
        return await (Task<IReadOnlyList<object>>)method.Invoke(this, [fkColumn, fkProperty, localeColumn, locale, conditionals, ct])!;
    }

    private async Task<IReadOnlyList<object>> QueryTranslationParentIdsGenericAsync<T>(
        string fkColumn, string fkProperty, string localeColumn, string locale,
        List<IConditionalModel> conditionals, CancellationToken ct) where T : class, new()
    {
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        var fkProp = typeof(T).GetProperty(fkProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (fkProp is null) return [];
        return rows
            .Select(r => fkProp.GetValue(r))
            .Where(v => v is not null)
            .Distinct()
            .ToList()!;
    }

    /// <summary>
    /// Builds a minimal <see cref="EntityDescriptor"/> for a translation entity type so that
    /// <see cref="ConditionalModelTranslator"/> can resolve camelCase field names to DB columns.
    /// Only the <c>FieldToProperty</c> map and <c>IdProperty</c> are needed.
    /// </summary>
    private static EntityDescriptor BuildTranslationDescriptor(Type translationType)
    {
        // Build a camelCase -> CLR property name map for all public instance properties.
        var map = translationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..],
                p => p.Name,
                StringComparer.OrdinalIgnoreCase);

        // Id property: first property decorated with IsPrimaryKey, fall back to "Id".
        var idProp = translationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<SugarColumn>() is { IsPrimaryKey: true })
            ?.Name ?? "Id";

        return new EntityDescriptor(translationType, map, idProp);
    }

    public async Task SyncTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct = default)
    {
        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        var localeColumn = db.EntityMaintenance.GetDbColumnName(localeProperty, translationType);
        var method = SyncTranslationsGenericAsyncDef.MakeGenericMethod(translationType);
        await (Task)method.Invoke(
            this, [fkColumn, fkProperty, localeProperty, fieldProperties, parentId, perLocale, ct])!;
    }

    private async Task SyncTranslationsGenericAsync<T>(
        string fkColumn,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct) where T : class, new()
    {
        if (perLocale.Count == 0) return;

        var type = typeof(T);
        var fkProp = type.GetProperty(fkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var localeProp = type.GetProperty(localeProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var fieldProps = fieldProperties.ToDictionary(
            f => f,
            f => type.GetProperty(f, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                 ?? throw new InvalidOperationException(
                     $"Translation entity '{type.Name}' has no field property '{f}'."),
            StringComparer.OrdinalIgnoreCase);

        // Build the rows + per-locale delete models outside the transaction (reflection only).
        var inserts = new List<T>(perLocale.Count);
        var deletes = new List<List<IConditionalModel>>(perLocale.Count);
        foreach (var (locale, values) in perLocale)
        {
            var row = new T();
            fkProp.SetValue(row, IdCoercion.Coerce(parentId, fkProp.PropertyType));
            localeProp.SetValue(row, locale);
            foreach (var (fieldName, prop) in fieldProps)
            {
                if (TryGetValueCaseInsensitive(values, fieldName, out var raw))
                    prop.SetValue(row, CoerceValue(raw, prop.PropertyType));
            }
            inserts.Add(row);

            // ConditionalType.In on the FK (Postgres-safe), Equal on the string locale.
            deletes.Add(
            [
                new ConditionalModel
                {
                    FieldName = fkColumn,
                    ConditionalType = ConditionalType.In,
                    FieldValue = parentId.ToString()
                },
                new ConditionalModel
                {
                    FieldName = db.EntityMaintenance.GetDbColumnName(localeProperty, type),
                    ConditionalType = ConditionalType.Equal,
                    FieldValue = locale
                }
            ]);
        }

        try
        {
            await db.Ado.BeginTranAsync();
            foreach (var del in deletes)
                await db.Deleteable<T>().Where(del).ExecuteCommandAsync(ct);
            await db.Insertable(inserts).ExecuteCommandAsync(ct);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private static bool TryGetValueCaseInsensitive(
        IReadOnlyDictionary<string, object?> dict, string key, out object? value)
    {
        if (dict.TryGetValue(key, out value)) return true;
        foreach (var (k, v) in dict)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) { value = v; return true; }
        }
        value = null;
        return false;
    }

    private static object? CoerceValue(object? raw, Type targetType) => IdCoercion.Coerce(raw, targetType);

    private EntityDescriptor Descriptor(string collection) =>
        registry.Get(collection) ?? throw new InvalidOperationException($"Unknown collection '{collection}'.");

    /// <summary>
    /// Converts a string ID to the PK property type. Handles Guid and all IConvertible types.
    /// </summary>
    private static object ConvertId(string id, EntityDescriptor d)
    {
        var idType = d.EntityType.GetProperty(d.IdProperty)!.PropertyType;
        var targetType = Nullable.GetUnderlyingType(idType) ?? idType;

        if (targetType == typeof(Guid))
            return Guid.Parse(id);

        try
        {
            return Convert.ChangeType(id, targetType);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                $"ID value '{id}' cannot be converted to type '{targetType.Name}' for collection '{d.EntityType.Name}'.",
                nameof(id), ex);
        }
    }

    /// <summary>
    /// Shallow-clones an entity by copying each public read/write property by value.
    /// This is suitable for the project's value-typed DTO entities where all properties
    /// hold primitives, strings, or value types. It is NOT safe for entities that hold
    /// <see cref="IDisposable"/> references or mutable reference-shared state, because
    /// both the original and the clone would share the same reference.
    /// </summary>
    private static object CloneEntity(object entity, Type entityType)
    {
        var clone = Activator.CreateInstance(entityType)!;
        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite))
        {
            prop.SetValue(clone, prop.GetValue(entity));
        }
        return clone;
    }

    private string? BuildOrderBy(IReadOnlyList<SortField> sort, EntityDescriptor d, string collection, string? queryLocale = null)
    {
        if (sort.Count == 0) return null;
        var collMeta = metadata.GetCollection(collection);
        var translatableFields = collMeta?.Translation?.Fields ?? [];

        var parts = sort.Select(s =>
        {
            if (RelationPath.IsRelationPath(s.Field))
                return $"{RelationOrderExpr(collection, s.Field)} {(s.Descending ? "DESC" : "ASC")}";

            // Translatable sort field: emit a locale-scoped correlated subquery.
            if (queryLocale is not null
                && translatableFields.Any(f => string.Equals(f, s.Field, StringComparison.OrdinalIgnoreCase))
                && collMeta?.Translation is not null)
            {
                return $"{TranslatableOrderExpr(collection, s.Field, collMeta.Translation, queryLocale, d)} {(s.Descending ? "DESC" : "ASC")}";
            }

            var prop = d.FieldToProperty.TryGetValue(s.Field, out var p) ? p : s.Field;
            var col = db.EntityMaintenance.GetDbColumnName(prop, d.EntityType);
            return $"{col} {(s.Descending ? "DESC" : "ASC")}";
        });
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Builds a locale-scoped correlated subquery ORDER BY expression for a translatable field:
    /// <c>(SELECT t.{fieldCol} FROM {translationTable} t WHERE t.{fk} = {parentTable}.{id} AND t.{localeCol} = '{locale}')</c>
    /// Mirrors the <see cref="RelationOrderExpr"/> form used for cross-relation sort.
    /// </summary>
    private string TranslatableOrderExpr(
        string collection, string fieldName,
        Domain.Metadata.Models.TranslationMetadata tm,
        string queryLocale,
        EntityDescriptor parentDesc)
    {
        var translationType = tm.TranslationEntityType;

        // Translation table name and column names via EntityMaintenance (no raw SQL names hardcoded).
        var translationTable = db.EntityMaintenance.GetTableName(translationType);
        var fkCol = db.EntityMaintenance.GetDbColumnName(tm.ForeignKeyProperty, translationType);
        var localeCol = db.EntityMaintenance.GetDbColumnName(tm.LocaleProperty, translationType);

        // Resolve camelCase field name -> CLR property -> DB column on the translation entity.
        var fieldClr = translationType
            .GetProperty(fieldName, System.Reflection.BindingFlags.Public |
                                    System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.IgnoreCase)?.Name ?? fieldName;
        var fieldCol = db.EntityMaintenance.GetDbColumnName(fieldClr, translationType);

        // Parent table and its PK column.
        var parentTable = db.EntityMaintenance.GetTableName(parentDesc.EntityType);
        var parentIdCol = db.EntityMaintenance.GetDbColumnName(parentDesc.IdProperty, parentDesc.EntityType);

        // Defense-in-depth: escape single quotes in the locale literal (SQL standard doubling)
        // so that even a crafted locale code that slipped past the charset validator cannot break
        // out of the literal and inject SQL.  The charset validator in ItemService is the primary
        // guard; this is the secondary sink-level guard.
        var safeLocale = queryLocale.Replace("'", "''");
        return $"(SELECT t.{fieldCol} FROM {translationTable} t WHERE t.{fkCol} = {parentTable}.{parentIdCol} AND t.{localeCol} = '{safeLocale}')";
    }

    /// <summary>
    /// Builds a correlated-subquery ORDER BY expression for a to-one relation path (validated
    /// all-to-one by QueryValidator before reaching here). This is the spike-validated form:
    /// ONE subquery with ONE JOIN per extra hop. The root table is referenced by its bare
    /// table name (SqlSugar uses no alias for a single-table Queryable&lt;T&gt;).
    ///   category.name        -> (SELECT t1.name FROM categories t1 WHERE t1.id = articles.category_id)
    ///   category.parent.name -> (SELECT t2.name FROM categories t1
    ///                             JOIN categories t2 ON t2.id = t1.parent_id
    ///                            WHERE t1.id = articles.category_id)
    /// </summary>
    private string RelationOrderExpr(string rootCollection, string path)
    {
        var rp = RelationPath.Parse(rootCollection, path, graph, metadata, options.MaxRelationDepth);
        var segs = rp.Segments;

        string FkCol(EntityDescriptor d, string camelFk)
        {
            var clr = d.FieldToProperty.TryGetValue(camelFk, out var p) ? p
                : throw new QueryException($"Foreign key '{camelFk}' is not a known property on '{d.EntityType.Name}'.");
            return db.EntityMaintenance.GetDbColumnName(clr, d.EntityType);
        }

        var rootDesc = registry.Get(rootCollection)!;
        var rootTable = db.EntityMaintenance.GetTableName(rootDesc.EntityType);

        var t1Desc = registry.Get(segs[0].Relation.TargetCollection)!;
        var t1Table = db.EntityMaintenance.GetTableName(t1Desc.EntityType);
        var t1IdCol = db.EntityMaintenance.GetDbColumnName(t1Desc.IdProperty, t1Desc.EntityType);

        var lastDesc = registry.Get(segs[^1].Relation.TargetCollection)!;
        var leafClr = lastDesc.FieldToProperty.TryGetValue(rp.LeafField, out var lp) ? lp : rp.LeafField;
        var leafCol = db.EntityMaintenance.GetDbColumnName(leafClr, lastDesc.EntityType);

        var sb = new StringBuilder();
        sb.Append($"(SELECT t{segs.Count}.{leafCol} FROM {t1Table} t1");

        var prevDesc = t1Desc;  // FK linking t{k} to t{k+1} lives on the previous entity
        for (var k = 1; k < segs.Count; k++)
        {
            var segDesc = registry.Get(segs[k].Relation.TargetCollection)!;
            var segTable = db.EntityMaintenance.GetTableName(segDesc.EntityType);
            var segIdCol = db.EntityMaintenance.GetDbColumnName(segDesc.IdProperty, segDesc.EntityType);
            var linkFkCol = FkCol(prevDesc, segs[k].Relation.ForeignKey!);
            sb.Append($" JOIN {segTable} t{k + 1} ON t{k + 1}.{segIdCol} = t{k}.{linkFkCol}");
            prevDesc = segDesc;
        }

        sb.Append($" WHERE t1.{t1IdCol} = {rootTable}.{FkCol(rootDesc, segs[0].Relation.ForeignKey!)})");
        return sb.ToString();
    }

}
