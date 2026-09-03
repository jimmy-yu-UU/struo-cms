// src/Struo.Infrastructure/Query/SoftDeleteOps.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;

namespace Struo.Infrastructure.Query;

internal sealed class SoftDeleteOps(ISqlSugarClient db, IEntityRegistry registry)
{
    private static readonly GenericDispatcher<Func<SoftDeleteOps, string, object, DateTime, Guid?, CancellationToken, Task<bool>>> SoftDeleteDispatcher =
        new(typeof(SoftDeleteOps), nameof(SoftDeleteGenericAsync), [typeof(string), typeof(object), typeof(DateTime), typeof(Guid?), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<SoftDeleteOps, string, object, CancellationToken, Task<bool>>> RestoreDispatcher =
        new(typeof(SoftDeleteOps), nameof(RestoreGenericAsync), [typeof(string), typeof(object), typeof(CancellationToken)]);

    public async Task<bool> SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        if (!typeof(ISoftDeletable).IsAssignableFrom(d.EntityType))
            throw new InvalidOperationException($"Collection '{collection}' does not implement ISoftDeletable.");

        var idColumn = db.EntityMaintenance.GetDbColumnName(d.IdProperty, d.EntityType);
        var typedId = RepositoryHelpers.ConvertId(id, d);

        return await SoftDeleteDispatcher.For(d.EntityType)(this, idColumn, typedId, deletedAt, deletedBy, ct);
    }

    private async Task<bool> SoftDeleteGenericAsync<T>(
        string idColumn, object id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct)
        where T : class, ISoftDeletable, new()
    {
        // Updateable<T> is not subject to the ISoftDeletable query filter, so without an explicit
        // guard the row would be located by id alone regardless of its current DeletedAt. The
        // WHERE below adds "deletedat IS NULL" so trashing an already-trashed row is an atomic no-op
        // AT THE SQL LEVEL (affected = 0) — not merely a pre-read check in ItemService, which would
        // leave a TOCTOU window where two concurrent DELETEs of the same live row could each pass the
        // check and both re-stamp/re-version and double-record a "delete" revision. Mirrors
        // RestoreGenericAsync's identical "deletedat IS NOT NULL" guard on the opposite side.
        // Parameter named "__sdId" (not "@id"): SqlSugar auto-binds an internal "@id" placeholder of
        // its own on Updateable<T>() for an entity whose PK property is named "Id" — colliding with a
        // plain "@id" here silently rebinds to that internal (unset/default) parameter instead of ours.
        // UpdateGenericAsync's "@__ocId" dodges the same collision.
        //
        // SetColumns(it => new T{...}) — not the string-fieldName overload — because when deletedBy is
        // null, SqlSugar's expression resolver (MemberInitExpressionResolve.Update) sees the target
        // property is Nullable<T> and types the resulting SQL parameter's DbType from the underlying
        // CLR type (Guid), instead of boxing a bare `(object?)null` with no type info at all. An
        // untyped null parameter is sent to Npgsql as `text`, which PG rejects (42804) against a
        // `uuid`/`timestamp` column — see RestoreGenericAsync below for the confirmed live-gate case.
        // For AuditableEntity subclasses, bump the optimistic-lock Version in the SAME UPDATE as
        // the trash stamp so the history timeline advances and a stale client 409s after a restore.
        var deletedAtColumn = db.EntityMaintenance.GetDbColumnName(nameof(ISoftDeletable.DeletedAt), typeof(T));
        var affected = await ApplyVersionBump(db.Updateable<T>()
                .SetColumns(it => new T { DeletedAt = deletedAt, DeletedBy = deletedBy }))
            .Where($"{idColumn} = @__sdId AND {deletedAtColumn} IS NULL", new { __sdId = id })
            .ExecuteCommandAsync(ct);
        return affected > 0;
    }

    // Chains a `Version = Version + 1` set onto the trash/restore UPDATE when T is an
    // AuditableEntity subclass. Built as a dynamic member-init expression
    // `it => new T { Version = it.Version + 1 }` (T is statically only ISoftDeletable, so Version can
    // only be reached via reflection); SqlSugar's expression resolver turns `it.Version + 1` into the
    // SQL fragment `version = version + 1` — no bound null parameter (no 42804 typed-null trap) and no
    // CAS (delete/restore carry no client version; the decision is increment-only). Non-AuditableEntity
    // ISoftDeletable types have no Version and are returned unchanged.
    private static IUpdateable<T> ApplyVersionBump<T>(IUpdateable<T> updateable) where T : class, new()
    {
        if (!typeof(AuditableEntity).IsAssignableFrom(typeof(T))) return updateable;

        var versionProp = typeof(T).GetProperty(
            nameof(AuditableEntity.Version), BindingFlags.Public | BindingFlags.Instance)!;
        var param = Expression.Parameter(typeof(T), "it");
        var incremented = Expression.Add(
            Expression.Property(param, versionProp), Expression.Constant(1L));
        var setExpr = Expression.Lambda<Func<T, T>>(
            Expression.MemberInit(Expression.New(typeof(T)), Expression.Bind(versionProp, incremented)),
            param);
        return updateable.SetColumns(setExpr);
    }

    public async Task<bool> RestoreAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        if (!typeof(ISoftDeletable).IsAssignableFrom(d.EntityType))
            throw new InvalidOperationException($"Collection '{collection}' does not implement ISoftDeletable.");

        var idColumn = db.EntityMaintenance.GetDbColumnName(d.IdProperty, d.EntityType);
        var typedId = RepositoryHelpers.ConvertId(id, d);

        return await RestoreDispatcher.For(d.EntityType)(this, idColumn, typedId, ct);
    }

    private async Task<bool> RestoreGenericAsync<T>(string idColumn, object id, CancellationToken ct)
        where T : class, ISoftDeletable, new()
    {
        // PG 42804 fix: `.SetColumns(deletedAtColumn, (object?)null)` binds a null
        // parameter with NO CLR type, so Npgsql infers `text` and PG rejects
        // `SET deletedat = @p(text)` against the `timestamp` column. The entity-typed object
        // initializer below goes through SqlSugar's expression resolver instead of the raw
        // string-fieldName overload: it recognizes DeletedAt/DeletedBy as Nullable<DateTime>/
        // Nullable<Guid> and assigns the null parameter's DbType from the underlying type
        // (DateTime / Guid), which PG accepts against the timestamp/uuid columns.
        // Bump Version in the SAME UPDATE (AuditableEntity subclasses only) — see ApplyVersionBump.
        // Guard the UPDATE itself with "deletedat IS NOT NULL" so restoring an already-live row
        // is an atomic no-op at the SQL level (affected = 0) — not merely a pre-read check in
        // ItemService, which would leave a TOCTOU window between the check and this UPDATE where two
        // concurrent restores of the same row could each re-stamp/re-version and double-record a
        // "restore" revision.
        var deletedAtColumn = db.EntityMaintenance.GetDbColumnName(nameof(ISoftDeletable.DeletedAt), typeof(T));
        var affected = await ApplyVersionBump(db.Updateable<T>()
                .SetColumns(it => new T { DeletedAt = null, DeletedBy = null }))
            .Where($"{idColumn} = @__sdId AND {deletedAtColumn} IS NOT NULL", new { __sdId = id })
            .ExecuteCommandAsync(ct);
        return affected > 0;
    }
}
