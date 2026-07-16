// src/Struo.Application/Query/Write/ItemPurgePipeline.cs
using Struo.Application.Metadata;
using Struo.Application.Revisions;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The referential-integrity pipeline for permanent deletes (DB-1/DB-2), extracted verbatim from
/// <see cref="ItemService"/> (ARC-1). Runs inside the caller's delete transaction. The Restrict guard
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
    /// The recursive purge core (DB-1/DB-2, Task 5). Runs, in order: (1) the Restrict guard — same
    /// as <see cref="CheckRestrictAsync"/>; (2) SetNull every inbound FK; (3) recursively purge every
    /// inbound Cascade row through this SAME method (<paramref name="visited"/> is a cross-recursion
    /// cycle guard: a (collection, id) pair already being purged is skipped rather than looping
    /// forever on a cyclic Cascade graph); (4) delete this item's own M2M junction rows (parent side)
    /// and any OTHER collection's M2M junction rows that target this item (target side); (5) delete
    /// its translation sidecar rows; (6) delete its revision history; (7) delete the row itself.
    /// Cascade-deleted rows go through steps 1-7 too, so their own junctions/translations/revisions
    /// are cleaned up exactly like the top-level target. Returns whether the row existed (step 7's
    /// result) — false for an id that does not exist, or one already visited in this purge.
    /// </summary>
    public async Task<bool> PurgeCoreAsync(
        string collection, string id, HashSet<(string Collection, string Id)> visited, CancellationToken ct)
    {
        if (!visited.Add((collection, id))) return false;

        var meta = Meta(collection);
        await CheckRestrictAsync(collection, id, ct);
        var typedId = TypedId(collection, id);

        foreach (var (sourceCollection, foreignKey) in graph.InboundSetNull(collection))
            await repository.SetForeignKeyNullAsync(sourceCollection, foreignKey, typedId, ct);

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
                    await PurgeCoreAsync(sourceCollection, childId, visited, ct);
            }
        }

        foreach (var desc in m2mSource.M2MDescriptors(collection))
            await repository.DeleteByPropertyAsync(desc.JunctionType, desc.ParentFkProperty, typedId, ct);
        foreach (var inboundDesc in m2mSource.InboundM2MDescriptors(collection))
            await repository.DeleteByPropertyAsync(
                inboundDesc.Descriptor.JunctionType, inboundDesc.Descriptor.TargetFkProperty, typedId, ct);

        if (meta.Translation is { } tm)
            await repository.DeleteByPropertyAsync(tm.TranslationEntityType, tm.ForeignKeyProperty, typedId, ct);

        if (meta.Revisions)
            await revisions.DeleteForItemAsync(collection, id, ct);

        return await repository.DeleteAsync(collection, id, ct);
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
