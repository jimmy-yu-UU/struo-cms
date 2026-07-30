// src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Rejects a self-referencing ManyToOne update that would create a parent cycle (A→B→…→A).
/// Metadata-driven via RelationMetadata.SelfReferencing, so every tree collection (mediafolder,
/// sample Category) is covered — previously only the SPA's excludeId pruning guarded this, which
/// a direct API caller bypasses. Create is exempt: a fresh server-generated id cannot appear in
/// any existing ancestor chain. Walks the incoming parent's ancestor chain; a dangling parent id
/// ends the walk (FK existence is not this guard's concern — accepted app-only stance). MaxDepth is
/// a defensive stop against pre-existing corrupt data looping forever.
/// </summary>
public sealed class SelfReferenceCycleGuard(IItemRepository repository, IEntityRegistry registry)
{
    private const int MaxDepth = 64;

    public async Task EnsureNoCycleAsync(
        string collection, CollectionMetadata meta, object entityAfterOverlay, CancellationToken ct)
    {
        foreach (var rel in meta.Relations)
        {
            if (!rel.SelfReferencing || rel.Kind != RelationKind.ManyToOne || rel.ForeignKey is null) continue;
            var d = registry.Get(collection);
            if (d is null) return;
            var fkProp = d.Properties.GetValueOrDefault(rel.ForeignKey);
            if (fkProp is null) continue;
            var selfId = d.Properties.GetValueOrDefault(d.IdProperty)?.GetValue(entityAfterOverlay)?.ToString();
            if (selfId is null) continue;

            var parentId = fkProp.GetValue(entityAfterOverlay)?.ToString();
            var depth = 0;
            while (parentId is not null)
            {
                if (string.Equals(parentId, selfId, StringComparison.OrdinalIgnoreCase))
                    throw new QueryException(
                        $"'{rel.ForeignKey}' would create a cycle in '{collection}'.");
                if (++depth > MaxDepth)
                    throw new QueryException(
                        $"'{rel.ForeignKey}' ancestor chain exceeds {MaxDepth} levels in '{collection}'.");
                // DeletedFilter.With: the guard reasons about FK topology, not row visibility — a
                // trashed (soft-deleted) ancestor's FK still exists and must be walked, or a cycle
                // passing through it would escape detection (Category is ISoftDeletable).
                var parent = await repository.GetByIdAsync(collection, parentId, DeletedFilter.With, ct);
                if (parent is null) break;
                parentId = fkProp.GetValue(parent)?.ToString();
            }
        }
    }
}
