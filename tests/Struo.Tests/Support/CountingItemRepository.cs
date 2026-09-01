using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Tests.Support;

/// <summary>
/// Forwards to a real <see cref="IItemRepository"/> and counts the batched follow-up queries
/// the deep expander issues (<see cref="QueryWhereInAsync"/> + <see cref="QueryEntityWhereInAsync"/>).
/// Used to assert the N+1 batching invariant.
/// </summary>
public sealed class CountingItemRepository(IItemRepository inner) : IItemRepository
{
    private int _whereInCalls;
    public int WhereInCalls => _whereInCalls;
    public void ResetCount() => _whereInCalls = 0;

    public Task<IReadOnlyList<object>> QueryWhereInAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref _whereInCalls);
        return inner.QueryWhereInAsync(collection, property, values, ct);
    }

    public Task<IReadOnlyList<object>> QueryEntityWhereInAsync(
        Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref _whereInCalls);
        return inner.QueryEntityWhereInAsync(entityType, propertyName, values, ct);
    }

    public Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref _whereInCalls);
        return inner.QueryWhereInFilteredAsync(collection, property, values, extraFilter, ct);
    }

    // Delegate every other member straight through (no counting).
    public Task<QueryResult> QueryAsync(
        string collection, QueryModel query, IReadOnlyList<string> searchableFields,
        string? queryLocale = null, DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default) =>
        inner.QueryAsync(collection, query, searchableFields, queryLocale, deleted, ct);

    public Task<object?> GetByIdAsync(string collection, string id,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default) =>
        inner.GetByIdAsync(collection, id, deleted, ct);

    public Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default) =>
        inner.CreateAsync(collection, entity, ct);

    public Task<object?> UpdateAsync(string collection, string id, object entity, CancellationToken ct = default) =>
        inner.UpdateAsync(collection, id, entity, ct);

    public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default) =>
        inner.DeleteAsync(collection, id, ct);

    public Task<bool> SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct = default) =>
        inner.SoftDeleteAsync(collection, id, deletedAt, deletedBy, ct);

    public Task<bool> RestoreAsync(string collection, string id, CancellationToken ct = default) =>
        inner.RestoreAsync(collection, id, ct);

    public Task InTransactionAsync(Func<Task> body, CancellationToken ct = default) =>
        inner.InTransactionAsync(body, ct);

    public Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct = default) =>
        inner.InTransactionAsync(body, ct);

    public Task<IReadOnlyList<object>> QueryIdsAsync(
        string collection, FilterNode leafCondition, CancellationToken ct = default) =>
        inner.QueryIdsAsync(collection, leafCondition, ct);

    public Task SyncManyToManyAsync(
        Type junctionType, string parentFkProperty, string targetFkProperty, string? sortProperty,
        object parentId, IReadOnlyList<object> targetIds, CancellationToken ct = default) =>
        inner.SyncManyToManyAsync(junctionType, parentFkProperty, targetFkProperty, sortProperty, parentId, targetIds, ct);

    public Task<IReadOnlyList<object>> LoadTranslationsAsync(
        Type translationType, string fkProperty, string localeProperty,
        IReadOnlyList<object> parentIds, string? locale, CancellationToken ct = default) =>
        inner.LoadTranslationsAsync(translationType, fkProperty, localeProperty, parentIds, locale, ct);

    public Task<IReadOnlyList<object>> QueryTranslationParentIdsAsync(
        Type translationType, string fkProperty, string localeProperty, string locale,
        FilterNode fieldCondition, CancellationToken ct = default) =>
        inner.QueryTranslationParentIdsAsync(translationType, fkProperty, localeProperty, locale, fieldCondition, ct);

    public Task SyncTranslationsAsync(
        Type translationType, string fkProperty, string localeProperty,
        IReadOnlyList<string> fieldProperties, object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct = default) =>
        inner.SyncTranslationsAsync(translationType, fkProperty, localeProperty, fieldProperties, parentId, perLocale, ct);

    public Task SetForeignKeyNullAsync(
        string sourceCollection, string foreignKeyProperty, object typedId, CancellationToken ct = default) =>
        inner.SetForeignKeyNullAsync(sourceCollection, foreignKeyProperty, typedId, ct);

    public Task DeleteByPropertyAsync(Type entityType, string property, object value, CancellationToken ct = default) =>
        inner.DeleteByPropertyAsync(entityType, property, value, ct);

    public Task<IReadOnlyList<object>> QueryWhereInWithDeletedAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default) =>
        inner.QueryWhereInWithDeletedAsync(collection, property, values, ct);
}
