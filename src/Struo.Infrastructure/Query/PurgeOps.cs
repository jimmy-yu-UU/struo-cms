// src/Struo.Infrastructure/Query/PurgeOps.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal sealed class PurgeOps(ISqlSugarClient db, IEntityRegistry registry)
{
    private static readonly GenericDispatcher<Func<PurgeOps, string, object, CancellationToken, Task>> SetForeignKeyNullDispatcher =
        new(typeof(PurgeOps), nameof(SetForeignKeyNullGenericAsync), [typeof(string), typeof(object), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<PurgeOps, string, object, CancellationToken, Task>> SetForeignKeyNullForTrashedDispatcher =
        new(typeof(PurgeOps), nameof(SetForeignKeyNullForTrashedGenericAsync), [typeof(string), typeof(object), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<PurgeOps, ValueTarget, object, CancellationToken, Task>> DeleteByPropertyDispatcher =
        new(typeof(PurgeOps), nameof(DeleteByPropertyGenericAsync), [typeof(ValueTarget), typeof(object), typeof(CancellationToken)]);

    // ── Purge referential-integrity primitives ─────────────

    public async Task SetForeignKeyNullAsync(
        string sourceCollection, string foreignKeyProperty, object typedId, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, sourceCollection);
        var clrProperty = d.FieldToProperty.TryGetValue(foreignKeyProperty, out var p) ? p : foreignKeyProperty;
        await SetForeignKeyNullDispatcher.For(d.EntityType)(this, clrProperty, typedId, ct);
    }

    // Only rows in the trash; a live row that references the id is left as it is.
    public async Task SetForeignKeyNullForTrashedAsync(
        string sourceCollection, string foreignKeyProperty, object typedId, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, sourceCollection);
        if (!typeof(ISoftDeletable).IsAssignableFrom(d.EntityType)) return;
        var clrProperty = d.FieldToProperty.TryGetValue(foreignKeyProperty, out var p) ? p : foreignKeyProperty;
        await SetForeignKeyNullForTrashedDispatcher.For(d.EntityType)(this, clrProperty, typedId, ct);
    }

    // True when the ORM column for the property accepts NULL: the metadata CodeFirst builds the column
    // from, after the factory conventions.
    public bool IsForeignKeyNullable(string collection, string property)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var clrProperty = d.FieldToProperty.TryGetValue(property, out var p) ? p : property;
        var column = db.EntityMaintenance.GetEntityInfo(d.EntityType).Columns
            .FirstOrDefault(c => string.Equals(c.PropertyName, clrProperty, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"'{d.EntityType.Name}' has no column for '{property}'.");
        return column.IsNullable;
    }

    private async Task SetForeignKeyNullForTrashedGenericAsync<T>(
        string clrPropertyName, object typedId, CancellationToken ct)
        where T : class, new() =>
        await db.Updateable<T>()
            .SetColumns(NullAssignment<T>(clrPropertyName))
            .Where(TypedPredicates.EqualAndNullState<T>(
                clrPropertyName, typedId, nameof(ISoftDeletable.DeletedAt), mustBeNull: false))
            .ExecuteCommandAsync(ct);

    private async Task SetForeignKeyNullGenericAsync<T>(
        string clrPropertyName, object typedId, CancellationToken ct)
        where T : class, new()
    {
        // SqlSugar's IUpdateable<T> has no raw-SQL-fragment SetColumns(string) overload — only
        // SetColumns(string field, object value) (which would bind an UNTYPED null parameter, the PG
        // 42804 trap) and the expression form SetColumns(it => new T {...}) used in SoftDeleteOps
        // (SoftDeleteGenericAsync/RestoreGenericAsync). Since the FK property name is only known
        // at runtime here (unlike those two compile-time call sites), build the equivalent
        // `it => new T { <Fk> = (FkType?)null }` member-init expression dynamically: the null constant
        // is typed to the FK property's own CLR type, so SqlSugar/Npgsql bind it correctly instead of
        // inferring `text`.
        var setExpr = NullAssignment<T>(clrPropertyName);

        // Updateable<T> is NOT subject to the ISoftDeletable query filter, so an already-trashed source
        // row referencing the purge target is still found and nulled.
        await db.Updateable<T>()
            .SetColumns(setExpr)
            .Where(TypedPredicates.Equal<T>(clrPropertyName, typedId))
            .ExecuteCommandAsync(ct);
    }

    private static Expression<Func<T, T>> NullAssignment<T>(string clrPropertyName)
    {
        var prop = typeof(T).GetProperty(clrPropertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?? throw new InvalidOperationException(
                       $"'{typeof(T).Name}' has no property '{clrPropertyName}'.");
        var param = Expression.Parameter(typeof(T), "it");
        var memberInit = Expression.MemberInit(
            Expression.New(typeof(T)),
            Expression.Bind(prop, Expression.Constant(null, prop.PropertyType)));
        return Expression.Lambda<Func<T, T>>(memberInit, param);
    }

    public async Task DeleteByPropertyAsync(
        Type entityType, string property, object value, CancellationToken ct = default)
    {
        var target = WhereInQueries.TargetOf(db, entityType, property);
        await DeleteByPropertyDispatcher.For(entityType)(this, target, value, ct);
    }

    private async Task DeleteByPropertyGenericAsync<T>(ValueTarget target, object value, CancellationToken ct)
        where T : class, new()
    {
        var conditionals = new List<IConditionalModel> { ValueConditions.BuildInternal(target, QueryOperator.Eq, value)! };
        await db.Deleteable<T>().Where(conditionals).ExecuteCommandAsync(ct);
    }
}
