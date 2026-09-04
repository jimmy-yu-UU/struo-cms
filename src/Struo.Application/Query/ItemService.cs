// src/Struo.Application/Query/ItemService.cs
using System.Text.Json;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Revisions;
using Struo.Application.Security;
using Struo.Domain.Auditing;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record PagedResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Data, int Total, int Limit, int Offset);

public sealed class ItemService(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IPermissionService permissions,
    IRelationshipGraph graph,
    IRelationExpander expander,
    IM2MDescriptorSource m2mSource,
    IRelationFilterResolver relationFilter,
    ILanguageProvider languages,
    StruoQueryOptions options,
    IHtmlSanitizer sanitizer,
    ICurrentUserAccessor currentUser,
    IRevisionStore revisions,
    RevisionSnapshotBuilder snapshotBuilder,
    IUserSessionRevocationService sessionRevocation) : IItemUseCases
{
    private readonly ItemDeserializer deserializer = new(registry, m2mSource, new(sanitizer));
    // ItemWriteSideSync gets its own ItemDeserializer instance — a field initializer cannot reference
    // the sibling `deserializer` field above (CS0236).
    private readonly ItemWriteSideSync writeSync =
        new(repository, m2mSource, languages, new(sanitizer), new(registry, m2mSource, new(sanitizer)), metadata, permissions);
    private readonly ItemPurgePipeline purge = new(repository, metadata, registry, graph, m2mSource, revisions);
    private readonly SelfReferenceCycleGuard cycleGuard = new(repository, registry);
    private readonly ItemProjector projector = new(registry, permissions, metadata);
    private readonly TranslationOverlay overlay =
        new(repository, registry, new(registry, permissions, metadata), permissions);
    private readonly DeepExpansionCoordinator deepExpansion =
        new(options, graph, metadata, registry, expander, new(registry, permissions, metadata), permissions);

    public async Task<PagedResult> QueryAsync(
        string collection, QueryModel raw, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        ValidateLocale(locale);

        // Compute the effective query locale: explicit locale ?? collection default ?? global default.
        // Only meaningful when the collection has a translation sidecar.
        var queryLocale = meta.Translation is not null
            ? (locale ?? languages.DefaultCode())
            : null;

        var validated = QueryValidator.Validate(raw, meta, options, graph, metadata, permissions);
        validated = validated with
        {
            Filter = await relationFilter.RewriteAsync(collection, validated.Filter, queryLocale, ct)
        };
        var searchable = QueryValidator.SearchableFields(meta);
        var result = await repository.QueryAsync(collection, validated, searchable, queryLocale, deleted, ct);

        var entities = result.Rows;
        var rows = entities.Select(r => projector.Project(r, meta, validated.Fields)).ToList();
        await deepExpansion.ExpandAsync(collection, raw.Deep, entities, rows, queryLocale, ct);
        await overlay.ApplyAsync(meta, entities, rows, locale, ct);
        return new PagedResult(rows, result.Total, validated.Limit, validated.Offset);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetAsync(
        string collection, string id, DeepSpec? deep = null, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        ValidateLocale(locale);
        var entity = await repository.GetByIdAsync(collection, id, deleted, ct);
        if (entity is null) return null;

        var projected = (Dictionary<string, object?>)projector.Project(entity, meta, null);
        var queryLocale = meta.Translation is not null ? (locale ?? languages.DefaultCode()) : null;
        await deepExpansion.ExpandAsync(collection, deep, [entity], [projected], queryLocale, ct);
        await overlay.ApplyAsync(meta, [entity], [projected], locale, ct);
        return projected;
    }

    private void ValidateLocale(string? locale)
    {
        if (locale is null) return;
        // Charset guard: reject ill-formed locale codes before the IsEnabled membership check
        // so a crafted code containing SQL metacharacters (e.g. a single quote) is stopped here
        // rather than reaching the sort-subquery literal in SqlSugarItemRepository.
        if (!LocaleFormat.IsValid(locale))
            throw new QueryException(
                $"Locale '{locale}' contains invalid characters. Codes must match [A-Za-z0-9_-]{{1,35}}.");
        if (!languages.IsEnabled(locale))
            throw new QueryException($"Unknown or disabled locale '{locale}'.");
    }

    public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        // File rows are owned by the upload pipeline: StorageKey/Size/ContentType/dimensions are all
        // derived from the blob, and FileName is ReadOnly so the generic write path cannot even set it
        // (it used to null it into a NOT NULL column and 500). Reject explicitly here rather than at
        // the controller, so REST and the GraphQL createFile mutation are both covered. Reads, updates
        // (the admin media UI edits per-locale Title/Alt this way) and deletes are unaffected.
        if (string.Equals(collection, FileCollection.Name, StringComparison.OrdinalIgnoreCase))
            throw new QueryException(
                "Files cannot be created through the generic items API. Upload one with POST /api/files instead.");
        // A non-object top-level body (array/scalar) reaches ValidateLanguageCodeIfNeeded /
        // Deserialize below, both of which assume an object and throw an unhandled
        // InvalidOperationException (-> 500) otherwise. Reject it here as the established
        // client-error (-> 400) path instead.
        if (body.ValueKind != JsonValueKind.Object)
            throw new QueryException("Request body must be a JSON object.");
        ValidateLanguageCodeIfNeeded(collection, body);
        var entity = deserializer.Deserialize(collection, body, meta);
        var d = registry.Get(collection)!;

        // Parent row + M2M junctions + translation sidecars must commit together or not at all —
        // otherwise a failure after the parent insert leaves a row that violates invariants the API
        // enforces (e.g. "default-locale translation required").
        object created = null!;
        await repository.InTransactionAsync(async () =>
        {
            created = await repository.CreateAsync(collection, entity, ct);
            var createdId = d.Properties.GetValueOrDefault(d.IdProperty)!.GetValue(created)!;
            await writeSync.SyncM2MAsync(collection, body, createdId, includeDeleted: false, ct);
            await writeSync.SyncTranslationsAsync(meta, body, createdId, isCreate: true, ct);
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, created, ct);
                await revisions.CaptureAsync(collection, createdId.ToString()!, "create", snapshot, ct: ct);
            }
        }, ct);
        InvalidateLanguagesIfNeeded(collection);
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
        // permission checks and before ValidateLanguageCodeIfNeeded/Deserialize.
        if (body.ValueKind != JsonValueKind.Object)
            throw new QueryException("Request body must be a JSON object.");
        ValidateLanguageCodeIfNeeded(collection, body);
        var d = registry.Get(collection)!;

        // Merge update: start from the existing row and overlay ONLY the fields the client actually
        // sent (writable, non-readonly, non-system). This preserves server-managed columns the client
        // never sends — e.g. a File's StorageKey/FileName/Size — so a partial PUT (status + translations
        // only) cannot wipe NOT NULL metadata. Relations/translations are synced separately below.
        var existing = await repository.GetByIdAsync(collection, id, ct: ct);
        if (existing is null) return null;
        var incoming = deserializer.Deserialize(collection, body, meta);
        var bodyKeys = body.ValueKind == JsonValueKind.Object
            ? body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in meta.Fields)
        {
            if (field.IsSystem || field.ReadOnly) continue;
            if (!bodyKeys.Contains(field.Name)) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.Properties.GetValueOrDefault(prop);
            if (pi is not { CanWrite: true }) continue;
            pi.SetValue(existing, pi.GetValue(incoming));
        }

        // Relation foreign keys (M2O / self-referencing tree) are declared via [CmsRelation], not
        // [CmsField], so the field overlay above skips them. Overlay each local FK the client
        // actually sent, keeping the same allowlist discipline (only declared relations, only keys
        // present in the body). Restricted to ManyToOne: O2M relations also carry a non-null
        // ForeignKey (it names the FK column on the *related* entity, not this one), so without
        // this guard the loop would try to overlay a property that doesn't exist/isn't writable
        // here; M2M has no local FK at all.
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

        // Self-referencing tree collections: reject a parentId that points at the item itself or
        // one of its descendants (cycle). Runs after the FK overlay so it sees the incoming value.
        await cycleGuard.EnsureNoCycleAsync(collection, meta, existing, ct);

        // Optimistic concurrency: guard the write on the version the client last read. When the
        // client echoes `version`, the repository's compare-and-swap rejects the update (409) if another
        // writer already moved the row on. Absent a client version we fall back to the freshly-loaded
        // value (no protection, but backward compatible for callers that don't track versions).
        if (existing is Struo.Domain.Auditing.AuditableEntity ex)
            ex.Version = JsonBodyUtil.TryReadVersion(body) ?? ex.Version;

        // Parent row + M2M + translations commit atomically (see CreateAsync).
        object? updated = null;
        await repository.InTransactionAsync(async () =>
        {
            updated = await repository.UpdateAsync(collection, id, existing, ct);
            if (updated is null) return;
            var updatedId = d.Properties.GetValueOrDefault(d.IdProperty)!.GetValue(updated)!;
            // A revert re-applies a past snapshot, which may reference an M2M target trashed since
            // capture — tolerate it (operation == "revert"); every other write path stays strict.
            await writeSync.SyncM2MAsync(collection, body, updatedId, includeDeleted: operation == "revert", ct);
            await writeSync.SyncTranslationsAsync(meta, body, updatedId, isCreate: false, ct);
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, updated!, ct);
                await revisions.CaptureAsync(collection, updatedId.ToString()!, operation, snapshot,
                    sourceRevisionNumber, ct: ct);
            }
        }, ct);
        if (updated is null) return null;
        InvalidateLanguagesIfNeeded(collection);
        return projector.Project(updated, meta, null);
    }

    private void InvalidateLanguagesIfNeeded(string collection)
    {
        if (string.Equals(collection, "language", StringComparison.OrdinalIgnoreCase))
            languages.Invalidate();
    }

    /// <summary>
    /// When writing to the <c>language</c> collection, validates that the <c>code</c> field in
    /// <paramref name="body"/> matches the strict locale-format whitelist so that a crafted code
    /// value cannot later be used to inject SQL via the translatable sort subquery.
    /// </summary>
    private static void ValidateLanguageCodeIfNeeded(string collection, JsonElement body)
    {
        if (!string.Equals(collection, "language", StringComparison.OrdinalIgnoreCase)) return;
        if (!body.TryGetProperty("code", out var codeElem)) return;
        var code = codeElem.ValueKind == JsonValueKind.String ? codeElem.GetString() : null;
        if (code is null) return; // missing/null code handled by required-field validation
        if (!LocaleFormat.IsValid(code))
            throw new QueryException(
                $"Language code '{code}' contains invalid characters. Codes must match [A-Za-z0-9_-]{{1,35}}.");
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

            // Restrict still guards the soft-delete (trash) branch — unchanged contract. The purge
            // branch below re-runs the identical check as the first step of PurgeCoreAsync, so every
            // recursively-cascaded row is ALSO Restrict-guarded, not just the top-level target.
            // The check runs INSIDE the same transaction as the trash write (mirroring the
            // purge branch's boundary below and RestoreAsync's boundary), narrowing the check-to-write
            // window to purge-parity. Under PG READ COMMITTED a plain SELECT takes no row lock, so a
            // referencing row committed by another txn between the check and the commit is still possible
            // — full closure would need FK/locking (accepted app-only stance), so this narrows rather than
            // eliminates the race. Note this now runs even when the row turns out to already be trashed
            // (affected = 0 below) — harmless (a pure read-only guard), and simpler/more consistent than
            // the previous pre-read short-circuit that skipped it entirely for a repeat DELETE.
            var actor = currentUser.GetCurrentUserId();

            // Trash bumps the item's Version (repository, AuditableEntity only) AND — for a
            // revisioned collection — records a "delete" revision. Both must commit together with the
            // trash stamp, so a capture failure rolls the whole trash back (no half-trashed row, no
            // orphan revision). Revision capture is gated on the atomic UPDATE actually having
            // affected the row, so a losing concurrent DELETE (or a repeat DELETE of an already-trashed
            // row) records nothing.
            await repository.InTransactionAsync(async () =>
            {
                await this.purge.CheckRestrictAsync(collection, id, ct);
                var softDeletedNow = await repository.SoftDeleteAsync(collection, id, DateTime.UtcNow, actor, ct);
                if (softDeletedNow)
                    await CaptureRevisionAsync(collection, id, meta, "delete", ct);
            }, ct);
            await RevokeSessionsIfUserAsync(collection, id);
            return true; // idempotent success: the row exists, whether newly trashed here or already trashed
        }

        var existed = await repository.InTransactionAsync(
            () => this.purge.PurgeCoreAsync(collection, id, new HashSet<(string Collection, string Id)>(), ct), ct);
        if (existed) await RevokeSessionsIfUserAsync(collection, id);
        return existed;
    }

    /// <summary>
    /// Deleting a `user` row (soft-delete or purge) must clear that user's <c>user_sessions</c> rows
    /// proactively: the per-request cookie-liveness check only catches a deleted user's session
    /// reactively, on its next use, so a session that is never presented again would otherwise leave
    /// its row (and cache entry) around until that user's next login — which, for a deleted user,
    /// never happens. Placed here (both DELETE branches share it), not in the REST controller, so REST
    /// and the GraphQL <c>deleteUser</c> mutation — which calls straight into this method — are both
    /// covered. Fires only after the delete has actually taken effect; the delete stays committed
    /// regardless of what happens next. CancellationToken.None: the delete already committed, so a
    /// disconnecting caller must not also skip revoking the now-deleted user's sessions. A revocation
    /// failure surfaces as a distinct, client-safe error rather than an indistinguishable success.
    /// </summary>
    private async Task RevokeSessionsIfUserAsync(string collection, string id)
    {
        if (!string.Equals(collection, UserCollection.Name, StringComparison.OrdinalIgnoreCase)) return;
        if (!Guid.TryParse(id, out var userId)) return;

        try
        {
            await sessionRevocation.RevokeAllForUserAsync(userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new SessionRevocationFailedException(
                "The user was deleted, but revoking their existing sessions failed. " +
                "Some sessions may still be active.", ex);
        }
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

        // The restore clears DeletedAt, bumps Version and — for a revisioned collection — records
        // a "restore" revision, all in ONE txn (capture rolls back with it).
        // Whether a row is ACTUALLY restored is decided by repository.RestoreAsync's atomic
        // "WHERE deletedat IS NOT NULL" UPDATE (affected-rows > 0), not by inspecting this pre-read
        // snapshot — a pre-read check here would leave a TOCTOU window where two concurrent restores of
        // the same row could each pass the check and double-record a "restore" revision. An already-live
        // row (or one restored by a concurrent request first) is a no-op: no Version bump, no revision.
        if (entity is ISoftDeletable)
        {
            await repository.InTransactionAsync(async () =>
            {
                var restoredNow = await repository.RestoreAsync(collection, id, ct);
                if (restoredNow)
                    await CaptureRevisionAsync(collection, id, meta, "restore", ct);
            }, ct);
        }

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
        string collection, string id, long revisionNumber, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        if (!meta.Revisions) return null;                                   // collection keeps no revisions -> 404

        var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
        if (rec is null) return null;                                       // unknown revision/item -> 404

        // The snapshot IS a valid update body by construction; drop `version` so revert does not echo a
        // stale optimistic-concurrency token (it would 409 against the current row). Keep the JsonDocument
        // alive across the awaited update (the body's JsonElement must stay valid).
        using var src = JsonDocument.Parse(rec.Snapshot);
        using var doc = JsonDocument.Parse(
            JsonBodyUtil.StripKeys(src.RootElement, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "version" }));
        return await UpdateCoreAsync(collection, id, doc.RootElement, "revert", revisionNumber, ct: ct);
    }

    /// <summary>Newest-first revision metadata for an item. Requires read permission. Empty for a
    /// non-revisioned collection.</summary>
    public async Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>> ListRevisionsAsync(
        string collection, string id, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        if (!meta.Revisions) return [];
        return await revisions.ListAsync(collection, id, ct);
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
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        if (!meta.Revisions) return null;
        var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
        if (rec is null) return null;
        return rec with { Snapshot = RevisionSnapshotRedactor.RedactHidden(rec.Snapshot, meta, m2mSource.M2MDescriptors(collection)) };
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
