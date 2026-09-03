// src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Auditing;
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
    // ORDER BY SQL construction (plain / translatable / relation-path) is extracted verbatim
    // into OrderByExpressionBuilder. Built from this repository's own deps rather than injected: the
    // existing test suite constructs SqlSugarItemRepository directly with this exact 5-arg signature,
    // and the pure-refactor acceptance gate forbids changing any test line. The builder is also
    // registered as a scoped DI service for future direct consumers.
    private readonly OrderByExpressionBuilder orderByBuilder = new(db, registry, graph, metadata, options);
    private readonly TransactionRunner transactions = new(db);
    private readonly WhereInQueries whereIn = new(db, registry);
    private readonly SoftDeleteOps softDelete = new(db, registry);
    private readonly PurgeOps purge = new(db, registry);
    private readonly ManyToManySync manyToMany = new(db, new TransactionRunner(db));
    // Cached generic method definitions — resolved once at class load, pinned by parameter-type signature.
    // Each private helper is async and returns a KNOWN Task<T> so the dispatcher can cast before awaiting.

    private static readonly MethodInfo RunQueryAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(RunQueryAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(List<IConditionalModel>), typeof(string), typeof(int), typeof(int), typeof(DeletedFilter), typeof(CancellationToken)])!;

    private static readonly MethodInfo GetByIdGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(GetByIdGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(DeletedFilter), typeof(CancellationToken)])!;

    private static readonly MethodInfo CreateGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(CreateGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(CancellationToken)])!;

    private static readonly MethodInfo UpdateGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(UpdateGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(CancellationToken)])!;

    private static readonly MethodInfo DeleteGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(DeleteGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object), typeof(CancellationToken)])!;

    private static readonly MethodInfo LoadTranslationsGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(LoadTranslationsGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(IReadOnlyList<object>), typeof(string), typeof(string), typeof(CancellationToken)])!;

    private static readonly MethodInfo QueryTranslationParentIdsGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(QueryTranslationParentIdsGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(List<IConditionalModel>), typeof(CancellationToken)])!;

    // Second-level dispatcher (T fixed by the call above, TFk resolved at runtime from the FK
    // property's actual CLR type) so the FK-only SQL projection below can be expressed as a genuinely
    // typed `Expression<Func<T, TFk>>` — SqlSugar's Select() does not translate a boxed
    // `Convert(member, object)` lambda into a single-column projection.
    private static readonly MethodInfo QueryTranslationFkSelectGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(QueryTranslationFkSelectGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(List<IConditionalModel>), typeof(LambdaExpression), typeof(CancellationToken)])!;

    private static readonly MethodInfo SyncTranslationsGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(SyncTranslationsGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(object),
             typeof(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>), typeof(CancellationToken)])!;

    // Per-dispatcher open-instance delegate caches, keyed by closed entity type.
    // Replaces per-call MakeGenericMethod().Invoke(this, [...]) — the MethodInfo.MakeGenericMethod cost
    // is paid once per (dispatcher, type) and the reflection *invoke* on every subsequent request is
    // replaced by a direct delegate call. The *Def MethodInfo fields above seed CreateDelegate; each
    // delegate's FIRST parameter is the receiver (open-instance form), and the remaining parameters +
    // return type match the corresponding private generic helper's EXACT closed signature. All helpers
    // are async (or return the Task directly), so exceptions surface on the awaited Task identically to
    // the old Invoke form (no TargetInvocationException wrapping to preserve).

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, List<IConditionalModel>, string?, int, int, DeletedFilter, CancellationToken, Task<QueryResult>>> RunQueryInvokers = new();

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, object, DeletedFilter, CancellationToken, Task<object?>>> GetByIdInvokers = new();

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, object, CancellationToken, Task<object>>> CreateInvokers = new();

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, object, CancellationToken, Task>> UpdateInvokers = new();

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, object, CancellationToken, Task>> DeleteInvokers = new();

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, string, IReadOnlyList<object>, string, string?, CancellationToken, Task<IReadOnlyList<object>>>> LoadTranslationsInvokers = new();

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, string, string, string, string, List<IConditionalModel>, CancellationToken, Task<IReadOnlyList<object>>>> QueryTranslationParentIdsInvokers = new();

    // Keyed by (translation entity type, FK CLR type) — two independent type parameters, so a
    // single-Type ConcurrentDictionary (the pattern every other Invokers cache above uses) doesn't fit.
    private static readonly ConcurrentDictionary<(Type EntityType, Type FkType),
        Func<SqlSugarItemRepository, List<IConditionalModel>, LambdaExpression, CancellationToken, Task<IReadOnlyList<object>>>> QueryTranslationFkSelectInvokers = new();

    private static readonly ConcurrentDictionary<Type,
        Func<SqlSugarItemRepository, string, string, string, IReadOnlyList<string>, object, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>, CancellationToken, Task>> SyncTranslationsInvokers = new();

    public async Task<QueryResult> QueryAsync(string collection, QueryModel query,
        IReadOnlyList<string> searchableFields, string? queryLocale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
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
                    // Checked INSIDE the loop, not once after it: the union across N translatable
                    // searchable fields is what has to stay bounded, and a per-field check after the
                    // fact would already have every field's ids in memory.
                    ResolvedIdSetGuard.EnsureCount(
                        allParentIds.Count, options.MaxResolvedFilterIds, $"search on '{field}'");
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
                        FieldValue = string.Join(",", allParentIds),
                        CSharpTypeName = RepositoryHelpers.TypeNameOfProperty(d.EntityType, d.IdProperty)  // PK is Guid -> uuid on PG
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

        var orderBy = orderByBuilder.BuildOrderBy(query.Sort, d, collection, queryLocale);
        var invoke = RunQueryInvokers.GetOrAdd(d.EntityType, static t =>
            RunQueryAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, List<IConditionalModel>, string?, int, int, DeletedFilter, CancellationToken, Task<QueryResult>>>());
        return await invoke(this, conditionals, orderBy, query.Limit, query.Offset, deleted, ct);
    }

    private async Task<QueryResult> RunQueryAsync<T>(
        List<IConditionalModel> conditionals, string? orderBy, int limit, int offset,
        DeletedFilter deleted, CancellationToken ct)
        where T : class, new()
    {
        // Only/With lift the global soft-delete floor (registered in
        // SqlSugarClientFactory) for this query; Only additionally restricts to trashed rows via
        // an extra DeletedAt-IS-NOT-NULL conditional. It stays a ConditionalModel rather than a
        // cast-based Where predicate on ISoftDeletable, which SqlSugar cannot translate reliably.
        var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(typeof(T));
        var effectiveConditionals = conditionals;
        if (deleted == DeletedFilter.Only && isSoftDeletable)
        {
            effectiveConditionals = [.. conditionals, new ConditionalModel
            {
                FieldName = db.EntityMaintenance.GetDbColumnName(nameof(ISoftDeletable.DeletedAt), typeof(T)),
                ConditionalType = ConditionalType.IsNot,
                FieldValue = null
            }];
        }

        ISugarQueryable<T> NewQueryable()
        {
            var q = db.Queryable<T>();
            if (deleted != DeletedFilter.Exclude && isSoftDeletable)
                q = q.ClearFilter<ISoftDeletable>();
            return q.Where(effectiveConditionals);
        }

        // True offset/limit windowing: offset is an absolute row count and need NOT be a multiple of
        // limit. The old code turned offset into a 1-based page index by integer division, which
        // silently returned the wrong window for any non-page-aligned offset (e.g. offset=25,limit=20
        // skipped 20 instead of 25). Count + Skip/Take gives the exact window.
        var total = await NewQueryable().CountAsync(ct);
        var queryable = NewQueryable();
        if (!string.IsNullOrWhiteSpace(orderBy)) queryable = queryable.OrderBy(orderBy);
        var rows = await queryable.Skip(offset).Take(limit).ToListAsync(ct);
        return new QueryResult(rows.Cast<object>().ToList(), total);
    }

    public async Task<object?> GetByIdAsync(string collection, string id,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var invoke = GetByIdInvokers.GetOrAdd(d.EntityType, static t =>
            GetByIdGenericAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, object, DeletedFilter, CancellationToken, Task<object?>>>());
        return await invoke(this, RepositoryHelpers.ConvertId(id, d), deleted, ct);
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
        var invoke = CreateInvokers.GetOrAdd(d.EntityType, static t =>
            CreateGenericAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, object, CancellationToken, Task<object>>>());
        return await invoke(this, entity, ct);
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

        var invoke = UpdateInvokers.GetOrAdd(d.EntityType, static t =>
            UpdateGenericAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, object, CancellationToken, Task>>());
        await invoke(this, clone, ct);
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

        var invoke = DeleteInvokers.GetOrAdd(d.EntityType, static t =>
            DeleteGenericAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, object, CancellationToken, Task>>());
        await invoke(this, RepositoryHelpers.ConvertId(id, d), ct);
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
        FilterNode? extraFilter, CancellationToken ct = default) =>
        whereIn.QueryWhereInFilteredAsync(collection, property, values, extraFilter, ct);

    public Task<IReadOnlyList<object>> QueryIdsAsync(
        string collection, FilterNode leafCondition, CancellationToken ct = default) =>
        whereIn.QueryIdsAsync(collection, leafCondition, ct);

    public Task SyncManyToManyAsync(
        Type junctionType,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<object> targetIds,
        CancellationToken ct = default) =>
        manyToMany.SyncManyToManyAsync(junctionType, parentFkProperty, targetFkProperty, sortProperty, parentId, targetIds, ct);

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
        var invoke = LoadTranslationsInvokers.GetOrAdd(translationType, static t =>
            LoadTranslationsGenericAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, string, IReadOnlyList<object>, string, string?, CancellationToken, Task<IReadOnlyList<object>>>>());
        return await invoke(this, fkColumn, parentIds, localeColumn, locale, ct);
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
                FieldValue = string.Join(",", parentIds.Select(v => v?.ToString())),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(parentIds.FirstOrDefault(v => v is not null))  // parent FK is Guid
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

        var invoke = QueryTranslationParentIdsInvokers.GetOrAdd(translationType, static t =>
            QueryTranslationParentIdsGenericAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, string, string, string, string, List<IConditionalModel>, CancellationToken, Task<IReadOnlyList<object>>>>());
        return await invoke(this, fkColumn, fkProperty, localeColumn, locale, conditionals, ct);
    }

    private async Task<IReadOnlyList<object>> QueryTranslationParentIdsGenericAsync<T>(
        string fkColumn, string fkProperty, string localeColumn, string locale,
        List<IConditionalModel> conditionals, CancellationToken ct) where T : class, new()
    {
        var fkProp = typeof(T).GetProperty(fkProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (fkProp is null) return [];

        // Project ONLY the FK column at the SQL level instead of materializing whole
        // translation rows (which carry potentially-large body/text columns) just to read one value
        // out of each. The lambda's return type must be the FK's REAL CLR type (Guid/string/...) —
        // SqlSugar's Select() does not turn a boxed `Convert(member, object)` body into a one-column
        // projection, it silently produces an empty/garbage result — so TFk is resolved and dispatched
        // via a second generic layer below rather than boxed here.
        var param = Expression.Parameter(typeof(T), "x");
        var selectBody = Expression.Property(param, fkProp);
        var selector = Expression.Lambda(selectBody, param);

        var invoke = QueryTranslationFkSelectInvokers.GetOrAdd((typeof(T), fkProp.PropertyType), static key =>
            QueryTranslationFkSelectGenericAsyncDef.MakeGenericMethod(key.EntityType, key.FkType)
                .CreateDelegate<Func<SqlSugarItemRepository, List<IConditionalModel>, LambdaExpression, CancellationToken, Task<IReadOnlyList<object>>>>());
        return await invoke(this, conditionals, selector, ct);
    }

    private async Task<IReadOnlyList<object>> QueryTranslationFkSelectGenericAsync<T, TFk>(
        List<IConditionalModel> conditionals, LambdaExpression selector, CancellationToken ct) where T : class, new()
    {
        var typedSelector = (Expression<Func<T, TFk>>)selector;
        var values = await db.Queryable<T>().Where(conditionals).Select(typedSelector).ToListAsync(ct);
        return values
            .Cast<object>()
            .Where(v => v is not null)
            .Distinct()
            .ToList();
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
        var invoke = SyncTranslationsInvokers.GetOrAdd(translationType, static t =>
            SyncTranslationsGenericAsyncDef.MakeGenericMethod(t)
                .CreateDelegate<Func<SqlSugarItemRepository, string, string, string, IReadOnlyList<string>, object, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>, CancellationToken, Task>>());
        await invoke(this, fkColumn, fkProperty, localeProperty, fieldProperties, parentId, perLocale, ct);
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
                    FieldValue = parentId.ToString(),
                    CSharpTypeName = RepositoryHelpers.TypeNameOf(parentId)  // parent FK is Guid
                },
                new ConditionalModel
                {
                    FieldName = db.EntityMaintenance.GetDbColumnName(localeProperty, type),
                    ConditionalType = ConditionalType.Equal,
                    FieldValue = locale
                }
            ]);
        }

        await InTransactionAsync(async () =>
        {
            foreach (var del in deletes)
                await db.Deleteable<T>().Where(del).ExecuteCommandAsync(ct);
            await db.Insertable(inserts).ExecuteCommandAsync(ct);
        }, ct);
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
