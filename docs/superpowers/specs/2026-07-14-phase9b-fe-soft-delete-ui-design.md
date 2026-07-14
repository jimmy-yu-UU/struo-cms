# Phase 9b-fe — Soft delete admin UI (Vue trash / restore / purge)

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 9 (soft delete / revisions / lifecycle hooks + unified response envelope).
> **Companion to:** [Phase 9b (backend soft delete)](2026-07-13-phase9b-soft-delete-design.md) — this slice
> is the deferred Vue admin UI (`9b-fe`) that the 9b spec §12 recorded as out of scope. 9b shipped the REST +
> GraphQL API backend-only (frontend untouched, 237 tests); this slice adds the admin UI on top of it.
> **Frontend-only.** No backend, Domain, Application, Infrastructure, or DB change.

## 0. Summary

An admin UI for the soft-delete capability that Phase 9b delivered API-first. In the collection list view a
user with **delete** permission on a soft-deletable collection can switch between an **Active** view (live
rows, today's list) and a **Trash** view (soft-deleted rows). Each row carries inline actions:

- **Active view:** a **Delete** action. For a soft-deletable collection it soft-deletes (moves the row to
  trash, reversible); for a non-soft-delete collection it hard-deletes (today's behaviour).
- **Trash view:** **Restore** (clears the deletion marker) and **Delete permanently** (purge, irreversible).

The item form's existing Delete button is corrected to match: for a soft-deletable collection it soft-deletes
with "moved to trash" messaging instead of the current "cannot be undone".

The whole slice consumes the Phase 9b endpoints already live-verified on real Postgres. **The backend is not
touched** — including the schema endpoint, which already emits `softDelete` per collection (see §2).

## 1. Backend contract this UI consumes (already shipped in 9b, no change)

| Concern | Endpoint / field | Notes |
|---|---|---|
| Soft-delete flag per collection | `GET /api/schema` → `collection.softDelete: boolean` | `CollectionMetadata.SoftDelete` is a public `init` property already serialized to camelCase JSON. **No backend change needed.** |
| List with deleted mode | `GET /api/items/{c}?deleted=exclude\|only\|with` | `only`/`with` require the collection's **delete** permission → `403`; unknown value → `400`; default `exclude`. |
| Soft-delete a row | `DELETE /api/items/{c}/{id}` | `204`. Soft-deletes an opted-in collection; hard-deletes a non-opted-in one. |
| Purge a row | `DELETE /api/items/{c}/{id}?purge=true` | `204`. Permanent removal. |
| Restore a row | `POST /api/items/{c}/{id}/restore` | `200`, returns `{ data: <restored row> }`; never-existed id → `404`. |

## 2. Data / API layer (three small files)

- **`src/types/schema.ts`** — `CollectionMeta` gains `softDelete?: boolean`. Optional (`?`) so the type
  tolerates any older payload; the backend already sends it.
- **`src/lib/buildListQuery.ts`** — a trailing positional parameter `deleted?: 'exclude' | 'only' | 'with'`
  is appended (keeps every existing call site source-compatible). Only a non-`exclude` value is written to
  `params.deleted`; `exclude` is the server default and stays absent from the query string.
- **`src/api/itemsApi.ts`**:
  - `ListOptions` gains `deleted?: 'exclude' | 'only' | 'with'`; `list()` threads it into `buildListQuery`.
  - `remove(collection, id, opts?: { purge?: boolean })` → `DELETE /items/{c}/{id}` (+ `?purge=true` when
    `opts.purge`). Back-compatible: existing zero-`opts` callers are unchanged.
  - **new** `restore(collection, id): Promise<Record<string, unknown>>` → `POST /items/{c}/{id}/restore`
    (the `apiClient` unwraps the `{ data }` envelope, as with `get`/`create`/`update`).

## 3. Collection list view — `src/views/CollectionListView.vue`

- New reactive `mode: 'active' | 'trash'` (default `'active'`).
- **Active / Trash segmented switch** (PrimeVue `SelectButton`), rendered **only when
  `meta.softDelete && canDelete`**. Switching resets pagination (`page = 0`) and reloads with the matching
  deleted mode: `active` → omitted (server default `exclude`); `trash` → `only`.
- `loadItems()` passes `deleted: mode === 'trash' ? 'only' : undefined` into `itemsApi.list`.
- **Actions column** — rendered whenever `canDelete` (see §7 scope note), with mode-dependent buttons:
  - **active mode:** **Delete**. Calls the delete handler (§5) → soft or hard by `meta.softDelete`.
  - **trash mode:** **Restore** and **Delete permanently** (purge).
- On any action success, **reload the current list** (the row leaves Active / appears in Trash — the list
  update is the feedback); on failure, set the existing inline `error` string.
- **Row click** opens the item form in active mode only; disabled in trash mode (the form does not support
  loading a trashed row — deliberately out of scope, consistent with 9b's decision to leave single-item
  trashed fetch for later).
- Confirmation via `useConfirm` + a `<ConfirmDialog />` added to this view's template (the
  `ConfirmationService` is already registered in `main.ts`; `ItemFormView` uses the same pattern).

### 3.1 Action handlers & confirmation (graded, per the approved UX)

| Action | Condition | Confirm | Call |
|---|---|---|---|
| Delete (soft) | active mode, `meta.softDelete` | light: "Move to trash? You can restore it later." | `itemsApi.remove(name, id)` |
| Delete (hard) | active mode, `!meta.softDelete` | strong: "Delete this item? This cannot be undone." | `itemsApi.remove(name, id)` |
| Restore | trash mode | none (safe, reversible) | `itemsApi.restore(name, id)` |
| Delete permanently (purge) | trash mode | strong: "Permanently delete this item? This cannot be undone." | `itemsApi.remove(name, id, { purge: true })` |

Handlers are exposed via `defineExpose` (matching this view's existing test style — Vitest calls the exposed
methods directly rather than driving PrimeVue internals).

## 4. Item form delete semantics — `src/views/ItemFormView.vue`

`onDelete` becomes soft-delete-aware:

- `meta.softDelete` → confirm message "Move to trash? You can restore it later.", `itemsApi.remove(name, id)`
  (soft), navigate back to the list.
- otherwise → today's behaviour unchanged (strong confirm + hard delete).

## 5. Delete-semantics helper (testable, no DOM)

The soft-vs-hard decision and its confirm copy are shared by the list and the form. To keep both views thin
and the logic unit-testable, a tiny pure module `src/lib/deleteAction.ts` exposes:

```ts
export type DeleteKind = 'soft' | 'hard'
export function deleteKindFor(meta: { softDelete?: boolean }): DeleteKind
export function deleteConfirm(kind: DeleteKind): { header: string; message: string }
export function purgeConfirm(): { header: string; message: string }
```

The views import these for confirm copy + branch selection; the actual `confirm.require(...)` /
`itemsApi.*` calls stay in the views (side effects), invoked from `defineExpose`d handlers.

## 6. Permissions & feedback

- The Active/Trash switch and the actions column render only when `auth.canDelete(collection)` is true. The
  backend `403`/`404` remain the authoritative guard; the UI gating is convenience, not security.
- Feedback reuses the existing pattern: **inline error text** (`error` on the list, `serverError` on the
  form) + **list reload** on success. **No `ToastService`** is added (the app currently has none; staying
  consistent).

## 7. Scope decision — actions column applies to all collections

The inline **Delete** action (active mode) renders for **any** collection where `canDelete` is true, not
only soft-deletable ones — a non-soft-delete collection gets a hard-delete button (strong confirm). Rationale:
one uniform actions-column code path (no per-collection column add/remove), consistent list UX. The
Active/Trash **switch** still appears only for soft-deletable collections (a non-soft collection has no trash).
This does change the list surface for non-soft collections (they gain an inline delete they previously reached
only through the form) — an accepted, deliberate UX improvement.

## 8. Testing strategy (TDD)

- **Vitest (unit / component):**
  - `buildListQuery` — `deleted` omitted for `exclude`/undefined, written for `only`/`with`.
  - `itemsApi` — `list({deleted})` sends the param; `remove(…, {purge:true})` hits `?purge=true` and plain
    `remove` does not; `restore` POSTs the restore path and returns the unwrapped row.
  - `deleteAction` — `deleteKindFor` soft vs hard; confirm-copy shape.
  - `CollectionListView` — switch visibility (`softDelete && canDelete` only); trash mode loads
    `deleted=only`; actions column present/absent by `canDelete`; active delete calls soft vs hard by
    `softDelete`; restore + purge call the right API; a non-soft collection shows no switch.
  - `ItemFormView` — soft vs hard confirm message + call by `softDelete`.
  - `schema` type — `softDelete` is consumed (covered implicitly by the store/view tests).
- **Playwright E2E (live, real Postgres) — `src/e2e/trash.spec.ts`** (new): log in → open a soft-deletable
  collection → create a row → soft-delete it (Active) → switch to Trash, see it → Restore → back in Active →
  soft-delete again → Delete permanently → gone from Trash. Uses the real backend on live PG (the same
  environment the 9b backend live gate used).

## 9. Acceptance gate

- `pnpm test` all green (237 baseline + new tests), `pnpm vue-tsc` clean (enforces the `softDelete` type +
  registry exhaustiveness), `pnpm build` succeeds.
- **Live Playwright E2E PASSES** the full delete → trash → restore → purge loop against the running backend on
  real Postgres, before the slice is declared done. Honors the project's live-verify culture (this loop
  mutates real data through the 9b write paths).

## 10. Out of scope (recorded so they aren't lost)

- Backend changes of any kind (9b already shipped and live-verified the API).
- Unified response envelope (**slice 9a**).
- Opening a trashed row's **edit form** (trash actions are inline only; single-item trashed fetch was left
  for later by 9b §12).
- Bulk empty-trash / select-multiple purge.
- GraphQL-surfaced admin UI (the admin SPA uses REST; GraphQL parity is a delivery-API concern).
- Soft-cascade visualisation (9b marks the parent only).
