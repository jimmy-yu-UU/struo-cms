using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record QueryResult(IReadOnlyList<object> Rows, int Total);

public interface IItemRepository
{
    Task<QueryResult> QueryAsync(string collection, QueryModel query, IReadOnlyList<string> searchableFields, CancellationToken ct = default);
    Task<object?> GetByIdAsync(string collection, string id, CancellationToken ct = default);
    Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default);
    Task<object?> UpdateAsync(string collection, string id, object entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>
    /// Returns all rows of <paramref name="collection"/> whose <paramref name="property"/>
    /// (a camelCase field name, e.g. <c>"id"</c>) is in <paramref name="values"/>.
    /// Empty <paramref name="values"/> returns an empty list without issuing a query.
    /// </summary>
    Task<IReadOnlyList<object>> QueryWhereInAsync(string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default);

    /// <summary>
    /// Returns all rows of the given CLR <paramref name="entityType"/> (e.g. a junction type)
    /// whose <paramref name="propertyName"/> (a CLR property name) is in <paramref name="values"/>.
    /// Empty <paramref name="values"/> returns an empty list without issuing a query.
    /// </summary>
    Task<IReadOnlyList<object>> QueryEntityWhereInAsync(Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default);
}
