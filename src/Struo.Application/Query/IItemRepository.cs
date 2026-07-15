using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record QueryResult(IReadOnlyList<object> Rows, int Total);

public interface IItemRepository
{
    /// <summary>
    /// Executes a query against <paramref name="collection"/> with the given filter/sort/search.
    /// <paramref name="queryLocale"/> is used to resolve translatable fields in sort and search
    /// against the translation sidecar at that locale; pass <c>null</c> to skip locale-aware paths.
    /// </summary>
    Task<QueryResult> QueryAsync(string collection, QueryModel query, IReadOnlyList<string> searchableFields,
        string? queryLocale = null, DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default);
    Task<object?> GetByIdAsync(string collection, string id,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default);
    Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default);
    Task<object?> UpdateAsync(string collection, string id, object entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>Stamps DeletedAt/DeletedBy on the row (soft delete). Returns false if the id is unknown.
    /// Operates against the id regardless of the soft-delete filter (Updateable is not subject to the
    /// query filter), so an already-trashed row is still located — idempotent.</summary>
    Task<bool> SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct = default);
    /// <summary>Clears DeletedAt/DeletedBy (restore). Returns false if the id is unknown. Filter-cleared.</summary>
    Task<bool> RestoreAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="body"/> inside a single database transaction: everything it writes
    /// commits together or rolls back together. Used to make an aggregate write (parent row + M2M
    /// junctions + translation sidecars) atomic. Nesting-safe — if a transaction is already open on
    /// the (scoped) connection, <paramref name="body"/> joins it instead of opening a new one.
    /// </summary>
    Task InTransactionAsync(Func<Task> body, CancellationToken ct = default);

    /// <summary>
    /// Returns all rows of <paramref name="collection"/> whose <paramref name="property"/>
    /// (a camelCase field name, e.g. <c>"id"</c>) is in <paramref name="values"/>.
    /// Empty <paramref name="values"/> returns an empty list without issuing a query.
    /// </summary>
    Task<IReadOnlyList<object>> QueryWhereInAsync(string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="QueryWhereInAsync"/> but ANDs an additional own-collection filter
    /// (already relation-rewritten to own columns) into the batched WHERE. Used by nested-list
    /// expansion (8c.3b) to push a to-many list's filter into the single batched fetch.
    /// </summary>
    Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, CancellationToken ct = default);

    /// <summary>
    /// Returns all rows of the given CLR <paramref name="entityType"/> (e.g. a junction type)
    /// whose <paramref name="propertyName"/> (a CLR property name) is in <paramref name="values"/>.
    /// Empty <paramref name="values"/> returns an empty list without issuing a query.
    /// </summary>
    Task<IReadOnlyList<object>> QueryEntityWhereInAsync(Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default);

    /// <summary>
    /// Returns the primary-key values of all rows of <paramref name="collection"/> matching a
    /// single own-collection (non-dotted) <paramref name="leafCondition"/>. Used by the
    /// cross-relation filter resolver as the leaf step of two-phase id-resolution.
    /// </summary>
    Task<IReadOnlyList<object>> QueryIdsAsync(string collection, FilterNode leafCondition, CancellationToken ct = default);

    /// <summary>
    /// Replaces all junction rows for <paramref name="parentId"/> on the given
    /// <paramref name="junctionType"/> so that exactly <paramref name="targetIds"/> are linked.
    /// Deletes all existing rows whose parent FK matches <paramref name="parentId"/>, then inserts
    /// one new row per target id in order. If <paramref name="sortProperty"/> is non-null it is set
    /// to the list index (0-based) on each inserted row.
    /// </summary>
    Task SyncManyToManyAsync(
        Type junctionType,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<object> targetIds,
        CancellationToken ct = default);

    /// <summary>
    /// Loads sidecar translation rows of <paramref name="translationType"/> whose
    /// <paramref name="fkProperty"/> (CLR name, e.g. <c>"ArticleId"</c>) is in
    /// <paramref name="parentIds"/>. When <paramref name="locale"/> is non-null, only rows whose
    /// <paramref name="localeProperty"/> equals it (case-insensitive) are returned. Empty
    /// <paramref name="parentIds"/> returns an empty list without issuing a query.
    /// </summary>
    Task<IReadOnlyList<object>> LoadTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<object> parentIds,
        string? locale,
        CancellationToken ct = default);

    /// <summary>
    /// Queries the translation sidecar table for rows matching <c>Locale == locale</c> AND the given
    /// <paramref name="fieldCondition"/> (which references a CLR property name on the translation
    /// entity), then returns the distinct FK (parent id) values from those rows.
    /// Used by the translatable filter resolver to produce parent-id sets at a given locale.
    /// </summary>
    Task<IReadOnlyList<object>> QueryTranslationParentIdsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        string locale,
        FilterNode fieldCondition,
        CancellationToken ct = default);

    /// <summary>
    /// Upserts translation rows for a single parent. For each entry in <paramref name="perLocale"/>
    /// (locale → camelField → value), deletes the existing row for that
    /// <c>(parentId, locale)</c> and inserts a fresh one with <paramref name="fieldProperties"/>
    /// (CLR property names) set from the provided values. Locales not present in
    /// <paramref name="perLocale"/> are left untouched (partial updates supported). The whole
    /// operation is transaction-wrapped.
    /// </summary>
    Task SyncTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct = default);

    // ── Purge referential-integrity primitives (DB-1/DB-2, Task 5) ─────────────
    // Default implementations THROW rather than silently no-op: a second implementation that forgot
    // to override one of these would otherwise silently orphan referential rows on purge — the exact
    // defect class DB-1/DB-2 exist to eliminate. The defaults still keep pre-existing test doubles
    // compiling; a double that actually exercises purge must override them explicitly.

    /// <summary>
    /// Sets every row in <paramref name="sourceCollection"/> whose <paramref name="foreignKeyProperty"/>
    /// (camelCase field name) equals <paramref name="typedId"/> to NULL (OnDelete.SetNull). Runs via
    /// SqlSugar's <c>Updateable&lt;T&gt;</c>, which is NOT subject to the global soft-delete query
    /// filter, so an already-trashed source row is still found and nulled.
    /// </summary>
    Task SetForeignKeyNullAsync(
        string sourceCollection, string foreignKeyProperty, object typedId, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IItemRepository.SetForeignKeyNullAsync must be overridden by implementations that support purge integrity.");

    /// <summary>
    /// Deletes every row of the given CLR <paramref name="entityType"/> (a junction or translation
    /// sidecar type — not necessarily a registered collection) whose <paramref name="property"/>
    /// (CLR property name) equals <paramref name="value"/>. Used by purge to clean up M2M junction
    /// rows and translation sidecar rows for a deleted item.
    /// </summary>
    Task DeleteByPropertyAsync(
        Type entityType, string property, object value, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IItemRepository.DeleteByPropertyAsync must be overridden by implementations that support purge integrity.");

    /// <summary>
    /// Like <see cref="QueryWhereInAsync"/> but bypasses the soft-delete query filter, so an
    /// already-trashed row of <paramref name="collection"/> that still references the purge target
    /// is found too (Cascade must recurse into it, not silently skip it).
    /// </summary>
    Task<IReadOnlyList<object>> QueryWhereInWithDeletedAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IItemRepository.QueryWhereInWithDeletedAsync must be overridden by implementations that support purge integrity.");
}
