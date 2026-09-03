// src/Struo.Infrastructure/Query/PurgeOps.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;

namespace Struo.Infrastructure.Query;

internal sealed class PurgeOps(ISqlSugarClient db, IEntityRegistry registry)
{
    private static readonly GenericDispatcher<Func<PurgeOps, string, string, object, CancellationToken, Task>> SetForeignKeyNullDispatcher =
        new(typeof(PurgeOps), nameof(SetForeignKeyNullGenericAsync), [typeof(string), typeof(string), typeof(object), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<PurgeOps, string, object, CancellationToken, Task>> DeleteByPropertyDispatcher =
        new(typeof(PurgeOps), nameof(DeleteByPropertyGenericAsync), [typeof(string), typeof(object), typeof(CancellationToken)]);

    // ── Purge referential-integrity primitives ─────────────

    public async Task SetForeignKeyNullAsync(
        string sourceCollection, string foreignKeyProperty, object typedId, CancellationToken ct = default)
    {
        var d = RepositoryHelpers.Descriptor(registry, sourceCollection);
        var clrProperty = d.FieldToProperty.TryGetValue(foreignKeyProperty, out var p) ? p : foreignKeyProperty;
        var fkColumn = db.EntityMaintenance.GetDbColumnName(clrProperty, d.EntityType);
        await SetForeignKeyNullDispatcher.For(d.EntityType)(this, clrProperty, fkColumn, typedId, ct);
    }

    private async Task SetForeignKeyNullGenericAsync<T>(
        string clrPropertyName, string fkColumn, object typedId, CancellationToken ct)
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
        var prop = typeof(T).GetProperty(clrPropertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?? throw new InvalidOperationException(
                       $"'{typeof(T).Name}' has no property '{clrPropertyName}'.");
        var param = Expression.Parameter(typeof(T), "it");
        var memberInit = Expression.MemberInit(
            Expression.New(typeof(T)),
            Expression.Bind(prop, Expression.Constant(null, prop.PropertyType)));
        var setExpr = Expression.Lambda<Func<T, T>>(memberInit, param);

        // WHERE side stays parameterized; "@__fk" (not "@id"/"@fk") avoids colliding with any
        // auto-bound internal parameter SqlSugar generates for Updateable<T>() (see
        // SoftDeleteOps.SoftDeleteGenericAsync's identical note on "@__sdId"). Updateable<T> is NOT subject to the
        // ISoftDeletable query filter, so an already-trashed source row referencing the purge target
        // is still found and nulled.
        await db.Updateable<T>()
            .SetColumns(setExpr)
            .Where($"{fkColumn} = @__fk", new { __fk = typedId })
            .ExecuteCommandAsync(ct);
    }

    public async Task DeleteByPropertyAsync(
        Type entityType, string property, object value, CancellationToken ct = default)
    {
        var column = db.EntityMaintenance.GetDbColumnName(property, entityType);
        await DeleteByPropertyDispatcher.For(entityType)(this, column, value, ct);
    }

    private async Task DeleteByPropertyGenericAsync<T>(string column, object value, CancellationToken ct)
        where T : class, new()
    {
        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = column,
                ConditionalType = ConditionalType.Equal,
                FieldValue = value.ToString(),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(value)
            }
        };
        await db.Deleteable<T>().Where(conditionals).ExecuteCommandAsync(ct);
    }
}
