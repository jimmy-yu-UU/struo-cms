// src/Struo.Infrastructure/Query/RelationExpander.cs
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Query;

/// <summary>
/// Expands <c>deep</c> relations for a page of already-fetched parent entities using
/// batched follow-up queries (stitching) — one query per relation per page, so it is
/// N+1-safe. Deliberately does NOT use SqlSugar <c>.Includes()</c>; relation rows are
/// projected through the caller-supplied <c>projectTarget</c> delegate so target rows
/// honour the same metadata projection (field whitelist, hidden/readable rules) as
/// top-level rows.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>M2O: collect parent FK values, query target where id IN, map FK -> target.</item>
/// <item>O2M: query target where reverseFk IN parentIds, group by reverseFk.</item>
/// <item>M2M: query junction where parentFk IN parentIds, collect targetIds, query
///   target where id IN, group per parent ordered by the junction sort column.</item>
/// </list>
/// </remarks>
public sealed class RelationExpander(
    IItemRepository repository, RelationshipGraph graph,
    IRelationFilterResolver filterResolver, StruoQueryOptions options)
    : IRelationExpander
{
    /// <summary>
    /// Builds, per parent id, a map of <c>relationName -&gt; (object?|list)</c> of projected
    /// target rows for every relation named in <paramref name="deep"/>.
    /// </summary>
    /// <param name="projectTarget">
    /// <c>(targetCollection, targetEntity, fields?) -&gt; camelCase dict</c>; reuses the
    /// item-service metadata projection for the target collection.
    /// </param>
    /// <param name="parentId">Reads the primary-key value off a parent entity.</param>
    /// <param name="readProp">Reads a property value off an entity by CLR/camel name.</param>
    public async Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
        string collection, IReadOnlyList<object> parents, DeepSpec deep,
        Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> projectTarget,
        Func<object, object> parentId, Func<object, string, object?> readProp,
        string? locale = null, CancellationToken ct = default)
    {
        // filterResolver is consumed for nested-filter push-down (Task 6, O2M/M2M target-side
        // rewrite) and options.MaxLimit is consumed for the per-parent sort/limit/offset windowing
        // (Task 7, ApplyListArgs). Guarded here too so a null DI registration fails fast.
        ArgumentNullException.ThrowIfNull(filterResolver);
        ArgumentNullException.ThrowIfNull(options);

        var result = new Dictionary<object, Dictionary<string, object?>>();
        foreach (var p in parents) result[parentId(p)] = new Dictionary<string, object?>();

        foreach (var (relName, spec) in deep.Relations)
        {
            var desc = graph.Descriptors(collection).FirstOrDefault(d =>
                           string.Equals(d.Meta.Name, relName, StringComparison.OrdinalIgnoreCase))
                       ?? throw new QueryException($"Unknown relation '{relName}' on '{collection}'.");
            var rel = desc.Meta;

            // (target entity, its projected mutable dict) pairs produced for this relation, so a
            // nested spec can recurse on the entities and merge sub-relations into the dicts.
            var expanded = new List<(object Entity, Dictionary<string, object?> Dict)>();

            switch (rel.Kind)
            {
                case RelationKind.ManyToOne:
                {
                    var fkValues = parents
                        .Select(p => readProp(p, rel.ForeignKey!))
                        .Where(v => v is not null)
                        .Distinct()
                        .ToList()!;
                    var targets = await repository.QueryWhereInAsync(rel.TargetCollection, "id", fkValues!, ct);
                    var byId = targets.ToDictionary(t => readProp(t, "id")!, t => t);
                    foreach (var p in parents)
                    {
                        var fk = readProp(p, rel.ForeignKey!);
                        if (fk is not null && byId.TryGetValue(fk, out var tr))
                        {
                            var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, tr, spec.Fields);
                            result[parentId(p)][relName] = d;
                            expanded.Add((tr, d));
                        }
                        else result[parentId(p)][relName] = null;
                    }
                    break;
                }
                case RelationKind.OneToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var o2mFilter = spec.Filter is null ? null
                        : await filterResolver.RewriteAsync(rel.TargetCollection, spec.Filter, locale, ct);
                    var children = await repository.QueryWhereInFilteredAsync(
                        rel.TargetCollection, desc.ReverseForeignKeyProperty!, ids, o2mFilter, ct);
                    var grouped = children
                        .GroupBy(ch => readProp(ch, desc.ReverseForeignKeyProperty!)!)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = new List<IReadOnlyDictionary<string, object?>>();
                        if (grouped.TryGetValue(pid, out var lst))
                            foreach (var ch in ApplyListArgs(lst, spec, readProp))
                            {
                                var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, ch, spec.Fields);
                                rows.Add(d);
                                expanded.Add((ch, d));
                            }
                        result[pid][relName] = rows;
                    }
                    break;
                }
                case RelationKind.ManyToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var junctions = await repository.QueryEntityWhereInAsync(
                        desc.JunctionType!, desc.JunctionParentFk!, ids, ct);
                    var targetIds = junctions
                        .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                        .Distinct()
                        .ToList();
                    var m2mFilter = spec.Filter is null ? null
                        : await filterResolver.RewriteAsync(rel.TargetCollection, spec.Filter, locale, ct);
                    var targets = (await repository.QueryWhereInFilteredAsync(
                            rel.TargetCollection, "id", targetIds, m2mFilter, ct))
                        .ToDictionary(t => readProp(t, "id")!, t => t);
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = new List<IReadOnlyDictionary<string, object?>>();
                        var linkedTargets = junctions
                            .Where(j => Equals(readProp(j, desc.JunctionParentFk!), pid))
                            .OrderBy(j => JunctionSortKey(desc.JunctionSort, readProp, j))
                            .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                            .Where(tid => targets.ContainsKey(tid))
                            .Select(tid => targets[tid])
                            .ToList();
                        foreach (var t in ApplyListArgs(linkedTargets, spec, readProp))
                        {
                            var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, t, spec.Fields);
                            rows.Add(d);
                            expanded.Add((t, d));
                        }
                        result[pid][relName] = rows;
                    }
                    break;
                }
                default:
                    throw new QueryException($"Unsupported relation kind '{rel.Kind}' for '{relName}'.");
            }

            // Recurse ONCE per relation-node: expand the target's own relations over the DISTINCT
            // target entities (batched, breadth-first per level — N+1-safe), then merge each nested
            // relation value into the corresponding target dict by target id.
            if (spec.Deep is not null && expanded.Count > 0)
            {
                var distinct = expanded.Select(e => e.Entity).Distinct().ToList();
                var sub = await ExpandAsync(
                    rel.TargetCollection, distinct, spec.Deep, projectTarget, parentId, readProp, locale, ct);
                foreach (var (entity, dict) in expanded)
                    if (sub.TryGetValue(parentId(entity), out var subMap))
                        foreach (var (k, v) in subMap) dict[k] = v;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies the nested-list <c>sort</c> (own-field, multi-key, asc/desc) then <c>offset</c>/<c>limit</c>
    /// to a single parent's group of target entities, in memory. When <c>Sort</c> is null the caller's
    /// existing order is preserved (O2M: fetch order; M2M: junction order). An omitted <c>Limit</c>
    /// returns all rows (8c.3a back-compat); an explicit <c>Limit</c> is clamped to
    /// <c>options.MaxLimit</c>. This is the per-parent windowing that keeps the batched fetch N+1-safe.
    /// Instance method: reads <c>options.MaxLimit</c> off the injected <see cref="StruoQueryOptions"/>.
    /// </summary>
    private IEnumerable<object> ApplyListArgs(
        List<object> entities, DeepRelationSpec spec, Func<object, string, object?> readProp)
    {
        IEnumerable<object> seq = entities;

        if (spec.Sort is { Count: > 0 } sorts)
        {
            IOrderedEnumerable<object>? ordered = null;
            foreach (var s in sorts)
            {
                var field = s.Field;
                Func<object, object?> key = e => readProp(e, field);
                ordered = ordered is null
                    ? (s.Descending
                        ? seq.OrderByDescending(key, RelationSortComparer.Instance)
                        : seq.OrderBy(key, RelationSortComparer.Instance))
                    : (s.Descending
                        ? ordered.ThenByDescending(key, RelationSortComparer.Instance)
                        : ordered.ThenBy(key, RelationSortComparer.Instance));
            }
            seq = ordered!;
        }

        var offset = spec.Offset.GetValueOrDefault();
        if (offset > 0) seq = seq.Skip(offset);
        if (spec.Limit is > 0) seq = seq.Take(Math.Min(spec.Limit.Value, options.MaxLimit));
        return seq;
    }

    /// <summary>
    /// Null-safe comparer for boxed own-field values (nulls sort first). Values on the same field
    /// share a CLR type, so <see cref="IComparable"/> ordering is well-defined; a non-comparable
    /// value degrades to equal (stable order preserved).
    /// </summary>
    private sealed class RelationSortComparer : IComparer<object?>
    {
        public static readonly RelationSortComparer Instance = new();
        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return x is IComparable c ? c.CompareTo(y) : 0;
        }
    }

    /// <summary>
    /// Computes a stable, total-ordering sort key for a junction row. When no sort column is
    /// configured (or the value is null/non-numeric) it falls back to 0 so ordering degrades
    /// gracefully to insertion order instead of throwing.
    /// </summary>
    private static int JunctionSortKey(
        string? sortProperty, Func<object, string, object?> readProp, object junction)
    {
        if (sortProperty is null) return 0;
        var value = readProp(junction, sortProperty);
        return value switch
        {
            null => 0,
            int i => i,
            long l => (int)l,
            IConvertible conv => SafeToInt(conv),
            _ => 0
        };
    }

    private static int SafeToInt(IConvertible value)
    {
        try { return value.ToInt32(System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }
}
