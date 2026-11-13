// src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// A self-reference key an update changes: the foreign key's field name and the id the row is moved
/// under (null when it is moved to the root).
/// </summary>
public sealed record SelfReferenceMove(string ForeignKey, string SelfId, string? NewParentId);

/// <summary>
/// Rejects a self-referencing ManyToOne update that would create a parent cycle (A→B→…→A).
/// Metadata-driven via RelationMetadata.SelfReferencing, so every tree collection (mediafolder,
/// sample Category) is covered — the SPA's excludeId pruning guards this only in the UI, which
/// a direct API caller bypasses. Create is exempt: a fresh server-generated id cannot appear in
/// any existing ancestor chain. Both steps run inside the write transaction. Before the row write,
/// <see cref="LockMovedRowAsync"/> takes a locking read of the row and compares the key stored at that
/// moment with the one about to be written; an equal key adds no edge, so only a changed key is walked.
/// After the row write, <see cref="EnsureNoCycleAsync"/> walks the new parent's ancestor chain with
/// locking reads and decides from the values they return, so a concurrent move of any row on the chain
/// waits or loses a deadlock instead of slipping past. A dangling parent id ends the walk (FK existence
/// is not this guard's concern - accepted app-only stance). MaxDepth is a defensive stop against
/// pre-existing corrupt data looping forever.
/// </summary>
public sealed class SelfReferenceCycleGuard(IItemRepository repository, IEntityRegistry registry)
{
    private const int MaxDepth = 64;

    /// <summary>
    /// Locks the row being updated and returns the self-reference keys whose stored value differs from the
    /// value about to be written. Empty for a collection without a self-reference (no read at all) and when
    /// the row does not exist (the row write then finds nothing).
    /// </summary>
    public async Task<IReadOnlyList<SelfReferenceMove>> LockMovedRowAsync(
        string collection, CollectionMetadata meta, object entityAfterOverlay, CancellationToken ct)
    {
        var d = registry.Get(collection);
        var keys = d is null ? [] : SelfReferenceKeys(meta);
        if (d is null || keys.Count == 0) return [];
        var selfId = d.Properties.GetValueOrDefault(d.IdProperty)?.GetValue(entityAfterOverlay)?.ToString();
        if (selfId is null) return [];

        var stored = await repository.GetByIdLockedAsync(collection, selfId, DeletedFilter.With, ct);
        if (stored is null) return [];

        var moves = new List<SelfReferenceMove>();
        foreach (var key in keys)
        {
            var prop = d.Properties.GetValueOrDefault(key);
            if (prop is null) continue;
            var storedParent = prop.GetValue(stored)?.ToString();
            var newParent = prop.GetValue(entityAfterOverlay)?.ToString();
            if (!string.Equals(storedParent, newParent, StringComparison.OrdinalIgnoreCase))
                moves.Add(new SelfReferenceMove(key, selfId, newParent));
        }
        return moves;
    }

    /// <summary>Walks each moved key's new ancestor chain under locks and rejects a cycle.</summary>
    public async Task EnsureNoCycleAsync(
        string collection, IReadOnlyList<SelfReferenceMove> moves, CancellationToken ct)
    {
        if (moves.Count == 0) return;
        var d = registry.Get(collection);
        if (d is null) return;
        foreach (var move in moves)
        {
            var prop = d.Properties.GetValueOrDefault(move.ForeignKey);
            if (prop is null) continue;
            await WalkChainAsync(collection, move, parent => prop.GetValue(parent)?.ToString(), ct);
        }
    }

    private static List<string> SelfReferenceKeys(CollectionMetadata meta) =>
        [.. meta.Relations
            .Where(r => r.SelfReferencing && r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
            .Select(r => r.ForeignKey!)];

    private async Task WalkChainAsync(
        string collection, SelfReferenceMove move, Func<object, string?> parentOf, CancellationToken ct)
    {
        var parentId = move.NewParentId;
        var depth = 0;
        while (parentId is not null)
        {
            if (string.Equals(parentId, move.SelfId, StringComparison.OrdinalIgnoreCase))
                throw new QueryException($"'{move.ForeignKey}' would create a cycle in '{collection}'.");
            if (++depth > MaxDepth)
                throw new QueryException(
                    $"'{move.ForeignKey}' ancestor chain exceeds {MaxDepth} levels in '{collection}'.");
            // DeletedFilter.With: the guard reasons about FK topology, not row visibility - a
            // trashed (soft-deleted) ancestor's FK still exists and must be walked, or a cycle
            // passing through it would escape detection (Category is ISoftDeletable).
            var parent = await repository.GetByIdLockedAsync(collection, parentId, DeletedFilter.With, ct);
            if (parent is null) break;
            parentId = parentOf(parent);
        }
    }
}
