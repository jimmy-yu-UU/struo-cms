// src/Struo.Infrastructure/Query/WhereInWindowQueries.cs
using System.Linq.Expressions;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

/// <summary>
/// One batched fetch of a to-many relation's rows for a single parent, with the nested filter,
/// sort, offset and limit all applied in SQL. Sorting reuses <see cref="OrderByExpressionBuilder"/>,
/// so it gets the top-level list's typed keys, locale-scoped translatable fields and stable id
/// tie-break.
/// </summary>
internal sealed class WhereInWindowQueries(
    ISqlSugarClient db, IEntityRegistry registry, IMetadataProvider metadata,
    FilterTranslator filters, OrderByExpressionBuilder orderBy)
{
    private static readonly GenericDispatcher<Func<WhereInWindowQueries, List<IConditionalModel>, IReadOnlyList<OrderTerm>, int, int, CancellationToken, Task<IReadOnlyList<object>>>> WindowDispatcher =
        new(typeof(WhereInWindowQueries), nameof(WindowGenericAsync),
            [typeof(List<IConditionalModel>), typeof(IReadOnlyList<OrderTerm>), typeof(int), typeof(int), typeof(CancellationToken)]);

    public async Task<IReadOnlyList<object>?> QueryAsync(WhereInWindowRequest request, CancellationToken ct)
    {
        var (collection, property, values, extraFilter, sort, offset, limit, queryLocale) = request;
        if (values.Count == 0) return [];
        if (SortsTranslatableFieldWithoutLocale(collection, sort, queryLocale)) return null;

        var (d, column) = WhereInQueries.ResolveColumn(db, registry, collection, property);

        var conditionals = new List<IConditionalModel> { WhereInQueries.InCondition(column, values) };
        if (extraFilter is not null)
            conditionals.AddRange(filters.Translate(collection, extraFilter, null, null, [], queryLocale));

        var terms = orderBy.BuildOrderBy(sort, d, collection, queryLocale);
        return await WindowDispatcher.For(d.EntityType)(this, conditionals, terms, offset, limit, ct);
    }

    // A translatable sort key needs a locale to pick its sidecar row; without one the typed builder
    // has no column to order by, so the caller keeps the in-memory path.
    private bool SortsTranslatableFieldWithoutLocale(
        string collection, IReadOnlyList<SortField> sort, string? queryLocale)
    {
        if (queryLocale is not null || sort.Count == 0) return false;
        var fields = metadata.GetCollection(collection)?.Translation?.Fields;
        return fields is not null
            && sort.Any(s => fields.Any(f => string.Equals(f, s.Field, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<IReadOnlyList<object>> WindowGenericAsync<T>(
        List<IConditionalModel> conditionals, IReadOnlyList<OrderTerm> terms, int offset, int limit,
        CancellationToken ct) where T : class, new()
    {
        var queryable = db.Queryable<T>().Where(conditionals);
        foreach (var term in terms)
            queryable = queryable.OrderBy((Expression<Func<T, object>>)term.Selector, term.Direction);
        var rows = await queryable.Skip(offset).Take(limit).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }
}
