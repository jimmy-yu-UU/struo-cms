// src/Struo.Application/Query/ItemService.cs
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Struo.Application.Abstractions;
using Struo.Application.Changes;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Revisions;
using Struo.Application.Search;
using Struo.Application.Security;
using Struo.Domain.Auditing;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record PagedResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Data, int Total, int Limit, int Offset,
    IReadOnlyList<FacetResult>? Facets = null, AggregateResult? Aggregate = null);

public sealed class ItemService(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IPermissionService permissions,
    IRelationshipGraph graph,
    IRelationExpander expander,
    IM2MDescriptorSource m2mSource,
    ILanguageProvider languages,
    StruoQueryOptions options,
    IHtmlSanitizer sanitizer,
    ICurrentUserAccessor currentUser,
    IRevisionStore revisions,
    RevisionSnapshotBuilder snapshotBuilder,
    // U5: trailing and optional — existing test files construct ItemService directly (necessary
    // constructor args are not optional there), and DI always injects the AddStruoData-registered
    // instance; null (direct construction) means the built-in LIKE search only.
    ISearchProvider? searchProvider = null,
    // U5b: trailing and optional, same rationale as searchProvider above; null means no listener
    // is registered (or a test harness doesn't care), so NotifyAsync below is a no-op.
    IItemChangeNotifier? notifier = null,
    // Trailing and optional like the others: the write interceptors and delete handlers DI collects
    // from every registration; null (direct construction in tests) means none.
    IEnumerable<IItemWriteInterceptor>? interceptors = null,
    IEnumerable<ICollectionDeleteHandler>? deleteHandlers = null) : IItemUseCases
{
    private const string ReadNotPermitted = "Read not permitted.";

    private readonly ItemDeserializer deserializer = new(registry, m2mSource, new(sanitizer));
    // ItemWriteSideSync gets its own ItemDeserializer instance — a field initializer cannot reference
    // the sibling `deserializer` field above (CS0236).
    private readonly ItemWriteSideSync writeSync =
        new(repository, m2mSource, languages, new(sanitizer), new(registry, m2mSource, new(sanitizer)), metadata, permissions);
    private readonly ItemPurgePipeline purge = new(repository, metadata, registry, graph, m2mSource, revisions);
    private readonly ItemWriteInterceptors writeInterceptors = new(interceptors, deleteHandlers);
    private readonly SelfReferenceCycleGuard cycleGuard = new(repository, registry);
    private readonly ItemProjector projector = new(registry, permissions, metadata);
    private readonly TranslationOverlay overlay =
        new(repository, registry, new(registry, permissions, metadata), permissions);
    private readonly DeepExpansionCoordinator deepExpansion =
        new(options, graph, metadata, registry, expander, new(registry, permissions, metadata), permissions);
    // U5: consulted once per list request by QueryAsync; null (direct construction in tests) means
    // the built-in LIKE search only, exactly what the DI default NullSearchProvider also yields.
    private readonly SearchCandidateResolver searchCandidates =
        new(searchProvider ?? NullSearchProvider.Instance, options, registry);

    public async Task<PagedResult> QueryAsync(
        string collection, QueryModel raw, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException(ReadNotPermitted);
        locale = await ValidateLocaleAsync(locale);

        // Effective query locale: explicit locale ?? global default. Computed unconditionally —
        // the subquery translator needs it even for a root collection without its own translation
        // sidecar (e.g. a relation path reaching into a translated related collection).
        var queryLocale = locale ?? await languages.DefaultCodeAsync();

        var budget = new FilterBudget();
        var validated = QueryValidator.Validate(raw, meta, options, graph, metadata, permissions, budget);
        // The deep tree shares the filter budget and is refused before any search or root query runs.
        var deepSpec = deepExpansion.Prepare(collection, raw.Deep, budget);
        var searchable = QueryValidator.SearchableFields(meta);
        // A registered ISearchProvider may answer the search with candidate ids (spec §3.2); the same
        // `validated` then feeds the list, every facet and the aggregate, so all three agree (R3).
        // meta.Name (not the raw `collection` argument) is the canonical camelCase collection name:
        // the REST route segment is case-insensitive (e.g. "/api/items/Article" resolves fine), so a
        // provider must always see the same name regardless of how the caller cased the request.
        validated = await searchCandidates.ResolveAsync(meta.Name, validated, searchable, queryLocale, ct);
        var result = await repository.QueryAsync(collection, validated, searchable, queryLocale, deleted, ct);

        var entities = result.Rows;
        var rows = entities.Select(r => projector.Project(r, meta, validated.Fields)).ToList();
        if (deepSpec is not null)
            await deepExpansion.ExpandPreparedAsync(collection, deepSpec, entities, rows, queryLocale, ct);
        await overlay.ApplyAsync(meta, entities, rows, locale, ct);
        var facets = await FacetsAsync(collection, meta, validated, searchable, queryLocale, deleted, ct);
        var aggregate = validated.Aggregate is null ? null
            : await repository.AggregateAsync(collection, validated, validated.Aggregate, searchable, queryLocale, deleted, ct);
        return new PagedResult(rows, result.Total, validated.Limit, validated.Offset, facets, aggregate);
    }

    /// <summary>
    /// Computes one <see cref="FacetResult"/> per requested facet path, in request order. Returns
    /// null when no facets were requested (spec R5: <c>meta.facets</c> must not appear at all in
    /// that case). Each facet's own current-value filter is pruned from the query it is computed
    /// under (<see cref="FacetFilterPruner"/>), so a facet's buckets reflect every OTHER active
    /// filter but never narrow themselves out by their own selection — the root's paginated rows
    /// keep the unpruned <paramref name="validated"/> filter throughout.
    /// </summary>
    private async Task<IReadOnlyList<FacetResult>?> FacetsAsync(
        string collection, CollectionMetadata meta, QueryModel validated, IReadOnlyList<string> searchable,
        string queryLocale, DeletedFilter deleted, CancellationToken ct)
    {
        if (validated.Facets is not { Count: > 0 }) return null;
        var resolved = QueryValidator.ResolveFacets(validated, meta, graph, metadata);
        var results = new List<FacetResult>(resolved.Count);
        foreach (var facet in resolved)
        {
            var pruned = validated with { Filter = FacetFilterPruner.Prune(validated.Filter, facet) };
            var request = new FacetRequest(collection, pruned, facet, searchable, queryLocale, deleted, options.MaxFacetValues);
            var buckets = await repository.FacetAsync(request, ct);
            results.Add(new FacetResult(facet.Raw, buckets));
        }
        return results;
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetAsync(
        string collection, string id, DeepSpec? deep = null, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException(ReadNotPermitted);
        locale = await ValidateLocaleAsync(locale);
        var deepSpec = deepExpansion.Prepare(collection, deep, budget: null);
        var entity = await repository.GetByIdAsync(collection, id, deleted, ct);
        if (entity is null) return null;

        var projected = (Dictionary<string, object?>)projector.Project(entity, meta, null);
        var queryLocale = locale ?? await languages.DefaultCodeAsync();
        if (deepSpec is not null)
            await deepExpansion.ExpandPreparedAsync(collection, deepSpec, [entity], [projected], queryLocale, ct);
        await overlay.ApplyAsync(meta, [entity], [projected], locale, ct);
        return projected;
    }

    // Returns the configured spelling of an enabled locale, so callers query and store one form.
    private async Task<string?> ValidateLocaleAsync(string? locale)
    {
        if (locale is null) return null;
        // Reject ill-formed locale codes before the membership check, so a crafted code
        // (for example one containing a quote) never reaches a query.
        if (!LocaleFormat.IsValid(locale))
            throw new QueryException(
                $"Locale '{locale}' contains invalid characters. Codes must match [A-Za-z0-9_-]{{1,35}}.");
        return await languages.CanonicalizeAsync(locale)
            ?? throw new QueryException($"Unknown or disabled locale '{locale}'.");
    }

    public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        // Rows of a non-creatable collection come from a dedicated pipeline of their own, so the
        // generic path refuses before any body inspection.
        if (!meta.Creatable)
            throw new QueryException($"Items cannot be created in collection '{meta.Name}' through the generic items API.");
        // A non-object top-level body (array or scalar) would reach Deserialize below, which assumes an
        // object and throws an unhandled InvalidOperationException (-> 500). Reject it here as a 400.
        if (body.ValueKind != JsonValueKind.Object)
            throw new QueryException("Request body must be a JSON object.");
        // No Required check yet: BeforeWrite may fill a Required field (a generated slug).
        var entity = deserializer.DeserializeForCreate(collection, body, meta);
        var d = registry.Get(collection)!;
        var hooks = writeInterceptors.Begin(
            ItemWriteOperation.Create, meta, entity, id: null, ItemDeserializer.PresentNames(body), body);
        await hooks.BeforeWriteAsync(ct);
        ItemDeserializer.EnforceRequiredFields(meta, d, entity);

        // Parent row + M2M junctions + translation sidecars must commit together or not at all —
        // otherwise a failure after the parent insert leaves a row that violates invariants the API
        // enforces (e.g. "default-locale translation required").
        object created = null!;
        object createdId = null!;
        await repository.InTransactionAsync(async () =>
        {
            created = await repository.CreateAsync(collection, entity, ct);
            createdId = d.Properties.GetValueOrDefault(d.IdProperty)!.GetValue(created)!;
            await writeSync.SyncM2MAsync(collection, body, createdId, includeDeleted: false, ct);
            await writeSync.SyncTranslationsAsync(meta, body, createdId, isCreate: true, ct);
            hooks.Stored(created, createdId);
            await hooks.InTransactionAsync(ct);
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, created, ct);
                await revisions.CaptureAsync(collection, createdId.ToString()!, "create", snapshot, ct: ct);
            }
        }, ct);
        await AfterCommitAndNotifyAsync(hooks, SingleChange(meta.Name, createdId, ItemChangeKind.Created));
        return projector.Project(created, meta, null);
    }

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
        => UpdateCoreAsync(collection, id, body, "update", sourceRevisionNumber: null, ct: ct);

    private async Task<IReadOnlyDictionary<string, object?>?> UpdateCoreAsync(
        string collection, string id, JsonElement body, string operation,
        long? sourceRevisionNumber, CancellationToken ct)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        // Same non-object-body guard as CreateAsync — placed identically, right after the
        // permission checks and before deserializing.
        if (body.ValueKind != JsonValueKind.Object)
            throw new QueryException("Request body must be a JSON object.");
        var d = registry.Get(collection)!;

        var loaded = await LoadForUpdateAsync(collection, id, meta, ct);
        if (loaded is null) return null;
        var (existing, preMerge) = loaded.Value;

        var bodyKeys = MergeBody(collection, meta, d, body, existing);

        var hooks = writeInterceptors.Begin(
            operation == "revert" ? ItemWriteOperation.Revert : ItemWriteOperation.Update,
            meta, existing, id, bodyKeys, body, preMerge);
        await hooks.BeforeWriteAsync(ct);
        await ValidateMergedAsync(collection, meta, d, body, existing, ct);

        var persisted = await PersistUpdateAsync(
            new UpdateTarget(collection, id, operation, sourceRevisionNumber), meta, d, body, existing, hooks, ct);
        if (persisted is null) return null;
        var (updated, updatedId) = persisted.Value;
        await AfterCommitAndNotifyAsync(hooks, SingleChange(meta.Name, updatedId, ItemChangeKind.Updated));
        return projector.Project(updated, meta, null);
    }

    // Loads the stored row (null -> 404); the merge mutates it.
    private async Task<(object Existing, object? PreMerge)?> LoadForUpdateAsync(
        string collection, string id, CollectionMetadata meta, CancellationToken ct)
    {
        var existing = await repository.GetByIdAsync(collection, id, ct: ct);
        if (existing is null) return null;
        // A second copy of the stored row for interceptors that compare old and new.
        var preMerge = writeInterceptors.AnyApplies(meta)
            ? await repository.GetByIdAsync(collection, id, ct: ct)
            : null;
        return (existing, preMerge);
    }

    // Merge update: overlays ONLY the fields the client actually sent (writable, non-readonly,
    // non-system) and the M2O foreign keys onto `existing`, and returns the body keys. This preserves
    // server-managed columns the client never sends — e.g. a File's StorageKey/FileName/Size — so a
    // partial PUT (status + translations only) cannot wipe NOT NULL metadata. Relations and
    // translations are synced separately by persist.
    private HashSet<string> MergeBody(
        string collection, CollectionMetadata meta, EntityDescriptor d, JsonElement body, object existing)
    {
        // No Required check here — an update only overlays fields the client actually sent (see
        // the loops below), so a Required field this body omits must not fail on the freshly-parsed
        // `incoming` alone; the Required check re-runs against the merged `existing` entity in
        // ValidateMergedAsync.
        var incoming = deserializer.DeserializeForUpdate(collection, body, meta);
        // Shared with ItemDeserializer.DeserializeCore's own presence gating, so the two can never
        // silently drift apart on what counts as "the client sent this key".
        var bodyKeys = ItemDeserializer.PresentNames(body);
        OverlayFields(meta, d, bodyKeys, existing, incoming);
        OverlayManyToOneKeys(meta, d, bodyKeys, existing, incoming);
        return bodyKeys;
    }

    private static void OverlayFields(
        CollectionMetadata meta, EntityDescriptor d, HashSet<string> bodyKeys, object existing, object incoming)
    {
        foreach (var field in meta.Fields)
        {
            if (field.IsSystem || field.ReadOnly) continue;
            if (!bodyKeys.Contains(field.Name)) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.Properties.GetValueOrDefault(prop);
            if (pi is not { CanWrite: true }) continue;
            pi.SetValue(existing, pi.GetValue(incoming));
        }
    }

    // Relation foreign keys (M2O / self-referencing tree) are declared via [CmsRelation], not
    // [CmsField], so the field overlay skips them. Overlay each local FK the client
    // actually sent, keeping the same allowlist discipline (only declared relations, only keys
    // present in the body). Restricted to ManyToOne: O2M relations also carry a non-null
    // ForeignKey (it names the FK column on the *related* entity, not this one), so without
    // this guard the loop would try to overlay a property that doesn't exist/isn't writable
    // here; M2M has no local FK at all.
    private static void OverlayManyToOneKeys(
        CollectionMetadata meta, EntityDescriptor d, HashSet<string> bodyKeys, object existing, object incoming)
    {
        foreach (var rel in meta.Relations)
        {
            if (rel.Kind != RelationKind.ManyToOne || rel.ForeignKey is null) continue;
            if (!bodyKeys.Contains(rel.ForeignKey)) continue;  // only overlay when the client sent this FK
            // ForeignKey is stored camelCase (e.g. "categoryId") but the CLR property is PascalCase
            // (e.g. CategoryId), so the lookup must be case-insensitive.
            var pi = d.Properties.GetValueOrDefault(rel.ForeignKey);
            if (pi is not { CanWrite: true }) continue;        // FK must be a writable property on THIS entity
            pi.SetValue(existing, pi.GetValue(incoming));
        }
    }

    // Required check runs here — against the MERGED entity, after both overlay loops in MergeBody —
    // rather than while deserializing `incoming`: an omitted Required field keeps whatever
    // `existing` already held (no failure), while an explicit null/blank/Guid.Empty for that
    // field still 400s with the same message CREATE uses. The write interceptors' BeforeWrite runs
    // immediately before it, and MaxLength and the per-interface validator phases already ran on
    // `incoming` inside DeserializeForUpdate, so a body that violates one of those rules AND
    // Required (on the merged entity) reports the earlier failure, exactly as CREATE does. A revert
    // passes a full snapshot as its body, so every field is re-sent and this check sees the same
    // values a full PUT would.
    private async Task ValidateMergedAsync(
        string collection, CollectionMetadata meta, EntityDescriptor d, JsonElement body, object existing,
        CancellationToken ct)
    {
        ItemDeserializer.EnforceRequiredFields(meta, d, existing);

        // Self-referencing tree collections: reject a parentId that points at the item itself or
        // one of its descendants (cycle). Runs after the FK overlay so it sees the incoming value.
        await cycleGuard.EnsureNoCycleAsync(collection, meta, existing, ct);

        // Optimistic concurrency: guard the write on the version the client last read. When the
        // client echoes `version`, the repository's compare-and-swap rejects the update (409) if another
        // writer already moved the row on. Absent a client version we fall back to the freshly-loaded
        // value (no protection, but backward compatible for callers that don't track versions).
        if (existing is Struo.Domain.Auditing.AuditableEntity ex)
            ex.Version = JsonBodyUtil.TryReadVersion(body) ?? ex.Version;
    }

    private readonly record struct UpdateTarget(string Collection, string Id, string Operation, long? SourceRevisionNumber);

    // Parent row + M2M + translations commit atomically (see CreateAsync). Null when the row vanished.
    private async Task<(object Updated, object UpdatedId)?> PersistUpdateAsync(
        UpdateTarget target, CollectionMetadata meta, EntityDescriptor d, JsonElement body,
        object existing, ItemWriteHooks hooks, CancellationToken ct)
    {
        var (collection, id, operation, sourceRevisionNumber) = target;
        object? updated = null;
        object updatedId = null!;
        await repository.InTransactionAsync(async () =>
        {
            updated = await repository.UpdateAsync(collection, id, existing, ct);
            if (updated is null) return;
            updatedId = d.Properties.GetValueOrDefault(d.IdProperty)!.GetValue(updated)!;
            // A revert re-applies a past snapshot, which may reference an M2M target trashed since
            // capture — tolerate it (operation == "revert"); every other write path stays strict.
            await writeSync.SyncM2MAsync(collection, body, updatedId, includeDeleted: operation == "revert", ct);
            await writeSync.SyncTranslationsAsync(meta, body, updatedId, isCreate: false, ct);
            hooks.Stored(updated, updatedId);
            await hooks.InTransactionAsync(ct);
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, updated, ct);
                await revisions.CaptureAsync(collection, updatedId.ToString()!, operation, snapshot,
                    sourceRevisionNumber, ct: ct);
            }
        }, ct);
        return updated is null ? null : (updated, updatedId);
    }

    /// <summary>
    /// Deletes an item. For a soft-deletable collection with <paramref name="purge"/> false this
    /// stamps <c>DeletedAt</c> (trash). Otherwise (explicit purge, or no soft-delete tier) it is a
    /// permanent removal via <see cref="ItemPurgePipeline.PurgeCoreAsync"/> (referential
    /// integrity) inside ONE transaction so a mid-pipeline failure leaves nothing half-deleted.
    /// </summary>
    public async Task<bool> DeleteAsync(string collection, string id, bool purge = false, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new PermissionDeniedException("Delete not permitted.");
        RequireSuperAdminForAdminOnly(meta);

        if (writeInterceptors.DeleteHandlerFor(meta) is { } handler)
            return await DeleteWithHandlerAsync(collection, meta, id, purge, handler, ct);

        if (meta.SoftDelete && !purge)
        {
            // Pre-read resolves ONLY unknown-id -> 404 (mirrors RestoreAsync's pre-read below). Whether
            // the row is ACTUALLY (still) live and gets trashed BY THIS CALL is decided inside the
            // transaction by repository.SoftDeleteAsync's own atomic "WHERE deletedat IS NULL" UPDATE
            // (affected-rows > 0) — not by inspecting this pre-read snapshot. A decision made here,
            // outside the transaction, would leave a TOCTOU window where two concurrent DELETEs of the
            // same live row could each pass the check and both re-stamp/re-version and double-record a
            // "delete" revision. An already-trashed row (or one trashed by a concurrent request first) is
            // a no-op: no Version bump, no revision — but caller-visible success is unchanged (the row
            // exists, so this call still reports true; only an unknown id reports false).
            var existing = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
            if (existing is null) return false; // unknown id -> unchanged (false)
            var trashHooks = writeInterceptors.Begin(ItemWriteOperation.Trash, meta, existing, id);
            await trashHooks.BeforeWriteAsync(ct);

            // Restrict still guards the soft-delete (trash) branch — unchanged contract. The purge
            // branch below re-runs the identical check as the first step of PurgeCoreAsync, so every
            // recursively-cascaded row is ALSO Restrict-guarded, not just the top-level target.
            // The check runs INSIDE the same transaction as the trash write (mirroring the
            // purge branch's boundary below and RestoreAsync's boundary), narrowing the check-to-write
            // window to purge-parity. Under PG READ COMMITTED a plain SELECT takes no row lock, so a
            // referencing row committed by another txn between the check and the commit is still possible
            // — full closure would need FK/locking (accepted app-only stance), so this narrows rather than
            // eliminates the race. This check runs even when the row is already trashed (trashedNow
            // false below) — harmless, since it is a pure read-only guard and repeating it mutates nothing.
            var actor = currentUser.GetCurrentUserId();

            // Trash bumps the item's Version (repository, AuditableEntity only) AND — for a
            // revisioned collection — records a "delete" revision. Both must commit together with the
            // trash stamp, so a capture failure rolls the whole trash back (no half-trashed row, no
            // orphan revision). Revision capture is gated on the atomic UPDATE actually having
            // affected the row, so a losing concurrent DELETE (or a repeat DELETE of an already-trashed
            // row) records nothing.
            var softDeletedNow = await repository.InTransactionAsync(async () =>
            {
                await this.purge.CheckRestrictAsync(collection, id, ct);
                var trashedNow = await repository.SoftDeleteAsync(collection, id, DateTime.UtcNow, actor, ct);
                if (trashedNow)
                {
                    await trashHooks.InTransactionAsync(ct);
                    await CaptureRevisionAsync(collection, id, meta, "delete", ct);
                }
                return trashedNow;
            }, ct);
            // AfterCommit runs for an already-trashed row too, with Changed false.
            trashHooks.SetChanged(softDeletedNow);
            await AfterCommitAndNotifyAsync(trashHooks, softDeletedNow
                ? SingleChange(meta.Name, this.purge.TypedId(collection, id), ItemChangeKind.Trashed)
                : null);
            return true; // idempotent success: the row exists, whether newly trashed here or already trashed
        }

        var purgeHooks = await BeginPurgeHooksAsync(collection, meta, id, ct);
        var changes = new ItemChangeSet();
        var existed = await repository.InTransactionAsync(async () =>
        {
            var purged = await this.purge.PurgeCoreAsync(collection, id, new HashSet<(string Collection, string Id)>(), changes, ct);
            if (purged) await purgeHooks.InTransactionAsync(ct);
            return purged;
        }, ct);
        // Gated on the top-level row having existed: with no DB-level FKs, a dangling child row (its FK
        // pointing at an id that was never a real row) could otherwise still be found by the Cascade/
        // SetNull/M2M scans above and raise its own Updated/Purged even though nothing here actually
        // existed to purge. Accepted — the only way to hit it is already-inconsistent data.
        if (existed) await AfterCommitAndNotifyAsync(purgeHooks, changes);
        return existed;
    }

    // The interceptors' pre-read of the row a purge removes. Skipped, like the hooks, when no interceptor
    // applies or the id is unknown (nothing to hand over).
    private async Task<ItemWriteHooks> BeginPurgeHooksAsync(
        string collection, CollectionMetadata meta, string id, CancellationToken ct)
    {
        if (!writeInterceptors.AnyApplies(meta)) return ItemWriteHooks.Empty;
        var row = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (row is null) return ItemWriteHooks.Empty;
        var hooks = writeInterceptors.Begin(ItemWriteOperation.Purge, meta, row, id);
        await hooks.BeforeWriteAsync(ct);
        return hooks;
    }

    // A registered delete handler replaces the trash/purge below; interceptors see BeforeWrite and, once
    // the handler reports the delete done, AfterCommit.
    private async Task<bool> DeleteWithHandlerAsync(
        string collection, CollectionMetadata meta, string id, bool purge,
        ICollectionDeleteHandler handler, CancellationToken ct)
    {
        if (!handler.IsValidId(id)) return false;
        var existing = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (existing is null) return false;
        var hooks = writeInterceptors.Begin(
            purge ? ItemWriteOperation.Purge : ItemWriteOperation.Trash, meta, existing, id);
        await hooks.BeforeWriteAsync(ct);
        var done = purge ? await handler.PurgeAsync(id, ct) : await handler.TrashAsync(id, ct);
        // The handler owns the transaction and any notification, so only AfterCommit applies here.
        if (done) await AfterCommitAndNotifyAsync(hooks, changes: null);
        return done;
    }

    /// <summary>
    /// Restores a soft-deleted row: looks it up ignoring the soft-delete floor (it is, by definition,
    /// trashed), clears its <c>DeletedAt</c>/<c>DeletedBy</c> marker if still set, and returns the
    /// re-read live projection. Returns null for an unknown id (404); idempotent when already live.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(
        string collection, string id, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new PermissionDeniedException("Delete not permitted.");
        RequireSuperAdminForAdminOnly(meta);

        // Find the row ignoring the soft-delete floor (it is, by definition, trashed). This pre-read
        // only resolves unknown-id -> 404 and confirms the collection is soft-deletable; it is NOT
        // relied on to decide whether a restore actually happens (see the note below).
        var entity = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (entity is null) return null;                       // unknown id -> 404
        var hooks = writeInterceptors.Begin(ItemWriteOperation.Restore, meta, entity, id);
        await hooks.BeforeWriteAsync(ct);

        // The restore clears DeletedAt, bumps Version and — for a revisioned collection — records
        // a "restore" revision, all in ONE txn (capture rolls back with it).
        // Whether a row is ACTUALLY restored is decided by repository.RestoreAsync's atomic
        // "WHERE deletedat IS NOT NULL" UPDATE (affected-rows > 0), not by inspecting this pre-read
        // snapshot — a pre-read check here would leave a TOCTOU window where two concurrent restores of
        // the same row could each pass the check and double-record a "restore" revision. An already-live
        // row (or one restored by a concurrent request first) is a no-op: no Version bump, no revision.
        var restoredNow = entity is ISoftDeletable && await repository.InTransactionAsync(async () =>
        {
            var r = await repository.RestoreAsync(collection, id, ct);
            if (r)
            {
                await hooks.InTransactionAsync(ct);
                await CaptureRevisionAsync(collection, id, meta, "restore", ct);
            }
            return r;
        }, ct);
        hooks.SetChanged(restoredNow);
        await AfterCommitAndNotifyAsync(hooks, restoredNow
            ? SingleChange(meta.Name, this.purge.TypedId(collection, id), ItemChangeKind.Restored)
            : null);

        var restored = await repository.GetByIdAsync(collection, id, DeletedFilter.Exclude, ct);
        return restored is null ? null : projector.Project(restored, meta, null);
    }

    /// <summary>
    /// For a revisioned collection, re-reads the just-trashed/just-restored row (ignoring the
    /// soft-delete floor) and captures a revision under <paramref name="operation"/> ("delete" /
    /// "restore"). No-op when the collection keeps no revisions or the row vanished. Must run inside
    /// the trash/restore transaction so the snapshot commits atomically with the state change.
    /// </summary>
    private async Task CaptureRevisionAsync(
        string collection, string id, CollectionMetadata meta, string operation, CancellationToken ct)
    {
        if (!meta.Revisions) return;
        var entity = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (entity is null) return;
        var snapshot = await snapshotBuilder.BuildAsync(collection, entity, ct);
        await revisions.CaptureAsync(collection, id, operation, snapshot, ct: ct);
    }

    /// <summary>
    /// Reverts an item to a past revision by re-applying that revision's snapshot as a normal update
    /// (append-only: a new "revert" revision is recorded; forward history is never deleted). Returns the
    /// re-read item, or null for an unknown collection-revision/item (→ 404). Requires write permission.
    /// A non-null <c>expectedVersion</c> must equal the item's current version, otherwise the update path
    /// throws <see cref="ConcurrencyConflictException"/> (→ 409 VERSION_CONFLICT).
    /// <para>
    /// PINNED BEHAVIOUR: because a payload M2M relation's snapshot is captured as
    /// <c>[{id, ...payload}]</c> object elements (not bare ids), reverting such a relation goes through
    /// the same <c>ItemWriteSideSync.EnsureJunctionPayloadGrant</c> gate as any other payload write — the
    /// caller must additionally hold the junction collection's write grant (and super-admin when the
    /// junction is <c>AdminOnly</c>), or this throws <see cref="PermissionDeniedException"/>, even though
    /// the caller already holds write on the parent collection. This is intentional and fails closed: a
    /// role that may write the parent but not the junction must not be able to smuggle a junction-payload
    /// change through revert. Grant the junction collection to any role that must be able to revert a
    /// parent carrying junction payload.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> RevertAsync(
        string collection, string id, long revisionNumber, long? expectedVersion = null,
        CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        if (!meta.Revisions) return null;                                   // collection keeps no revisions -> 404

        var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
        if (rec is null) return null;                                       // unknown revision/item -> 404

        // The snapshot IS a valid update body by construction. Its own `version` is the token of the
        // past revision, so it is replaced by the caller's expected version (the update path's
        // compare-and-swap then raises the 409 when the row has moved on), or dropped when the caller
        // sent none. Keep the JsonDocument alive across the awaited update (the body's JsonElement must
        // stay valid).
        using var src = JsonDocument.Parse(rec.Snapshot);
        using var doc = JsonDocument.Parse(JsonBodyUtil.WithVersion(src.RootElement, expectedVersion));
        return await UpdateCoreAsync(collection, id, doc.RootElement, "revert", revisionNumber, ct: ct);
    }

    /// <summary>Newest-first revision metadata for an item. Requires read permission. Empty for a
    /// non-revisioned collection; null (→ 404) for an unknown item or a trashed item the caller lacks
    /// delete permission to see.</summary>
    public async Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>?> ListRevisionsAsync(
        string collection, string id, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException(ReadNotPermitted);
        if (!meta.Revisions) return [];
        if (!await IsItemVisibleForRevisionsAsync(collection, id, ct)) return null;
        return await revisions.ListAsync(collection, id, ct);
    }

    /// <summary>True when the item exists and is either live or trashed with the caller holding
    /// delete permission. Hides trashed items' history from read-only callers as not-found.</summary>
    private async Task<bool> IsItemVisibleForRevisionsAsync(string collection, string id, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (entity is null) return false;
        return entity is not ISoftDeletable { DeletedAt: not null } || permissions.CanDelete(collection);
    }

    /// <summary>A single revision incl. its snapshot. Requires read permission. Null for an
    /// unknown revision or a non-revisioned collection (→ 404).
    /// <para>
    /// The captured snapshot is FULL item state incl. <c>Hidden</c> fields (a revert needs
    /// them all); that full-fidelity form is internal to <see cref="RevertAsync"/> only. The snapshot
    /// returned HERE to external callers (REST/GraphQL <c>xRevision</c>) has hidden fields redacted via
    /// <see cref="RevisionSnapshotRedactor.RedactHidden"/> first. Otherwise gated only by collection-level
    /// <c>CanRead</c>; a future field-level-read-grant feature should tighten this further.
    /// </para></summary>
    public async Task<Struo.Application.Revisions.RevisionRecord?> GetRevisionAsync(
        string collection, string id, long revisionNumber, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException(ReadNotPermitted);
        if (!meta.Revisions) return null;
        if (!await IsItemVisibleForRevisionsAsync(collection, id, ct)) return null;
        var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
        if (rec is null) return null;
        return rec with { Snapshot = RevisionSnapshotRedactor.RedactHidden(rec.Snapshot, meta, m2mSource.M2MDescriptors(collection)) };
    }

    // U5b: post-commit change notification. CancellationToken.None on purpose — the write already
    // committed, so a disconnecting caller must not leave a listener (an index) behind. The
    // never-throw guarantee (a listener failure must not fail the write) lives in the notifier
    // implementation itself (Struo.Infrastructure.Changes.ItemChangeNotifier) — a fork replacing
    // IItemChangeNotifier must preserve it, or a bad listener would turn a successful write into an
    // error response.
    private Task NotifyAsync(ItemChangeSet changes) =>
        notifier is null || changes.Count == 0 ? Task.CompletedTask : notifier.NotifyAsync(changes.ToList(), CancellationToken.None);

    // Interceptor AfterCommit hooks run first; a throw skips the rest, the change notification still
    // goes out (the write is committed), then the original exception surfaces.
    private async Task AfterCommitAndNotifyAsync(ItemWriteHooks hooks, ItemChangeSet? changes)
    {
        ExceptionDispatchInfo? failure = null;
        try
        {
            await hooks.AfterCommitAsync();
        }
        catch (Exception ex)
        {
            failure = ExceptionDispatchInfo.Capture(ex);
        }
        if (changes is not null) await NotifyAsync(changes);
        failure?.Throw();
    }

    private static ItemChangeSet SingleChange(string collection, object id, ItemChangeKind kind)
    {
        var set = new ItemChangeSet();
        set.Add(collection, id.ToString()!, kind);
        return set;
    }

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);

    // Writes to an AdminOnly collection (user/role/permission/userRole) require a super-admin even with
    // a per-collection write/delete grant — else a delegated grant on userRole could self-assign the
    // super-admin role (privilege escalation). Reads stay ungated (ordinary RBAC governs them).
    private void RequireSuperAdminForAdminOnly(CollectionMetadata meta)
    {
        if (meta.AdminOnly && !permissions.IsSuperAdmin)
            throw new PermissionDeniedException($"Writes to '{meta.Name}' require a super-admin.");
    }
}
