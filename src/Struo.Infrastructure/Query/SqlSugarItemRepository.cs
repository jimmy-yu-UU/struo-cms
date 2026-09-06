// src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs
using System.Reflection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Query.Write;
using Struo.Domain.Auditing;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

public sealed class SqlSugarItemRepository(
    ISqlSugarClient db,
    IEntityRegistry registry,
    IRelationshipGraph graph,
    IMetadataProvider metadata,
    StruoQueryOptions options,
    ILogger<SqlSugarItemRepository>? logger = null) : IItemRepository
{
    // This class is a facade over IItemRepository. Two supporting types back it — GenericDispatcher
    // (the generic-dispatch primitive used below) and RepositoryHelpers — plus the ten instance
    // fields declared next: TransactionRunner, FilterTranslator, WhereInQueries, SoftDeleteOps,
    // PurgeOps, ManyToManySync, TranslationStore, OrderByExpressionBuilder, FacetQueries and
    // AggregateQueries. The facade itself still implements Query/GetById/Create/Update/Delete (the
    // optimistic-concurrency check, the identity-PK read-back and the offset/limit paging all live
    // in this file); every other IItemRepository member just delegates to one of eight fields —
    // transactions, whereIn, softDelete, purge, manyToMany, translations, facets and aggregates;
    // orderByBuilder is not a delegation target, it is used inside QueryAsync
    // (orderByBuilder.BuildOrderBy(...)), and filters is used inline in QueryAsync
    // (filters.Translate(...)) rather than delegated to. Among the ten fields,
    // OrderByExpressionBuilder is the only one registered as a scoped DI service — kept registered
    // for a future direct consumer, though none exists today. The other nine are not registered:
    // nothing outside this class needs them, and the test suite constructs SqlSugarItemRepository
    // directly with this exact 5-arg constructor (26 test files do), so any new required
    // constructor parameter is not an option — `logger` above is optional (defaults to null) for
    // exactly that reason. manyToMany, translations and facets take `new TransactionRunner(db)`
    // rather than the `transactions` field below because a field initializer cannot reference another
    // instance field (CS0236); TransactionRunner holds no state beyond `db`, so the second
    // instance behaves identically to sharing the first. whereIn, facets and aggregates similarly
    // take a second, separate `new FilterTranslator(...)` rather than the `filters` field below it
    // for the same CS0236 reason; FilterTranslator holds no state beyond its constructor arguments,
    // so the second instance behaves identically to sharing the first.
    private readonly OrderByExpressionBuilder orderByBuilder = new(db, registry, graph, metadata, options);
    private readonly TransactionRunner transactions = new(db);
    private readonly FilterTranslator filters = new(db, graph, metadata, registry, options);
    private readonly WhereInQueries whereIn = new(db, registry, new FilterTranslator(db, graph, metadata, registry, options));
    private readonly SoftDeleteOps softDelete = new(db, registry);
    private readonly PurgeOps purge = new(db, registry);
    private readonly ManyToManySync manyToMany = new(db, new TransactionRunner(db), logger);
    private readonly TranslationStore translations = new(db, new TransactionRunner(db));
    private readonly FacetQueries facets = new(db, registry, graph, metadata,
        new FilterTranslator(db, graph, metadata, registry, options), new TranslationStore(db, new TransactionRunner(db)));
    private readonly AggregateQueries aggregates = new(db, registry, new FilterTranslator(db, graph, metadata, registry, options));

    private static readonly GenericDispatcher<Func<SqlSugarItemRepository, List<IConditionalModel>, string?, int, int, DeletedFilter, CancellationToken, Task<QueryResult>>> RunQueryDispatcher =
        new(typeof(SqlSugarItemRepository), nameof(RunQueryAsync),
            [typeof(List<IConditionalModel>), typeof(string), typeof(int), typeof(int), typeof(DeletedFilter), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<SqlSugarItemRepository, object, DeletedFilter, CancellationToken, Task<object?>>> GetByIdDispatcher =
        new(typeof(SqlSugarItemRepository), nameof(GetByIdGenericAsync), [typeof(object), typeof(DeletedFilter), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<SqlSugarItemRepository, object, CancellationToken, Task<object>>> CreateDispatcher =
        new(typeof(SqlSugarItemRepository), nameof(CreateGenericAsync), [typeof(object), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<SqlSugarItemRepository, object, CancellationToken, Task>> UpdateDispatcher =
        new(typeof(SqlSugarItemRepository), nameof(UpdateGenericAsync), [typeof(object), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<SqlSugarItemRepository, object, CancellationToken, Task>> DeleteDispatcher =
        new(typeof(SqlSugarItemRepository), nameof(DeleteGenericAsync), [typeof(object), typeof(CancellationToken)]);

    public async Task<QueryResult> QueryAsync(string collection, QueryModel query,
        IReadOnlyList<string> searchableFields, string? queryLocale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var conditionals = filters.Translate(collection, query.Filter, query.Search, searchableFields, queryLocale);
        var orderBy = orderByBuilder.BuildOrderBy(query.Sort, d, collection, queryLocale);
        return await RunQueryDispatcher.For(d.EntityType)(this, conditionals, orderBy, query.Limit, query.Offset, deleted, ct);
    }

    private async Task<QueryResult> RunQueryAsync<T>(
        List<IConditionalModel> conditionals, string? orderBy, int limit, int offset,
        DeletedFilter deleted, CancellationToken ct)
        where T : class, new()
    {
        // True offset/limit windowing: offset is an absolute row count and need NOT be a multiple of
        // limit. The old code turned offset into a 1-based page index by integer division, which
        // silently returned the wrong window for any non-page-aligned offset (e.g. offset=25,limit=20
        // skipped 20 instead of 25). Count + Skip/Take gives the exact window.
        var total = await DeletedScope.Root<T>(db, conditionals, deleted).CountAsync(ct);
        var queryable = DeletedScope.Root<T>(db, conditionals, deleted);
        if (!string.IsNullOrWhiteSpace(orderBy)) queryable = queryable.OrderBy(orderBy);
        var rows = await queryable.Skip(offset).Take(limit).ToListAsync(ct);
        return new QueryResult(rows.Cast<object>().ToList(), total);
    }

    public async Task<object?> GetByIdAsync(string collection, string id,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        return await GetByIdDispatcher.For(d.EntityType)(this, RepositoryHelpers.ConvertId(id, d), deleted, ct);
    }

    private async Task<object?> GetByIdGenericAsync<T>(object id, DeletedFilter deleted, CancellationToken ct) where T : class, new()
    {
        var q = db.Queryable<T>();
        if (deleted != DeletedFilter.Exclude && typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            q = q.ClearFilter<ISoftDeletable>();
        // InSingleAsync has no CancellationToken overload; In(id).FirstAsync(ct) forwards the token.
        return await q.In(id).FirstAsync(ct);
    }

    public Task InTransactionAsync(Func<Task> body, CancellationToken ct = default) =>
        transactions.InTransactionAsync(body, ct);

    public Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct = default) =>
        transactions.InTransactionAsync(body, ct);

    public async Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var pk = d.EntityType.GetProperty(d.IdProperty)!;
        if (pk.PropertyType == typeof(Guid) && pk.GetValue(entity) is Guid cur && cur == Guid.Empty)
            pk.SetValue(entity, Guid.CreateVersion7());
        return await CreateDispatcher.For(d.EntityType)(this, entity, ct);
    }

    // ExecuteReturnEntityAsync has no CancellationToken overload (5.1.4.216); its only effect beyond
    // ExecuteCommandAsync is to back-populate a DB-generated identity PK onto the entity. Most
    // collections use client-generated Guid PKs (set in CreateAsync above) — nothing to read back,
    // so ExecuteCommandAsync(ct) + returning the same instance is equivalent while forwarding the
    // token. Identity-PK collections (e.g. Language: long IsIdentity) DO need the read-back, so
    // they keep ExecuteReturnEntityAsync and — like the tran APIs — cannot forward ct.
    private async Task<object> CreateGenericAsync<T>(object entity, CancellationToken ct) where T : class, new()
    {
        var hasIdentityPk = db.EntityMaintenance.GetEntityInfo(typeof(T))
            .Columns.Any(c => c.IsPrimarykey && c.IsIdentity);
        if (hasIdentityPk)
        {
            ct.ThrowIfCancellationRequested();
            return (await db.Insertable((T)entity).ExecuteReturnEntityAsync())!;
        }

        await db.Insertable((T)entity).ExecuteCommandAsync(ct);
        return entity;
    }

    public async Task<object?> UpdateAsync(string collection, string id, object entity,
        CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var existing = await GetByIdAsync(collection, id, ct: ct);
        if (existing is null) return null;

        // Clone so the caller's object is never mutated.
        var clone = CloneEntity(entity, d.EntityType);
        d.EntityType.GetProperty(d.IdProperty)!.SetValue(clone, RepositoryHelpers.ConvertId(id, d));

        await UpdateDispatcher.For(d.EntityType)(this, clone, ct);
        return await GetByIdAsync(collection, id, ct: ct);
    }

    private async Task UpdateGenericAsync<T>(object entity, CancellationToken ct) where T : class, new()
    {
        // Optimistic concurrency: for auditable collection entities, bump the version and update
        // WHERE id = ? AND version = expected. If a concurrent writer already advanced the version, zero
        // rows match and we surface a 409 instead of silently overwriting their change. The id + version
        // are bound as typed parameters (real Guid / long), so PG's uuid column matches correctly.
        if (entity is Struo.Domain.Auditing.AuditableEntity ae)
        {
            var expected = ae.Version;
            ae.Version = expected + 1;
            var idColumn = db.EntityMaintenance.GetDbColumnName(nameof(Struo.Domain.Auditing.AuditableEntity.Id), typeof(T));
            var versionColumn = db.EntityMaintenance.GetDbColumnName(nameof(Struo.Domain.Auditing.AuditableEntity.Version), typeof(T));
            var affected = await db.Updateable((T)entity)
                .Where($"{idColumn} = @__ocId AND {versionColumn} = @__ocVersion",
                    new { __ocId = ae.Id, __ocVersion = expected })
                .ExecuteCommandAsync(ct);
            if (affected == 0)
                throw new Struo.Domain.Query.ConcurrencyConflictException(
                    "The record was modified by someone else since you loaded it. Reload and try again.");
            return;
        }

        await db.Updateable((T)entity).ExecuteCommandAsync(ct);
    }

    public async Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        // Look up regardless of the soft-delete filter: a hard delete (purge) must be able to
        // remove a row that is already trashed (DeletedAt set), not just a live one.
        var existing = await GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (existing is null) return false;

        await DeleteDispatcher.For(d.EntityType)(this, RepositoryHelpers.ConvertId(id, d), ct);
        return true;
    }

    private async Task DeleteGenericAsync<T>(object id, CancellationToken ct) where T : class, new() =>
        await db.Deleteable<T>().In(id).ExecuteCommandAsync(ct);

    public Task SetForeignKeyNullAsync(
        string sourceCollection, string foreignKeyProperty, object typedId, CancellationToken ct = default) =>
        purge.SetForeignKeyNullAsync(sourceCollection, foreignKeyProperty, typedId, ct);

    public Task DeleteByPropertyAsync(
        Type entityType, string property, object value, CancellationToken ct = default) =>
        purge.DeleteByPropertyAsync(entityType, property, value, ct);

    public Task<IReadOnlyList<object>> QueryWhereInWithDeletedAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default) =>
        whereIn.QueryWhereInWithDeletedAsync(collection, property, values, ct);

    public Task<bool> SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct = default) =>
        softDelete.SoftDeleteAsync(collection, id, deletedAt, deletedBy, ct);

    public Task<bool> RestoreAsync(string collection, string id, CancellationToken ct = default) =>
        softDelete.RestoreAsync(collection, id, ct);

    public Task<IReadOnlyList<object>> QueryWhereInAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default) =>
        whereIn.QueryWhereInAsync(collection, property, values, ct);

    public Task<IReadOnlyList<object>> QueryEntityWhereInAsync(
        Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default) =>
        whereIn.QueryEntityWhereInAsync(entityType, propertyName, values, ct);

    public Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, string? queryLocale, CancellationToken ct = default) =>
        whereIn.QueryWhereInFilteredAsync(collection, property, values, extraFilter, queryLocale, ct);

    public Task SyncManyToManyAsync(
        Type junctionType,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<JunctionLink> links,
        CancellationToken ct = default) =>
        manyToMany.SyncManyToManyAsync(junctionType, parentFkProperty, targetFkProperty, sortProperty, parentId, links, ct);

    public Task<IReadOnlyList<object>> LoadTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<object> parentIds,
        string? locale,
        CancellationToken ct = default) =>
        translations.LoadTranslationsAsync(translationType, fkProperty, localeProperty, parentIds, locale, ct);

    public Task SyncTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct = default) =>
        translations.SyncTranslationsAsync(translationType, fkProperty, localeProperty, fieldProperties, parentId, perLocale, ct);

    public Task<IReadOnlyList<FacetBucket>> FacetAsync(
        string collection, QueryModel prunedQuery, ResolvedFacetPath facet, IReadOnlyList<string> searchableFields,
        string? queryLocale, DeletedFilter deleted, int maxValues, CancellationToken ct = default) =>
        facets.FacetAsync(collection, prunedQuery, facet, searchableFields, queryLocale, deleted, maxValues, ct);

    public Task<AggregateResult> AggregateAsync(
        string collection, QueryModel query, AggregateSpec spec, IReadOnlyList<string> searchableFields,
        string? queryLocale, DeletedFilter deleted, CancellationToken ct = default) =>
        aggregates.AggregateAsync(collection, query, spec, searchableFields, queryLocale, deleted, ct);

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

}
