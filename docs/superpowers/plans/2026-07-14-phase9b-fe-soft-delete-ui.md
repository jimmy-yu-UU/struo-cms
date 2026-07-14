# Phase 9b-fe — Soft delete admin UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Vue admin UI (Active/Trash switch, inline delete/restore/purge) on top of the already-shipped Phase 9b soft-delete REST API.

**Architecture:** Frontend-only. A pure helper (`deleteAction.ts`) centralizes the soft-vs-hard decision and confirm copy; `buildListQuery`/`itemsApi` gain a `deleted` mode + `purge`/`restore`; `CollectionListView` gets a mode switch and an actions column; `ItemFormView`'s delete becomes soft-delete-aware. No backend/Domain/Application/Infrastructure/DB change — the `/api/schema` endpoint already emits `softDelete`.

**Tech Stack:** Vue 3 (`<script setup>`, Composition API), TypeScript, PrimeVue (DataTable/Column/Button/SelectButton/ConfirmDialog), Pinia, Vitest + @vue/test-utils, Playwright (live E2E).

## Global Constraints

- **Frontend-only** — no change under `src/Struo.*`, `tests/Struo.Tests`, `db/`, or any backend file.
- **No new npm dependencies** — SelectButton/ConfirmDialog ship with the installed `primevue`.
- **Immutability** — never mutate props/rows in place; build new objects (project `coding-style.md`).
- **Outbound JSON is camelCase**; `softDelete` is the schema field name.
- **Deleted modes** are exactly the strings `'exclude' | 'only' | 'with'`; the server default is `exclude` and MUST be omitted from the query string.
- **Confirm copy (verbatim):** soft → header `Move to trash`, message `Move this item to trash? You can restore it later.`; hard → header `Confirm delete`, message `Delete this item? This cannot be undone.`; purge → header `Delete permanently`, message `Permanently delete this item? This cannot be undone.` (The hard strings are byte-identical to today's `ItemFormView` copy so its existing test stays green.)
- **Test commands** run from `frontend/`: unit `pnpm test`, types `pnpm vue-tsc`, build `pnpm build`, E2E `pnpm exec playwright test`.
- **Baselines to preserve:** `pnpm test` currently **237** passing; backend untouched at **599**.

---

### Task 1: Delete-semantics helper (`deleteAction.ts`)

**Files:**
- Create: `frontend/src/lib/deleteAction.ts`
- Test: `frontend/src/lib/deleteAction.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `type DeleteKind = 'soft' | 'hard'`
  - `deleteKindFor(meta: { softDelete?: boolean } | null | undefined): DeleteKind`
  - `deleteConfirm(kind: DeleteKind): { header: string; message: string }`
  - `purgeConfirm(): { header: string; message: string }`

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/lib/deleteAction.test.ts
import { describe, it, expect } from 'vitest'
import { deleteKindFor, deleteConfirm, purgeConfirm } from './deleteAction'

describe('deleteKindFor', () => {
  it('returns soft when the collection opts into soft delete', () => {
    expect(deleteKindFor({ softDelete: true })).toBe('soft')
  })
  it('returns hard when softDelete is false, missing, or meta is nullish', () => {
    expect(deleteKindFor({ softDelete: false })).toBe('hard')
    expect(deleteKindFor({})).toBe('hard')
    expect(deleteKindFor(null)).toBe('hard')
    expect(deleteKindFor(undefined)).toBe('hard')
  })
})

describe('confirm copy', () => {
  it('soft delete confirm mentions restoring', () => {
    expect(deleteConfirm('soft')).toEqual({
      header: 'Move to trash',
      message: 'Move this item to trash? You can restore it later.',
    })
  })
  it('hard delete confirm matches the existing irreversible copy', () => {
    expect(deleteConfirm('hard')).toEqual({
      header: 'Confirm delete',
      message: 'Delete this item? This cannot be undone.',
    })
  })
  it('purge confirm is strongly worded', () => {
    expect(purgeConfirm()).toEqual({
      header: 'Delete permanently',
      message: 'Permanently delete this item? This cannot be undone.',
    })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test -- src/lib/deleteAction.test.ts`
Expected: FAIL — "Failed to resolve import './deleteAction'".

- [ ] **Step 3: Write minimal implementation**

```ts
// frontend/src/lib/deleteAction.ts
export type DeleteKind = 'soft' | 'hard'

export function deleteKindFor(meta: { softDelete?: boolean } | null | undefined): DeleteKind {
  return meta?.softDelete ? 'soft' : 'hard'
}

export function deleteConfirm(kind: DeleteKind): { header: string; message: string } {
  return kind === 'soft'
    ? { header: 'Move to trash', message: 'Move this item to trash? You can restore it later.' }
    : { header: 'Confirm delete', message: 'Delete this item? This cannot be undone.' }
}

export function purgeConfirm(): { header: string; message: string } {
  return { header: 'Delete permanently', message: 'Permanently delete this item? This cannot be undone.' }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test -- src/lib/deleteAction.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/deleteAction.ts frontend/src/lib/deleteAction.test.ts
git commit -m "feat(9b-fe): delete-semantics helper (soft vs hard + confirm copy)"
```

---

### Task 2: `buildListQuery` gains a `deleted` mode

**Files:**
- Modify: `frontend/src/lib/buildListQuery.ts`
- Test: `frontend/src/lib/buildListQuery.test.ts`

**Interfaces:**
- Consumes: existing `buildListQuery(page, rows, sort?, search?, filter?, locale?)`.
- Produces: `buildListQuery(page, rows, sort?, search?, filter?, locale?, deleted?: 'exclude' | 'only' | 'with')` — appends `params.deleted` only when `deleted` is truthy and not `'exclude'`.

- [ ] **Step 1: Write the failing test** (append to the existing `buildListQuery filter + locale` file — add a new `describe`)

```ts
// append to frontend/src/lib/buildListQuery.test.ts
describe('buildListQuery deleted mode', () => {
  it('omits deleted for undefined or exclude (server default)', () => {
    expect(buildListQuery(0, 25).deleted).toBeUndefined()
    expect(buildListQuery(0, 25, undefined, undefined, undefined, undefined, 'exclude').deleted).toBeUndefined()
  })
  it('emits deleted for only and with', () => {
    expect(buildListQuery(0, 25, undefined, undefined, undefined, undefined, 'only').deleted).toBe('only')
    expect(buildListQuery(0, 25, undefined, undefined, undefined, undefined, 'with').deleted).toBe('with')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test -- src/lib/buildListQuery.test.ts`
Expected: FAIL — `'only'` cases return `undefined` (7th arg ignored; signature has no `deleted`).

- [ ] **Step 3: Write minimal implementation** (full file)

```ts
// frontend/src/lib/buildListQuery.ts
export type FilterSpec = Record<string, { op: string; value: string }>

export function buildListQuery(
  page: number,
  rows: number,
  sort?: string,
  search?: string,
  filter?: FilterSpec,
  locale?: string,
  deleted?: 'exclude' | 'only' | 'with',
): Record<string, string> {
  const params: Record<string, string> = {
    limit: String(rows),
    offset: String(page * rows),
  }
  if (sort) params.sort = sort
  if (search && search.trim() !== '') params.search = search
  if (filter) {
    for (const [field, { op, value }] of Object.entries(filter)) {
      params[`filter[${field}][${op}]`] = value
    }
  }
  if (locale) params.locale = locale
  if (deleted && deleted !== 'exclude') params.deleted = deleted
  return params
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test -- src/lib/buildListQuery.test.ts`
Expected: PASS (all, incl. the 2 new).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/buildListQuery.ts frontend/src/lib/buildListQuery.test.ts
git commit -m "feat(9b-fe): buildListQuery deleted mode (omit exclude, emit only/with)"
```

---

### Task 3: `itemsApi` — `deleted` on list, `purge` on remove, new `restore`

**Files:**
- Modify: `frontend/src/api/itemsApi.ts`
- Test: `frontend/src/api/itemsApi.test.ts`

**Interfaces:**
- Consumes: `buildListQuery(..., deleted?)` from Task 2; `apiClient.getRaw/get/post/delete`.
- Produces:
  - `type DeletedMode = 'exclude' | 'only' | 'with'`
  - `ListOptions` gains `deleted?: DeletedMode`.
  - `remove(collection, id, opts?: { purge?: boolean }): Promise<void>`
  - `restore(collection, id): Promise<Record<string, unknown>>`

- [ ] **Step 1: Write the failing test** (append two `describe`s to the existing file)

```ts
// append to frontend/src/api/itemsApi.test.ts
describe('itemsApi.list deleted mode', () => {
  beforeEach(() => vi.clearAllMocks())
  it('forwards deleted=only to the query string', async () => {
    ;(apiClient.getRaw as any).mockResolvedValue({ data: [], meta: { total: 0 } })
    await itemsApi.list('article', { page: 0, rows: 25, deleted: 'only' })
    expect(apiClient.getRaw).toHaveBeenCalledWith('/items/article?limit=25&offset=0&deleted=only')
  })
  it('omits deleted when exclude/undefined', async () => {
    ;(apiClient.getRaw as any).mockResolvedValue({ data: [], meta: { total: 0 } })
    await itemsApi.list('article', { page: 0, rows: 25, deleted: 'exclude' })
    expect(apiClient.getRaw).toHaveBeenCalledWith('/items/article?limit=25&offset=0')
  })
})

describe('itemsApi soft-delete ops', () => {
  beforeEach(() => vi.clearAllMocks())
  it('remove without opts deletes plainly', async () => {
    const spy = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined)
    await itemsApi.remove('article', '1')
    expect(spy).toHaveBeenCalledWith('/items/article/1')
  })
  it('remove with purge appends ?purge=true', async () => {
    const spy = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined)
    await itemsApi.remove('article', '1', { purge: true })
    expect(spy).toHaveBeenCalledWith('/items/article/1?purge=true')
  })
  it('restore posts the restore path and returns the row', async () => {
    const spy = vi.spyOn(apiClient, 'post').mockResolvedValue({ id: '1', status: 'draft' })
    const res = await itemsApi.restore('article', '1')
    expect(spy).toHaveBeenCalledWith('/items/article/1/restore')
    expect(res).toEqual({ id: '1', status: 'draft' })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test -- src/api/itemsApi.test.ts`
Expected: FAIL — `deleted=only` absent; `restore` is not a function; `remove` ignores `{ purge }`.

- [ ] **Step 3: Write minimal implementation** (full file)

```ts
// frontend/src/api/itemsApi.ts
import { apiClient } from './apiClient'
import { buildListQuery, type FilterSpec } from '../lib/buildListQuery'

export type DeletedMode = 'exclude' | 'only' | 'with'

export type ListOptions = {
  page: number
  rows: number
  sort?: string
  search?: string
  filter?: FilterSpec
  locale?: string
  deleted?: DeletedMode
}
export type ListResult = { data: Record<string, unknown>[]; total: number }

type ListEnvelope = { data: Record<string, unknown>[]; meta: { total: number } }

export const itemsApi = {
  async list(collection: string, opts: ListOptions): Promise<ListResult> {
    const params = buildListQuery(
      opts.page, opts.rows, opts.sort, opts.search, opts.filter, opts.locale, opts.deleted,
    )
    const qs = new URLSearchParams(params).toString()
    const path = qs ? `/items/${collection}?${qs}` : `/items/${collection}`
    const res = await apiClient.getRaw<ListEnvelope>(path)
    return { data: res.data, total: res.meta.total }
  },

  async get(
    collection: string,
    id: string,
    opts?: { locale?: string; deep?: string[] },
  ): Promise<Record<string, unknown>> {
    const params = new URLSearchParams()
    if (opts?.locale) params.set('locale', opts.locale)
    if (opts?.deep && opts.deep.length) params.set('deep', opts.deep.join(','))
    const qs = params.toString()
    return apiClient.get<Record<string, unknown>>(`/items/${collection}/${id}${qs ? `?${qs}` : ''}`)
  },
  async create(collection: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(`/items/${collection}`, payload)
  },
  async update(collection: string, id: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.put<Record<string, unknown>>(`/items/${collection}/${id}`, payload)
  },
  async remove(collection: string, id: string, opts?: { purge?: boolean }): Promise<void> {
    const qs = opts?.purge ? '?purge=true' : ''
    await apiClient.delete<void>(`/items/${collection}/${id}${qs}`)
  },
  async restore(collection: string, id: string): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(`/items/${collection}/${id}/restore`)
  },
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test -- src/api/itemsApi.test.ts`
Expected: PASS (existing + 5 new). The existing `remove deletes by id` test still passes (no opts → no query string).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/itemsApi.ts frontend/src/api/itemsApi.test.ts
git commit -m "feat(9b-fe): itemsApi deleted mode + purge + restore"
```

---

### Task 4: `CollectionListView` — Active/Trash mode + switch + trash load

**Files:**
- Modify: `frontend/src/types/schema.ts` (add `softDelete?: boolean` to `CollectionMeta`)
- Modify: `frontend/src/views/CollectionListView.vue`
- Test: `frontend/src/views/CollectionListView.test.ts`

**Interfaces:**
- Consumes: `itemsApi.list({ deleted })` (Task 3); `auth.canDelete` (existing store getter).
- Produces (exposed via `defineExpose` for tests): `mode: Ref<'active'|'trash'>`, `setMode(m: 'active'|'trash'): void`, `showTrashSwitch: ComputedRef<boolean>`, `canDelete: ComputedRef<boolean>`.

- [ ] **Step 1: Add `softDelete` to the schema type**

```ts
// frontend/src/types/schema.ts — inside `export type CollectionMeta = { ... }`, add after defaultDisplayField:
  softDelete?: boolean // Phase 9b: true when the collection's entity implements ISoftDeletable
```

- [ ] **Step 2: Write the failing tests** (add to `CollectionListView.test.ts`; also extend the `itemsApi` mock and add PrimeVue + confirm mocks at the top of the file)

Update the existing mock line and add mocks + a soft-schema seeder near the top of the file:

```ts
// REPLACE the existing: vi.mock('../api/itemsApi', () => ({ itemsApi: { list: vi.fn() } }))
vi.mock('../api/itemsApi', () => ({
  itemsApi: { list: vi.fn(), remove: vi.fn(), restore: vi.fn() },
}))
vi.mock('primevue/selectbutton', () => ({ default: { name: 'SelectButton', template: '<div />' } }))
vi.mock('primevue/confirmdialog', () => ({ default: { name: 'ConfirmDialog', template: '<div />' } }))
vi.mock('primevue/button', () => ({ default: { name: 'Button', template: '<button />' } }))
const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

function seedSoftSchema() {
  const schema = useSchemaStore()
  schema.collections = [{
    name: 'article', label: 'Article', defaultDisplayField: 'status', softDelete: true,
    fields: [{ name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
      sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
      options: [{ value: 'draft', label: 'Draft' }] }],
    relations: [],
  }]
}
```

Add these tests inside the `describe('CollectionListView', ...)` block (append; also clear `confirmRequire` in `beforeEach`: add `confirmRequire.mockClear()`):

```ts
it('shows the trash switch only for a soft-delete collection with delete permission', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
  const w = mount(CollectionListView)
  await flushPromises()
  expect((w.vm as any).showTrashSwitch).toBe(true)
})

it('hides the trash switch when the collection is not soft-deletable', async () => {
  seedSchema(); seedLanguage() // seedSchema's article has no softDelete
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
  const w = mount(CollectionListView)
  await flushPromises()
  expect((w.vm as any).showTrashSwitch).toBe(false)
})

it('hides the trash switch without delete permission', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: false,
    permissions: { article: { read: true, write: false, delete: false } } }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
  const w = mount(CollectionListView)
  await flushPromises()
  expect((w.vm as any).showTrashSwitch).toBe(false)
})

it('setMode(trash) resets page and reloads with deleted=only', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
  const w = mount(CollectionListView)
  await flushPromises()
  vi.mocked(itemsApi.list).mockClear()
  ;(w.vm as any).setMode('trash')
  await flushPromises()
  expect(itemsApi.list).toHaveBeenCalledWith('article',
    { page: 0, rows: 25, sort: undefined, search: undefined, locale: 'en', deleted: 'only' })
})

it('does not navigate on row click in trash mode', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
  const w = mount(CollectionListView)
  await flushPromises()
  ;(w.vm as any).setMode('trash')
  ;(w.vm as any).onRowClick({ data: { id: '42' } })
  expect(pushMock).not.toHaveBeenCalled()
})
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd frontend && pnpm test -- src/views/CollectionListView.test.ts`
Expected: FAIL — `showTrashSwitch`/`setMode` undefined; trash reload asserts `deleted: 'only'` not sent.

- [ ] **Step 4: Implement mode + switch + trash load in the view**

In `CollectionListView.vue` `<script setup>`: add imports and state, thread `deleted` into `loadItems`, guard `onRowClick`, and expose the new members.

```ts
// add to the imports block
import SelectButton from 'primevue/selectbutton'

// add after `const canWrite = ...`
const canDelete = computed(() => auth.canDelete(name.value))
const mode = ref<'active' | 'trash'>('active')
const showTrashSwitch = computed(() => !!meta.value?.softDelete && canDelete.value)
const modeOptions = [
  { label: 'Active', value: 'active' as const },
  { label: 'Trash', value: 'trash' as const },
]
```

In `loadItems`, add `deleted` to the `itemsApi.list` options object:

```ts
    const res = await itemsApi.list(name.value, {
      page: page.value,
      rows: perPage.value,
      sort,
      search: search.value || undefined,
      locale: langStore.defaultCode || undefined,
      deleted: mode.value === 'trash' ? 'only' : undefined,
    })
```

Add `setMode` and guard `onRowClick`:

```ts
function setMode(m: 'active' | 'trash'): void {
  mode.value = m
  page.value = 0
  loadItems()
}
```

```ts
function onRowClick(e: { data: Record<string, unknown> }): void {
  if (mode.value === 'trash') return
  const rid = e.data.id
  if (rid != null) router.push({ name: 'collection-item', params: { name: name.value, id: String(rid) } })
}
```

In the `watch(name, ...)` reset block, also reset the mode:

```ts
watch(name, () => {
  page.value = 0
  sortField.value = undefined
  sortOrder.value = undefined
  search.value = ''
  mode.value = 'active'
  loadItems()
})
```

Extend `defineExpose` with the new members:

```ts
defineExpose({ loadItems, onPage, onSort, onSearchInput, onRowClick, onNew, canWrite, canDelete,
  mode, setMode, showTrashSwitch, rows, total, loading, error, cellValue })
```

In the `<template>`, render the switch in the header (after the `<h2>`), binding `SelectButton` to `mode` via `setMode`:

```html
        <SelectButton
          v-if="showTrashSwitch"
          :model-value="mode"
          :options="modeOptions"
          option-label="label"
          option-value="value"
          :allow-empty="false"
          @update:model-value="setMode($event)"
        />
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd frontend && pnpm test -- src/views/CollectionListView.test.ts`
Expected: PASS (existing + 5 new). The existing mount tests still pass — `deleted: undefined` is ignored by `toEqual`, and the switch does not render for `seedSchema` (no `softDelete`).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/types/schema.ts frontend/src/views/CollectionListView.vue frontend/src/views/CollectionListView.test.ts
git commit -m "feat(9b-fe): CollectionListView Active/Trash mode + switch + trash load"
```

---

### Task 5: `CollectionListView` — actions column (delete / restore / purge)

**Files:**
- Modify: `frontend/src/views/CollectionListView.vue`
- Test: `frontend/src/views/CollectionListView.test.ts`

**Interfaces:**
- Consumes: `deleteKindFor`/`deleteConfirm`/`purgeConfirm` (Task 1); `itemsApi.remove`/`restore` (Task 3); `useConfirm` (registered in `main.ts`).
- Produces (exposed): `onDelete(row): void`, `onRestore(row): Promise<void>`, `onPurge(row): void`.

- [ ] **Step 1: Write the failing tests** (append inside the `describe('CollectionListView', ...)` block)

```ts
it('active delete on a soft-delete collection soft-deletes (no purge) and reloads', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
  vi.mocked(itemsApi.remove).mockResolvedValue(undefined)
  const w = mount(CollectionListView)
  await flushPromises()
  ;(w.vm as any).onDelete({ id: '1' })
  const arg = confirmRequire.mock.calls[0][0]
  expect(arg.message).toContain('restore')
  vi.mocked(itemsApi.list).mockClear()
  await arg.accept()
  await flushPromises()
  expect(itemsApi.remove).toHaveBeenCalledWith('article', '1')
  expect(itemsApi.list).toHaveBeenCalled() // reloaded
})

it('active delete on a non-soft collection uses the irreversible confirm', async () => {
  seedSchema(); seedLanguage() // no softDelete
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1', status: 'draft' }], total: 1 })
  const w = mount(CollectionListView)
  await flushPromises()
  ;(w.vm as any).onDelete({ id: '1' })
  expect(confirmRequire.mock.calls[0][0].message).toContain('cannot be undone')
})

it('purge asks for a strong confirm then removes with purge=true', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
  vi.mocked(itemsApi.remove).mockResolvedValue(undefined)
  const w = mount(CollectionListView)
  await flushPromises()
  ;(w.vm as any).setMode('trash')
  await flushPromises()
  ;(w.vm as any).onPurge({ id: '1' })
  const arg = confirmRequire.mock.calls[0][0]
  expect(arg.message).toContain('Permanently')
  await arg.accept()
  expect(itemsApi.remove).toHaveBeenCalledWith('article', '1', { purge: true })
})

it('restore calls the API directly (no confirm) and reloads', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
  vi.mocked(itemsApi.restore).mockResolvedValue({ id: '1' })
  const w = mount(CollectionListView)
  await flushPromises()
  ;(w.vm as any).setMode('trash')
  await flushPromises()
  vi.mocked(itemsApi.list).mockClear()
  await (w.vm as any).onRestore({ id: '1' })
  await flushPromises()
  expect(confirmRequire).not.toHaveBeenCalled()
  expect(itemsApi.restore).toHaveBeenCalledWith('article', '1')
  expect(itemsApi.list).toHaveBeenCalled()
})

it('surfaces an inline error when a row action fails', async () => {
  seedSoftSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
  vi.mocked(itemsApi.restore).mockRejectedValue(new Error('Restore failed.'))
  const w = mount(CollectionListView)
  await flushPromises()
  ;(w.vm as any).setMode('trash')
  await flushPromises()
  await (w.vm as any).onRestore({ id: '1' })
  await flushPromises()
  expect((w.vm as any).error).toContain('Restore failed.')
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd frontend && pnpm test -- src/views/CollectionListView.test.ts`
Expected: FAIL — `onDelete`/`onRestore`/`onPurge` are not functions.

- [ ] **Step 3: Implement the action handlers + actions column**

In `<script setup>` add imports and handlers:

```ts
// add to imports
import Button from 'primevue/button'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { deleteKindFor, deleteConfirm, purgeConfirm } from '../lib/deleteAction'

// add near the other composables (after `const langStore = ...`)
const confirm = useConfirm()

// add with the other functions
function rowId(row: Record<string, unknown>): string {
  return String(row.id)
}

async function runAction(fn: () => Promise<void>): Promise<void> {
  error.value = ''
  try {
    await fn()
    await loadItems()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Action failed.'
  }
}

function onDelete(row: Record<string, unknown>): void {
  const kind = deleteKindFor(meta.value)
  const { header, message } = deleteConfirm(kind)
  confirm.require({ header, message, accept: () => runAction(() => itemsApi.remove(name.value, rowId(row))) })
}

function onPurge(row: Record<string, unknown>): void {
  const { header, message } = purgeConfirm()
  confirm.require({ header, message, accept: () => runAction(() => itemsApi.remove(name.value, rowId(row), { purge: true })) })
}

async function onRestore(row: Record<string, unknown>): Promise<void> {
  await runAction(async () => { await itemsApi.restore(name.value, rowId(row)) })
}
```

Extend `defineExpose` (add the three handlers):

```ts
defineExpose({ loadItems, onPage, onSort, onSearchInput, onRowClick, onNew, canWrite, canDelete,
  mode, setMode, showTrashSwitch, onDelete, onRestore, onPurge, rows, total, loading, error, cellValue })
```

Add `<ConfirmDialog />` at the top of the `<section>` (before `<template v-if="!meta">`), and an actions `<Column>` after the field columns loop inside `<DataTable>`. The buttons use `@click.stop` so a row-action click does not also trigger `onRowClick`:

```html
    <section class="collection-list">
      <ConfirmDialog />
      ...
```

```html
        <Column v-if="canDelete" header="" :style="{ width: '12rem' }">
          <template #body="{ data }">
            <template v-if="mode === 'active'">
              <Button label="Delete" severity="danger" text size="small" @click.stop="onDelete(data)" />
            </template>
            <template v-else>
              <Button label="Restore" text size="small" @click.stop="onRestore(data)" />
              <Button label="Delete permanently" severity="danger" text size="small" @click.stop="onPurge(data)" />
            </template>
          </template>
        </Column>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd frontend && pnpm test -- src/views/CollectionListView.test.ts`
Expected: PASS (existing + 5 new).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/views/CollectionListView.vue frontend/src/views/CollectionListView.test.ts
git commit -m "feat(9b-fe): CollectionListView actions column (soft-delete/restore/purge)"
```

---

### Task 6: `ItemFormView` — soft-delete-aware delete

**Files:**
- Modify: `frontend/src/views/ItemFormView.vue`
- Test: `frontend/src/views/ItemFormView.test.ts`

**Interfaces:**
- Consumes: `deleteKindFor`/`deleteConfirm` (Task 1); existing `itemsApi.remove`.
- Produces: no new exports; `onDelete` now branches on `meta.softDelete`.

- [ ] **Step 1: Write the failing tests** (append inside `describe('ItemFormView', ...)`; `confirmRequire` mock already exists in this file)

```ts
it('delete on a soft-delete collection uses the move-to-trash confirm', async () => {
  routeParams = { name: 'article', id: '5' }
  const { schema } = setupStores()
  ;(schema.get as any).mockReturnValue({ ...meta, softDelete: true })
  vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
  const w = mount(ItemFormView, { global: { stubs } })
  await w.vm.init()
  ;(w.vm as any).onDelete()
  expect(confirmRequire.mock.calls[0][0].message).toContain('restore')
})

it('delete on a non-soft collection keeps the irreversible confirm', async () => {
  routeParams = { name: 'article', id: '5' }
  setupStores() // meta has no softDelete
  vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
  const w = mount(ItemFormView, { global: { stubs } })
  await w.vm.init()
  ;(w.vm as any).onDelete()
  expect(confirmRequire.mock.calls[0][0].message).toContain('cannot be undone')
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd frontend && pnpm test -- src/views/ItemFormView.test.ts`
Expected: FAIL — the soft case asserts `restore` but the hard-coded message is `cannot be undone`.

- [ ] **Step 3: Implement soft-aware `onDelete`**

Add the import and rewrite `onDelete` in `ItemFormView.vue`:

```ts
// add to imports
import { deleteKindFor, deleteConfirm } from '../lib/deleteAction'
```

```ts
function onDelete(): void {
  const { header, message } = deleteConfirm(deleteKindFor(meta.value))
  confirm.require({
    header,
    message,
    accept: async () => {
      try {
        await itemsApi.remove(name.value, id.value!)
        router.push({ name: 'collection-list', params: { name: name.value } })
      } catch (e) {
        serverError.value = e instanceof Error ? e.message : 'Delete failed.'
      }
    },
  })
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd frontend && pnpm test -- src/views/ItemFormView.test.ts`
Expected: PASS (existing + 2 new). The existing delete test (meta without `softDelete`) still sees the identical `Delete this item? This cannot be undone.` copy.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts
git commit -m "feat(9b-fe): ItemFormView soft-delete-aware delete confirm"
```

---

### Task 7: Live Playwright E2E — delete → trash → restore → purge

**Files:**
- Create: `frontend/e2e/trash.spec.ts`

**Interfaces:**
- Consumes: the running SPA + backend on live Postgres; the `Article` collection (implements `ISoftDeletable`, requires an `en` Title/Body).

- [ ] **Step 1: Write the E2E spec** (mirrors `e2e/items.spec.ts` helpers; the actions column buttons carry visible labels `Delete`/`Restore`/`Delete permanently`; the mode switch renders `Active`/`Trash`; PrimeVue confirm accept button is `Yes`)

```ts
// frontend/e2e/trash.spec.ts
import { test, expect, type Page } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'
const STAMP = process.env.E2E_STAMP ?? 'e2e'

async function login(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

function labelMatch(label: string): RegExp {
  return new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\*?$`)
}
function translatableFieldByLabel(page: Page, label: string) {
  return page.locator('.field:visible', { has: page.getByText(labelMatch(label)) })
}
async function chooseStatus(page: Page, optionLabel: 'Draft' | 'Published'): Promise<void> {
  const field = page.locator('.field', { has: page.getByText(labelMatch('Status')) })
  await field.getByRole('combobox').click()
  await page.getByRole('option', { name: optionLabel }).click()
}
// A DataTable row scoped by its visible title cell.
function rowByTitle(page: Page, title: string) {
  return page.getByRole('row', { has: page.getByText(title, { exact: true }) })
}

test('soft-delete an article, see it in trash, restore, then purge', async ({ page }) => {
  await login(page)
  const title = `E2E Trash ${STAMP}`

  // Create.
  await page.goto('/collections/article')
  await page.getByRole('button', { name: 'New' }).click()
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  await translatableFieldByLabel(page, 'Body').locator('textarea').fill('E2E trash body.')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title, { exact: true })).toBeVisible()

  // Soft-delete from the Active list (inline action). Confirm = "Yes".
  await rowByTitle(page, title).getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('button', { name: 'Yes' }).click()
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)

  // Switch to Trash — the row is there.
  await page.getByText('Trash', { exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toBeVisible()

  // Restore — leaves the trash.
  await rowByTitle(page, title).getByRole('button', { name: 'Restore', exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)

  // Back to Active — it's live again.
  await page.getByText('Active', { exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toBeVisible()

  // Soft-delete again, then purge it from Trash.
  await rowByTitle(page, title).getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('button', { name: 'Yes' }).click()
  await page.getByText('Trash', { exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  await rowByTitle(page, title).getByRole('button', { name: 'Delete permanently', exact: true }).click()
  await page.getByRole('button', { name: 'Yes' }).click()
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)
})
```

- [ ] **Step 2: Start the backend on live Postgres and the SPA**

The API must run against real Postgres (same env as the 9b backend live gate). In one terminal from the repo root: `dotnet run --project src/Struo.Api --no-launch-profile` (uses `appsettings.Development.json` → live PG + Redis + MinIO). Playwright's `webServer` (see `playwright.config.ts`) starts the Vite dev server automatically.

- [ ] **Step 3: Run the E2E**

Run: `cd frontend && pnpm exec playwright test trash.spec.ts`
Expected: PASS (1 test). If credentials differ, pass `E2E_EMAIL`/`E2E_PASSWORD`/`E2E_STAMP` env vars.

- [ ] **Step 4: Commit**

```bash
git add frontend/e2e/trash.spec.ts
git commit -m "test(9b-fe): live E2E delete/trash/restore/purge loop"
```

---

### Task 8: Full-suite verification + roadmap update

**Files:**
- Modify: `docs/ROADMAP.md` (add the 9b-fe row + a verification baseline line)

- [ ] **Step 1: Run the full frontend gate**

Run (from `frontend/`):
```bash
pnpm test        # expect all green: 237 baseline + ~19 new
pnpm vue-tsc     # expect: clean (0 errors) — proves the softDelete type is consumed
pnpm build       # expect: succeeds (pre-existing >500 kB chunk advisory is OK)
```
Record the exact new total from `pnpm test` output.

- [ ] **Step 2: Update the roadmap**

Add a table row under the Phase 9 section of `docs/ROADMAP.md`:

```markdown
| 9b-fe | Soft delete admin UI (Vue Active/Trash switch + inline soft-delete/restore/purge; itemsApi deleted/purge/restore; soft-aware form delete) | ✅ done (live-verified: real PG — delete→trash→restore→purge E2E) | [spec](superpowers/specs/2026-07-14-phase9b-fe-soft-delete-ui-design.md) | [plan](superpowers/plans/2026-07-14-phase9b-fe-soft-delete-ui.md) |
```

And change the `9b-fe` status in the "Status at a glance" **Next up** bullet from planned to done, plus add a one-line verification baseline (frontend test count + live E2E pass), mirroring prior slices' wording.

- [ ] **Step 3: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs(9b-fe): roadmap — soft-delete admin UI done + live-verified"
```

---

## Self-Review

**Spec coverage:**
- §2 data/API layer → Tasks 1–3 (helper, buildListQuery, itemsApi). ✅
- §3 list view switch + trash load → Task 4; §3.1 actions + graded confirm → Tasks 1 + 5. ✅
- §4 form delete semantics → Task 6. ✅
- §5 testable helper → Task 1. ✅
- §6 permission gating + inline-error/reload feedback → Tasks 4 (`canDelete`/`showTrashSwitch`) + 5 (`runAction` error + reload). ✅
- §7 actions column for all collections (hard for non-soft) → Task 5 (`onDelete` via `deleteKindFor`) + test "non-soft uses irreversible confirm". ✅
- §8 test strategy → Vitest in each task + Task 7 E2E. ✅
- §9 acceptance gate → Task 8. ✅
- §10 out-of-scope (no backend, no trashed-row edit form) → honored: `onRowClick` returns early in trash mode (Task 4); no backend files touched.

**Placeholder scan:** No TBD/TODO; every code step shows full content. ✅

**Type consistency:** `DeletedMode`/`deleted` param identical across Tasks 2–4; `deleteKindFor`/`deleteConfirm`/`purgeConfirm` signatures identical in Tasks 1, 5, 6; `remove(collection, id, { purge })` and `restore(collection, id)` identical in Tasks 3, 5, 6; `setMode`/`showTrashSwitch`/`mode` exposed in Task 4 and used in Task 5 tests. ✅
