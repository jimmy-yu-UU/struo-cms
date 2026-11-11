// src/Struo.Infrastructure/Query/WhereInQueries.cs
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal sealed class WhereInQueries(ISqlSugarClient db, IEntityRegistry registry, FilterTranslator filters)
{
    private static readonly GenericDispatcher<Func<WhereInQueries, List<IConditionalModel>, int?, CancellationToken, Task<IReadOnlyList<object>>>> WhereInFilteredDispatcher =
        new(typeof(WhereInQueries), nameof(WhereInFilteredGenericAsync), [typeof(List<IConditionalModel>), typeof(int?), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<WhereInQueries, List<IConditionalModel>, CancellationToken, Task<IReadOnlyList<object>>>> WhereInWithDeletedDispatcher =
        new(typeof(WhereInQueries), nameof(WhereInWithDeletedGenericAsync), [typeof(List<IConditionalModel>), typeof(CancellationToken)]);

    public Task<IReadOnlyList<object>> QueryWhereInAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        // Map camelCase field name -> CLR property name (fall back to the raw name, e.g. "id").
        var clrProperty = d.FieldToProperty.TryGetValue(property, out var p) ? p : property;
        return QueryEntityWhereInAsync(d.EntityType, clrProperty, values, ct: ct);
    }

    public async Task<IReadOnlyList<object>> QueryEntityWhereInAsync(
        Type entityType, string propertyName, IReadOnlyList<object> values,
        int? maxRows = null, CancellationToken ct = default)
    {
        // Empty value set -> never emit an "IN ()"; return empty.
        if (values.Count == 0) return [];

        // The typed In primitive (not Equal, which binds FieldValue as text and fails as
        // "bigint = text" 42883 on PostgreSQL); the values are checked against the column's CLR type.
        var conditionals = new List<IConditionalModel> { InCondition(TargetOf(db, entityType, propertyName), values) };
        return await WhereInFilteredDispatcher.For(entityType)(this, conditionals, maxRows, ct);
    }

    // Typed Take; SqlSugar emits the backend's own row-limit form (LIMIT / TOP / FETCH FIRST).
    private static ISugarQueryable<T> Capped<T>(ISugarQueryable<T> query, int? maxRows) =>
        maxRows is { } n ? query.Take(n) : query;

    // The collection's descriptor and the column of one of its fields (a camelCase field name,
    // falling back to the raw name, e.g. "id") with the CLR type values are checked against.
    internal static (EntityDescriptor Descriptor, ValueTarget Target) ResolveColumn(
        ISqlSugarClient db, IEntityRegistry registry, string collection, string property)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var clrProperty = d.FieldToProperty.TryGetValue(property, out var p) ? p : property;
        return (d, TargetOf(db, d.EntityType, clrProperty));
    }

    internal static ValueTarget TargetOf(ISqlSugarClient db, Type entityType, string clrProperty)
    {
        var info = entityType.GetProperty(clrProperty, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException($"'{entityType.Name}' has no property '{clrProperty}'.");
        var column = db.EntityMaintenance.GetDbColumnName(info.Name, entityType);
        return new ValueTarget(column, column, info.PropertyType, db.CurrentConnectionConfig.DbType);
    }

    // `column IN (values)`; shared by the filtered and the windowed fetch. Every value must already be of
    // the column's CLR type: callers pass ids read from the database or parsed to the key type.
    internal static IConditionalModel InCondition(ValueTarget target, IReadOnlyList<object> values) =>
        ValueConditions.BuildInternal(target, QueryOperator.In, values)!;

    public async Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, string? queryLocale, int? maxRows = null, CancellationToken ct = default)
    {
        if (values.Count == 0) return [];
        var (d, target) = ResolveColumn(db, registry, collection, property);

        var conditionals = new List<IConditionalModel> { InCondition(target, values) };
        // AND the extra filter — relation paths/predicates and translatable leaves are pushed down as
        // subqueries by FilterTranslator. SqlSugar ANDs consecutive IConditionalModel entries.
        if (extraFilter is not null)
            conditionals.AddRange(filters.Translate(collection, extraFilter, null, null, [], queryLocale));

        return await WhereInFilteredDispatcher.For(d.EntityType)(this, conditionals, maxRows, ct);
    }

    private async Task<IReadOnlyList<object>> WhereInFilteredGenericAsync<T>(
        List<IConditionalModel> conditionals, int? maxRows, CancellationToken ct) where T : class, new()
    {
        var rows = await Capped(db.Queryable<T>().Where(conditionals), maxRows).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<object>> QueryWhereInWithDeletedAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        if (values.Count == 0) return [];
        var (d, target) = ResolveColumn(db, registry, collection, property);
        var conditionals = new List<IConditionalModel> { InCondition(target, values) };
        return await WhereInWithDeletedDispatcher.For(d.EntityType)(this, conditionals, ct);
    }

    private async Task<IReadOnlyList<object>> WhereInWithDeletedGenericAsync<T>(
        List<IConditionalModel> conditionals, CancellationToken ct) where T : class, new()
    {
        var q = db.Queryable<T>();
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            q = q.ClearFilter<ISoftDeletable>();  // include trashed rows — Cascade must still find/recurse into them
        var rows = await q.Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }
}
