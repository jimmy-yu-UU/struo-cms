# File Soft-Delete + Retire `archived` Status — Design (#12)

- **Date:** 2026-07-27
- **Issue:** Admin-UX backlog #12 (the last remaining item)
- **Scope:** Backend (core) + minimal media-library trash UI + tests
- **Status:** Approved for spec → implementation plan

## 0. Problem

Backlog #12 raised that a collection's "使用中 / 回收桶" (soft-delete) and a
`status = archived` value are **semantically redundant and conflicting**: both express
"this asset is no longer in active public use, but not gone." Having both forces the user to
reason about two overlapping axes.

`File` (the media library's backing collection, core: `src/Struo.Infrastructure/Files/File.cs`)
is the concrete case:

- It has a `Status` field with options `draft / published / archived`.
- It is **not** soft-deletable — its destructive lifecycle runs through a dedicated pipeline
  (`FileService` / `FilesController`), where `DELETE /api/files/{id}` is a **hard delete**
  (removes the row + sidecar `FileTranslation` rows + clears any `site_settings.logofileid`
  reference + best-effort deletes the storage blob).

## 1. Decision: eliminate the redundancy, don't document around it

Rather than keeping `archived` and defining it as orthogonal to trash, we **remove `archived`**.
The recycle-bin (soft-delete) mechanism fully covers "retired but recoverable." The final,
mutually-exclusive state model for `File` is:

| State | Axis | Meaning |
|-------|------|---------|
| `Status = draft` | publish / visibility | not public; to "unpublish" a file, set it back to `draft` |
| `Status = published` | publish / visibility | publicly servable (anonymous `/content`) |
| **trashed** (`DeletedAt` set) | existence | removed from the library, `/content` 404 for everyone, restorable; purge = permanent |

`archived` disappears. There is exactly one "make this not-public" state (`draft`) and one
"remove this" state (trash). This is the formal answer to #12.

## 2. Architecture: the dedicated pipeline owns the destructive lifecycle

`File`'s trash / restore / purge run through **`FileService` / `FilesController`**, *not* the
generic `ItemService`. Rationale: the core `ItemService` deliberately knows nothing about the
physical storage blob or `site_settings`; `File` alone has a blob and a logo reference to manage.
Keeping that knowledge in `FileService` preserves the §2 dependency rule and the existing
separation.

The generic `itemsApi` continues to serve **list / get / update** for the media library
(`/api/items/file`). Those paths need no File-specific handling. Only **delete / restore / purge**
go through the dedicated pipeline.

### Reused framework primitives

Making `File` implement `ISoftDeletable` lights up existing framework behavior for free:

- The global SqlSugar query filter (`SqlSugarClientFactory`) auto-excludes rows with
  `DeletedAt != null` from **every** `Queryable<File>`. Because `FileService.GetAsync` is
  `db.Queryable<File>().In(id).FirstAsync(ct)`, a trashed file's `Get` and `Download` endpoints
  return **404 automatically** — no controller change needed.
- The repository already exposes atomic, TOCTOU-safe helpers used by `ItemService`:
  - `Task<bool> SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? actor, CancellationToken)`
  - `Task<bool> RestoreAsync(string collection, string id, CancellationToken)`
  `FileService` reuses these (`collection = "file"`) instead of reimplementing the
  `WHERE deletedat IS NULL` logic.
- The generic read filter already supports `?deleted=only|with|exclude`, so the media library
  trash view is a pure frontend read change.

## 3. Backend changes

### 3.1 Entity (`File.cs`)

- `public sealed class File : AuditableEntity, ISoftDeletable` — adds `DateTime? DeletedAt` and
  `Guid? DeletedBy` (package-free, per `ISoftDeletable`).
- `CmsOptions` on `Status`: `"draft:Draft", "published:Published"` — **remove** `"archived:Archived"`.
- No revisions (`File` is not `[CmsCollection(Revisions = true)]`) → no revision capture on
  trash/restore.

### 3.2 `FileService` — split the current `DeleteAsync`

Replace the single hard-delete with three operations. All share the existing nesting-safe
`repository.InTransactionAsync` boundary; the storage blob operation stays **outside** the
transaction (best-effort, as today).

- **`Task<bool> TrashAsync(Guid id, CancellationToken)`** (new default delete):
  - Reuse `repository.SoftDeleteAsync("file", id, DateTime.UtcNow, actor, ct)` (atomic
    `WHERE deletedat IS NULL`; returns false if not found / already trashed).
  - On success, clear `site_settings.logofileid` if this file was the logo (same entity-typed
    `SetColumns` NULL as today — a trashed file is filtered out and would otherwise leave a
    dangling/broken logo reference).
  - **Retain** the blob and the sidecar `FileTranslation` rows (restore needs them).
- **`Task<bool> PurgeAsync(Guid id, CancellationToken)`** (the current hard-delete behavior):
  - Must first `ClearFilter<ISoftDeletable>()` so an already-trashed row is visible to purge.
  - In-txn: delete `FileTranslation` rows → delete `File` row → clear `site_settings.logofileid`.
  - Out-of-txn: best-effort `storage.DeleteAsync(row.StorageKey)`.
- **`Task<bool> RestoreAsync(Guid id, CancellationToken)`**:
  - Reuse `repository.RestoreAsync("file", id, ct)` (atomic clear of `DeletedAt`/`DeletedBy`;
    no-op if already live). Restored file keeps its `Status` (which, after §3.3 migration, is
    `draft` for anything converted from `archived`).

The current `GetAsync` signature is unchanged.

### 3.3 `FilesController`

- `DELETE /api/files/{id}` — default **trash** (`TrashAsync`). Query flag `?purge=true` →
  `PurgeAsync`. Both gated on `access.CanDelete()`. Returns `204` on success, `404` if the row
  didn't exist / wasn't in the expected state.
- `POST /api/files/{id}/restore` — `RestoreAsync`, gated on `access.CanDelete()` (same authority
  as delete, consistent with the generic `/items/{collection}/{id}/restore` convention). `204` /
  `404`.

**Known deferred edge (out of scope):** the generic `DELETE /api/items/file/{id}?purge` would
purge the row + sidecar via metadata but would **not** delete the blob or clear the logo ref
(orphan blob). The frontend never uses that path for files. Recorded as a deferred minor; not in
this batch.

### 3.4 Migration — `db/migrations/003-file-soft-delete.sql` (core)

Idempotent, PostgreSQL (runtime target). Dev `InitTables` adds the columns from the entity
automatically; this migration is for the production bootstrap path.

1. `ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedat timestamptz NULL;`
2. `ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedby uuid NULL;`
3. **Retire existing `archived` rows** — treat them as trashed (faithful to "archived is replaced
   by trash"), reversible via restore, non-destructive:
   ```sql
   UPDATE files
      SET deletedat = COALESCE(deletedat, now()),
          status    = 'draft'
    WHERE status = 'archived';
   ```
   (A restored file comes back as `draft`, i.e. not auto-republished.)

The `001-core-baseline.sql` bootstrap and dev `InitTables` are unaffected beyond the additive
columns.

## 4. Frontend changes (minimal trash UI)

- **`filesApi`**: `remove(id, opts?: { purge?: boolean })` (append `?purge=true`) and
  `restore(id)` (`POST /files/{id}/restore`).
- **`MediaDetailDialog.vue`**: delete uses the **soft-delete** confirm copy
  (`confirm.softDeleteMessage`, already in i18n) instead of the hard-delete copy. When the dialog
  is opened on a trashed file (trash view), show **Restore** and **Delete permanently** (purge)
  actions.
- **`MediaLibraryView.vue`**: add a "使用中 / 回收桶" `SelectButton` (mirroring
  `CollectionListView` 9b-fe), gated on `canDelete`. The trash view lists via
  `itemsApi.list('file', { deleted: 'only', ... })` (generic read already supports it — no backend
  change). Restore / purge actions call the new `filesApi` methods and refresh.
- Remove the `archived` branch in `CollectionListView.vue:77` (status tag color) and any residual
  `archived` option in media/status components.

## 5. Testing

**Backend (SQLite unit/integration + live PG+MinIO gate):**
- `TrashAsync` retains blob + `FileTranslation` rows; stamps `DeletedAt`/`DeletedBy`.
- `PurgeAsync` deletes blob + translations + row; works on an already-trashed row.
- Both `TrashAsync` and `PurgeAsync` clear `site_settings.logofileid` when the file is the logo.
- `GetAsync` / `Download` return 404 for a trashed file (query-filter exclusion).
- `RestoreAsync` restores; concurrent/duplicate trash & restore are no-ops (bool false).
- Migration: a seeded `status='archived'` row becomes trashed + `draft` and is idempotent on
  re-run.

**Frontend (vitest):**
- `filesApi.remove(purge)` / `restore` hit the right URLs.
- `MediaLibraryView` active/trash toggle switches the `deleted` param and renders restore/purge
  actions.
- `MediaDetailDialog` uses soft-delete confirm; trash-view actions call restore/purge.

**Live e2e (Playwright, PG+MinIO, per the established env):**
Trash a file → disappears from library + `/content` 404 → open 回收桶 → restore → reappears →
trash again → purge → gone + blob actually deleted from storage.

## 6. Out of scope (YAGNI)

- Blob cleanup on the generic `items` purge path (deferred minor above).
- Scheduled/automatic trash emptying (retention policy).
- Bulk trash/restore/purge operations.
- Any change to sample collections beyond what the `archived` removal touches in shared FE helpers.

## 7. Semantic summary (formal #12 answer, for docs/backlog)

> The media library no longer has an `archived` status. A file is either **draft** (not public),
> **published** (public), or **in the recycle bin** (removed, restorable, `/content` unavailable;
> permanently deleted on purge). "Archive" and "delete-to-recycle-bin" were the same intent, so
> they are now one mechanism — the recycle bin.
