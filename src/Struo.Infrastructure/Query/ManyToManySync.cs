// src/Struo.Infrastructure/Query/ManyToManySync.cs
using System.Reflection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Query.Write;

namespace Struo.Infrastructure.Query;

internal sealed class ManyToManySync(ISqlSugarClient db, TransactionRunner transactions, ILogger? logger)
{
    private static readonly GenericDispatcher<Func<ManyToManySync, string, string, string, string?, object, IReadOnlyList<JunctionLink>, CancellationToken, Task>> SyncDispatcher =
        new(typeof(ManyToManySync), nameof(SyncM2MGenericAsync),
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(object), typeof(IReadOnlyList<JunctionLink>), typeof(CancellationToken)]);

    public async Task SyncManyToManyAsync(
        Type junctionType,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<JunctionLink> links,
        CancellationToken ct = default)
    {
        var parentColumn = db.EntityMaintenance.GetDbColumnName(parentFkProperty, junctionType);
        await SyncDispatcher.For(junctionType)(this, parentColumn, parentFkProperty, targetFkProperty, sortProperty, parentId, links, ct);
    }

    private async Task SyncM2MGenericAsync<T>(
        string parentColumn,
        string parentFkProperty,
        string targetFkProperty,
        string? sortProperty,
        object parentId,
        IReadOnlyList<JunctionLink> links,
        CancellationToken ct) where T : class, new()
    {
        var type = typeof(T);
        var parentProp = type.GetProperty(parentFkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var targetProp = type.GetProperty(targetFkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var sortProp   = sortProperty is null ? null
            : type.GetProperty(sortProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var pkProp     = type.GetProperty(db.EntityMaintenance.GetEntityInfo<T>().Columns.Single(c => c.IsPrimarykey).PropertyName)!;
        var tableName  = db.EntityMaintenance.GetTableName<T>();

        // ConditionalType.In (not Equal): Equal binds FieldValue as text -> "bigint = text" 42883 on PostgreSQL. In is the Postgres-safe primitive the other collaborators use (WhereInQueries, TranslationStore).
        var parentConditional = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = parentColumn,
                ConditionalType = ConditionalType.In,
                FieldValue = parentId.ToString(),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(parentId)
            }
        };

        // Delete + insert/update in a single transaction so a failed write never leaves the parent
        // with a half-synced set of junction rows. Joins the caller's aggregate transaction when one is open.
        await transactions.InTransactionAsync(async () =>
        {
            var existing = await db.Queryable<T>().Where(parentConditional).ToListAsync(ct);

            // Index existing rows by coerced target id. A pair that appears more than once is legacy data
            // from before the one-row-per-pair rule: keep the first row, delete the rest, and say so.
            var byTarget = new Dictionary<object, T>();
            var duplicates = new List<object>();
            foreach (var row in existing)
            {
                var key = targetProp.GetValue(row)!;
                if (!byTarget.TryAdd(key, row)) duplicates.Add(pkProp.GetValue(row)!);
            }
            if (duplicates.Count > 0)
            {
                logger?.LogWarning(
                    "Junction table {Table} had {Count} duplicate row(s) for parent {ParentId}; keeping the first row per target and deleting the rest.",
                    tableName, duplicates.Count, parentId);
                await db.Deleteable<T>().In(duplicates.ToArray()).ExecuteCommandAsync(ct);
            }

            var incoming = new Dictionary<object, (JunctionLink Link, int Index)>();
            for (var i = 0; i < links.Count; i++)
                incoming[IdCoercion.Coerce(links[i].TargetId, targetProp.PropertyType)!] = (links[i], i);

            var toDelete = byTarget.Where(kv => !incoming.ContainsKey(kv.Key)).Select(kv => pkProp.GetValue(kv.Value)!).ToArray();
            if (toDelete.Length > 0)
                await db.Deleteable<T>().In(toDelete).ExecuteCommandAsync(ct);

            var toInsert = new List<T>();
            foreach (var (targetId, (link, index)) in incoming)
            {
                if (byTarget.TryGetValue(targetId, out var row))
                {
                    var changed = new List<string>();
                    if (sortProp is not null && !Equals(sortProp.GetValue(row), Convert.ChangeType(index, sortProp.PropertyType)))
                    {
                        sortProp.SetValue(row, Convert.ChangeType(index, sortProp.PropertyType));
                        changed.Add(sortProp.Name);
                    }
                    changed.AddRange(ApplyPayload(type, row, link.Payload));
                    if (changed.Count > 0)
                        await db.Updateable(row).UpdateColumns(changed.ToArray()).ExecuteCommandAsync(ct);
                    continue;
                }

                var fresh = new T();
                parentProp.SetValue(fresh, IdCoercion.Coerce(parentId, parentProp.PropertyType));
                targetProp.SetValue(fresh, targetId);
                // Use Convert.ChangeType so the sort index (int) is coerced to whatever numeric
                // type the sort column declares (e.g. int, long, short).
                sortProp?.SetValue(fresh, Convert.ChangeType(index, sortProp.PropertyType));
                ApplyPayload(type, fresh, link.Payload);
                toInsert.Add(fresh);
            }
            if (toInsert.Count > 0)
                await db.Insertable(toInsert).ExecuteCommandAsync(ct);
        }, ct);
    }

    // Sets each payload property whose current value differs; returns the CLR names that changed.
    private static List<string> ApplyPayload(Type type, object row, IReadOnlyDictionary<string, object?>? payload)
    {
        var changed = new List<string>();
        if (payload is null) return changed;
        foreach (var (name, value) in payload)
        {
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !prop.CanWrite) continue;
            var coerced = value is null ? null : CoerceScalar(value, prop.PropertyType);
            if (Equals(prop.GetValue(row), coerced)) continue;
            prop.SetValue(row, coerced);
            changed.Add(prop.Name);
        }
        return changed;
    }

    private static object CoerceScalar(object value, Type target)
    {
        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (t.IsInstanceOfType(value)) return value;
        if (t == typeof(Guid)) return Guid.Parse(value.ToString()!);
        if (t.IsEnum) return Enum.Parse(t, value.ToString()!, ignoreCase: true);
        return Convert.ChangeType(value, t, System.Globalization.CultureInfo.InvariantCulture);
    }
}
