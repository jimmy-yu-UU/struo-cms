// src/Struo.Infrastructure/Query/WhereInQueries.cs
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal sealed class WhereInQueries(ISqlSugarClient db, IEntityRegistry registry, FilterTranslator filters)
{
    private static readonly GenericDispatcher<Func<WhereInQueries, string, IReadOnlyList<object>, CancellationToken, Task<IReadOnlyList<object>>>> WhereInDispatcher =
        new(typeof(WhereInQueries), nameof(WhereInGenericAsync), [typeof(string), typeof(IReadOnlyList<object>), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<WhereInQueries, List<IConditionalModel>, CancellationToken, Task<IReadOnlyList<object>>>> WhereInFilteredDispatcher =
        new(typeof(WhereInQueries), nameof(WhereInFilteredGenericAsync), [typeof(List<IConditionalModel>), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<WhereInQueries, string, IReadOnlyList<object>, CancellationToken, Task<IReadOnlyList<object>>>> WhereInWithDeletedDispatcher =
        new(typeof(WhereInQueries), nameof(WhereInWithDeletedGenericAsync), [typeof(string), typeof(IReadOnlyList<object>), typeof(CancellationToken)]);

    public Task<IReadOnlyList<object>> QueryWhereInAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
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
        return await WhereInDispatcher.For(entityType)(this, column, values, ct);
    }

    private async Task<IReadOnlyList<object>> WhereInGenericAsync<T>(
        string column, IReadOnlyList<object> values, CancellationToken ct) where T : class, new()
    {
        // Use a ConditionalModel (ConditionalType.In) rather than the typed .In(string, ...)
        // overload: SqlSugar's In(string, FieldType[]) is value-typed/array-bound and brittle
        // across heterogeneous CLR id types. The comma-joined value form matches how the
        // ConditionalModelTranslator emits IN clauses.
        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = column,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", values.Select(v => v?.ToString())),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(values.FirstOrDefault(v => v is not null))
            }
        };
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, string? queryLocale, CancellationToken ct = default)
    {
        if (values.Count == 0) return [];
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var clrProperty = d.FieldToProperty.TryGetValue(property, out var p) ? p : property;
        var column = db.EntityMaintenance.GetDbColumnName(clrProperty, d.EntityType);

        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = column,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", values.Select(v => v?.ToString())),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(values.FirstOrDefault(v => v is not null))
            }
        };
        // AND the extra filter — relation paths/predicates and translatable leaves are pushed down as
        // subqueries by FilterTranslator. SqlSugar ANDs consecutive IConditionalModel entries.
        if (extraFilter is not null)
            conditionals.AddRange(filters.Translate(collection, extraFilter, null, [], queryLocale));

        return await WhereInFilteredDispatcher.For(d.EntityType)(this, conditionals, ct);
    }

    private async Task<IReadOnlyList<object>> WhereInFilteredGenericAsync<T>(
        List<IConditionalModel> conditionals, CancellationToken ct) where T : class, new()
    {
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<object>> QueryWhereInWithDeletedAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        if (values.Count == 0) return [];
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var clrProperty = d.FieldToProperty.TryGetValue(property, out var p) ? p : property;
        var column = db.EntityMaintenance.GetDbColumnName(clrProperty, d.EntityType);
        return await WhereInWithDeletedDispatcher.For(d.EntityType)(this, column, values, ct);
    }

    private async Task<IReadOnlyList<object>> WhereInWithDeletedGenericAsync<T>(
        string column, IReadOnlyList<object> values, CancellationToken ct) where T : class, new()
    {
        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = column,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", values.Select(v => v?.ToString())),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(values.FirstOrDefault(v => v is not null))
            }
        };
        var q = db.Queryable<T>();
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            q = q.ClearFilter<ISoftDeletable>();  // include trashed rows — Cascade must still find/recurse into them
        var rows = await q.Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }
}
