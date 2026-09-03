// src/Struo.Infrastructure/Query/ManyToManySync.cs
using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Query;

internal sealed class ManyToManySync(ISqlSugarClient db, TransactionRunner transactions)
{
    private static readonly GenericDispatcher<Func<ManyToManySync, string, string, string, string?, object, IReadOnlyList<object>, CancellationToken, Task>> SyncDispatcher =
        new(typeof(ManyToManySync), nameof(SyncM2MGenericAsync),
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(object), typeof(IReadOnlyList<object>), typeof(CancellationToken)]);

    public async Task SyncManyToManyAsync(
        Type junctionType,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<object> targetIds,
        CancellationToken ct = default)
    {
        var parentColumn = db.EntityMaintenance.GetDbColumnName(parentFkProperty, junctionType);
        await SyncDispatcher.For(junctionType)(this, parentColumn, parentFkProperty, targetFkProperty, sortProperty, parentId, targetIds, ct);
    }

    private async Task SyncM2MGenericAsync<T>(
        string parentColumn,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<object> targetIds,
        CancellationToken ct) where T : class, new()
    {
        // ConditionalType.In (not Equal): Equal binds FieldValue as text -> "bigint = text" 42883 on PostgreSQL. In is the Postgres-safe primitive the other collaborators use (WhereInQueries, TranslationStore).
        var deleteConditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = parentColumn,
                ConditionalType = ConditionalType.In,
                FieldValue = parentId.ToString(),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(parentId)
            }
        };

        // Build new rows before opening the transaction so reflection work stays outside the tx.
        var type = typeof(T);
        var parentProp = type.GetProperty(parentFkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var targetProp = type.GetProperty(targetFkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var sortProp   = sortProperty is null ? null
            : type.GetProperty(sortProperty, BindingFlags.Public | BindingFlags.Instance)!;

        var rows = new List<T>(targetIds.Count);
        for (var i = 0; i < targetIds.Count; i++)
        {
            var row = new T();
            parentProp.SetValue(row, IdCoercion.Coerce(parentId,    parentProp.PropertyType));
            targetProp.SetValue(row, IdCoercion.Coerce(targetIds[i], targetProp.PropertyType));
            // Use Convert.ChangeType so the sort index (int) is coerced to whatever numeric
            // type the sort column declares (e.g. int, long, short).
            sortProp?.SetValue(row, Convert.ChangeType(i, sortProp.PropertyType));
            rows.Add(row);
        }

        // Delete + insert in a single transaction so a failed insert never leaves the parent
        // with zero junction rows. Joins the caller's aggregate transaction when one is open.
        await transactions.InTransactionAsync(async () =>
        {
            await db.Deleteable<T>().Where(deleteConditionals).ExecuteCommandAsync(ct);
            if (rows.Count > 0)
                await db.Insertable(rows).ExecuteCommandAsync(ct);
        }, ct);
    }
}
