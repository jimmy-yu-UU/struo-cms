// src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Rejects a self-referencing ManyToOne update that would create a parent cycle (A→B→…→A).
/// Metadata-driven via RelationMetadata.SelfReferencing, so every tree collection (mediafolder,
/// sample Category) is covered — the SPA's excludeId pruning guards this only in the UI, which
/// a direct API caller bypasses. Create is exempt: a fresh server-generated id cannot appear in
/// any existing ancestor chain. It must run inside the write transaction: it locks the row being
/// moved and every row on the incoming parent's ancestor chain with locking reads, and decides from
/// the values those reads return, so a concurrent move of any row on the chain waits or loses a
/// deadlock instead of slipping past. A dangling parent id ends the walk (FK existence is not this
/// guard's concern — accepted app-only stance). MaxDepth is a defensive stop against pre-existing
/// corrupt data looping forever.
/// </summary>
public sealed class SelfReferenceCycleGuard(IItemRepository repository, IEntityRegistry registry)
{
    private const int MaxDepth = 64;

    public async Task EnsureNoCycleAsync(
        string collection, CollectionMetadata meta, object entityAfterOverlay, CancellationToken ct)
    {
        var d = registry.Get(collection);
        if (d is null) return;
        foreach (var rel in meta.Relations)
        {
            if (!rel.SelfReferencing || rel.Kind != RelationKind.ManyToOne || rel.ForeignKey is null) continue;
            var fkProp = d.Properties.GetValueOrDefault(rel.ForeignKey);
            if (fkProp is null) continue;
            var selfId = d.Properties.GetValueOrDefault(d.IdProperty)?.GetValue(entityAfterOverlay)?.ToString();
            if (selfId is null) continue;

            await repository.GetByIdLockedAsync(collection, selfId, DeletedFilter.With, ct);
            await WalkChainAsync(collection, rel.ForeignKey, selfId, fkProp.GetValue(entityAfterOverlay)?.ToString(),
                parent => fkProp.GetValue(parent)?.ToString(), ct);
        }
    }

    private async Task WalkChainAsync(
        string collection, string foreignKey, string selfId, string? parentId,
        Func<object, string?> parentOf, CancellationToken ct)
    {
        var depth = 0;
        while (parentId is not null)
        {
            if (string.Equals(parentId, selfId, StringComparison.OrdinalIgnoreCase))
                throw new QueryException($"'{foreignKey}' would create a cycle in '{collection}'.");
            if (++depth > MaxDepth)
                throw new QueryException(
                    $"'{foreignKey}' ancestor chain exceeds {MaxDepth} levels in '{collection}'.");
            // DeletedFilter.With: the guard reasons about FK topology, not row visibility — a
            // trashed (soft-deleted) ancestor's FK still exists and must be walked, or a cycle
            // passing through it would escape detection (Category is ISoftDeletable).
            var parent = await repository.GetByIdLockedAsync(collection, parentId, DeletedFilter.With, ct);
            if (parent is null) break;
            parentId = parentOf(parent);
        }
    }
}
