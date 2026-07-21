# FE-R7 — Revision History / Revert UI (9c-fe) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a revision-history + revert UI to the item edit page for collections with revisions enabled (`Article`), consuming the existing Phase 9c REST API.

**Architecture:** A right-side PrimeVue `Drawer` opened from `ItemFormView`'s page header. The drawer owns data-fetching and the revert side-effect; a pure presentational `RevisionSnapshotView` renders a selected revision (summary + read-only JSON) and raises a `revert` event. On successful revert the drawer emits `reverted` with the reverted item, which `ItemFormView` applies via the existing `setModel`/`parseItemToForm` path. Pure frontend — no backend, route, or dependency changes.

**Tech Stack:** Vue 3 (`<script setup>` + TS), PrimeVue 4.5 (`Drawer`, `Button`, `ConfirmDialog`/`useConfirm`, `useToast`), vue-i18n 11 (`legacy:false`), Pinia, vitest + @vue/test-utils.

## Global Constraints

- Outbound/inbound JSON is camelCase. The API envelope is `{ success, data, meta? }`; `apiClient.get`/`post` already unwrap `.data`.
- Cookie-auth writes require the `X-Struo-CSRF` header — already added centrally by `apiClient` for non-safe methods; do NOT add it manually.
- Immutability: never mutate model objects in place; build new objects (spread). Follows existing `setModel` re-baseline pattern.
- i18n: every user-facing string goes through `t(...)`; keys must be symmetric between `zh-TW` and `en` (enforced by `locales.test.ts`). Default locale zh-TW, fallback en.
- No `console.log` in production code. Handle every API error with a user-facing message; never swallow silently.
- Files stay focused (<800 lines; these are all far smaller). Tests colocated as `*.test.ts` next to source.
- Gate before declaring done: `pnpm build` (vue-tsc) clean AND full vitest suite green. FE test baseline before this slice = **476**.
- The design prototype dir `docs/struo-cms-frontend-design/` stays untracked — never `git add` it.
- Run all frontend commands from `frontend/` (e.g. `cd frontend && pnpm ...`).

---

## File Structure

**New:**
- `frontend/src/lib/revisionOperation.ts` — maps a raw `operation` string to an i18n label key. (+ `.test.ts`)
- `frontend/src/components/revisions/RevisionSnapshotView.vue` — pure presentation of one revision (summary + JSON + revert button). (+ `.test.ts`)
- `frontend/src/components/revisions/RevisionHistoryDrawer.vue` — drawer orchestrator: list, select→detail, revert. (+ `.test.ts`)

**Modified:**
- `frontend/src/types/schema.ts` — add `revisions?: boolean` to `CollectionMeta`.
- `frontend/src/api/itemsApi.ts` — add `RevisionInfo`/`RevisionDetail` types + `listRevisions`/`getRevision`/`revert`. (+ `itemsApi.test.ts`)
- `frontend/src/locales/zh-TW.ts` + `frontend/src/locales/en.ts` — new `revisions` namespace. (+ `locales.test.ts`)
- `frontend/src/views/ItemFormView.vue` — History button + drawer wiring + `onReverted`. (+ `ItemFormView.test.ts`)

---

## Task 1: API layer — schema flag + revisions client methods

**Files:**
- Modify: `frontend/src/types/schema.ts` (add `revisions?` to `CollectionMeta`)
- Modify: `frontend/src/api/itemsApi.ts` (add types + 3 methods)
- Test: `frontend/src/api/itemsApi.test.ts`

**Interfaces:**
- Consumes: `apiClient.get<T>(path)`, `apiClient.post<T>(path, body?)` (both unwrap `.data`).
- Produces:
  - `type RevisionInfo = { revisionNumber: number; operation: string; createdAt: string; createdBy: string | null }`
  - `type RevisionDetail = RevisionInfo & { snapshot: unknown }`
  - `itemsApi.listRevisions(collection: string, id: string): Promise<RevisionInfo[]>`
  - `itemsApi.getRevision(collection: string, id: string, n: number): Promise<RevisionDetail>`
  - `itemsApi.revert(collection: string, id: string, n: number): Promise<Record<string, unknown>>`
  - `CollectionMeta.revisions?: boolean`

- [ ] **Step 1: Write the failing tests**

Append to `frontend/src/api/itemsApi.test.ts`:

```ts
describe('itemsApi revisions', () => {
  beforeEach(() => vi.clearAllMocks())

  it('listRevisions gets the revisions path and unwraps the array', async () => {
    const rows = [{ revisionNumber: 2, operation: 'update', createdAt: '2026-07-21T00:00:00Z', createdBy: 'u1' }]
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue(rows)
    const res = await itemsApi.listRevisions('article', '5')
    expect(spy).toHaveBeenCalledWith('/items/article/5/revisions')
    expect(res).toEqual(rows)
  })

  it('getRevision gets the numbered path and returns the snapshot record', async () => {
    const rec = { revisionNumber: 2, operation: 'update', createdAt: '2026-07-21T00:00:00Z', createdBy: null, snapshot: { status: 'draft' } }
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue(rec)
    const res = await itemsApi.getRevision('article', '5', 2)
    expect(spy).toHaveBeenCalledWith('/items/article/5/revisions/2')
    expect(res).toEqual(rec)
  })

  it('revert posts the revert path and returns the reverted item', async () => {
    const spy = vi.spyOn(apiClient, 'post').mockResolvedValue({ id: '5', status: 'draft' })
    const res = await itemsApi.revert('article', '5', 2)
    expect(spy).toHaveBeenCalledWith('/items/article/5/revisions/2/revert')
    expect(res).toEqual({ id: '5', status: 'draft' })
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && pnpm vitest run src/api/itemsApi.test.ts`
Expected: FAIL — `itemsApi.listRevisions is not a function` (etc.).

- [ ] **Step 3: Add the `revisions` flag to the schema type**

In `frontend/src/types/schema.ts`, inside `CollectionMeta`, add the field right after `softDelete?`:

```ts
  softDelete?: boolean // Phase 9b: true when the collection's entity implements ISoftDeletable
  revisions?: boolean // Phase 9c: true when the collection is revisioned ([CmsCollection(Revisions=true)])
```

- [ ] **Step 4: Add the revisions types + methods to itemsApi**

In `frontend/src/api/itemsApi.ts`, add the types after the existing `ListResult` types:

```ts
export type RevisionInfo = {
  revisionNumber: number
  operation: string
  createdAt: string
  createdBy: string | null
}
export type RevisionDetail = RevisionInfo & { snapshot: unknown }
```

Then add these three methods inside the `itemsApi` object (after `restore`):

```ts
  async listRevisions(collection: string, id: string): Promise<RevisionInfo[]> {
    return apiClient.get<RevisionInfo[]>(`/items/${collection}/${id}/revisions`)
  },
  async getRevision(collection: string, id: string, n: number): Promise<RevisionDetail> {
    return apiClient.get<RevisionDetail>(`/items/${collection}/${id}/revisions/${n}`)
  },
  async revert(collection: string, id: string, n: number): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(`/items/${collection}/${id}/revisions/${n}/revert`)
  },
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && pnpm vitest run src/api/itemsApi.test.ts`
Expected: PASS (all itemsApi tests, incl. the 3 new ones).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/types/schema.ts frontend/src/api/itemsApi.ts frontend/src/api/itemsApi.test.ts
git commit -m "feat(frontend): revisions API client + schema flag (FE-R7)"
```

---

## Task 2: i18n — `revisions` namespace

**Files:**
- Modify: `frontend/src/locales/zh-TW.ts` (add `revisions` block)
- Modify: `frontend/src/locales/en.ts` (add `revisions` block)
- Test: `frontend/src/locales/locales.test.ts`

**Interfaces:**
- Produces: `t('revisions.*')` keys — `title, open, loading, loadError, retry, empty, colRevision, colOperation, colWhen, colWho, opCreate, opUpdate, opRevert, opUnknown, system, snapshot, revert, revertConfirmHeader, revertConfirmMessage, reverted, revertFailed, detailError, selectHint`. `revertConfirmMessage` and `reverted` take a `{n}` param.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/locales/locales.test.ts` inside `describe('locale packs', ...)`:

```ts
  it('carries the revisions namespace in both packs', () => {
    expect(zhTW.revisions.open).toBe('歷史紀錄')
    expect(en.revisions.open).toBe('History')
  })
```

(The existing `zh-TW and en expose symmetric key sets` test will also fail until both packs get the block — that is expected and desired.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && pnpm vitest run src/locales/locales.test.ts`
Expected: FAIL — `zhTW.revisions is undefined` and the symmetry test errors.

- [ ] **Step 3: Add the `revisions` block to zh-TW**

In `frontend/src/locales/zh-TW.ts`, add after the `media` namespace (keep the trailing comma structure consistent):

```ts
  revisions: {
    title: '修訂紀錄',
    open: '歷史紀錄',
    loading: '載入中…',
    loadError: '無法載入修訂紀錄',
    retry: '重試',
    empty: '尚無修訂紀錄',
    colRevision: '版本',
    colOperation: '操作',
    colWhen: '時間',
    colWho: '操作者',
    opCreate: '新建',
    opUpdate: '更新',
    opRevert: '還原',
    opUnknown: '變更',
    system: '系統',
    snapshot: '快照',
    revert: '還原至此版',
    revertConfirmHeader: '確認還原',
    revertConfirmMessage: '將把此項目還原至版本 {n}，並覆蓋目前尚未儲存的編輯。此動作會新增一筆還原紀錄。',
    reverted: '已還原至版本 {n}',
    revertFailed: '還原失敗',
    detailError: '無法載入此版快照',
    selectHint: '選擇左側的版本以檢視快照',
  },
```

- [ ] **Step 4: Add the mirrored `revisions` block to en**

In `frontend/src/locales/en.ts`, add after the `media` namespace:

```ts
  revisions: {
    title: 'Revision history',
    open: 'History',
    loading: 'Loading…',
    loadError: 'Failed to load revisions',
    retry: 'Retry',
    empty: 'No revisions yet',
    colRevision: 'Revision',
    colOperation: 'Operation',
    colWhen: 'Time',
    colWho: 'By',
    opCreate: 'Created',
    opUpdate: 'Updated',
    opRevert: 'Reverted',
    opUnknown: 'Changed',
    system: 'System',
    snapshot: 'Snapshot',
    revert: 'Revert to this revision',
    revertConfirmHeader: 'Confirm revert',
    revertConfirmMessage: 'This reverts the item to revision {n} and overwrites your current unsaved edits. A new revision will be recorded.',
    reverted: 'Reverted to revision {n}',
    revertFailed: 'Revert failed',
    detailError: 'Failed to load this revision',
    selectHint: 'Select a revision to view its snapshot',
  },
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd frontend && pnpm vitest run src/locales/locales.test.ts`
Expected: PASS (both the new test and the symmetry test).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts frontend/src/locales/locales.test.ts
git commit -m "feat(frontend): revisions i18n namespace zh-TW + en (FE-R7)"
```

---

## Task 3: `lib/revisionOperation` helper

**Files:**
- Create: `frontend/src/lib/revisionOperation.ts`
- Test: `frontend/src/lib/revisionOperation.test.ts`

**Interfaces:**
- Produces: `revisionOperationKey(operation: string): string` — returns the i18n key for a raw operation (`create`→`revisions.opCreate`, `update`→`revisions.opUpdate`, `revert`→`revisions.opRevert`, anything else → `revisions.opUnknown`).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/revisionOperation.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { revisionOperationKey } from './revisionOperation'

describe('revisionOperationKey', () => {
  it('maps known operations to their i18n keys', () => {
    expect(revisionOperationKey('create')).toBe('revisions.opCreate')
    expect(revisionOperationKey('update')).toBe('revisions.opUpdate')
    expect(revisionOperationKey('revert')).toBe('revisions.opRevert')
  })

  it('falls back to opUnknown for unrecognised operations', () => {
    expect(revisionOperationKey('something-else')).toBe('revisions.opUnknown')
    expect(revisionOperationKey('')).toBe('revisions.opUnknown')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && pnpm vitest run src/lib/revisionOperation.test.ts`
Expected: FAIL — cannot resolve `./revisionOperation`.

- [ ] **Step 3: Write the implementation**

Create `frontend/src/lib/revisionOperation.ts`:

```ts
// Maps a raw revision `operation` (a free-form string from the 9c API; known values are
// create/update/revert) to a vue-i18n key. Unknown values fall back to a generic label so a
// future backend operation never renders a raw token.
const KEYS: Record<string, string> = {
  create: 'revisions.opCreate',
  update: 'revisions.opUpdate',
  revert: 'revisions.opRevert',
}

export function revisionOperationKey(operation: string): string {
  return KEYS[operation] ?? 'revisions.opUnknown'
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && pnpm vitest run src/lib/revisionOperation.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/revisionOperation.ts frontend/src/lib/revisionOperation.test.ts
git commit -m "feat(frontend): revisionOperationKey helper (FE-R7)"
```

---

## Task 4: `RevisionSnapshotView.vue` (pure presentation)

**Files:**
- Create: `frontend/src/components/revisions/RevisionSnapshotView.vue`
- Test: `frontend/src/components/revisions/RevisionSnapshotView.test.ts`

**Interfaces:**
- Consumes: `RevisionDetail` (from `itemsApi`), `revisionOperationKey` (Task 3), `t` (vue-i18n).
- Props: `{ detail: RevisionDetail | null; loading: boolean; error: string; canRevert: boolean }`.
- Emits: `(e: 'revert', revisionNumber: number)`.
- Produces (for Task 5): the component named `RevisionSnapshotView`, rendering `.rev-json` (pretty JSON `<pre>`), `.rev-summary`, and a `.rev-revert-btn` (only when `canRevert` and a detail is present).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/revisions/RevisionSnapshotView.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RevisionSnapshotView from './RevisionSnapshotView.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { revisions: {
    opUpdate: 'Updated', opUnknown: 'Changed', system: 'System', snapshot: 'Snapshot',
    revert: 'Revert to this revision', selectHint: 'Select a revision', loading: 'Loading…',
  } } },
})
const stubs = { Button: true }
const detail = { revisionNumber: 3, operation: 'update', createdAt: '2026-07-21T10:00:00Z', createdBy: 'user-1', snapshot: { status: 'draft' } }

function mountView(props: Record<string, unknown>) {
  return mount(RevisionSnapshotView, { props, global: { plugins: [i18n], stubs } })
}

describe('RevisionSnapshotView', () => {
  it('renders the summary and pretty-printed JSON snapshot', () => {
    const w = mountView({ detail, loading: false, error: '', canRevert: true })
    expect(w.text()).toContain('Updated')
    expect(w.find('.rev-json').text()).toContain('"status": "draft"')
  })

  it('shows a hint when no detail is selected', () => {
    const w = mountView({ detail: null, loading: false, error: '', canRevert: true })
    expect(w.text()).toContain('Select a revision')
    expect(w.find('.rev-json').exists()).toBe(false)
  })

  it('renders the revert button only when canRevert', () => {
    expect(mountView({ detail, loading: false, error: '', canRevert: true }).find('.rev-revert-btn').exists()).toBe(true)
    expect(mountView({ detail, loading: false, error: '', canRevert: false }).find('.rev-revert-btn').exists()).toBe(false)
  })

  it('emits revert with the revision number when the button is clicked', async () => {
    const w = mountView({ detail, loading: false, error: '', canRevert: true })
    await w.find('.rev-revert-btn').trigger('click')
    expect(w.emitted('revert')?.[0]).toEqual([3])
  })

  it('shows the error message when error is set', () => {
    const w = mountView({ detail: null, loading: false, error: 'boom', canRevert: true })
    expect(w.text()).toContain('boom')
  })
})
```

Note: the stubbed `Button` still renders a clickable root element carrying its `class`, so `.rev-revert-btn` and `@click` work in the test.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && pnpm vitest run src/components/revisions/RevisionSnapshotView.test.ts`
Expected: FAIL — cannot resolve `./RevisionSnapshotView.vue`.

- [ ] **Step 3: Write the component**

Create `frontend/src/components/revisions/RevisionSnapshotView.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import { useI18n } from 'vue-i18n'
import { revisionOperationKey } from '../../lib/revisionOperation'
import type { RevisionDetail } from '../../api/itemsApi'

const props = defineProps<{
  detail: RevisionDetail | null
  loading: boolean
  error: string
  canRevert: boolean
}>()
const emit = defineEmits<{ (e: 'revert', revisionNumber: number): void }>()
const { t } = useI18n()

const operationLabel = computed(() =>
  props.detail ? t(revisionOperationKey(props.detail.operation)) : '',
)
const whenText = computed(() =>
  props.detail ? new Date(props.detail.createdAt).toLocaleString() : '',
)
const whoText = computed(() => props.detail?.createdBy ?? t('revisions.system'))
const prettyJson = computed(() => {
  if (!props.detail) return ''
  try {
    return JSON.stringify(props.detail.snapshot, null, 2)
  } catch {
    return ''
  }
})
</script>

<template>
  <div class="rev-detail">
    <p v-if="loading" class="rev-notice">{{ t('revisions.loading') }}</p>
    <p v-else-if="error" class="rev-notice rev-error" role="alert">{{ error }}</p>
    <p v-else-if="!detail" class="rev-notice">{{ t('revisions.selectHint') }}</p>
    <template v-else>
      <header class="rev-summary">
        <div class="rev-summary__row">
          <span class="rev-badge">#{{ detail.revisionNumber }}</span>
          <span class="rev-op">{{ operationLabel }}</span>
        </div>
        <dl class="rev-meta">
          <div><dt>{{ t('revisions.colWhen') }}</dt><dd>{{ whenText }}</dd></div>
          <div><dt>{{ t('revisions.colWho') }}</dt><dd>{{ whoText }}</dd></div>
        </dl>
      </header>
      <section class="rev-snapshot">
        <h3>{{ t('revisions.snapshot') }}</h3>
        <pre class="rev-json">{{ prettyJson }}</pre>
      </section>
      <div v-if="canRevert" class="rev-actions">
        <Button
          class="rev-revert-btn"
          :label="t('revisions.revert')"
          icon="pi pi-replay"
          severity="warn"
          @click="emit('revert', detail.revisionNumber)"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.rev-detail { display: grid; gap: 16px; align-content: start; min-width: 0; }
.rev-notice { color: var(--muted); margin: 0; }
.rev-error { color: var(--danger); }
.rev-summary { display: grid; gap: 8px; }
.rev-summary__row { display: flex; align-items: center; gap: 10px; }
.rev-badge {
  font-variant-numeric: tabular-nums; font-weight: 700; color: var(--primary, var(--fg));
  background: color-mix(in srgb, var(--primary, #38bdf8) 12%, transparent);
  padding: 2px 8px; border-radius: var(--radius, 8px);
}
.rev-op { font-weight: 600; color: var(--fg); }
.rev-meta { display: grid; gap: 4px; margin: 0; }
.rev-meta div { display: flex; gap: 8px; font-size: .85rem; }
.rev-meta dt { color: var(--muted); margin: 0; min-width: 3.5rem; }
.rev-meta dd { color: var(--fg); margin: 0; }
.rev-snapshot h3 { margin: 0 0 6px; font-size: .8rem; color: var(--muted); font-weight: 500; }
.rev-json {
  margin: 0; padding: 12px; border: 1px solid var(--border); border-radius: var(--radius, 8px);
  background: var(--bg); color: var(--fg); font-size: .8rem; line-height: 1.5;
  max-height: 50vh; overflow: auto; white-space: pre; word-break: normal;
}
.rev-actions { display: flex; justify-content: flex-end; }
</style>
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && pnpm vitest run src/components/revisions/RevisionSnapshotView.test.ts`
Expected: PASS (all 5).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/revisions/RevisionSnapshotView.vue frontend/src/components/revisions/RevisionSnapshotView.test.ts
git commit -m "feat(frontend): RevisionSnapshotView (summary + JSON + revert) (FE-R7)"
```

---

## Task 5: `RevisionHistoryDrawer.vue` (orchestrator)

**Files:**
- Create: `frontend/src/components/revisions/RevisionHistoryDrawer.vue`
- Test: `frontend/src/components/revisions/RevisionHistoryDrawer.test.ts`

**Interfaces:**
- Consumes: `itemsApi.listRevisions/getRevision/revert` (Task 1), `RevisionSnapshotView` (Task 4), `revisionOperationKey` (Task 3), `useConfirm`, `useToast`, `t`.
- Props: `{ visible: boolean; collection: string; itemId: string; canRevert: boolean }`.
- Emits: `(e: 'update:visible', value: boolean)`, `(e: 'reverted', item: Record<string, unknown>)`.
- Produces (for Task 6): component named `RevisionHistoryDrawer`; exposes (for tests) `load`, `select`, `onRevert`, `revisions`, `selected`, `detail`, `listLoading`, `listError`, `detailLoading`, `detailError`, `reverting`.
- Behaviour: on `visible`→true, calls `load()`. `select(rev)` loads detail. `onRevert(n)` opens a confirm; on accept calls `itemsApi.revert`, emits `reverted`, reloads the list, toasts success; on error toasts failure and stays open.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/revisions/RevisionHistoryDrawer.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RevisionHistoryDrawer from './RevisionHistoryDrawer.vue'
import { itemsApi } from '../../api/itemsApi'

const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { revisions: {
    title: 'Revision history', loading: 'Loading…', loadError: 'Failed to load revisions',
    retry: 'Retry', empty: 'No revisions yet', colWhen: 'Time', colWho: 'By', snapshot: 'Snapshot',
    opCreate: 'Created', opUpdate: 'Updated', opRevert: 'Reverted', opUnknown: 'Changed', system: 'System',
    revert: 'Revert to this revision', revertConfirmHeader: 'Confirm revert',
    revertConfirmMessage: 'Revert to {n}?', reverted: 'Reverted to {n}', revertFailed: 'Revert failed',
    detailError: 'Failed to load this revision', selectHint: 'Select a revision',
  } } },
})
// Stub Drawer so its content always renders (teleport/visible internals are not under test).
const stubs = { Drawer: { template: '<div class="drawer"><slot /></div>' }, Button: true, ConfirmDialog: true }

function mountDrawer(props: Record<string, unknown> = {}) {
  return mount(RevisionHistoryDrawer, {
    props: { visible: true, collection: 'article', itemId: '5', canRevert: true, ...props },
    global: { plugins: [i18n], stubs },
  })
}

const rows = [
  { revisionNumber: 2, operation: 'update', createdAt: '2026-07-21T02:00:00Z', createdBy: 'u1' },
  { revisionNumber: 1, operation: 'create', createdAt: '2026-07-21T01:00:00Z', createdBy: 'u1' },
]

describe('RevisionHistoryDrawer', () => {
  beforeEach(() => { vi.clearAllMocks() })

  it('loads the list when opened and renders rows', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    const w = mountDrawer()
    await w.vm.load()
    expect(itemsApi.listRevisions).toHaveBeenCalledWith('article', '5')
    expect((w.vm as any).revisions).toHaveLength(2)
  })

  it('shows the empty state when there are no revisions', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue([])
    const w = mountDrawer()
    await w.vm.load()
    expect((w.vm as any).revisions).toHaveLength(0)
    expect(w.text()).toContain('No revisions yet')
  })

  it('records a list error when loading fails', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockRejectedValue(new Error('net'))
    const w = mountDrawer()
    await w.vm.load()
    expect((w.vm as any).listError).toBe('Failed to load revisions')
  })

  it('select loads the detail for a revision', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    const detail = { ...rows[0], snapshot: { status: 'draft' } }
    const spy = vi.spyOn(itemsApi, 'getRevision').mockResolvedValue(detail)
    const w = mountDrawer()
    await w.vm.load()
    await (w.vm as any).select(rows[0])
    expect(spy).toHaveBeenCalledWith('article', '5', 2)
    expect((w.vm as any).detail).toEqual(detail)
  })

  it('onRevert confirms, reverts, emits reverted, reloads, and toasts success', async () => {
    const list = vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    vi.spyOn(itemsApi, 'revert').mockResolvedValue({ id: '5', status: 'draft' })
    const w = mountDrawer()
    await w.vm.load()
    list.mockClear()
    ;(w.vm as any).onRevert(2)
    expect(confirmRequire).toHaveBeenCalled()
    await confirmRequire.mock.calls[0][0].accept()
    expect(itemsApi.revert).toHaveBeenCalledWith('article', '5', 2)
    expect(w.emitted('reverted')?.[0]).toEqual([{ id: '5', status: 'draft' }])
    expect(list).toHaveBeenCalledTimes(1) // reloaded after revert
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
  })

  it('onRevert toasts an error and does not emit when revert fails', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    vi.spyOn(itemsApi, 'revert').mockRejectedValue(new Error('nope'))
    const w = mountDrawer()
    await w.vm.load()
    ;(w.vm as any).onRevert(2)
    await confirmRequire.mock.calls[0][0].accept()
    expect(w.emitted('reverted')).toBeUndefined()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }))
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && pnpm vitest run src/components/revisions/RevisionHistoryDrawer.test.ts`
Expected: FAIL — cannot resolve `./RevisionHistoryDrawer.vue`.

- [ ] **Step 3: Write the component**

Create `frontend/src/components/revisions/RevisionHistoryDrawer.vue`:

```vue
<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Drawer from 'primevue/drawer'
import Button from 'primevue/button'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useI18n } from 'vue-i18n'
import RevisionSnapshotView from './RevisionSnapshotView.vue'
import { revisionOperationKey } from '../../lib/revisionOperation'
import { itemsApi, type RevisionInfo, type RevisionDetail } from '../../api/itemsApi'

const props = defineProps<{
  visible: boolean
  collection: string
  itemId: string
  canRevert: boolean
}>()
const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
  (e: 'reverted', item: Record<string, unknown>): void
}>()

const confirm = useConfirm()
const toast = useToast()
const { t } = useI18n()

const revisions = ref<RevisionInfo[]>([])
const selected = ref<RevisionInfo | null>(null)
const detail = ref<RevisionDetail | null>(null)
const listLoading = ref(false)
const listError = ref('')
const detailLoading = ref(false)
const detailError = ref('')
const reverting = ref(false)

const isEmpty = computed(() => !listLoading.value && !listError.value && revisions.value.length === 0)

function opLabel(op: string): string {
  return t(revisionOperationKey(op))
}
function whenLabel(iso: string): string {
  return new Date(iso).toLocaleString()
}

async function load(): Promise<void> {
  listLoading.value = true
  listError.value = ''
  selected.value = null
  detail.value = null
  try {
    revisions.value = await itemsApi.listRevisions(props.collection, props.itemId)
  } catch (e) {
    listError.value = e instanceof Error ? e.message : t('revisions.loadError')
    // Normalise unknown errors to the friendly copy; keep server messages when present.
    if (!(e instanceof Error) || !e.message) listError.value = t('revisions.loadError')
    else listError.value = t('revisions.loadError')
  } finally {
    listLoading.value = false
  }
}

async function select(rev: RevisionInfo): Promise<void> {
  selected.value = rev
  detail.value = null
  detailLoading.value = true
  detailError.value = ''
  try {
    detail.value = await itemsApi.getRevision(props.collection, props.itemId, rev.revisionNumber)
  } catch {
    detailError.value = t('revisions.detailError')
  } finally {
    detailLoading.value = false
  }
}

function onRevert(n: number): void {
  confirm.require({
    header: t('revisions.revertConfirmHeader'),
    message: t('revisions.revertConfirmMessage', { n }),
    accept: async () => {
      reverting.value = true
      try {
        const item = await itemsApi.revert(props.collection, props.itemId, n)
        emit('reverted', item)
        await load()
        toast.add({ severity: 'success', summary: t('revisions.reverted', { n }), life: 2500 })
      } catch {
        toast.add({ severity: 'error', summary: t('revisions.revertFailed'), life: 3500 })
      } finally {
        reverting.value = false
      }
    },
  })
}

watch(
  () => props.visible,
  (open) => { if (open) void load() },
  { immediate: true },
)

defineExpose({ load, select, onRevert, revisions, selected, detail, listLoading, listError, detailLoading, detailError, reverting })
</script>

<template>
  <Drawer
    :visible="visible"
    position="right"
    :header="t('revisions.title')"
    class="rev-drawer"
    @update:visible="(v: boolean) => emit('update:visible', v)"
  >
    <ConfirmDialog />
    <div class="rev-layout">
      <aside class="rev-list">
        <p v-if="listLoading" class="rev-notice">{{ t('revisions.loading') }}</p>
        <div v-else-if="listError" class="rev-notice rev-error" role="alert">
          <span>{{ listError }}</span>
          <Button :label="t('revisions.retry')" size="small" text @click="load" />
        </div>
        <p v-else-if="isEmpty" class="rev-notice">{{ t('revisions.empty') }}</p>
        <ul v-else class="rev-items">
          <li v-for="rev in revisions" :key="rev.revisionNumber">
            <button
              type="button"
              class="rev-item"
              :class="{ 'rev-item--active': selected?.revisionNumber === rev.revisionNumber }"
              @click="select(rev)"
            >
              <span class="rev-item__num">#{{ rev.revisionNumber }}</span>
              <span class="rev-item__op">{{ opLabel(rev.operation) }}</span>
              <span class="rev-item__when">{{ whenLabel(rev.createdAt) }}</span>
            </button>
          </li>
        </ul>
      </aside>
      <RevisionSnapshotView
        class="rev-pane"
        :detail="detail"
        :loading="detailLoading"
        :error="detailError"
        :can-revert="canRevert && !reverting"
        @revert="onRevert"
      />
    </div>
  </Drawer>
</template>

<style scoped>
.rev-drawer :deep(.p-drawer) { width: 46rem; max-width: 100vw; }
.rev-layout { display: grid; grid-template-columns: 16rem 1fr; gap: 20px; height: 100%; min-height: 0; }
.rev-list { border-right: 1px solid var(--border); padding-right: 12px; overflow: auto; }
.rev-notice { color: var(--muted); margin: 0; display: flex; align-items: center; gap: 8px; }
.rev-error { color: var(--danger); }
.rev-items { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
.rev-item {
  width: 100%; text-align: left; display: grid; gap: 2px; cursor: pointer;
  padding: 8px 10px; border: 1px solid transparent; border-radius: var(--radius, 8px);
  background: transparent; color: var(--fg);
}
.rev-item:hover { background: color-mix(in srgb, var(--fg) 6%, transparent); }
.rev-item--active { border-color: var(--border); background: color-mix(in srgb, var(--primary, #38bdf8) 10%, transparent); }
.rev-item__num { font-weight: 700; font-variant-numeric: tabular-nums; }
.rev-item__op { font-size: .85rem; }
.rev-item__when { font-size: .75rem; color: var(--muted); }
.rev-pane { min-width: 0; overflow: auto; }
@media (max-width: 640px) { .rev-layout { grid-template-columns: 1fr; } }
</style>
```

- [ ] **Step 4: Simplify the `load()` catch block**

The catch block above is intentionally redundant to make the failing test explicit; collapse it to the final behaviour (always the friendly copy, matching the test which asserts `'Failed to load revisions'`):

```ts
  } catch {
    listError.value = t('revisions.loadError')
  } finally {
```

Replace the whole `try { ... } catch (e) { ... }` list-error branch with:

```ts
  try {
    revisions.value = await itemsApi.listRevisions(props.collection, props.itemId)
  } catch {
    listError.value = t('revisions.loadError')
  } finally {
    listLoading.value = false
  }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd frontend && pnpm vitest run src/components/revisions/RevisionHistoryDrawer.test.ts`
Expected: PASS (all 6).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/revisions/RevisionHistoryDrawer.vue frontend/src/components/revisions/RevisionHistoryDrawer.test.ts
git commit -m "feat(frontend): RevisionHistoryDrawer orchestrator (FE-R7)"
```

---

## Task 6: Integrate into `ItemFormView.vue`

**Files:**
- Modify: `frontend/src/views/ItemFormView.vue`
- Test: `frontend/src/views/ItemFormView.test.ts`

**Interfaces:**
- Consumes: `RevisionHistoryDrawer` (Task 5), `CollectionMeta.revisions` (Task 1), existing `setModel`/`parseItemToForm`, `useToast`, `t('revisions.*')`.
- Produces: History button (rendered when `!isCreate && meta.revisions`); `showHistory` ref; `onReverted(item)` handler that applies the reverted item via `setModel(parseItemToForm(...))`. Both exposed for tests.

- [ ] **Step 1: Write the failing tests**

Add to `frontend/src/views/ItemFormView.test.ts`. First extend the test i18n `messages.en` object with a `revisions` block (add alongside the existing `itemForm` block):

```ts
    revisions: { open: 'History', title: 'Revision history', reverted: 'Reverted to {n}' },
```

Add `RevisionHistoryDrawer: true` to the `stubs` object:

```ts
const stubs = { ItemForm: true, Button: true, ConfirmDialog: true, RevisionHistoryDrawer: true }
```

Then add these tests inside `describe('ItemFormView', ...)`:

```ts
  it('renders the history drawer only for revisioned collections in edit mode', async () => {
    routeParams = { name: 'article', id: '5' }
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, revisions: true })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    expect(w.find('revision-history-drawer-stub').exists()).toBe(true)
  })

  it('does not render the history drawer when the collection is not revisioned', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores() // meta has no revisions flag
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    expect(w.find('revision-history-drawer-stub').exists()).toBe(false)
  })

  it('does not render the history drawer in create mode even if revisioned', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, revisions: true })
    const w = mountView()
    await w.vm.init()
    expect(w.find('revision-history-drawer-stub').exists()).toBe(false)
  })

  it('onReverted applies the reverted item into the form model and re-baselines', async () => {
    routeParams = { name: 'article', id: '5' }
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, revisions: true })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).onReverted({ id: '5', status: 'reverted-status', translations: {}, version: 4 })
    expect((w.vm as any).model.shared.status).toBe('reverted-status')
    expect((w.vm as any).model.version).toBe(4)
    // re-baselined: leaving must not prompt
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && pnpm vitest run src/views/ItemFormView.test.ts`
Expected: FAIL — `onReverted is not a function` and the drawer stub is not found.

- [ ] **Step 3: Wire the drawer into the view — script**

In `frontend/src/views/ItemFormView.vue`:

Add the import (with the other component imports). Do NOT import `useToast` here — the success toast is owned by the drawer (Task 5); `onReverted` only refreshes the form, so a view-level toast would double-notify and leave an unused variable:

```ts
import RevisionHistoryDrawer from '../components/revisions/RevisionHistoryDrawer.vue'
```

Add state (after `const notFound = ref(false)` block, near the other refs):

```ts
const showHistory = ref(false)
```

Add a stable string item-id for the drawer prop (after the `isCreate` computed):

```ts
const idStr = computed(() => id.value ?? '')
```

Add the handler (after `reloadLatest`):

```ts
function onReverted(item: Record<string, unknown>): void {
  if (!meta.value) return
  // Apply the reverted item exactly like a fresh load: setModel re-baselines, so the form is not
  // considered dirty afterwards (same contract as reloadLatest). The drawer already toasts success
  // for the revert action itself; this refreshes the on-screen form to match.
  setModel(parseItemToForm(meta.value, item, langStore.languages))
  errors.value = {}
  serverError.value = ''
}
```

Update `defineExpose` to add `onReverted` and `showHistory`:

```ts
defineExpose({ init, onSubmit, onDelete, onCancel, reloadLatest, onReverted, showHistory, model, errors, serverError, notFound, loading, conflict })
```

- [ ] **Step 4: Wire the drawer into the view — template**

In the `#actions` slot of `PageHeader`, add the History button BEFORE the Delete button:

```vue
        <template #actions>
          <Button v-if="!isCreate && meta.revisions" :label="t('revisions.open')" icon="pi pi-history"
                  severity="secondary" text @click="showHistory = true" />
          <Button v-if="!isCreate && canDelete" :label="t('itemForm.delete')" severity="danger" @click="onDelete" />
          <Button v-if="canWrite" :label="t('itemForm.save')" :loading="submitting" @click="onSubmit" />
        </template>
```

Add the drawer at the end of the `<template v-else>` block, after `</ItemForm>` and before the closing `</template>`:

```vue
      <RevisionHistoryDrawer
        v-if="!isCreate && meta.revisions"
        v-model:visible="showHistory"
        :collection="name"
        :item-id="idStr"
        :can-revert="canWrite"
        @reverted="onReverted"
      />
```

- [ ] **Step 5: Run the tests + type-check to verify they pass**

Run: `cd frontend && pnpm vitest run src/views/ItemFormView.test.ts`
Expected: PASS (all existing + 4 new).

Run: `cd frontend && pnpm build`
Expected: vue-tsc clean (no unused-variable or type errors). If `idStr` or the import is reported unused, fix before committing.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts
git commit -m "feat(frontend): wire revision history drawer into ItemFormView (FE-R7)"
```

---

## Task 7: Full verification gate + live smoke

**Files:** none (verification only).

- [ ] **Step 1: Run the full unit suite**

Run: `cd frontend && pnpm vitest run`
Expected: all green; total count = 476 + new tests (3 API + 1 locales + 2 helper + 5 snapshot + 6 drawer + 4 view = **≈497**).

- [ ] **Step 2: Run the type-check / production build**

Run: `cd frontend && pnpm build`
Expected: vue-tsc + vite build succeed, 0 errors.

- [ ] **Step 3: Live PG Playwright smoke (manual, orchestrator-run)**

Bring up the real stack and exercise the flow against `Article` (has `Revisions = true`):
- Backend: `dotnet run --project src/Struo.Api --no-launch-profile --no-build` with `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://127.0.0.1:5080` (PG `localhost:5432`, Redis 6379 per `appsettings.Development.json`).
- Frontend: `cd frontend && pnpm dev --host 127.0.0.1` (Vite :5173).
- Login as bootstrap admin (`admin@admin.com` / `admin#90196080`).
- Open an Article → edit page shows the **History** button. Open it → drawer lists revisions newest-first → select one → summary + JSON render → click Revert → confirm → form updates to the reverted state → a new `revert` revision appears at the top of the reloaded list → success toast. Toggle dark mode: drawer + JSON block track theme tokens.
- Expected: no console errors originating from FE-R7 code (env-only MinIO/startup noise is acceptable, as documented for prior slices).

- [ ] **Step 4: Whole-branch review (Opus) + fixes**

Dispatch a whole-branch review; address any Critical/Important findings; re-run Steps 1–2 after fixes.

- [ ] **Step 5: Final commit / ready-to-merge**

Ensure the working tree is clean apart from the untracked `docs/struo-cms-frontend-design/`. Branch is ready for merge (merge vs PR is the user's call).

---

## Self-Review

**Spec coverage:**
- §3.1 schema `revisions?` → Task 1. ✓
- §3.2 API client 3 methods + types → Task 1. ✓
- §3.3 i18n `revisions` ns + parity → Task 2. ✓
- §3.4 `revisionOperation` helper → Task 3; JSON pretty-print inline → Task 4. ✓
- §3.5 `RevisionSnapshotView` → Task 4; `RevisionHistoryDrawer` → Task 5. ✓
- §3.6 ItemFormView integration (button gating, drawer, `onReverted`/`setModel`) → Task 6. ✓
- §4 gating (`!isCreate && meta.revisions`; revert gated `canWrite`) → Tasks 5/6. ✓
- §4 revert overwrites unsaved edits + confirm message → Task 2 copy + Task 5 confirm. ✓
- §4 post-revert re-baseline → Task 6 (`setModel`) + test. ✓
- §4 empty history → Task 5 empty state + test. ✓
- §5 error handling (list/detail/revert) → Task 5 states + tests. ✓
- §6 tests + gates + live smoke → Tasks 1–7. ✓

**Placeholder scan:** No TBD/TODO. Every code step shows full code. The only deliberate two-phase step is Task 5 Step 3→4 (write-then-simplify the catch block), fully specified. ✓

**Type consistency:** `RevisionInfo`/`RevisionDetail` defined in Task 1 and consumed by name in Tasks 4/5. `listRevisions`/`getRevision`/`revert` signatures identical across Tasks 1, 5. `revisionOperationKey` name consistent Tasks 3/4/5. `onReverted`/`showHistory`/`idStr` consistent within Task 6. Emits `reverted`/`update:visible` consistent Tasks 5/6. The view does not import `useToast` (the drawer owns the success toast) — no unused variable. ✓
