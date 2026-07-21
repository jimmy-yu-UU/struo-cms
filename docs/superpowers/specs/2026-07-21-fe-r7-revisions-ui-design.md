# FE-R7 — Revision History / Revert UI (9c-fe) — Design

**Date:** 2026-07-21
**Slice:** FE-R7 (the final slice of the frontend admin redesign FE-R0..R7)
**Type:** Pure frontend. No backend, route, or dependency changes.
**Consumes:** the existing Phase 9c revisions REST API (backend-only, live-verified on PG).

---

## 1. Goal & Scope

Give the item edit page a **revision history + revert** surface for collections that have
revisions enabled (`[CmsCollection(Revisions = true)]` — currently only `Article`). A user with
read access to the collection can open a history drawer, browse past revisions newest-first, view
a chosen revision's snapshot, and (with write access) revert the item to that revision.

The design prototype (`docs/struo-cms-frontend-design/`) has **no** revisions screen, so this is
designed fresh in the established FE-R0 design language (PrimeVue + custom Aura preset, theme
tokens, vue-i18n).

**In scope:** history list, snapshot view, revert with confirmation, in-place form refresh after
revert, i18n, gating by capability + permission, error/empty/loading states, tests + live smoke.

**Out of scope (YAGNI):**
- Version **diff / compare** between revisions (summary + raw JSON is enough to decide).
- Resolving `createdBy` (a `Guid`) to a human user name (no users-lookup dependency; show the id
  or `—`).
- Pagination of the history list (backend returns the full list newest-first, no pagination).
- Any change to the 9c backend, routes, or npm dependencies.

---

## 2. Backend contract (already shipped — for reference only)

REST, under `/api/items/{collection}/{id}` (envelope: `{ success, data, meta? }`):

| Method | Path | Returns (`data`) | RBAC |
|---|---|---|---|
| GET | `/revisions` | `[{ revisionNumber, operation, createdAt, createdBy }]`, newest-first, no pagination | read |
| GET | `/revisions/{n}` | `{ revisionNumber, operation, createdAt, createdBy, snapshot }` — `snapshot` is a **structured JSON object** | read |
| POST | `/revisions/{n}/revert` | the reverted item (same shape `GET /items/{collection}/{id}` returns) | write |

- `operation` is a free string; known values are `create`, `update`, `revert`.
- `createdAt` is an ISO timestamp; `createdBy` is a `Guid?` (null for system / unattributed).
- `revisionNumber` is a monotonic per-`(collection,itemId)` `long`.
- Revert is **append-only**: it re-applies the snapshot via the normal update path and appends a
  new revision with `operation = "revert"`; forward history is never deleted. Translation revert is
  an **overlay** (locales added after the reverted revision are left intact) — an existing 9c
  semantic, not surfaced specially in the UI.
- Cookie-auth writes require the `X-Struo-CSRF` header — already handled centrally by `apiClient`.

The **capability flag** is already emitted: `SchemaController` serializes the domain
`CollectionMetadata` directly, which includes `Revisions` → JSON `revisions` (camelCase). Only the
frontend `schema.ts` type is missing the field.

---

## 3. Components & responsibilities

### 3.1 Types — `types/schema.ts`
Add `revisions?: boolean` to `CollectionMeta` (mirrors the already-emitted backend field; sits
next to `softDelete?`). No other type changes.

### 3.2 API client — `api/itemsApi.ts`
Add three methods and their types (thin wrappers over `apiClient`, consistent with existing ones):

```ts
export type RevisionInfo = {
  revisionNumber: number
  operation: string
  createdAt: string
  createdBy: string | null
}
export type RevisionDetail = RevisionInfo & { snapshot: unknown }

listRevisions(collection, id): Promise<RevisionInfo[]>            // GET  /revisions
getRevision(collection, id, n): Promise<RevisionDetail>          // GET  /revisions/{n}
revert(collection, id, n): Promise<Record<string, unknown>>      // POST /revisions/{n}/revert
```

### 3.3 i18n — new `revisions` namespace (`locales/zh-TW.ts` + `en.ts`)
Keys for: drawer title, list column/labels, operation labels (`create`/`update`/`revert` +
unknown fallback), "created by" / system / unknown, empty state, load error + retry, "view
snapshot" affordance, "revert to this revision" button, revert confirm header/message (message
notes it overwrites current unsaved edits), revert success/failure toasts, "snapshot" section
label. Extend `locales.test.ts` for key parity between zh-TW and en.

### 3.4 Pure helpers — `lib/`
- `lib/revisionOperation.ts` — `revisionOperationKey(operation: string): string` maps a raw
  operation to an i18n label key, with a fallback for unknown values. Unit-tested.
- Snapshot rendering uses `JSON.stringify(snapshot, null, 2)` inline (no dedicated module needed);
  timestamp formatting reuses the existing app convention.

### 3.5 Components — `components/revisions/`
- **`RevisionHistoryDrawer.vue`** — orchestrator. PrimeVue `Drawer` on the right,
  `v-model:visible`. Props: `collection: string`, `itemId: string`, `canRevert: boolean`.
  - On open (visible→true): loads the list via `itemsApi.listRevisions`. States: loading / error
    (with retry) / empty / list.
  - Master–detail: selecting a revision loads its detail via `itemsApi.getRevision` and shows it
    in `RevisionSnapshotView`.
  - Handles the revert flow (confirm → `itemsApi.revert`): on success, re-emits `reverted`
    (the reverted item) to the parent, reloads its own list (the new `revert` revision appears),
    and shows a success toast; on failure, shows an error toast and stays open.
  - Emits: `update:visible`, `reverted` (payload = reverted item record).
- **`RevisionSnapshotView.vue`** — presentation of a single revision: summary header
  (revision number, operation label, timestamp, created-by) + a read-only, scrollable
  pretty-printed JSON block, and the "revert to this revision" button (rendered only when
  `canRevert`). Emits `revert` (the revision number) to the drawer, which owns the confirm + API
  call. Keeping the API/confirm in the drawer keeps this component pure-presentational and easy to
  test.

Decomposition rationale: the drawer owns data-fetching and side effects; the snapshot view is
pure presentation. Each is understandable and testable in isolation.

### 3.6 Integration — `views/ItemFormView.vue`
- Add a **"History"** button to `PageHeader` `#actions`, rendered only when
  `!isCreate && meta.revisions` (a secondary/text button placed before Delete/Save). Viewing needs
  only read access, which the user already has by being on the page.
- Clicking it opens `RevisionHistoryDrawer` (a `showHistory` ref). Pass `canRevert = canWrite`.
- Listen for the drawer's `reverted` event: `setModel(parseItemToForm(meta, revertedItem,
  langStore.languages))` (reuses the existing model-application path; `setModel` re-baselines so the
  form is not considered dirty, identical to the 409 `reloadLatest` behavior) + a success toast.
  The drawer stays open with its refreshed history.

---

## 4. Behavior details

- **Capability + permission gating.**
  - History button visible: `!isCreate && meta.revisions === true`.
  - Revert button visible: additionally `canWrite`. Backend enforces both read (list/detail) and
    write (revert) regardless — the UI gate is a convenience, not the security boundary.
- **Revert overwrites unsaved edits.** Revert is a server-side write; after it succeeds the form is
  replaced via `setModel`, discarding any in-progress edits. The revert confirmation message states
  this explicitly.
- **Post-revert dirty state.** `setModel` re-baselines the form, so the leave/unload guards stay
  quiet after a revert (consistent with `reloadLatest`).
- **Empty history.** Rendered as a friendly empty state. (Article captures a revision on create and
  on every update, so in practice there is ≥1, but the empty branch is handled.)

---

## 5. Error handling

| Failure | Handling |
|---|---|
| List load fails | Error state inside the drawer with a retry action. |
| Detail load fails | Error state in the detail panel; list stays usable. |
| Revert fails (403 / 409 / network) | Error toast; drawer stays open; no form mutation. |
| Snapshot is unexpectedly non-serializable | `JSON.stringify` guarded; fall back to empty/`—`. |

All API errors surface a user-facing message; nothing is silently swallowed.

---

## 6. Testing & verification

**Unit / component (vitest, jsdom):**
- `itemsApi` — the three new methods build the correct paths and unwrap the envelope.
- `lib/revisionOperation` — known values + unknown fallback.
- `locales.test.ts` — `revisions` ns key parity (zh-TW ↔ en).
- `RevisionSnapshotView` — summary rendering, JSON block, revert button gating + `revert` emit.
- `RevisionHistoryDrawer` — list render, select→detail, revert confirm→`itemsApi.revert`→`reverted`
  emit + list reload, empty/error/loading states, `canRevert` gating.
- `ItemFormView` — History button gating (`!isCreate && meta.revisions`); `reverted` → `setModel`
  + toast.

**Gates:**
- `pnpm build` (vue-tsc) clean + full vitest suite green (baseline **476**, expected to increase).
- Whole-branch review by Opus (Ready-to-merge, address Critical/Important).
- **Live PG Playwright smoke** on `Article` (has `Revisions = true`), backend `:5080` Development
  on real PG + MinIO, Vite `:5173`, bootstrap admin: open the drawer → see history newest-first →
  select a revision → see summary + JSON → revert → form updates to the reverted state → a new
  `revert` revision appears at the top of the list → dark-mode lockstep.

---

## 7. Process

- Branch `fe-r7-revisions-ui`.
- Subagent-driven: implementation = Sonnet, per-task review = Opus, final whole-branch review =
  Opus. SDD ledger at `.superpowers/sdd/progress.md`.
- Spec: this file. Plan: `docs/superpowers/plans/2026-07-21-fe-r7-revisions-ui.md`.
- Merge vs PR: user's call at the end, consistent with prior slices.

---

## 8. File inventory (anticipated)

**New:**
- `frontend/src/components/revisions/RevisionHistoryDrawer.vue` (+ `.test.ts`)
- `frontend/src/components/revisions/RevisionSnapshotView.vue` (+ `.test.ts`)
- `frontend/src/lib/revisionOperation.ts` (+ `.test.ts`)

**Modified:**
- `frontend/src/types/schema.ts` (add `revisions?`)
- `frontend/src/api/itemsApi.ts` (+ 3 methods + types) (+ `itemsApi.test.ts`)
- `frontend/src/locales/zh-TW.ts` + `en.ts` (+ `revisions` ns) (+ `locales.test.ts`)
- `frontend/src/views/ItemFormView.vue` (History button + drawer wiring) (+ `ItemFormView.test.ts`)
