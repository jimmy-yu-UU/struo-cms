# File Soft-Delete + Retire `archived` Status — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the media library's `File` collection soft-deletable (trash / restore / purge) through its dedicated pipeline, and remove the now-redundant `archived` status value.

**Architecture:** `File` implements `ISoftDeletable`, lighting up the framework's global query filter for free. Destructive lifecycle (trash/restore/purge) stays in the dedicated `FileService`/`FilesController` (which alone manages the physical blob + `site_settings` logo reference); the generic `itemsApi` continues to serve list/get/update. `DeletedAt` retires `archived`: the only "not-public" state becomes `draft`, the only "removed" state becomes trash.

**Tech Stack:** .NET 10 / C# · SqlSugarCore · PostgreSQL (runtime) + SQLite (tests) · xUnit + AwesomeAssertions · Vue 3 + PrimeVue + vue-i18n · vitest · Playwright (live e2e).

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL except idempotent forward-only files in `db/migrations/`.
- `File` is **core** (`src/Struo.Infrastructure/Files/`); its schema change is a core migration.
- Package-free `ISoftDeletable` (no SqlSugar attributes on the interface members).
- Outbound JSON camelCase. REST envelope: success `204 No Content` for delete/restore; failure via `ApiResults.Fail`.
- Reuse the existing atomic repository primitives — do NOT reimplement `WHERE deletedat IS NULL` logic:
  - `Task<bool> IItemRepository.SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken)`
  - `Task<bool> IItemRepository.RestoreAsync(string collection, string id, CancellationToken)`
- `File`'s collection route/name is `"file"`.
- Commit after each task. Conventional-commit messages, no attribution trailer.
- Verify on live PostgreSQL before declaring DB work done (SQLite green ≠ PG correct).

---

### Task 1: `File` implements `ISoftDeletable` and drops `archived`

**Files:**
- Modify: `src/Struo.Infrastructure/Files/File.cs`
- Test: `tests/Struo.Tests/Files/FileCollectionTests.cs` (add facts)

**Interfaces:**
- Consumes: `Struo.Domain.Auditing.ISoftDeletable` (`DateTime? DeletedAt`, `Guid? DeletedBy`).
- Produces: `File : AuditableEntity, ISoftDeletable`; metadata `SoftDelete == true` for collection `"file"`; `Status` `CmsOptions` = `draft`, `published` only.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Struo.Tests/Files/FileCollectionTests.cs`:

```csharp
[Fact]
public void File_is_soft_deletable()
{
    typeof(Struo.Domain.Auditing.ISoftDeletable)
        .IsAssignableFrom(typeof(Struo.Infrastructure.Files.File))
        .Should().BeTrue();
}

[Fact]
public void File_status_options_no_longer_include_archived()
{
    var meta = MetadataScanner.ScanTypes([typeof(Struo.Infrastructure.Files.File)])
        .Single(c => string.Equals(c.Route, "file", StringComparison.OrdinalIgnoreCase));
    var status = meta.Fields.Single(f => f.Name == "status");
    status.Options.Select(o => o.Value).Should().BeEquivalentTo(["draft", "published"]);
    meta.SoftDelete.Should().BeTrue();
}
```

(If `FileCollectionTests` lacks the `using`s/harness for `MetadataScanner`, mirror the scan usage already present in `FileServiceLogoLifecycleTests.cs`. Adjust `Options`/`Route`/`Name` property accessors to match `CollectionMetadata`/`FieldMetadata` — inspect `src/Struo.Domain/Metadata/Models/`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~FileCollectionTests"`
Expected: FAIL — `File` is not `ISoftDeletable`; `archived` still present.

- [ ] **Step 3: Modify `File.cs`**

Change the class declaration and the `Status` options:

```csharp
public sealed class File : AuditableEntity, ISoftDeletable
```

```csharp
    [CmsField(Label = "Status", Interface = FieldInterface.Select, Sort = 6)]
    [CmsOptions("draft:Draft", "published:Published")]
    public string Status { get; set; } = "draft";
```

Add the two interface members (near the other nullable columns; SqlSugar maps them as nullable):

```csharp
    [SugarColumn(IsNullable = true)] public DateTime? DeletedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? DeletedBy { get; set; }
```

`using Struo.Domain.Auditing;` is already imported.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~FileCollectionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Files/File.cs tests/Struo.Tests/Files/FileCollectionTests.cs
git commit -m "feat(file): implement ISoftDeletable and drop archived status (#12)"
```

---

### Task 2: `FileService` — `TrashAsync` + `RestoreAsync` (keep `DeleteAsync` as purge)

**Files:**
- Modify: `src/Struo.Infrastructure/Files/FileService.cs`
- Test: `tests/Struo.Tests/Files/FileServiceSoftDeleteTests.cs` (create)

**Interfaces:**
- Consumes: `IItemRepository.SoftDeleteAsync` / `RestoreAsync` (signatures in Global Constraints); existing `FileService.DeleteAsync(Guid, CancellationToken)` (hard purge: translations + row + logo-ref clear + best-effort blob delete).
- Produces:
  - `Task<bool> FileService.TrashAsync(Guid id, CancellationToken ct = default)` — stamps `DeletedAt`/`DeletedBy` via `repository.SoftDeleteAsync("file", id, DateTime.UtcNow, actor, ct)`; also clears `site_settings.logofileid` when the file is the logo; retains blob + `FileTranslation` rows. Returns `SoftDeleteAsync`'s bool.
  - `Task<bool> FileService.RestoreAsync(Guid id, CancellationToken ct = default)` — `repository.RestoreAsync("file", id, ct)`.
  - `DeleteAsync` is unchanged and is the **purge** operation.

**Note:** `DeleteAsync` serves the spec's `PurgeAsync` role. Kept under its existing name to avoid churning `FileServiceLogoLifecycleTests` / `FileServiceTransactionTests` / `FileServiceCancellationTests`, which already assert its hard-delete behavior.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Files/FileServiceSoftDeleteTests.cs`. Reuse the harness from `FileServiceLogoLifecycleTests.cs` (copy the `NoopStorage`/`NoopImages`/`StubLanguages` nested classes, ctor DB wiring, `InsertFileAsync`, `InsertSiteSettingsAsync`), then add a `CountingStorage` to observe blob deletes:

```csharp
private sealed class CountingStorage : IFileStorage
{
    public int Deletes { get; private set; }
    public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(string key, CancellationToken ct = default) { Deletes++; return Task.CompletedTask; }
    public bool SupportsPresignedUrls => false;
    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
}
```

Wire `_svc` with the `CountingStorage` instance (`_storage`) so tests can assert `Deletes`. Facts:

```csharp
[Fact]
public async Task Trash_stamps_deletedat_and_retains_row_blob_and_translations()
{
    var id = await InsertFileAsync();
    await _db.Insertable(new FileTranslation { Id = Guid.NewGuid(), FileId = id, Locale = "en", Title = "t" }).ExecuteCommandAsync();

    var ok = await _svc.TrashAsync(id);

    ok.Should().BeTrue();
    _storage.Deletes.Should().Be(0);                                   // blob retained
    var row = await _db.Queryable<File>().ClearFilter<ISoftDeletable>().In(id).FirstAsync();
    row!.DeletedAt.Should().NotBeNull();
    row.DeletedBy.Should().Be(Tester);
    (await _db.Queryable<FileTranslation>().Where(t => t.FileId == id).CountAsync()).Should().Be(1); // translations retained
}

[Fact]
public async Task Trashed_file_is_excluded_from_default_reads()
{
    var id = await InsertFileAsync();
    await _svc.TrashAsync(id);
    (await _svc.GetAsync(id)).Should().BeNull();                       // query filter hides it → 404 upstream
}

[Fact]
public async Task Trash_of_current_logo_clears_logofileid()
{
    var id = await InsertFileAsync();
    await InsertSiteSettingsAsync(id);
    await _svc.TrashAsync(id);
    var s = await _db.Queryable<SiteSettings>().In(SiteSettings.SingletonId).FirstAsync();
    s!.LogoFileId.Should().BeNull();
}

[Fact]
public async Task Restore_clears_deletedat_and_makes_file_readable_again()
{
    var id = await InsertFileAsync();
    await _svc.TrashAsync(id);

    var restored = await _svc.RestoreAsync(id);

    restored.Should().BeTrue();
    (await _svc.GetAsync(id)).Should().NotBeNull();
}

[Fact]
public async Task Restore_of_live_file_is_noop_false()
{
    var id = await InsertFileAsync();
    (await _svc.RestoreAsync(id)).Should().BeFalse();
}

[Fact]
public async Task Purge_of_trashed_file_deletes_row_and_blob()
{
    var id = await InsertFileAsync();
    await _svc.TrashAsync(id);

    var purged = await _svc.DeleteAsync(id);                           // DeleteAsync == purge

    purged.Should().BeTrue();
    _storage.Deletes.Should().Be(1);                                  // blob deleted on purge
    (await _db.Queryable<File>().ClearFilter<ISoftDeletable>().In(id).AnyAsync()).Should().BeFalse();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~FileServiceSoftDeleteTests"`
Expected: FAIL — `TrashAsync`/`RestoreAsync` do not exist.

- [ ] **Step 3: Add the two methods to `FileService`**

Insert alongside `DeleteAsync`. The current-user id is obtained the same way `SoftDeleteAsync`'s `actor` is threaded — read the actor via the `ICurrentUserAccessor` the service/repository already has, or pass through the DB's ambient user. If `FileService` has no direct accessor, add an `ICurrentUserAccessor currentUser` constructor parameter (already registered in DI) and use `currentUser.UserId`. The logo-clear step mirrors the existing `DeleteAsync` body.

```csharp
public async Task<bool> TrashAsync(Guid id, CancellationToken ct = default)
{
    bool trashed = false;
    await repository.InTransactionAsync(async () =>
    {
        trashed = await repository.SoftDeleteAsync("file", id.ToString(), DateTime.UtcNow, currentUser.UserId, ct);
        if (!trashed) return;
        // A trashed file is filtered out of every read; if it is the current brand logo, clear the
        // reference now so ConfigController stops resolving it into a dead /content URL (mirrors DeleteAsync).
        await db.Updateable<SiteSettings>()
            .SetColumns(s => new SiteSettings { LogoFileId = null })
            .Where(s => s.LogoFileId == id)
            .ExecuteCommandAsync(ct);
    }, ct);
    return trashed;   // blob + FileTranslation rows retained for restore
}

public Task<bool> RestoreAsync(Guid id, CancellationToken ct = default) =>
    repository.RestoreAsync("file", id.ToString(), ct);
```

If a `currentUser` parameter was added, update `FileService`'s constructor and every construction site (DI registration in `src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`, plus the test harnesses in `FileServiceLogoLifecycleTests`/`FileServiceSoftDeleteTests` — pass `new TestCurrentUserAccessor(Tester)`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~FileServiceSoftDeleteTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Files/FileService.cs src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs tests/Struo.Tests/Files/
git commit -m "feat(file): FileService trash + restore (purge stays as DeleteAsync) (#12)"
```

---

### Task 3: `FilesController` — default trash, `?purge`, restore endpoint

**Files:**
- Modify: `src/Struo.Api/Controllers/FilesController.cs`
- Test: `tests/Struo.Tests/Files/FileRbacTests.cs` or the WAF harness in `tests/Struo.Tests/Api/SoftDeleteEndpointTests.cs` (add File-specific facts mirroring that harness)

**Interfaces:**
- Consumes: `FileService.TrashAsync`, `FileService.DeleteAsync` (purge), `FileService.RestoreAsync`; `IFileAccessPolicy.CanDelete()`.
- Produces: `DELETE /api/files/{id}` (default trash; `?purge=true` → purge); `POST /api/files/{id}/restore` (restore). Both gated `CanDelete`; `204`/`404`.

- [ ] **Step 1: Write the failing tests**

Mirror the harness already used by `tests/Struo.Tests/Api/SoftDeleteEndpointTests.cs` (WebApplicationFactory + authenticated write user). Add facts:

```
- DELETE /api/files/{id}         → 204, and GET /api/files/{id} then returns 404 (trashed, filtered)
- POST  /api/files/{id}/restore  → 204, and GET /api/files/{id} then returns 200 (live again)
- DELETE /api/files/{id}?purge=true on a trashed file → 204, row gone
- DELETE without delete permission → 403/PermissionDenied envelope (reuse FileRbacTests pattern)
```

Use the exact assertion/auth style already present in those files (do not invent a new harness).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~FileRbacTests|FullyQualifiedName~SoftDeleteEndpointTests"`
Expected: FAIL — restore route missing; DELETE still hard-deletes.

- [ ] **Step 3: Update the controller**

Replace the `Delete` action and add `Restore`:

```csharp
[HttpDelete("{id:guid}")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public async Task<IActionResult> Delete(Guid id, [FromQuery] bool purge = false, CancellationToken ct = default)
{
    if (!access.CanDelete())
        throw new PermissionDeniedException("Delete not permitted.");
    var ok = purge ? await files.DeleteAsync(id, ct) : await files.TrashAsync(id, ct);
    return ok ? NoContent() : NotFound();
}

[HttpPost("{id:guid}/restore")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
{
    if (!access.CanDelete())
        throw new PermissionDeniedException("Delete not permitted.");
    return await files.RestoreAsync(id, ct) ? NoContent() : NotFound();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~FileRbacTests|FullyQualifiedName~SoftDeleteEndpointTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/FilesController.cs tests/Struo.Tests/
git commit -m "feat(api): files DELETE defaults to trash, add ?purge and restore endpoint (#12)"
```

---

### Task 4: Core migration `003-file-soft-delete.sql`

**Files:**
- Create: `db/migrations/003-file-soft-delete.sql`

**Interfaces:**
- Consumes: existing `files` table (from `001-core-baseline.sql`).
- Produces: `files.deletedat`, `files.deletedby`; existing `status='archived'` rows converted to trashed + `draft`.

- [ ] **Step 1: Write the migration**

```sql
-- 003-file-soft-delete.sql
-- 2026-07-27 · #12 · File soft-delete columns + retire the redundant 'archived' status.
-- Idempotent, forward-only. DB-7: temporal columns are timestamptz.
ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedat timestamptz NULL;
ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedby uuid NULL;

-- 'archived' is replaced by the recycle bin: existing archived files become trashed (reversible via
-- restore) and their status is normalised to 'draft' (a restored file is not auto-republished).
UPDATE files
   SET deletedat = COALESCE(deletedat, now()),
       status    = 'draft'
 WHERE status = 'archived';
```

- [ ] **Step 2: Verify idempotency + effect on live PG**

Apply the bootstrap against a live PostgreSQL (per the project's live-verify workflow), then re-run the migration; expect no error and zero further row changes. Confirm `\d files` shows both new columns.
Expected: both `ALTER`s no-op on second run; `UPDATE` affects 0 rows on second run.

- [ ] **Step 3: Commit**

```bash
git add db/migrations/003-file-soft-delete.sql
git commit -m "feat(db): migration 003 — files soft-delete columns + retire archived (#12)"
```

---

### Task 5: `filesApi` — `remove(purge)` + `restore`

**Files:**
- Modify: `frontend/src/api/filesApi.ts`
- Test: `frontend/src/api/filesApi.test.ts` (create if absent; else add to existing)

**Interfaces:**
- Consumes: `apiClient.delete`, `apiClient.post`.
- Produces: `filesApi.remove(id: string, opts?: { purge?: boolean }): Promise<void>`; `filesApi.restore(id: string): Promise<void>`.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { filesApi } from './filesApi'

describe('filesApi soft-delete', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('remove() trashes by default', async () => {
    const del = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined as never)
    await filesApi.remove('abc')
    expect(del).toHaveBeenCalledWith('/files/abc')
  })

  it('remove({purge:true}) appends ?purge=true', async () => {
    const del = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined as never)
    await filesApi.remove('abc', { purge: true })
    expect(del).toHaveBeenCalledWith('/files/abc?purge=true')
  })

  it('restore() posts to the restore route', async () => {
    const post = vi.spyOn(apiClient, 'post').mockResolvedValue(undefined as never)
    await filesApi.restore('abc')
    expect(post).toHaveBeenCalledWith('/files/abc/restore')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/api/filesApi.test.ts`
Expected: FAIL — `remove` takes no opts; `restore` undefined.

- [ ] **Step 3: Update `filesApi.ts`**

```ts
  async remove(id: string, opts?: { purge?: boolean }): Promise<void> {
    const qs = opts?.purge ? '?purge=true' : ''
    await apiClient.delete<void>(`/files/${id}${qs}`)
  },
  async restore(id: string): Promise<void> {
    await apiClient.post<void>(`/files/${id}/restore`)
  },
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm vitest run src/api/filesApi.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/filesApi.ts frontend/src/api/filesApi.test.ts
git commit -m "feat(media-fe): filesApi remove(purge) + restore (#12)"
```

---

### Task 6: `MediaLibraryView` — 使用中 / 回收桶 toggle + restore/purge actions

**Files:**
- Modify: `frontend/src/views/MediaLibraryView.vue`
- Modify: `frontend/src/locales/en.ts`, `frontend/src/locales/zh-TW.ts` (media ns keys if missing)
- Test: `frontend/src/views/MediaLibraryView.test.ts` (add facts)

**Interfaces:**
- Consumes: `itemsApi.list('file', { deleted: 'only' | undefined, ... })`, `filesApi.remove`, `filesApi.restore`, the `canDelete` gate already computed in this view (mirror `CollectionListView`).
- Produces: a reactive `view: 'active' | 'trash'` that drives the `deleted` list param and swaps row actions.

- [ ] **Step 1: Write the failing test**

Mirror the existing `MediaLibraryView.test.ts` mounting harness. Assert:

```
- default view calls itemsApi.list('file', <no deleted param>)
- switching to trash calls itemsApi.list('file', { deleted: 'only', ... })
- in trash view, the restore action calls filesApi.restore(id) then reloads
- in trash view, the purge action calls filesApi.remove(id, { purge: true }) then reloads
- the toggle is hidden when canDelete is false
```

Use the same `vi.spyOn(itemsApi, 'list')` / `filesApi` mock style already present in that test file.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/views/MediaLibraryView.test.ts`
Expected: FAIL — no toggle / trash param.

- [ ] **Step 3: Implement**

Add a `SelectButton` bound to `view` (options 使用中/回收桶), gated `v-if="canDelete"`, mirroring `CollectionListView`'s Active/Trash control. Thread the param into the existing `itemsApi.list('file', {...})` call in `load()`:

```ts
const res = await itemsApi.list('file', {
  // ...existing page/rows/sort/search/type/folder params...
  ...(view.value === 'trash' ? { deleted: 'only' } : {}),
})
```

In trash view, replace the detail-open/delete row actions with **Restore** (`await filesApi.restore(id); await load()`) and **Delete permanently** (`await purgeConfirm(...)` then `await filesApi.remove(id, { purge: true }); await load()`). Reuse `purgeConfirm` from `frontend/src/lib/deleteAction.ts` and the existing confirm dialog. Add any missing i18n keys under the `media` namespace in both locale files (e.g. `media.viewActive`, `media.viewTrash`, `media.restore`, `media.trashNotice`) — reuse `collectionList`/`confirm` copy where equivalent keys already exist.

- [ ] **Step 4: Run test + typecheck**

Run: `cd frontend && pnpm vitest run src/views/MediaLibraryView.test.ts && pnpm build`
Expected: PASS + clean `vue-tsc` build (build is the FE gate, not just vitest).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/views/MediaLibraryView.vue frontend/src/locales/ frontend/src/views/MediaLibraryView.test.ts
git commit -m "feat(media-fe): media library trash view with restore/purge (#12)"
```

---

### Task 7: `MediaDetailDialog` soft-delete copy + drop `archived` FE remnants

**Files:**
- Modify: `frontend/src/components/media/MediaDetailDialog.vue`
- Modify: `frontend/src/views/CollectionListView.vue` (remove the `archived` tag branch at ~line 77)
- Test: `frontend/src/components/media/MediaDetailDialog.test.ts` (update the delete-flow fact)

**Interfaces:**
- Consumes: `filesApi.remove` (now trash by default), `deleteConfirm(t, 'soft')` from `frontend/src/lib/deleteAction.ts`.
- Produces: dialog delete = soft-delete confirm; no `archived` status option/branch anywhere in FE.

- [ ] **Step 1: Write/adjust the failing test**

Update `MediaDetailDialog.test.ts` so the delete-flow fact expects the **soft-delete** confirm copy (`confirm.softDeleteMessage`) rather than the hard-delete copy, and that confirming calls `filesApi.remove(id)` (no purge). Keep the existing spy structure.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/components/media/MediaDetailDialog.test.ts`
Expected: FAIL — dialog still uses `deleteConfirm(t, 'hard')`.

- [ ] **Step 3: Implement**

In `MediaDetailDialog.vue`, change the delete confirm from `...deleteConfirm(t, 'hard')` to `...deleteConfirm(t, 'soft')` (verify the `deleteAction.ts` signature accepts a `'soft' | 'hard'` mode; if it only exposes `deleteConfirm`/`purgeConfirm`, use `deleteConfirm(t)` for the soft path). In `CollectionListView.vue`, delete the `if (s === 'archived') return 'warn'` line. Grep `frontend/src` for any remaining `archived` literal in media/status components and remove leftover options.

- [ ] **Step 4: Run test + full FE gate**

Run: `cd frontend && pnpm vitest run && pnpm build`
Expected: all vitest PASS + clean build.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/media/MediaDetailDialog.vue frontend/src/views/CollectionListView.vue frontend/src/components/media/MediaDetailDialog.test.ts
git commit -m "feat(media-fe): detail dialog soft-delete copy; drop archived status remnants (#12)"
```

---

### Task 8: Full verification + live e2e gate

**Files:**
- Modify: `frontend/e2e/media.spec.ts` (add a soft-delete flow) — or create `frontend/e2e/media-soft-delete.spec.ts`
- Modify: `docs/admin-ux-issues-backlog.md` (mark #12 done)

**Interfaces:** none (verification task).

- [ ] **Step 1: Backend + FE unit suites green**

Run: `dotnet test tests/Struo.Tests` and `cd frontend && pnpm vitest run && pnpm build`
Expected: all green.

- [ ] **Step 2: Write the live e2e flow**

Mirror the existing `frontend/e2e/media.spec.ts` login/env conventions (dev admin `admin@admin.com`, `E2E_API=:5221`, unique `E2E_STAMP`, `--workers=1`, backend SEC-7 limiter `Enabled=false`). Steps:

```
1. Upload a file → appears in 使用中.
2. Trash it → disappears from 使用中; GET /api/files/{id}/content returns 404.
3. Switch to 回收桶 → file listed there.
4. Restore → back in 使用中; /content serves again (200).
5. Trash again → 永久刪除 (purge) → gone from 回收桶; /content 404 permanently.
```

- [ ] **Step 3: Run the live e2e against PG + MinIO**

Run (per project live-verify workflow): start backend on :5221, `cd frontend && pnpm exec playwright test e2e/media-soft-delete.spec.ts --workers=1`
Expected: PASS. Also re-run the existing media/media-folders specs to confirm no regression.

- [ ] **Step 4: Mark #12 done + commit**

Update `docs/admin-ux-issues-backlog.md` row #12 status → ✅ 完成 with a one-line note (File soft-delete via dedicated pipeline; `archived` retired). Commit:

```bash
git add frontend/e2e/ docs/admin-ux-issues-backlog.md
git commit -m "test(e2e): file soft-delete flow; docs: mark #12 done (#12)"
```

---

## Self-Review

**Spec coverage:**
- §1 remove `archived` → Task 1 (entity) + Task 4 (data migration) + Task 7 (FE remnants). ✅
- §2 dedicated pipeline owns lifecycle; generic reads unchanged → Tasks 2–3. ✅
- §3.1 entity `ISoftDeletable` + status options → Task 1. ✅
- §3.2 Trash/Purge/Restore, blob only on purge, logo-ref cleared on trash+purge → Task 2 (purge=existing DeleteAsync, already clears logo). ✅
- §3.3 controller default-trash/`?purge`/restore → Task 3. ✅
- §3.4 migration 003 → Task 4. ✅
- §4 FE trash UI (filesApi, MediaLibraryView, MediaDetailDialog, CollectionListView) → Tasks 5–7. ✅
- §5 testing (backend unit, FE unit, live e2e) → Tasks 2/3/5/6/7 unit + Task 8 e2e. ✅
- §6 out-of-scope respected (no generic-purge blob cleanup, no retention job, no bulk ops). ✅

**Placeholder scan:** Test bodies for the WAF-based controller test (Task 3) and the mount-harness FE tests (Task 6) reference existing harnesses rather than reproducing 50-line fixtures — deliberate, because the exact harness lives in a named sibling file the implementer opens; the assertions and routes are given concretely. No `TBD`/`TODO`.

**Type consistency:** `TrashAsync(Guid, CancellationToken)→Task<bool>`, `RestoreAsync(Guid, CancellationToken)→Task<bool>`, `DeleteAsync`=purge — used consistently across Tasks 2/3. `filesApi.remove(id, {purge})` / `filesApi.restore(id)` consistent across Tasks 5/6/7. Repository `SoftDeleteAsync`/`RestoreAsync` signatures match `IItemRepository`. Collection route `"file"` used throughout.

**Open verification for the implementer (surface at Task 2):** confirm whether `FileService` already has access to the current-user id; if not, add the `ICurrentUserAccessor` ctor param as described and update the DI + test construction sites in the same task.
