using Struo.Application.Query.Write;
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
    /// <summary>Clears DeletedAt/DeletedBy (restore) via an atomic <c>WHERE deletedat IS NOT NULL</c> UPDATE.
    /// Returns false if the id is unknown OR the row is already live (nothing to restore); callers use the
    /// affected-rows result to decide whether to record a "restore" revision. Filter-cleared.</summary>
    Task<bool> RestoreAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="body"/> inside a single database transaction: everything it writes
    /// commits together or rolls back together, making an aggregate write (parent row + M2M
    /// junctions + translation sidecars) atomic. Nesting-safe — if a transaction is already open on
    /// the (scoped) connection, <paramref name="body"/> joins it instead of opening a new one.
    /// </summary>
    Task InTransactionAsync(Func<Task> body, CancellationToken ct = default);

    /// <summary>
    /// Same transaction semantics as <see cref="InTransactionAsync(Func{Task},CancellationToken)"/>,
    /// but flows <paramref name="body"/>'s result out instead of requiring a captured mutable.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct = default);

    /// <summary>
    /// Returns all rows of <paramref name="collection"/> whose <paramref name="property"/>
    /// (a camelCase field name, e.g. <c>"id"</c>) is in <paramref name="values"/>.
    /// Empty <paramref name="values"/> returns an empty list without issuing a query.
    /// </summary>
    Task<IReadOnlyList<object>> QueryWhereInAsync(string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="QueryWhereInAsync"/> but ANDs an additional filter into the batched WHERE —
    /// own-collection leaves, relation paths/predicates and translatable leaves (at <paramref name="queryLocale"/>)
    /// are all pushed down as SQL subqueries. Used by nested-list expansion to push a to-many list's
    /// filter into the single batched fetch.
    /// </summary>
    Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, string? queryLocale, CancellationToken ct = default);

    /// <summary>
    /// Returns all rows of the given CLR <paramref name="entityType"/> (e.g. a junction type)
    /// whose <paramref name="propertyName"/> (a CLR property name) is in <paramref name="values"/>.
    /// Empty <paramref name="values"/> returns an empty list without issuing a query.
    /// </summary>
    Task<IReadOnlyList<object>> QueryEntityWhereInAsync(Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default);

    /// <summary>
    /// Diffs and patches the junction rows for <paramref name="parentId"/> on the given
    /// <paramref name="junctionType"/> so that exactly the targets named in <paramref name="links"/>
    /// remain linked, in that order. Rows for targets absent from <paramref name="links"/> are
    /// deleted; rows for targets newly present are inserted; rows for targets already present keep
    /// their primary key — they are never deleted and reinserted. If <paramref name="sortProperty"/>
    /// is non-null it is set to each link's list index (0-based). A <see cref="JunctionLink.Payload"/>
    /// dictionary merges only the given keys (CLR property names) into the row; a null payload
    /// (<see cref="JunctionLink.Bare"/>) leaves an existing row's payload untouched and leaves a new
    /// row's payload columns at their default. If the existing data holds more than one row for the
    /// same target (legacy duplicate), the first is kept and the rest are deleted, with one warning
    /// logged naming the table and the count removed.
    /// </summary>
    Task SyncManyToManyAsync(
        Type junctionType,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<JunctionLink> links,
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

    // ── Purge referential-integrity primitives ─────────────
    // Default implementations THROW rather than silently no-op: a second implementation that forgot
    // to override one of these would otherwise silently orphan referential rows on purge — the exact
    // defect class these primitives exist to eliminate. The defaults still keep pre-existing test doubles
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

    /// <summary>
    /// Computes value/count buckets for one resolved facet path over <see cref="FacetRequest.Collection"/>,
    /// scoped by <see cref="FacetRequest.PrunedQuery"/> (the facet's own current selection already pruned
    /// out — see <see cref="FacetFilterPruner"/>). <see cref="FacetRequest.Deleted"/> governs the root rows
    /// only; a related/junction side queried for a relation or leaf facet always keeps the soft-delete
    /// floor. Buckets are ordered by count descending then value ascending and capped at
    /// <see cref="FacetRequest.MaxValues"/> rows.
    /// </summary>
    Task<IReadOnlyList<FacetBucket>> FacetAsync(FacetRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IItemRepository.FacetAsync must be overridden by implementations that support facets.");

    /// <summary>
    /// Computes count/sum/min/max/avg over <paramref name="collection"/>, scoped by
    /// <paramref name="query"/>'s filter/search and <paramref name="deleted"/> on the root rows.
    /// <see cref="AggregateResult.Values"/>'s outer keys are only the ops present in
    /// <paramref name="spec"/>; the inner keys keep the field spelling the caller requested.
    /// </summary>
    Task<AggregateResult> AggregateAsync(
        string collection, QueryModel query, AggregateSpec spec, IReadOnlyList<string> searchableFields,
        string? queryLocale, DeletedFilter deleted, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IItemRepository.AggregateAsync must be overridden by implementations that support aggregates.");
}
