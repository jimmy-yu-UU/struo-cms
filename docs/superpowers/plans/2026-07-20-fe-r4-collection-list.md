# FE-R4 Collection List — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-skin `CollectionListView.vue` to the FE-R0 design language (page-head + toolbar + DataTable + table-footer) by extracting three reusable `components/common/` presentational components, preserving every existing behaviour of the current view.

**Architecture:** Pure frontend. Three new stateless presentational components (`PageHeader`, `ListToolbar`, `TableFooter`) with well-defined prop/slot interfaces, plus a rebuilt `CollectionListView` that assembles them while carrying its existing script logic over verbatim (lazy load / latest-wins / debounced search / sort / 9b-fe soft delete / RBAC gates). A new `collectionList` i18n namespace extracts hardcoded strings.

**Tech Stack:** Vue 3 `<script setup lang="ts">`, PrimeVue (Aura preset), vue-i18n (legacy:false), Vitest + @vue/test-utils, vue-tsc.

## Global Constraints

- **Pure frontend** — no backend / API / route / persistence change; no `dotnet` gate, no live PG gate.
- **No new dependencies** — reuse existing PrimeVue components + primeicons; no `pnpm add`.
- **Honest data** — do NOT add a publish-status column/tag or locale-coverage column (StruoCMS has neither). Table columns stay schema-driven via existing `selectListColumns(meta)`.
- **Preserve all §2 behaviour** of the spec — lazy pagination, server sort, 300ms trailing debounced search (cancel on unmount + collection switch), latest-wins guard, RBAC gates (`canRead`/`canWrite`/`canDelete`), 9b-fe soft delete (Active/Trash `SelectButton` gated `softDelete && canDelete`; active→Delete, trash→Restore + Delete permanently), `schema.load()` retry, `watch(name)` reset, translatable cell resolution, row-click-to-edit (active only), error + empty states.
- **Gate** — both `pnpm build` (vue-tsc) and `pnpm test` (vitest) green. Run from `frontend/`.
- **Style** — component-scoped styles only; layout via FE-R0 OKLch tokens (`--fg`, `--muted`, `--border`, `--surface`, `--radius`, spacing); PrimeVue controls keep the Aura skin; light/dark flip in lockstep.
- **i18n** — new `collectionList` namespace added to BOTH `src/locales/zh-TW.ts` (default) and `src/locales/en.ts`; keys must be identical in both (enforced by `src/locales/locales.test.ts`).
- Spec: `docs/superpowers/specs/2026-07-20-fe-r4-collection-list-design.md`.

---

### Task 1: `collectionList` i18n namespace

**Files:**
- Modify: `frontend/src/locales/zh-TW.ts` (add `collectionList` block before closing `}`)
- Modify: `frontend/src/locales/en.ts` (add matching `collectionList` block)
- Test: `frontend/src/locales/locales.test.ts` (existing parity test — no edit, must stay green)

**Interfaces:**
- Consumes: nothing.
- Produces: i18n keys `collectionList.{count,new,searchPlaceholder,range,active,trash,delete,restore,purge,empty,notFound,noAccess}`. `count` takes `{n}`; `range` takes `{from,to,total}`. Consumed by Task 2 (`range`) and Task 5 (all).

- [ ] **Step 1: Add the zh-TW namespace**

In `frontend/src/locales/zh-TW.ts`, add this block as the last entry of the default-exported object (after the `dashboard` block, before the final `}`):

```typescript
  collectionList: {
    count: '共 {n} 筆',
    new: '新增',
    searchPlaceholder: '搜尋…',
    range: '顯示 {from}–{to} / 共 {total} 筆',
    active: '使用中',
    trash: '回收桶',
    delete: '刪除',
    restore: '還原',
    purge: '永久刪除',
    empty: '沒有資料',
    notFound: '找不到集合',
    noAccess: '您沒有此集合的存取權',
  },
```

- [ ] **Step 2: Add the matching en namespace**

In `frontend/src/locales/en.ts`, add this block in the same position:

```typescript
  collectionList: {
    count: '{n} items',
    new: 'New',
    searchPlaceholder: 'Search…',
    range: 'Showing {from}–{to} of {total}',
    active: 'Active',
    trash: 'Trash',
    delete: 'Delete',
    restore: 'Restore',
    purge: 'Delete permanently',
    empty: 'No records',
    notFound: 'Collection not found',
    noAccess: "You don't have access to this collection",
  },
```

- [ ] **Step 3: Run the locale parity + typecheck to verify**

Run (from `frontend/`): `pnpm test -- locales.test.ts`
Expected: PASS (zh-TW and en have identical key sets).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts
git commit -m "feat(frontend): collectionList i18n namespace zh-TW + en (FE-R4)"
```

---

### Task 2: `TableFooter.vue`

**Files:**
- Create: `frontend/src/components/common/TableFooter.vue`
- Test: `frontend/src/components/common/TableFooter.test.ts`

**Interfaces:**
- Consumes: i18n key `collectionList.range` (Task 1).
- Produces: `<TableFooter :first="number" :rows="number" :total="number" />` — renders `collectionList.range` with `{ from, to, total }` where `from = total === 0 ? 0 : first + 1`, `to = Math.min(first + rows, total)`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/common/TableFooter.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import TableFooter from './TableFooter.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { collectionList: { range: 'Showing {from}–{to} of {total}' } } },
})

function mountTF(props: { first: number; rows: number; total: number }) {
  return mount(TableFooter, { props, global: { plugins: [i18n] } })
}

describe('TableFooter', () => {
  it('renders the 1-based range for a full first page', () => {
    expect(mountTF({ first: 0, rows: 25, total: 128 }).text()).toBe('Showing 1–25 of 128')
  })
  it('clamps `to` to total on a partial last page', () => {
    expect(mountTF({ first: 100, rows: 25, total: 128 }).text()).toBe('Showing 101–128 of 128')
  })
  it('renders 0–0 of 0 when there are no records', () => {
    expect(mountTF({ first: 0, rows: 25, total: 0 }).text()).toBe('Showing 0–0 of 0')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `frontend/`): `pnpm test -- TableFooter.test.ts`
Expected: FAIL — cannot resolve `./TableFooter.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/common/TableFooter.vue`:

```vue
<!-- frontend/src/components/common/TableFooter.vue -->
<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ first: number; rows: number; total: number }>()

const from = computed(() => (props.total === 0 ? 0 : props.first + 1))
const to = computed(() => Math.min(props.first + props.rows, props.total))
</script>

<template>
  <span class="table-footer caption">
    {{ $t('collectionList.range', { from, to, total }) }}
  </span>
</template>

<style scoped>
.table-footer {
  color: var(--muted);
  font-size: 0.875rem;
}
</style>
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `frontend/`): `pnpm test -- TableFooter.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/common/TableFooter.vue frontend/src/components/common/TableFooter.test.ts
git commit -m "feat(frontend): TableFooter common component (FE-R4)"
```

---

### Task 3: `PageHeader.vue`

**Files:**
- Create: `frontend/src/components/common/PageHeader.vue`
- Test: `frontend/src/components/common/PageHeader.test.ts`

**Interfaces:**
- Consumes: nothing (no i18n; caller passes resolved strings).
- Produces: `<PageHeader :title="string" :caption="string?"><template #actions>…</template></PageHeader>` — renders `.page-head` > (`.titles` > `<h1>` + optional `<p class="caption">`) + `.head-actions` slot. No caption element when `caption` is falsy.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/common/PageHeader.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PageHeader from './PageHeader.vue'

describe('PageHeader', () => {
  it('renders the title in an h1', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' } })
    expect(w.get('h1').text()).toBe('Articles')
  })
  it('renders the caption when provided', () => {
    const w = mount(PageHeader, { props: { title: 'Articles', caption: '128 items' } })
    expect(w.get('.caption').text()).toBe('128 items')
  })
  it('omits the caption element when caption is absent', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' } })
    expect(w.find('.caption').exists()).toBe(false)
  })
  it('renders the actions slot', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' }, slots: { actions: '<button>New</button>' } })
    expect(w.get('.head-actions').text()).toBe('New')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `frontend/`): `pnpm test -- PageHeader.test.ts`
Expected: FAIL — cannot resolve `./PageHeader.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/common/PageHeader.vue`:

```vue
<!-- frontend/src/components/common/PageHeader.vue -->
<script setup lang="ts">
defineProps<{ title: string; caption?: string }>()
</script>

<template>
  <header class="page-head">
    <div class="titles">
      <h1>{{ title }}</h1>
      <p v-if="caption" class="caption">{{ caption }}</p>
    </div>
    <div class="head-actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.page-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  margin-bottom: 20px;
}
.titles { display: grid; gap: 4px; }
.titles h1 { margin: 0; font-size: 1.5rem; color: var(--fg); }
.caption { margin: 0; color: var(--muted); font-size: 0.875rem; }
.head-actions { display: flex; gap: 10px; align-items: center; }
</style>
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `frontend/`): `pnpm test -- PageHeader.test.ts`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/common/PageHeader.vue frontend/src/components/common/PageHeader.test.ts
git commit -m "feat(frontend): PageHeader common component (FE-R4)"
```

---

### Task 4: `ListToolbar.vue`

**Files:**
- Create: `frontend/src/components/common/ListToolbar.vue`
- Test: `frontend/src/components/common/ListToolbar.test.ts`

**Interfaces:**
- Consumes: PrimeVue `InputText`.
- Produces: `<ListToolbar :search-value="string" :search-placeholder="string?" @search="(v: string) => …"><template #filters>…</template></ListToolbar>` — renders `.toolbar` with a search-icon-wrapped `InputText` (accessible label) + a `#filters` slot. Emits `search` with the raw input string on every `input` event. Debounce stays the consumer's responsibility.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/common/ListToolbar.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ListToolbar from './ListToolbar.vue'

function mountLT(props: Record<string, unknown> = {}, slots: Record<string, string> = {}) {
  return mount(ListToolbar, {
    props: { searchValue: '', searchPlaceholder: 'Search…', ...props },
    slots,
    global: { plugins: [PrimeVue] },
  })
}

describe('ListToolbar', () => {
  it('emits `search` with the raw value on input', async () => {
    const w = mountLT()
    const input = w.get('input')
    await input.setValue('hello')
    expect(w.emitted('search')?.at(-1)).toEqual(['hello'])
  })
  it('renders the search icon', () => {
    expect(mountLT().find('.pi-search').exists()).toBe(true)
  })
  it('applies the placeholder to the input', () => {
    expect(mountLT({ searchPlaceholder: 'Find…' }).get('input').attributes('placeholder')).toBe('Find…')
  })
  it('renders the filters slot', () => {
    const w = mountLT({}, { filters: '<div class="marker">F</div>' })
    expect(w.find('.marker').exists()).toBe(true)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `frontend/`): `pnpm test -- ListToolbar.test.ts`
Expected: FAIL — cannot resolve `./ListToolbar.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/common/ListToolbar.vue`:

```vue
<!-- frontend/src/components/common/ListToolbar.vue -->
<script setup lang="ts">
import InputText from 'primevue/inputtext'

defineProps<{ searchValue: string; searchPlaceholder?: string }>()
const emit = defineEmits<{ search: [value: string] }>()

function onInput(e: Event): void {
  emit('search', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="toolbar">
    <span class="iwrap">
      <i class="pi pi-search" aria-hidden="true" />
      <InputText
        type="search"
        :model-value="searchValue"
        :placeholder="searchPlaceholder"
        :aria-label="searchPlaceholder"
        @input="onInput"
      />
    </span>
    <div class="filters">
      <slot name="filters" />
    </div>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
  padding: 0 0 14px;
}
.iwrap {
  position: relative;
  flex: 1 1 220px;
  max-width: 320px;
  display: flex;
  align-items: center;
}
.iwrap .pi-search {
  position: absolute;
  left: 12px;
  color: var(--muted);
  pointer-events: none;
}
.iwrap :deep(input) {
  width: 100%;
  padding-left: 34px;
}
.filters { display: flex; gap: 10px; align-items: center; margin-left: auto; }
</style>
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `frontend/`): `pnpm test -- ListToolbar.test.ts`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/common/ListToolbar.vue frontend/src/components/common/ListToolbar.test.ts
git commit -m "feat(frontend): ListToolbar common component (FE-R4)"
```

---

### Task 5: Rebuild `CollectionListView.vue` + rewrite test + full gate

**Files:**
- Modify (rebuild template + i18n strings): `frontend/src/views/CollectionListView.vue`
- Modify (rewrite): `frontend/src/views/CollectionListView.test.ts`

**Interfaces:**
- Consumes: `PageHeader` (Task 3), `ListToolbar` (Task 4), `TableFooter` (Task 2), `collectionList` i18n (Task 1); existing `itemsApi`, `selectListColumns`, `formatCell`, `deleteAction` helpers, `createLatestWins`, `debounce`, stores.
- Produces: the rebuilt view; `defineExpose` surface is unchanged from the current view.

The **script block logic is carried over from the current view verbatim** — do NOT rewrite `loadItems`, `listLoad`, `onPage`, `onSort`, `debouncedSearch`, `onSearchInput`, `onRowClick`, `onNew`, `setMode`, `onDelete`, `onPurge`, `onRestore`, `runAction`, `watch(name)`, `onMounted`/`onUnmounted`, `cellValue`, `fieldOf`, `defineExpose`. Only: (a) add `useI18n` + component imports, (b) replace the `modeOptions` literals with i18n, (c) replace the `<template>`.

- [ ] **Step 1: Adapt the existing test harness for i18n (minimal, targeted edit)**

The existing `CollectionListView.test.ts` (23 cases) is **almost entirely `wrapper.vm.*`-based** — it calls exposed methods (`onSort`, `setMode`, `loadItems`, `onDelete`, `onRestore`, `onSearchInput`, …) and reads exposed refs (`rows`, `loading`, `error`, `showTrashSwitch`). Those assertions do **not** touch the template and stay **unchanged**. The ONE structural change: once the view calls `useI18n()`, mounting without an i18n plugin throws — so every `mount(CollectionListView)` must receive an i18n plugin. Make exactly these edits:

(1) Add i18n imports + instance + a mount helper near the top of the file (after the existing mocks):

```typescript
import { createI18n } from 'vue-i18n'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: {
    en: {
      collectionList: {
        count: '{n} items', new: 'New', searchPlaceholder: 'Search…',
        range: 'Showing {from}–{to} of {total}', active: 'Active', trash: 'Trash',
        delete: 'Delete', restore: 'Restore', purge: 'Delete permanently',
        empty: 'No records', notFound: 'Collection not found',
        noAccess: "You don't have access to this collection",
      },
    },
  },
})

function mountView() {
  return mount(CollectionListView, { global: { plugins: [i18n] } })
}
```

(2) Replace every `mount(CollectionListView)` call in the file with `mountView()` (all 20+ call sites — plain find/replace; no argument was passed before, so no options are lost).

(3) The two DOM-text assertions keep passing because the en values match the previously-hardcoded strings:
   - `expect(wrapper.text()).not.toContain('Collection not found')` (line ~104) — `notFound` = `'Collection not found'`. ✓
   - `expect(wrapper.text()).not.toContain("don't have access")` / `.toContain("don't have access")` (lines ~105, ~140) — `noAccess` contains `"don't have access"`. ✓

(4) Leave untouched: the `deleteConfirm`/`purgeConfirm` copy assertions (`'restore'`, `'cannot be undone'`, `'Permanently'`) — that copy comes from `lib/deleteAction.ts`, **not** the view's i18n, and is out of FE-R4 scope.

(5) Add ONE new case asserting the new markup renders (the only genuinely new template surface worth a DOM assertion):

```typescript
it('renders the PageHeader title and count caption', async () => {
  seedSchema(); seedLanguage()
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
  const w = mountView()
  await flushPromises()
  expect(w.get('h1').text()).toBe('Article')
  expect(w.text()).toContain('1 items') // collectionList.count with n=total
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `frontend/`): `pnpm test -- CollectionListView.test.ts`
Expected: FAIL on the new `renders the PageHeader title and count caption` case — the old template renders `<h2>{{ meta.label }}</h2>` and a bare header, so `w.get('h1')` throws / `'1 items'` count caption is absent. (The 23 carried-over `vm.*` cases still pass — they don't depend on the template.)

- [ ] **Step 3: Rebuild the view**

In `frontend/src/views/CollectionListView.vue`:

(a) Add imports + i18n to the existing `<script setup>` (keep everything else):

```typescript
import { useI18n } from 'vue-i18n'
import PageHeader from '../components/common/PageHeader.vue'
import ListToolbar from '../components/common/ListToolbar.vue'
import TableFooter from '../components/common/TableFooter.vue'
// ...existing imports stay...
const { t } = useI18n()
```

(b) Replace the `modeOptions` literal with i18n labels:

```typescript
const modeOptions = computed(() => [
  { label: t('collectionList.active'), value: 'active' as const },
  { label: t('collectionList.trash'), value: 'trash' as const },
])
```

(c) Replace the entire `<template>` with the assembled version (script logic unchanged):

```vue
<template>
  <section class="collection-list">
    <ConfirmDialog />
    <template v-if="!meta">
      <p class="notice">{{ t('collectionList.notFound') }}</p>
    </template>
    <template v-else-if="!canRead">
      <p class="notice">{{ t('collectionList.noAccess') }}</p>
    </template>
    <template v-else>
      <PageHeader :title="meta.label" :caption="t('collectionList.count', { n: total })">
        <template #actions>
          <Button v-if="canWrite" :label="t('collectionList.new')" icon="pi pi-plus" @click="onNew" />
        </template>
      </PageHeader>

      <ListToolbar
        :search-value="search"
        :search-placeholder="t('collectionList.searchPlaceholder')"
        @search="onSearchInput"
      >
        <template #filters>
          <SelectButton
            v-if="showTrashSwitch"
            :model-value="mode"
            :options="modeOptions"
            option-label="label"
            option-value="value"
            :allow-empty="false"
            @update:model-value="setMode($event)"
          />
        </template>
      </ListToolbar>

      <p v-if="error" class="error" role="alert">{{ error }}</p>

      <DataTable
        :value="rows"
        lazy
        paginator
        :rows="perPage"
        :total-records="total"
        :loading="loading"
        @page="onPage"
        @sort="onSort"
        @row-click="onRowClick"
      >
        <Column
          v-for="col in columns"
          :key="col.field"
          :field="col.field"
          :header="col.header"
          :sortable="col.sortable"
        >
          <template #body="{ data }">
            {{ formatCell(cellValue(data, fieldOf(col.field)!), fieldOf(col.field)!) }}
          </template>
        </Column>
        <Column v-if="canDelete" header="" :style="{ width: '12rem' }">
          <template #body="{ data }">
            <template v-if="mode === 'active'">
              <Button :label="t('collectionList.delete')" severity="danger" text size="small" @click.stop="onDelete(data)" />
            </template>
            <template v-else>
              <Button :label="t('collectionList.restore')" text size="small" @click.stop="onRestore(data)" />
              <Button :label="t('collectionList.purge')" severity="danger" text size="small" @click.stop="onPurge(data)" />
            </template>
          </template>
        </Column>
        <template #empty>{{ t('collectionList.empty') }}</template>
        <template #paginatorstart>
          <TableFooter :first="page * perPage" :rows="perPage" :total="total" />
        </template>
      </DataTable>
    </template>
  </section>
</template>
```

(d) Keep the existing `<style scoped>` for `.notice` / `.error`; the layout classes now live in the child components. Remove any now-unused `.list-header` styles.

- [ ] **Step 4: Run the view test to verify it passes**

Run (from `frontend/`): `pnpm test -- CollectionListView.test.ts`
Expected: PASS.

- [ ] **Step 5: Full gate — whole suite + typecheck build**

Run (from `frontend/`):
```bash
pnpm test
pnpm build
```
Expected: `pnpm test` all green (new component specs + rewritten view + untouched suites); `pnpm build` (vue-tsc) succeeds with no type errors. Fix any `.vue` prop/emit type mismatches surfaced only by vue-tsc (vitest strips types — build is the real type gate).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/views/CollectionListView.vue frontend/src/views/CollectionListView.test.ts
git commit -m "feat(frontend): rebuild CollectionListView on design system (FE-R4)"
```

---

## Notes for the executor

- **Do not regress the current view's logic.** Task 5 is a template + i18n swap over carried-over script. If a preserved behaviour test is hard to satisfy, the template is wrong — not the logic.
- **`#paginatorstart` fallback** (spec §4, risk): if PrimeVue places the range text awkwardly relative to the pager, render `TableFooter` in a footer row directly under the `<DataTable>` while keeping the built-in `paginator` for page control. Either way, the built-in lazy paginator stays.
- **Search debounce lives in the view**, not `ListToolbar`. The toolbar emits raw input; `onSearchInput` (unchanged) owns the 300ms debounce and cancel-on-switch.
- **primeicons** already installed — `pi pi-search` / `pi pi-plus` need no new dependency.
- Live smoke (spec §9) is recommended after merge but is **not** a hard gate for this pure-frontend slice.
