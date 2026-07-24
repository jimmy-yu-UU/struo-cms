# Admin UX Batch B.1 — RBAC follow-up fixes — Design Spec (2026-07-24)

## Background

Live use of Batch B (merged `651c1f1`) surfaced three issues (user-reported, all reproduced):

1. **Leave-guard dialog "does nothing"** — on the Role form, when BOTH the generic form and the
   permission matrix are dirty, two independent `onBeforeRouteLeave` guards each show an
   identical "Unsaved changes" dialog. Clicking Yes on the first leaves the page unchanged and
   immediately pops the second identical dialog — perceived as "the options don't work / always
   cancels". (Reproduced: dialog 1 Yes → same page + dialog 2; dialog 2 Yes → finally leaves.)
2. **Effective-permissions preview is stale** — changing the Roles TagSelect doesn't update the
   panel; save navigates to the list, so the refreshed data is never seen. User must re-enter.
3. **New role has no matrix** — create mode deliberately hid the matrix (no role id to PUT);
   user must save, re-open, then edit permissions.

**Approved designs (user-selected):** #2 = live hypothetical preview driven by the current
TagSelect selection (`?roles=` query on the effective-permissions endpoint); #3 = create-mode
matrix buffered locally and submitted together with the form's Save.

## Design

### 1. Unified leave guard (#1)

- `PermissionMatrix.vue` **stops registering its own route guard**: remove `onBeforeRouteLeave`,
  `useConfirm`, and the `unsavedConfirm` import. It keeps exposing `dirty` (already in
  `defineExpose`).
- `ItemFormView.vue` becomes the single guard owner: `guardLeave()` checks
  `isDirty(baseline, model) || (matrixRef?.dirty ?? false)`; the `beforeunload` handler checks
  the same combined condition. One dialog, one Yes discards everything. This also extends the
  NAV-1 `onBeforeRouteUpdate` coverage to matrix edits (previously a known gap).
- `PermissionMatrix.save()` now returns `Promise<boolean>` (true on success) so the parent can
  sequence on it (§3).
- **Edit-mode form Save flushes a dirty matrix**: in `onSubmit` success for the role collection
  (edit mode), if the matrix is dirty, `await matrixRef.save()`; on failure (false) do NOT
  navigate (matrix's own error toast already fired; user stays to retry). This removes the
  confusing "form Save → unsaved-changes prompt about the matrix you thought you saved" path.
  The matrix's own "Save permissions" button remains for permission-only edits.

### 2. Live effective-permissions preview (#2)

- **Backend**: `GET /api/users/{id:guid}/effective-permissions` gains an optional `roles` query
  parameter (comma-separated role GUIDs). When present, the preview is computed for that
  hypothetical role set instead of the user's stored roles: `IRolePermissionStore` gains
  `LoadForRolesAsync(IReadOnlyList<Guid> roleIds, ct)` — loads those roles + their permission
  rows; an EMPTY list follows the same public-floor semantics as a role-less user (matching
  `LoadForUserAsync`). Unknown role ids → 400 (`BAD_USER_INPUT`); malformed GUIDs → 400.
  `PermissionResolver.Resolve` and the Me-style projection stay identical. Without the
  parameter, behavior is unchanged.
- **Frontend**: `EffectivePermissionsPanel` gains prop `roleIds?: string[]` (the form's current
  `model.relations.roles` selection, passed by ItemFormView). The panel watches it and refetches
  (debounced 300 ms) with `?roles=<csv>` — an empty selection sends `roles=` (empty → public
  floor). A permanent hint line (`rbac.effectivePreviewHint`: preview follows the currently
  selected roles, including unsaved changes) replaces the save-refresh mental model. The
  post-save `reload()` call and its no-op comment are removed (superseded).

### 3. Create-mode matrix (#3)

- `ItemFormView` mounts the matrix for `role` whenever the caller is super-admin — the
  `!isCreate` gate is dropped.
- `PermissionMatrix` gains prop `createMode?: boolean`. In create mode: no GET (grants start
  empty, baseline `'{}'`), and the "Save permissions" button is hidden — the form's Save owns
  submission. New exposed getter `currentEntries(): RolePermissionEntry[]` returns the folded
  non-all-false entries.
- `onSubmit` create path for `role`: after `itemsApi.create` returns the new id, if the matrix
  has any entries, `rbacApi.putRolePermissions(newId, entries)`. On grants-PUT failure: toast
  `rbac.grantsSaveFailedAfterCreate` ("角色已建立，但權限儲存失敗…") and navigate to the new
  role's EDIT page (not the list) so the user can retry from the loaded matrix.
- i18n: new keys `rbac.effectivePreviewHint`, `rbac.grantsSaveFailedAfterCreate` (both locales).

## Out of scope

- save-in-place for any form (user chose the hypothetical-preview option instead).
- Concurrent-editor conflict handling on the matrix (known deferred).

## Testing

- BE: `?roles=` — hypothetical set honored (grants of the GIVEN roles, not stored ones), empty
  list → public floor, unknown id → 400, malformed → 400, no param → unchanged stored behavior.
- FE unit: single guard (both-dirty → ONE confirm flow; matrix-only dirty still guards; Yes
  proceeds), matrix `createMode` (no GET, no save button, `currentEntries`), create+grants flow
  (create → PUT with buffered entries; failure → navigate to edit page), panel refetch on
  roleIds change (debounced, `?roles=` param), edit-mode form Save flushes dirty matrix and
  blocks navigation on matrix-save failure.
- Live smoke: reproduce all three original complaints and verify fixed on live PG.

## Execution

Branch `admin-ux-batch-b1` from `main`. Implementation subagents: Sonnet; reviews: Fable 5.
