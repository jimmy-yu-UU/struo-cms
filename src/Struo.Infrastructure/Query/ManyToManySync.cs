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
        var pkColumn   = db.EntityMaintenance.GetDbColumnName(pkProp.Name, type);

        // Payload keys are CLR property names; these are the structural columns a payload dictionary
        // must never be allowed to overwrite. The Application-layer M2MDescriptor already excludes
        // them when building JunctionLink.Payload (Task 3) — this is defense in depth against a
        // caller that bypasses that layer.
        var reservedNames = new HashSet<string>(StringComparer.Ordinal) { pkProp.Name, parentFkProperty, targetFkProperty };
        if (sortProperty is not null) reservedNames.Add(sortProperty);

        // Application (Task 3) is expected to de-duplicate targets before calling in; a duplicate
        // target id here is a caller bug, not data to silently repair by last-wins — fail loud so
        // it is caught in development instead of quietly reordering/dropping a link.
        var incoming = new Dictionary<object, (JunctionLink Link, int Index)>();
        for (var i = 0; i < links.Count; i++)
        {
            var key = IdCoercion.Coerce(links[i].TargetId, targetProp.PropertyType)!;
            if (!incoming.TryAdd(key, (links[i], i)))
                throw new ArgumentException(
                    $"Duplicate target id '{links[i].TargetId}' for junction table '{tableName}' — each target may appear at most once in links.",
                    nameof(links));
        }

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
            // Ordered by ascending PK so that when a target has more than one row (legacy duplicate),
            // "keep the first" deterministically means "keep the lowest PK" on every backend.
            var existing = await db.Queryable<T>().Where(parentConditional).OrderBy($"{pkColumn} ASC").ToListAsync(ct);

            // Index existing rows by target id. This side's keys are already typed as
            // targetProp.PropertyType (read straight off the entity); `incoming`'s keys are coerced
            // to that same CLR type via IdCoercion above — that shared type is why the two
            // dictionaries' lookups agree.
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
                    "Junction table {Table} had {Count} duplicate row(s) for parent {ParentId}; keeping the lowest-PK row per target and deleting the rest.",
                    tableName, duplicates.Count, parentId);
                await db.Deleteable<T>().In(duplicates.ToArray()).ExecuteCommandAsync(ct);
            }

            var toDelete = byTarget.Where(kv => !incoming.ContainsKey(kv.Key)).Select(kv => pkProp.GetValue(kv.Value)!).ToArray();
            if (toDelete.Length > 0)
                await db.Deleteable<T>().In(toDelete).ExecuteCommandAsync(ct);

            var toInsert = new List<T>();
            var toUpdate = new List<T>();
            foreach (var (targetId, (link, index)) in incoming)
            {
                var sortValue = sortProp is null ? null : ConvertSortValue(index, sortProp.PropertyType);

                if (byTarget.TryGetValue(targetId, out var row))
                {
                    var changed = new List<string>();
                    if (sortProp is not null && !Equals(sortProp.GetValue(row), sortValue))
                    {
                        sortProp.SetValue(row, sortValue);
                        changed.Add(sortProp.Name);
                    }
                    changed.AddRange(ApplyPayload(type, row, link.Payload, reservedNames));
                    if (changed.Count > 0) toUpdate.Add(row);
                    continue;
                }

                var fresh = new T();
                parentProp.SetValue(fresh, IdCoercion.Coerce(parentId, parentProp.PropertyType));
                targetProp.SetValue(fresh, targetId);
                sortProp?.SetValue(fresh, sortValue);
                ApplyPayload(type, fresh, link.Payload, reservedNames);
                toInsert.Add(fresh);
            }
            if (toInsert.Count > 0)
                await db.Insertable(toInsert).ExecuteCommandAsync(ct);

            // Full-row entity update (Updateable(entity), not Updateable(entity).UpdateColumns(...)):
            // (1) UpdateColumns takes CLR property names with no GetDbColumnName mapping, so a payload
            // property with a [SugarColumn(ColumnName=...)] alias could silently never reach the
            // database; (2) UpdateColumns restricts the generated SET list, which excludes AuditAop's
            // UpdatedAt/UpdatedBy stamping (DataFilterType.UpdateByObject) for an AuditableEntity
            // junction; (3) one batched statement instead of N serial round trips for a reorder of N
            // rows. `changed` above is used only as the "did anything on this row differ" signal —
            // every column of the already-mutated, DB-read entity is written back, so untouched
            // columns round-trip their current values and AOP stamping applies like any other update.
            if (toUpdate.Count > 0)
                await db.Updateable(toUpdate).ExecuteCommandAsync(ct);
        }, ct);
    }

    private static object ConvertSortValue(int index, Type sortPropType)
    {
        // Convert.ChangeType throws for a Nullable<T> target (e.g. int?); unwrap to the underlying
        // numeric type first, same as CoerceScalar does below for payload values.
        var t = Nullable.GetUnderlyingType(sortPropType) ?? sortPropType;
        return Convert.ChangeType(index, t);
    }

    // Sets each payload property whose current value differs; returns the CLR names that changed.
    // `reservedNames` (PK, both FKs, sort) are structural columns the payload must never touch even
    // if a caller's dictionary happens to name one.
    private static List<string> ApplyPayload(
        Type type, object row, IReadOnlyDictionary<string, object?>? payload, IReadOnlySet<string> reservedNames)
    {
        var changed = new List<string>();
        if (payload is null) return changed;
        foreach (var (name, value) in payload)
        {
            if (reservedNames.Contains(name)) continue;
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
