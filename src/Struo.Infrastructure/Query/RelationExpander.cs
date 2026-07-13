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
        // filterResolver/options are DI-plumbed now for Tasks 6 (nested-filter push-down) and 7
        // (MaxLimit clamp); not consumed yet this task, so guard-only to keep them "read" (no
        // behaviour change beyond a defensive DI-contract check).
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
                    var children = await repository.QueryWhereInAsync(
                        rel.TargetCollection, desc.ReverseForeignKeyProperty!, ids, ct);
                    var grouped = children
                        .GroupBy(ch => readProp(ch, desc.ReverseForeignKeyProperty!)!)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = new List<IReadOnlyDictionary<string, object?>>();
                        if (grouped.TryGetValue(pid, out var lst))
                            foreach (var ch in lst)
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
                    var targets = (await repository.QueryWhereInAsync(rel.TargetCollection, "id", targetIds, ct))
                        .ToDictionary(t => readProp(t, "id")!, t => t);
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = new List<IReadOnlyDictionary<string, object?>>();
                        var linked = junctions
                            .Where(j => Equals(readProp(j, desc.JunctionParentFk!), pid))
                            .OrderBy(j => JunctionSortKey(desc.JunctionSort, readProp, j))
                            .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                            .Where(tid => targets.ContainsKey(tid));
                        foreach (var tid in linked)
                        {
                            var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, targets[tid], spec.Fields);
                            rows.Add(d);
                            expanded.Add((targets[tid], d));
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
