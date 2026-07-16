// src/Struo.Application/Query/Read/DeepExpansionCoordinator.cs
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Validates the requested deep relations against the relationship graph and the
/// configured <see cref="StruoQueryOptions.MaxRelationDepth"/>, then nests the expanded
/// (batched) relation rows into each projected parent dictionary. No-op when
/// <c>deep</c> is null/empty or there are no parent rows.
/// </summary>
public sealed class DeepExpansionCoordinator(
    StruoQueryOptions options,
    IRelationshipGraph graph,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IRelationExpander expander,
    ItemProjector projector)
{
    /// <inheritdoc cref="DeepExpansionCoordinator"/>
    public async Task ExpandAsync(
        string collection, DeepSpec? deep,
        IReadOnlyList<object> entities, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string? locale, CancellationToken ct)
    {
        if (deep is null || deep.Relations.Count == 0) return;

        // Validate the whole nested tree: nesting depth <= MaxRelationDepth, and every relation
        // name resolves against its own level's collection. Runs before any query executes,
        // and independent of row count — an over-depth/unknown-relation request must be
        // rejected even when the parent query matched zero rows.
        ValidateDeepTree(collection, deep, depth: 1);
        if (entities.Count == 0) return;

        var parentDesc = registry.Get(collection)!;

        object ParentId(object entity) =>
            PropertyAccessorCache.Read(entity, parentDesc.IdProperty)
            ?? throw new QueryException($"Cannot expand relations: a '{collection}' row has no id.");

        var nested = await expander.ExpandAsync(
            collection, entities, deep, projector.ProjectFor, ParentId, PropertyAccessorCache.Read, locale, ct);

        for (var i = 0; i < entities.Count; i++)
        {
            var pid = ParentId(entities[i]);
            if (!nested.TryGetValue(pid, out var relMap)) continue;
            var dict = (Dictionary<string, object?>)rows[i];
            foreach (var (relName, value) in relMap) dict[relName] = value;
        }
    }

    /// <summary>
    /// Recursively validates a <see cref="DeepSpec"/> tree: throws if nesting depth exceeds
    /// <see cref="StruoQueryOptions.MaxRelationDepth"/> or a relation name is unknown at its level.
    /// Also validates per-level nested-list args (filter/sort/limit/offset): to-many-only,
    /// filter fields whitelisted against the target collection, sort restricted to the target's
    /// own fields (no cross-relation sort for nested lists), and non-negative limit/offset.
    /// </summary>
    private void ValidateDeepTree(string coll, DeepSpec spec, int depth)
    {
        if (depth > options.MaxRelationDepth)
            throw new QueryException(
                $"Relation nesting too deep (depth {depth}); the maximum is {options.MaxRelationDepth}.");
        foreach (var (relName, relSpec) in spec.Relations)
        {
            var rel = graph.Resolve(coll, relName)
                ?? throw new QueryException($"Unknown relation '{relName}' on '{coll}'.");

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

            var targetMeta = Meta(rel.TargetCollection);
            if (relSpec.Filter is not null)
                QueryValidator.Validate(
                    new QueryModel(null, relSpec.Filter, [], 0, 0, null),
                    targetMeta, options, graph, metadata);

            if (relSpec.Sort is not null)
                foreach (var s in relSpec.Sort)
                {
                    if (RelationPath.IsRelationPath(s.Field))
                        throw new QueryException(
                            $"Sort across relations is not supported for nested lists: '{s.Field}'.");
                    QueryValidator.Validate(
                        new QueryModel(null, null, [s], 0, 0, null),
                        targetMeta, options, graph, metadata);
                }

            if (relSpec.Deep is not null)
                ValidateDeepTree(rel.TargetCollection, relSpec.Deep, depth + 1);
        }
    }

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);
}
