// src/Struo.Application/Query/Read/DeepExpansionCoordinator.cs
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Validates the requested deep relations against the relationship graph and the configured
/// <see cref="StruoQueryOptions.MaxRelationDepth"/> (<see cref="Prepare"/>), then nests the
/// expanded (batched) relation rows into each projected parent dictionary
/// (<see cref="ExpandPreparedAsync"/>). Both are no-ops when <c>deep</c> is null/empty.
/// </summary>
public sealed class DeepExpansionCoordinator(
    StruoQueryOptions options,
    IRelationshipGraph graph,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IRelationExpander expander,
    ItemProjector projector,
    IPermissionService permissions)
{
    /// <summary>
    /// Prunes the requested relations to the readable ones and validates the whole nested tree
    /// (nesting depth, relation names, per-level filter/sort/limit/offset, the shared filter
    /// <paramref name="budget"/>). Throws <see cref="QueryException"/> on a violation. Needs no
    /// rows, so callers run it before the first query and the verdict never depends on row count.
    /// Returns null when nothing remains to expand.
    /// </summary>
    public DeepSpec? Prepare(string collection, DeepSpec? deep, FilterBudget? budget)
    {
        if (deep is null || deep.Relations.Count == 0) return null;

        // Pruning precedes validation on purpose: a nested filter/sort on an unreadable relation is
        // never validated against that collection's metadata, so its error messages cannot be used
        // to probe which fields exist there.
        var visible = PruneUnreadable(collection, deep);
        if (visible.Relations.Count == 0) return null;

        var nodes = 0;
        ValidateDeepTree(collection, visible, depth: 1, budget ?? new FilterBudget(), ref nodes);
        return visible;
    }

    /// <summary>
    /// Nests the expanded relation rows of an already <see cref="Prepare">prepared</see> spec into
    /// each projected parent dictionary. No-op when there are no parent rows.
    /// </summary>
    public async Task ExpandPreparedAsync(
        string collection, DeepSpec visible,
        IReadOnlyList<object> entities, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string? locale, CancellationToken ct)
    {
        if (entities.Count == 0) return;

        var parentDesc = registry.Get(collection)!;

        object ParentId(object entity) =>
            PropertyAccessorCache.Read(entity, parentDesc.IdProperty)
            ?? throw new QueryException($"Cannot expand relations: a '{collection}' row has no id.");

        // canReadJunction: gates the `_junction` payload sub-object per relation (omitted, never
        // refused, mirroring PruneUnreadable's stance on target-collection reads above) — passed
        // through so the expander's own internal recursion for nested `deep` re-applies it at
        // every level, since a nested M2M relation is expanded inside RelationExpander itself and
        // never comes back through this coordinator.
        var nested = await expander.ExpandAsync(
            collection, entities, visible,
            new EntityAccessors(projector.ProjectFor, ParentId, PropertyAccessorCache.Read),
            locale, permissions.CanRead, ct);

        for (var i = 0; i < entities.Count; i++)
        {
            var pid = ParentId(entities[i]);
            if (!nested.TryGetValue(pid, out var relMap)) continue;
            var dict = (Dictionary<string, object?>)rows[i];
            foreach (var (relName, value) in relMap) dict[relName] = value;
        }
    }

    /// <summary>
    /// Recursively drops every requested relation whose target collection the caller has no read
    /// grant on. Dropping rather than refusing: an expansion is a convenience, and refusing would
    /// fail the entire read — the admin SPA's item form requests <c>deep</c> for every editable
    /// relation it finds in metadata, so one unreadable target would break the form on every
    /// collection for every narrowly-granted role. A relation that does not resolve is kept, so
    /// <see cref="ValidateDeepTree"/> still rejects it by name.
    /// </summary>
    private DeepSpec PruneUnreadable(string coll, DeepSpec spec)
    {
        var kept = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relName, relSpec) in spec.Relations)
        {
            var rel = graph.Resolve(coll, relName);
            if (rel is not null && !permissions.CanRead(rel.TargetCollection)) continue;
            kept[relName] = rel is not null && relSpec.Deep is not null
                ? relSpec with { Deep = PruneUnreadable(rel.TargetCollection, relSpec.Deep) }
                : relSpec;
        }
        return new DeepSpec(kept);
    }

    /// <summary>
    /// Recursively validates a <see cref="DeepSpec"/> tree: throws if nesting depth exceeds
    /// <see cref="StruoQueryOptions.MaxRelationDepth"/> or a relation name is unknown at its level.
    /// Also validates per-level nested-list args (filter/sort/limit/offset): to-many-only,
    /// filter fields whitelisted against the target collection, sort restricted to the target's
    /// own fields (no cross-relation sort for nested lists), and non-negative limit/offset.
    /// </summary>
    private void ValidateDeepTree(string coll, DeepSpec spec, int depth, FilterBudget budget, ref int nodes)
    {
        if (depth > options.MaxRelationDepth)
            throw new QueryException(
                $"Relation nesting too deep (depth {depth}); the maximum is {options.MaxRelationDepth}.");
        foreach (var (relName, relSpec) in spec.Relations)
        {
            var rel = graph.Resolve(coll, relName)
                ?? throw new QueryException($"Unknown relation '{relName}' on '{coll}'.");
            if (++nodes > options.MaxDeepRelations)
                throw new QueryException($"Too many relations in deep (max {options.MaxDeepRelations}).");

            var hasArgs = relSpec.Filter is not null || relSpec.Sort is not null
                          || relSpec.Limit is not null || relSpec.Offset is not null;
            if (hasArgs && rel.Kind == RelationKind.ManyToOne)
                throw new QueryException(
                    $"filter/sort/limit/offset are only supported on to-many relations; " +
                    $"'{relName}' on '{coll}' is many-to-one.");

            if (relSpec.Limit is < 0)
                throw new QueryException($"Nested 'limit' must not be negative for relation '{relName}'.");
            if (relSpec.Offset is < 0)
                throw new QueryException($"Nested 'offset' must not be negative for relation '{relName}'.");

            ValidateNestedFilterAndSort(rel.TargetCollection, relSpec, budget);

            if (relSpec.Deep is not null)
                ValidateDeepTree(rel.TargetCollection, relSpec.Deep, depth + 1, budget, ref nodes);
        }
    }

    private void ValidateNestedFilterAndSort(string target, DeepRelationSpec relSpec, FilterBudget budget)
    {
        var targetMeta = Meta(target);
        if (relSpec.Filter is not null)
            QueryValidator.Validate(
                new QueryModel(null, relSpec.Filter, [], 0, 0, null),
                targetMeta, options, graph, metadata, permissions, budget);

        if (relSpec.Sort is null) return;

        var crossing = relSpec.Sort.FirstOrDefault(s => RelationPath.IsRelationPath(s.Field));
        if (crossing is not null)
            throw new QueryException(
                $"Sort across relations is not supported for nested lists: '{crossing.Field}'.");
        QueryValidator.Validate(
            new QueryModel(null, null, relSpec.Sort, 0, 0, null),
            targetMeta, options, graph, metadata, permissions);
    }

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);
}
