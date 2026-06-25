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
}
