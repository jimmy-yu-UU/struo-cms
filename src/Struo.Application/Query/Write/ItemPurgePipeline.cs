// src/Struo.Application/Query/Write/ItemPurgePipeline.cs
using Struo.Application.Changes;
using Struo.Application.Metadata;
using Struo.Application.Revisions;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The referential-integrity pipeline for permanent deletes, extracted verbatim from
/// <see cref="ItemService"/>. Runs inside the caller's delete transaction. The Restrict guard
/// is shared by the soft-delete (trash) branch and, recursively, by every level of the purge core.
/// </summary>
public sealed class ItemPurgePipeline(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IRelationshipGraph graph,
    IM2MDescriptorSource m2mSource,
    IRevisionStore revisions)
{
    /// <summary>
    /// Throws <see cref="RelationConflictException"/> if any inbound OnDelete.Restrict relation on
    /// <paramref name="collection"/> still has a row referencing <paramref name="id"/>. Shared by the
    /// soft-delete (trash) branch and, recursively, by every level of <see cref="PurgeCoreAsync"/>.
    /// </summary>
    public async Task CheckRestrictAsync(string collection, string id, CancellationToken ct)
    {
        var inbound = graph.InboundRestrict(collection);
        if (inbound.Count == 0) return;

        // Coerce the string id to the PK's CLR type ONCE so QueryWhereInAsync receives a typed
        // value that matches the FK column. Fail loudly on misconfiguration / bad id — a silent
        // fallback would make the IN comparison miss and skip a real Restrict block.
        var typedId = TypedId(collection, id);
        foreach (var (sourceCollection, foreignKey) in inbound)
        {
            var refs = await repository.QueryWhereInAsync(sourceCollection, foreignKey, [typedId], ct);
            if (refs.Count > 0)
                throw new RelationConflictException(
                    $"Cannot delete '{collection}/{id}': referenced by '{sourceCollection}'.");
        }
    }

    /// <summary>
    /// The recursive purge core. Runs, in order: (1) the Restrict guard — same
    /// as <see cref="CheckRestrictAsync"/>; (2) SetNull every inbound FK; (3) recursively purge every
    /// inbound Cascade row through this SAME method (<paramref name="visited"/> is a cross-recursion
    /// cycle guard: a (collection, id) pair already being purged is skipped rather than looping
    /// forever on a cyclic Cascade graph); (4) delete this item's own M2M junction rows (parent side)
    /// and any OTHER collection's M2M junction rows that target this item (target side); (5) delete
    /// its translation sidecar rows; (6) delete its revision history; (7) delete the row itself.
    /// Cascade-deleted rows go through steps 1-7 too, so their own junctions/translations/revisions
    /// are cleaned up exactly like the top-level target. Returns whether the row existed (step 7's
    /// result) — false for an id that does not exist, or one already visited in this purge.
    /// <paramref name="changes"/> collects the whole cascade's <see cref="ItemChange"/>s (one
    /// <see cref="ItemChangeKind.Purged"/> per deleted row, one <see cref="ItemChangeKind.Updated"/>
    /// per live SetNull child, and one <see cref="ItemChangeKind.Updated"/> per live parent that loses
    /// an inbound M2M link) across every recursive call sharing the same set, for the caller to
    /// raise as ONE post-commit notification.
    /// </summary>
    public async Task<bool> PurgeCoreAsync(
        string collection, string id, HashSet<(string Collection, string Id)> visited,
        ItemChangeSet changes, CancellationToken ct)
    {
        if (!visited.Add((collection, id))) return false;

        var meta = Meta(collection);
        await CheckRestrictAsync(collection, id, ct);
        var typedId = TypedId(collection, id);

        foreach (var (sourceCollection, foreignKey) in graph.InboundSetNull(collection))
        {
            await CollectSetNullUpdatesAsync(sourceCollection, foreignKey, typedId, changes, ct);
            await repository.SetForeignKeyNullAsync(sourceCollection, foreignKey, typedId, ct);
        }

        foreach (var (sourceCollection, foreignKey) in graph.InboundCascade(collection))
        {
            var srcDesc = registry.Get(sourceCollection);
            if (srcDesc is null) continue;   // defensive: RelationshipGraph already validates targets are known
            var srcIdProp = srcDesc.EntityType.GetProperty(srcDesc.IdProperty);
            if (srcIdProp is null) continue;

            // Bypasses the soft-delete filter: an already-trashed row of a Cascade source collection
            // that still references the purge target must still be found and cascade-purged too.
            var referencing = await repository.QueryWhereInWithDeletedAsync(sourceCollection, foreignKey, [typedId], ct);
            foreach (var row in referencing)
            {
                var childId = srcIdProp.GetValue(row)?.ToString();
                if (childId is not null)
                    await PurgeCoreAsync(sourceCollection, childId, visited, changes, ct);
            }
        }

        foreach (var desc in m2mSource.M2MDescriptors(collection))
            await repository.DeleteByPropertyAsync(desc.JunctionType, desc.ParentFkProperty, typedId, ct);
        foreach (var inboundDesc in m2mSource.InboundM2MDescriptors(collection))
        {
            await CollectInboundM2MUpdatesAsync(inboundDesc, typedId, changes, ct);
            await repository.DeleteByPropertyAsync(
                inboundDesc.Descriptor.JunctionType, inboundDesc.Descriptor.TargetFkProperty, typedId, ct);
        }

        if (meta.Translation is { } tm)
            await repository.DeleteByPropertyAsync(tm.TranslationEntityType, tm.ForeignKeyProperty, typedId, ct);

        if (meta.Revisions)
            await revisions.DeleteForItemAsync(collection, id, ct);

        var deleted = await repository.DeleteAsync(collection, id, ct);
        if (deleted) changes.Add(meta.Name, typedId.ToString()!, ItemChangeKind.Purged);
        return deleted;
    }

    // U5b: a SetNull child's document changes too (its FK is about to be cleared), so record an
    // Updated for every LIVE row that still points at the purge target — read before the UPDATE,
    // through the same typed QueryWhereInAsync the Restrict guard uses. Trashed children are skipped:
    // they are not in any index and a later restore raises its own Restored.
    private async Task CollectSetNullUpdatesAsync(
        string sourceCollection, string foreignKey, object typedId, ItemChangeSet changes, CancellationToken ct)
    {
        var srcDesc = registry.Get(sourceCollection);
        var srcIdProp = srcDesc?.EntityType.GetProperty(srcDesc.IdProperty);
        if (srcDesc is null || srcIdProp is null) return;
        var srcName = Meta(sourceCollection).Name;
        foreach (var row in await repository.QueryWhereInAsync(sourceCollection, foreignKey, [typedId], ct))
        {
            var childId = srcIdProp.GetValue(row)?.ToString();
            if (childId is not null) changes.Add(srcName, childId, ItemChangeKind.Updated);
        }
    }

    // U5b fix round 1 (spec R4 extension): purging a target whose inbound M2M junction rows are about
    // to be deleted also changes each LIVE parent that carried the link — e.g. purging a Tag drops it
    // from every Article that referenced it. Reads the junction rows via the same typed
    // QueryEntityWhereInAsync the delete just below uses, resolves each row's parent id (reflection on
    // the junction type, same style as the Cascade/SetNull id reads above), then keeps only LIVE
    // parents via QueryWhereInAsync (soft-delete floor applies), mirroring CollectSetNullUpdatesAsync's
    // own live-only semantics — a trashed parent is skipped, not raised.
    private async Task CollectInboundM2MUpdatesAsync(
        InboundM2MDescriptor inbound, object typedId, ItemChangeSet changes, CancellationToken ct)
    {
        var desc = inbound.Descriptor;
        var junctionRows = await repository.QueryEntityWhereInAsync(desc.JunctionType, desc.TargetFkProperty, [typedId], ct);
        if (junctionRows.Count == 0) return;

        var parentFkProp = desc.JunctionType.GetProperty(desc.ParentFkProperty);
        if (parentFkProp is null) return;
        var parentIds = junctionRows
            .Select(row => parentFkProp.GetValue(row))
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();
        if (parentIds.Count == 0) return;

        var srcDesc = registry.Get(inbound.SourceCollection);
        var srcIdProp = srcDesc?.EntityType.GetProperty(srcDesc.IdProperty);
        if (srcDesc is null || srcIdProp is null) return;

        var srcName = Meta(inbound.SourceCollection).Name;
        foreach (var row in await repository.QueryWhereInAsync(inbound.SourceCollection, srcDesc.IdProperty, parentIds, ct))
        {
            var parentId = srcIdProp.GetValue(row)?.ToString();
            if (parentId is not null) changes.Add(srcName, parentId, ItemChangeKind.Updated);
        }
    }

    /// <summary>Coerces a string id to <paramref name="collection"/>'s PK CLR type (e.g. Guid, long).</summary>
    public object TypedId(string collection, string id)
    {
        var d = registry.Get(collection) ?? throw new CollectionNotFoundException(collection);
        var pkProp = d.EntityType.GetProperty(d.IdProperty,
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                     ?? throw new InvalidOperationException(
                         $"Collection '{collection}' has no primary-key property '{d.IdProperty}'.");
        return IdParsing.ParseTo(id, pkProp.PropertyType);
    }

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);
}
