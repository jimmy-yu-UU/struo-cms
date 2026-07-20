# FE-R3 Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the placeholder `DashboardView.vue` into the prototype's four-widget dashboard (stat cards, recent updates, quick actions) populated with honest, real data sourced purely on the frontend.

**Architecture:** Pure-frontend fan-out over readable content collections via the existing `itemsApi` (`sort=-updatedAt`, `rows=8`) yields both per-collection totals and recent rows in one batch; media/user counts come from single `rows=1` queries gated by `canRead`. Pure functions carry all testable logic (`resolveItemTitle`, `aggregateRecentUpdates`, `buildQuickActions`, `resolveGreetingKey`, `contentCollections`); a thin composable (`useDashboardData`) orchestrates loading; three presentational components render. No backend, API, route, or persistence change.

**Tech Stack:** Vue 3 (`<script setup lang="ts">`), Pinia, PrimeVue, vue-i18n (`legacy:false`), vitest + @vue/test-utils.

## Global Constraints

- **No backend/API/route/persistence change.** Frontend only. No `dotnet` gate.
- **Design-reference rule:** prototype is a *visual* reference only — no code copied from it.
- **Immutability:** pure functions return new arrays/objects; never mutate inputs (§coding-style).
- **File size:** many small focused files; extract pure logic out of components.
- **i18n:** all user-facing strings via `useI18n()` `t(...)`; `zh-TW` + `en` **key sets must stay symmetric** (existing recursive key-parity test enforces this).
- **System collections (dashboard):** `SYSTEM_COLLECTIONS = ['file', 'user']` — excluded from content items total / collections count / recent updates; each gets its own stat card.
- **Greeting:** time-of-day only, **no name** (`/auth/me` exposes no name).
- **FE gate = both `pnpm test` AND `pnpm build`** (vue-tsc catches type errors that vitest's transpile skips — 9a-fe lesson). Existing baseline **399** tests must stay green.
- Test-run command for one file: `pnpm exec vitest run <path>`. Full suite: `pnpm test`. Type/build gate: `pnpm build` (run from `frontend/`).

---

### Task 1: `resolveItemTitle` pure helper + refactor `resolveDisplayLabel` to reuse it

**Files:**
- Create: `frontend/src/lib/resolveItemTitle.ts`
- Create: `frontend/src/lib/resolveItemTitle.test.ts`
- Modify: `frontend/src/lib/resolveDisplayLabel.ts` (reuse the extracted `readField` + title logic)

**Interfaces:**
- Produces:
  - `type TitleRow = Record<string, unknown> & { id?: unknown; translations?: Record<string, Record<string, unknown>> }`
  - `function readField(row: TitleRow, targetMeta: CollectionMeta, locale: string, fieldKey: string): unknown`
  - `function resolveItemTitle(row: TitleRow, targetMeta: CollectionMeta, locale: string): string`
- Consumes: `CollectionMeta` from `../types/schema`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/resolveItemTitle.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { resolveItemTitle } from './resolveItemTitle'
import type { CollectionMeta, FieldMeta } from '../types/schema'

function field(over: Partial<FieldMeta> = {}): FieldMeta {
  return { name: 'title', label: 'Title', interface: 'text', required: false, searchable: false,
    sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false, ...over }
}
function meta(over: Partial<CollectionMeta> = {}): CollectionMeta {
  return { name: 't', label: 'T', fields: [], relations: [], defaultDisplayField: null, ...over }
}

describe('resolveItemTitle', () => {
  it('reads a non-translatable defaultDisplayField', () => {
    const m = meta({ defaultDisplayField: 'title', fields: [field()] })
    expect(resolveItemTitle({ id: '1', title: 'Hello' }, m, 'en')).toBe('Hello')
  })
  it('reads a translatable defaultDisplayField from translations[locale]', () => {
    const m = meta({ defaultDisplayField: 'title', fields: [field({ translatable: true })] })
    const row = { id: '2', translations: { en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } } }
    expect(resolveItemTitle(row, m, 'zh-TW')).toBe('嗨')
  })
  it('falls back to id when no display field resolves', () => {
    expect(resolveItemTitle({ id: '3' }, meta(), 'en')).toBe('3')
  })
  it('falls back to empty string when there is no id', () => {
    expect(resolveItemTitle({}, meta(), 'en')).toBe('')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/lib/resolveItemTitle.test.ts`
Expected: FAIL — cannot find module `./resolveItemTitle`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/resolveItemTitle.ts`:

```ts
import type { CollectionMeta } from '../types/schema'

export type TitleRow = Record<string, unknown> & {
  id?: unknown
  translations?: Record<string, Record<string, unknown>>
}

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

export function readField(
  row: TitleRow,
  targetMeta: CollectionMeta,
  locale: string,
  fieldKey: string,
): unknown {
  const field = targetMeta.fields.find((f) => f.name.toLowerCase() === fieldKey.toLowerCase())
  const key = field?.name ?? camel(fieldKey)
  if (field?.translatable) {
    const t = row.translations?.[locale]
    return t?.[key]
  }
  return row[key]
}

export function resolveItemTitle(row: TitleRow, targetMeta: CollectionMeta, locale: string): string {
  if (targetMeta.defaultDisplayField) {
    const v = readField(row, targetMeta, locale, targetMeta.defaultDisplayField)
    if (v != null && v !== '') return String(v)
  }
  return row.id != null ? String(row.id) : ''
}
```

- [ ] **Step 4: Refactor `resolveDisplayLabel` to reuse the extracted helpers**

Replace the entire contents of `frontend/src/lib/resolveDisplayLabel.ts` with:

```ts
import type { CollectionMeta, RelationMeta } from '../types/schema'
import { readField, resolveItemTitle, type TitleRow } from './resolveItemTitle'

export function resolveDisplayLabel(
  row: TitleRow,
  relation: RelationMeta,
  targetMeta: CollectionMeta,
  locale: string,
): string {
  const template = relation.displayTemplate
  if (template) {
    const out = template.replace(/\{(\w+)\}/g, (_m, token: string) => {
      const v = readField(row, targetMeta, locale, token)
      return v == null || v === '' ? '' : String(v)
    })
    if (out.trim() !== '') return out
  }
  return resolveItemTitle(row, targetMeta, locale)
}
```

- [ ] **Step 5: Run both tests to verify they pass**

Run: `pnpm exec vitest run src/lib/resolveItemTitle.test.ts src/lib/resolveDisplayLabel.test.ts`
Expected: PASS (new file green; existing `resolveDisplayLabel` behaviour unchanged).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/resolveItemTitle.ts frontend/src/lib/resolveItemTitle.test.ts frontend/src/lib/resolveDisplayLabel.ts
git commit -m "feat(frontend): resolveItemTitle helper; resolveDisplayLabel reuses it (FE-R3)"
```

---

### Task 2: `dashboardCollections` — SYSTEM_COLLECTIONS + content filter

**Files:**
- Create: `frontend/src/lib/dashboardCollections.ts`
- Create: `frontend/src/lib/dashboardCollections.test.ts`

**Interfaces:**
- Produces:
  - `const SYSTEM_COLLECTIONS = ['file', 'user'] as const`
  - `function contentCollections(collections: CollectionMeta[], canRead: (name: string) => boolean): CollectionMeta[]`
- Consumes: `CollectionMeta` from `../types/schema`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/dashboardCollections.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { SYSTEM_COLLECTIONS, contentCollections } from './dashboardCollections'
import type { CollectionMeta } from '../types/schema'

const coll = (name: string): CollectionMeta => ({
  name, label: name, fields: [], relations: [], defaultDisplayField: null,
})

describe('dashboardCollections', () => {
  it('SYSTEM_COLLECTIONS is file + user', () => {
    expect([...SYSTEM_COLLECTIONS]).toEqual(['file', 'user'])
  })
  it('keeps readable non-system collections', () => {
    const all = [coll('article'), coll('category'), coll('file'), coll('user')]
    const out = contentCollections(all, () => true)
    expect(out.map((c) => c.name)).toEqual(['article', 'category'])
  })
  it('drops collections the user cannot read', () => {
    const all = [coll('article'), coll('secret')]
    const out = contentCollections(all, (n) => n === 'article')
    expect(out.map((c) => c.name)).toEqual(['article'])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/lib/dashboardCollections.test.ts`
Expected: FAIL — cannot find module `./dashboardCollections`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/dashboardCollections.ts`:

```ts
import type { CollectionMeta } from '../types/schema'

/**
 * Collections the dashboard treats as system: each gets its own stat card and is excluded from the
 * content items total / collections count / recent updates. Note this is stricter than `buildNav`,
 * which only hardcodes-excludes 'file' — the dashboard additionally excludes 'user' so the users card
 * does not double-count into the items total.
 */
export const SYSTEM_COLLECTIONS = ['file', 'user'] as const

export function contentCollections(
  collections: CollectionMeta[],
  canRead: (name: string) => boolean,
): CollectionMeta[] {
  const system: readonly string[] = SYSTEM_COLLECTIONS
  return collections.filter((c) => !system.includes(c.name) && canRead(c.name))
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/lib/dashboardCollections.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/dashboardCollections.ts frontend/src/lib/dashboardCollections.test.ts
git commit -m "feat(frontend): dashboard SYSTEM_COLLECTIONS + contentCollections filter (FE-R3)"
```

---

### Task 3: `aggregateRecentUpdates` pure helper

**Files:**
- Create: `frontend/src/lib/aggregateRecentUpdates.ts`
- Create: `frontend/src/lib/aggregateRecentUpdates.test.ts`

**Interfaces:**
- Produces:
  - `type RecentRow = { id: string; collection: string; collectionLabel: string; title: string; updatedAt: string | null }`
  - `function aggregateRecentUpdates(rows: RecentRow[], limit: number): RecentRow[]`
- Consumes: nothing external.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/aggregateRecentUpdates.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { aggregateRecentUpdates, type RecentRow } from './aggregateRecentUpdates'

const row = (id: string, updatedAt: string | null): RecentRow => ({
  id, collection: 'article', collectionLabel: 'Article', title: `T${id}`, updatedAt,
})

describe('aggregateRecentUpdates', () => {
  it('sorts by updatedAt descending and caps at limit', () => {
    const input = [row('a', '2026-07-10T00:00:00Z'), row('b', '2026-07-14T00:00:00Z'), row('c', '2026-07-12T00:00:00Z')]
    expect(aggregateRecentUpdates(input, 2).map((r) => r.id)).toEqual(['b', 'c'])
  })
  it('sorts rows with null/invalid updatedAt last, without throwing', () => {
    const input = [row('a', null), row('b', '2026-07-14T00:00:00Z'), row('c', 'not-a-date')]
    expect(aggregateRecentUpdates(input, 10).map((r) => r.id)).toEqual(['b', 'a', 'c'])
  })
  it('returns a new array and does not mutate the input', () => {
    const input = [row('a', '2026-07-10T00:00:00Z'), row('b', '2026-07-14T00:00:00Z')]
    const out = aggregateRecentUpdates(input, 10)
    expect(out).not.toBe(input)
    expect(input.map((r) => r.id)).toEqual(['a', 'b'])
  })
  it('handles empty input', () => {
    expect(aggregateRecentUpdates([], 8)).toEqual([])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/lib/aggregateRecentUpdates.test.ts`
Expected: FAIL — cannot find module.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/aggregateRecentUpdates.ts`:

```ts
export type RecentRow = {
  id: string
  collection: string
  collectionLabel: string
  title: string
  updatedAt: string | null
}

function timestamp(iso: string | null): number {
  if (!iso) return Number.NEGATIVE_INFINITY
  const t = Date.parse(iso)
  return Number.isNaN(t) ? Number.NEGATIVE_INFINITY : t
}

/** Merge already-flattened rows, newest first; rows with null/invalid updatedAt sort last. */
export function aggregateRecentUpdates(rows: RecentRow[], limit: number): RecentRow[] {
  return [...rows].sort((a, b) => timestamp(b.updatedAt) - timestamp(a.updatedAt)).slice(0, limit)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/lib/aggregateRecentUpdates.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/aggregateRecentUpdates.ts frontend/src/lib/aggregateRecentUpdates.test.ts
git commit -m "feat(frontend): aggregateRecentUpdates pure helper (FE-R3)"
```

---

### Task 4: `buildQuickActions` pure helper

**Files:**
- Create: `frontend/src/lib/quickActions.ts`
- Create: `frontend/src/lib/quickActions.test.ts`

**Interfaces:**
- Produces:
  - `type QuickAction = { kind: 'uploadMedia' } | { kind: 'newItem'; collection: string; label: string }`
  - `function buildQuickActions(collections: CollectionMeta[], canWrite: (name: string) => boolean, maxNewItems: number): QuickAction[]`
- Consumes: `CollectionMeta` from `../types/schema`; `SYSTEM_COLLECTIONS` from `./dashboardCollections`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/quickActions.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { buildQuickActions } from './quickActions'
import type { CollectionMeta } from '../types/schema'

const coll = (name: string, label = name): CollectionMeta => ({
  name, label, fields: [], relations: [], defaultDisplayField: null,
})

describe('buildQuickActions', () => {
  it('includes uploadMedia only when the user can write files', () => {
    expect(buildQuickActions([], (n) => n === 'file', 3)).toEqual([{ kind: 'uploadMedia' }])
    expect(buildQuickActions([], () => false, 3)).toEqual([])
  })
  it('emits a newItem action per writable content collection', () => {
    const all = [coll('article', 'Article'), coll('category', 'Category')]
    const out = buildQuickActions(all, () => true, 3)
    expect(out).toContainEqual({ kind: 'newItem', collection: 'article', label: 'Article' })
    expect(out).toContainEqual({ kind: 'newItem', collection: 'category', label: 'Category' })
  })
  it('excludes system collections from newItem actions', () => {
    const all = [coll('file'), coll('user')]
    const out = buildQuickActions(all, () => true, 3)
    expect(out.some((a) => a.kind === 'newItem')).toBe(false)
  })
  it('caps newItem actions at maxNewItems', () => {
    const all = [coll('a'), coll('b'), coll('c'), coll('d')]
    const out = buildQuickActions(all, (n) => n !== 'file', 2)
    expect(out.filter((a) => a.kind === 'newItem')).toHaveLength(2)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/lib/quickActions.test.ts`
Expected: FAIL — cannot find module.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/quickActions.ts`:

```ts
import type { CollectionMeta } from '../types/schema'
import { SYSTEM_COLLECTIONS } from './dashboardCollections'

export type QuickAction =
  | { kind: 'uploadMedia' }
  | { kind: 'newItem'; collection: string; label: string }

export function buildQuickActions(
  collections: CollectionMeta[],
  canWrite: (name: string) => boolean,
  maxNewItems: number,
): QuickAction[] {
  const actions: QuickAction[] = []
  if (canWrite('file')) actions.push({ kind: 'uploadMedia' })

  const system: readonly string[] = SYSTEM_COLLECTIONS
  const writableContent = collections.filter((c) => !system.includes(c.name) && canWrite(c.name))
  for (const c of writableContent.slice(0, maxNewItems)) {
    actions.push({ kind: 'newItem', collection: c.name, label: c.label })
  }
  return actions
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/lib/quickActions.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/quickActions.ts frontend/src/lib/quickActions.test.ts
git commit -m "feat(frontend): buildQuickActions pure helper (FE-R3)"
```

---

### Task 5: `resolveGreetingKey` pure helper

**Files:**
- Create: `frontend/src/lib/greeting.ts`
- Create: `frontend/src/lib/greeting.test.ts`

**Interfaces:**
- Produces: `function resolveGreetingKey(hour: number): 'morning' | 'afternoon' | 'evening'`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/greeting.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { resolveGreetingKey } from './greeting'

describe('resolveGreetingKey', () => {
  it('morning before noon', () => {
    expect(resolveGreetingKey(0)).toBe('morning')
    expect(resolveGreetingKey(11)).toBe('morning')
  })
  it('afternoon from 12 to before 18', () => {
    expect(resolveGreetingKey(12)).toBe('afternoon')
    expect(resolveGreetingKey(17)).toBe('afternoon')
  })
  it('evening from 18 onward', () => {
    expect(resolveGreetingKey(18)).toBe('evening')
    expect(resolveGreetingKey(23)).toBe('evening')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/lib/greeting.test.ts`
Expected: FAIL — cannot find module.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/greeting.ts`:

```ts
export function resolveGreetingKey(hour: number): 'morning' | 'afternoon' | 'evening' {
  if (hour < 12) return 'morning'
  if (hour < 18) return 'afternoon'
  return 'evening'
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/lib/greeting.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/greeting.ts frontend/src/lib/greeting.test.ts
git commit -m "feat(frontend): resolveGreetingKey time-of-day helper (FE-R3)"
```

---

### Task 6: `dashboard` i18n namespace (zh-TW + en)

**Files:**
- Modify: `frontend/src/locales/zh-TW.ts` (add `dashboard` namespace)
- Modify: `frontend/src/locales/en.ts` (add symmetric `dashboard` namespace)

**Interfaces:**
- Produces: i18n keys under `dashboard.*` (consumed by Tasks 7–11).

- [ ] **Step 1: Run the existing key-parity test to confirm the baseline is green**

Run: `pnpm exec vitest run src/locales`
Expected: PASS (locales currently symmetric).

- [ ] **Step 2: Add the `dashboard` namespace to `zh-TW.ts`**

Insert this block into the default-exported object in `frontend/src/locales/zh-TW.ts` (after the `login` block, before the closing `}`):

```ts
  dashboard: {
    title: '儀表板',
    greeting: {
      morning: '早安 — 這是您網站今天的概況。',
      afternoon: '午安 — 這是您網站今天的概況。',
      evening: '晚安 — 這是您網站今天的概況。',
    },
    stats: {
      items: '內容項目',
      collections: '集合',
      media: '媒體檔案',
      users: '使用者',
    },
    recent: {
      title: '最近更新',
      colTitle: '標題',
      colCollection: '集合',
      colUpdated: '更新時間',
      empty: '尚無最近更新的內容。',
    },
    quick: {
      title: '快速動作',
      uploadMedia: '上傳媒體',
      newItem: '新增 {label}',
      empty: '沒有可用的快速動作。',
    },
    loading: '載入儀表板中…',
    error: '無法載入儀表板資料。',
  },
```

- [ ] **Step 3: Add the symmetric `dashboard` namespace to `en.ts`**

Insert the matching block into `frontend/src/locales/en.ts` (same position, mirroring existing structure):

```ts
  dashboard: {
    title: 'Dashboard',
    greeting: {
      morning: 'Good morning — here is your site at a glance today.',
      afternoon: 'Good afternoon — here is your site at a glance today.',
      evening: 'Good evening — here is your site at a glance today.',
    },
    stats: {
      items: 'Content items',
      collections: 'Collections',
      media: 'Media files',
      users: 'Users',
    },
    recent: {
      title: 'Recent updates',
      colTitle: 'Title',
      colCollection: 'Collection',
      colUpdated: 'Updated',
      empty: 'No recent content updates yet.',
    },
    quick: {
      title: 'Quick actions',
      uploadMedia: 'Upload media',
      newItem: 'New {label}',
      empty: 'No quick actions available.',
    },
    loading: 'Loading dashboard…',
    error: 'Could not load dashboard data.',
  },
```

- [ ] **Step 4: Run the key-parity test to verify symmetry holds**

Run: `pnpm exec vitest run src/locales`
Expected: PASS (recursive key sets still symmetric).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts
git commit -m "feat(frontend): dashboard i18n namespace zh-TW + en (FE-R3)"
```

---

### Task 7: `StatCard.vue` presentational component

**Files:**
- Create: `frontend/src/components/dashboard/StatCard.vue`
- Create: `frontend/src/components/dashboard/StatCard.test.ts`

**Interfaces:**
- Produces: `<StatCard :caption="string" :value="number | string" />`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/dashboard/StatCard.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatCard from './StatCard.vue'

describe('StatCard', () => {
  it('renders caption and value', () => {
    const w = mount(StatCard, { props: { caption: 'Content items', value: 128 } })
    expect(w.text()).toContain('Content items')
    expect(w.text()).toContain('128')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/components/dashboard/StatCard.test.ts`
Expected: FAIL — cannot find `./StatCard.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/dashboard/StatCard.vue`:

```vue
<script setup lang="ts">
defineProps<{ caption: string; value: number | string }>()
</script>

<template>
  <div class="card stat">
    <span class="stat__caption">{{ caption }}</span>
    <b class="stat__value">{{ value }}</b>
  </div>
</template>

<style scoped>
.stat {
  padding: 18px 20px;
  display: grid;
  gap: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--surface);
}
.stat__caption {
  color: var(--muted);
  font-size: 0.85rem;
}
.stat__value {
  font-family: var(--mono);
  font-size: 1.85rem;
  font-weight: 650;
  letter-spacing: -0.02em;
  color: var(--fg);
}
</style>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/components/dashboard/StatCard.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/StatCard.vue frontend/src/components/dashboard/StatCard.test.ts
git commit -m "feat(frontend): StatCard component (FE-R3)"
```

---

### Task 8: `RecentUpdatesTable.vue` presentational component

**Files:**
- Create: `frontend/src/components/dashboard/RecentUpdatesTable.vue`
- Create: `frontend/src/components/dashboard/RecentUpdatesTable.test.ts`

**Interfaces:**
- Consumes: `RecentRow` from `../../lib/aggregateRecentUpdates`.
- Produces: `<RecentUpdatesTable :rows="RecentRow[]" @select="(row: RecentRow) => void" />`. Renders an empty-state row (using `t('dashboard.recent.empty')`) when `rows` is empty.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/dashboard/RecentUpdatesTable.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RecentUpdatesTable from './RecentUpdatesTable.vue'
import type { RecentRow } from '../../lib/aggregateRecentUpdates'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { dashboard: { recent: { colTitle: 'Title', colCollection: 'Collection', colUpdated: 'Updated', empty: 'No recent content updates yet.' } } } },
})

const rows: RecentRow[] = [
  { id: '1', collection: 'article', collectionLabel: 'Article', title: 'Hello', updatedAt: '2026-07-14T10:00:00Z' },
]

function mountTable(props: { rows: RecentRow[] }) {
  return mount(RecentUpdatesTable, { props, global: { plugins: [i18n] } })
}

describe('RecentUpdatesTable', () => {
  it('renders a row with title and collection label', () => {
    const w = mountTable({ rows })
    expect(w.text()).toContain('Hello')
    expect(w.text()).toContain('Article')
  })
  it('emits select with the row when a row is clicked', async () => {
    const w = mountTable({ rows })
    await w.get('[data-test="recent-row"]').trigger('click')
    expect(w.emitted('select')?.[0]).toEqual([rows[0]])
  })
  it('shows the empty state when there are no rows', () => {
    const w = mountTable({ rows: [] })
    expect(w.text()).toContain('No recent content updates yet.')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/components/dashboard/RecentUpdatesTable.test.ts`
Expected: FAIL — cannot find `./RecentUpdatesTable.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/dashboard/RecentUpdatesTable.vue`:

```vue
<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { RecentRow } from '../../lib/aggregateRecentUpdates'

defineProps<{ rows: RecentRow[] }>()
defineEmits<{ (e: 'select', row: RecentRow): void }>()
const { t } = useI18n()

function formatUpdated(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString()
}
</script>

<template>
  <table class="recent">
    <thead>
      <tr>
        <th>{{ t('dashboard.recent.colTitle') }}</th>
        <th>{{ t('dashboard.recent.colCollection') }}</th>
        <th>{{ t('dashboard.recent.colUpdated') }}</th>
      </tr>
    </thead>
    <tbody>
      <tr v-if="rows.length === 0">
        <td class="recent__empty" colspan="3">{{ t('dashboard.recent.empty') }}</td>
      </tr>
      <tr
        v-for="row in rows"
        :key="`${row.collection}:${row.id}`"
        data-test="recent-row"
        class="recent__row"
        @click="$emit('select', row)"
      >
        <td class="recent__title">{{ row.title }}</td>
        <td>{{ row.collectionLabel }}</td>
        <td class="recent__updated">{{ formatUpdated(row.updatedAt) }}</td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
.recent {
  width: 100%;
  border-collapse: collapse;
}
.recent th,
.recent td {
  text-align: left;
  padding: 10px 12px;
  border-bottom: 1px solid var(--border);
}
.recent th {
  color: var(--muted);
  font-size: 0.8rem;
  font-weight: 600;
}
.recent__row {
  cursor: pointer;
}
.recent__row:hover {
  background: var(--bg);
}
.recent__title {
  color: var(--accent);
  font-weight: 550;
}
.recent__updated {
  font-family: var(--mono);
  color: var(--muted);
  white-space: nowrap;
}
.recent__empty {
  color: var(--muted);
  text-align: center;
  padding: 24px 12px;
}
</style>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/components/dashboard/RecentUpdatesTable.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/RecentUpdatesTable.vue frontend/src/components/dashboard/RecentUpdatesTable.test.ts
git commit -m "feat(frontend): RecentUpdatesTable component (FE-R3)"
```

---

### Task 9: `QuickActions.vue` presentational component

**Files:**
- Create: `frontend/src/components/dashboard/QuickActions.vue`
- Create: `frontend/src/components/dashboard/QuickActions.test.ts`

**Interfaces:**
- Consumes: `QuickAction` from `../../lib/quickActions`.
- Produces: `<QuickActions :actions="QuickAction[]" @run="(action: QuickAction) => void" />`. Empty-state text when `actions` is empty.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/dashboard/QuickActions.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import PrimeVue from 'primevue/config'
import QuickActions from './QuickActions.vue'
import type { QuickAction } from '../../lib/quickActions'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { dashboard: { quick: { uploadMedia: 'Upload media', newItem: 'New {label}', empty: 'No quick actions available.' } } } },
})

function mountQA(actions: QuickAction[]) {
  return mount(QuickActions, { props: { actions }, global: { plugins: [i18n, PrimeVue] } })
}

describe('QuickActions', () => {
  it('renders a button per action with resolved labels', () => {
    const w = mountQA([{ kind: 'uploadMedia' }, { kind: 'newItem', collection: 'article', label: 'Article' }])
    expect(w.text()).toContain('Upload media')
    expect(w.text()).toContain('New Article')
  })
  it('emits run with the action when a button is clicked', async () => {
    const action: QuickAction = { kind: 'uploadMedia' }
    const w = mountQA([action])
    await w.get('[data-test="quick-action"]').trigger('click')
    expect(w.emitted('run')?.[0]).toEqual([action])
  })
  it('shows the empty state when there are no actions', () => {
    const w = mountQA([])
    expect(w.text()).toContain('No quick actions available.')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/components/dashboard/QuickActions.test.ts`
Expected: FAIL — cannot find `./QuickActions.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/dashboard/QuickActions.vue`:

```vue
<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import type { QuickAction } from '../../lib/quickActions'

defineProps<{ actions: QuickAction[] }>()
defineEmits<{ (e: 'run', action: QuickAction): void }>()
const { t } = useI18n()

function labelFor(action: QuickAction): string {
  return action.kind === 'uploadMedia'
    ? t('dashboard.quick.uploadMedia')
    : t('dashboard.quick.newItem', { label: action.label })
}
</script>

<template>
  <div class="quick">
    <p v-if="actions.length === 0" class="quick__empty">{{ t('dashboard.quick.empty') }}</p>
    <Button
      v-for="(action, i) in actions"
      :key="i"
      data-test="quick-action"
      class="quick__btn"
      severity="secondary"
      :label="labelFor(action)"
      @click="$emit('run', action)"
    />
  </div>
</template>

<style scoped>
.quick {
  display: grid;
  gap: 10px;
}
.quick__btn {
  width: 100%;
  justify-content: flex-start;
}
.quick__empty {
  color: var(--muted);
}
</style>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/components/dashboard/QuickActions.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/QuickActions.vue frontend/src/components/dashboard/QuickActions.test.ts
git commit -m "feat(frontend): QuickActions component (FE-R3)"
```

---

### Task 10: `useDashboardData` composable (fan-out orchestration)

**Files:**
- Create: `frontend/src/composables/useDashboardData.ts`
- Create: `frontend/src/composables/useDashboardData.test.ts`

**Interfaces:**
- Consumes: `itemsApi` (`list(collection, opts) => Promise<{ data: Record<string, unknown>[]; total: number }>`), `useAuthStore`, `useSchemaStore`, `useLanguageStore`, `contentCollections`, `SYSTEM_COLLECTIONS`, `resolveItemTitle`, `aggregateRecentUpdates` (`RecentRow`), `buildQuickActions` (`QuickAction`), `resolveGreetingKey`.
- Produces:
  - `type DashboardStats = { items: number; collections: number; media: number | null; users: number | null }`
  - `function useDashboardData(): { stats: Ref<DashboardStats>; recent: Ref<RecentRow[]>; quickActions: Ref<QuickAction[]>; greetingKey: Ref<'morning'|'afternoon'|'evening'>; loading: Ref<boolean>; error: Ref<string>; load: () => Promise<void> }`

**Notes for the implementer:**
- `RECENT_LIMIT = 8`, `MAX_QUICK_NEW_ITEMS = 3`.
- Only query `'file'`/`'user'` when `canRead` is true; otherwise the corresponding stat is `null` (card hidden by the view).
- Use `Promise.allSettled` for the content fan-out so one failing collection doesn't fail the load. Set `error` only when there were content collections to query **and every one rejected**.
- Read `updatedAt` from each row as `typeof row.updatedAt === 'string' ? row.updatedAt : null`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/composables/useDashboardData.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSchemaStore } from '../stores/schemaStore'
import { useAuthStore } from '../stores/authStore'
import { useLanguageStore } from '../stores/languageStore'
import type { CollectionMeta } from '../types/schema'

const listMock = vi.fn()
vi.mock('../api/itemsApi', () => ({ itemsApi: { list: (...a: unknown[]) => listMock(...a) } }))

const coll = (name: string, defaultDisplayField: string | null = 'title'): CollectionMeta => ({
  name, label: name.toUpperCase(), fields: [
    { name: 'title', label: 'Title', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
  ], relations: [], defaultDisplayField,
})

async function run() {
  const { useDashboardData } = await import('./useDashboardData')
  const d = useDashboardData()
  await d.load()
  return d
}

beforeEach(() => {
  setActivePinia(createPinia())
  listMock.mockReset()
  const schema = useSchemaStore()
  schema.collections = [coll('article'), coll('category'), { name: 'file', label: 'File', fields: [], relations: [], defaultDisplayField: null }, { name: 'user', label: 'User', fields: [], relations: [], defaultDisplayField: null }]
  schema.loaded = true
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  lang.loaded = true
  const auth = useAuthStore()
  auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
})

describe('useDashboardData', () => {
  it('sums content totals and aggregates recent updates across content collections', async () => {
    listMock.mockImplementation((collection: string) => {
      if (collection === 'article') return Promise.resolve({ data: [{ id: 'a1', title: 'A1', updatedAt: '2026-07-14T00:00:00Z' }], total: 10 })
      if (collection === 'category') return Promise.resolve({ data: [{ id: 'c1', title: 'C1', updatedAt: '2026-07-15T00:00:00Z' }], total: 5 })
      return Promise.resolve({ data: [], total: 3 }) // file / user rows=1 count
    })
    const d = await run()
    expect(d.stats.value.items).toBe(15)
    expect(d.stats.value.collections).toBe(2)
    expect(d.stats.value.media).toBe(3)
    expect(d.stats.value.users).toBe(3)
    expect(d.recent.value.map((r) => r.id)).toEqual(['c1', 'a1']) // newest first
    expect(d.recent.value[0].collectionLabel).toBe('CATEGORY')
    expect(d.error.value).toBe('')
  })

  it('hides media/users stats when the user cannot read them', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u2', isSuperAdmin: false, permissions: { article: { read: true, write: false, delete: false }, category: { read: true, write: false, delete: false } } }
    listMock.mockResolvedValue({ data: [], total: 0 })
    const d = await run()
    expect(d.stats.value.media).toBeNull()
    expect(d.stats.value.users).toBeNull()
  })

  it('tolerates a single failing collection query (allSettled)', async () => {
    listMock.mockImplementation((collection: string) => {
      if (collection === 'article') return Promise.reject(new Error('boom'))
      return Promise.resolve({ data: [{ id: 'c1', title: 'C1', updatedAt: '2026-07-15T00:00:00Z' }], total: 5 })
    })
    const d = await run()
    expect(d.stats.value.items).toBe(5) // article dropped, category counted
    expect(d.recent.value.map((r) => r.id)).toEqual(['c1'])
    expect(d.error.value).toBe('')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/composables/useDashboardData.test.ts`
Expected: FAIL — cannot find `./useDashboardData`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/composables/useDashboardData.ts`:

```ts
import { ref, type Ref } from 'vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { contentCollections } from '../lib/dashboardCollections'
import { resolveItemTitle } from '../lib/resolveItemTitle'
import { aggregateRecentUpdates, type RecentRow } from '../lib/aggregateRecentUpdates'
import { buildQuickActions, type QuickAction } from '../lib/quickActions'
import { resolveGreetingKey } from '../lib/greeting'
import type { CollectionMeta } from '../types/schema'

const RECENT_LIMIT = 8
const MAX_QUICK_NEW_ITEMS = 3

export type DashboardStats = {
  items: number
  collections: number
  media: number | null
  users: number | null
}

export type UseDashboardData = {
  stats: Ref<DashboardStats>
  recent: Ref<RecentRow[]>
  quickActions: Ref<QuickAction[]>
  greetingKey: Ref<'morning' | 'afternoon' | 'evening'>
  loading: Ref<boolean>
  error: Ref<string>
  load: () => Promise<void>
}

export function useDashboardData(): UseDashboardData {
  const auth = useAuthStore()
  const schema = useSchemaStore()
  const lang = useLanguageStore()

  const stats = ref<DashboardStats>({ items: 0, collections: 0, media: null, users: null })
  const recent = ref<RecentRow[]>([])
  const quickActions = ref<QuickAction[]>([])
  const greetingKey = ref<'morning' | 'afternoon' | 'evening'>('morning')
  const loading = ref(false)
  const error = ref('')

  async function countOf(collection: string, locale: string | undefined): Promise<number | null> {
    if (!auth.canRead(collection)) return null
    try {
      const res = await itemsApi.list(collection, { page: 0, rows: 1, locale })
      return res.total
    } catch {
      return null
    }
  }

  async function load(): Promise<void> {
    loading.value = true
    error.value = ''
    try {
      await Promise.all([schema.load(), lang.load()])
      const locale = lang.defaultCode || undefined
      const content: CollectionMeta[] = contentCollections(schema.collections, auth.canRead)

      const settled = await Promise.allSettled(
        content.map((c) =>
          itemsApi
            .list(c.name, { page: 0, rows: RECENT_LIMIT, sort: '-updatedAt', locale })
            .then((res) => ({ meta: c, res })),
        ),
      )

      let itemsTotal = 0
      const rows: RecentRow[] = []
      let anyFulfilled = false
      for (const s of settled) {
        if (s.status !== 'fulfilled') continue
        anyFulfilled = true
        const { meta, res } = s.value
        itemsTotal += res.total
        for (const row of res.data) {
          rows.push({
            id: String(row.id),
            collection: meta.name,
            collectionLabel: meta.label,
            title: resolveItemTitle(row, meta, locale ?? ''),
            updatedAt: typeof row.updatedAt === 'string' ? row.updatedAt : null,
          })
        }
      }

      const [media, users] = await Promise.all([countOf('file', locale), countOf('user', locale)])

      stats.value = { items: itemsTotal, collections: content.length, media, users }
      recent.value = aggregateRecentUpdates(rows, RECENT_LIMIT)
      quickActions.value = buildQuickActions(schema.collections, auth.canWrite, MAX_QUICK_NEW_ITEMS)
      greetingKey.value = resolveGreetingKey(new Date().getHours())

      if (content.length > 0 && !anyFulfilled) error.value = 'error'
    } catch {
      error.value = 'error'
    } finally {
      loading.value = false
    }
  }

  return { stats, recent, quickActions, greetingKey, loading, error, load }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/composables/useDashboardData.test.ts`
Expected: PASS (all three cases).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/composables/useDashboardData.ts frontend/src/composables/useDashboardData.test.ts
git commit -m "feat(frontend): useDashboardData composable (fan-out) (FE-R3)"
```

---

### Task 11: Rebuild `DashboardView.vue` (integration) + full gate

**Files:**
- Modify: `frontend/src/views/DashboardView.vue` (replace placeholder)
- Create: `frontend/src/views/DashboardView.test.ts`

**Interfaces:**
- Consumes: `useDashboardData` (Task 10), `StatCard` (7), `RecentUpdatesTable` (8), `QuickActions` (9), `useI18n`, `useRouter`.
- Navigation wiring: recent-row `select` → `router.push({ name: 'collection-item', params: { name: row.collection, id: row.id } })`; quick-action `run` → `uploadMedia` → `router.push({ name: 'media' })`, `newItem` → `router.push({ name: 'collection-create', params: { name: action.collection } })`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/views/DashboardView.test.ts`:

```ts
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import { createI18n } from 'vue-i18n'
import PrimeVue from 'primevue/config'
import zhTW from '../locales/zh-TW'
import en from '../locales/en'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const state = {
  stats: ref({ items: 15, collections: 2, media: 3, users: null as number | null }),
  recent: ref([{ id: 'a1', collection: 'article', collectionLabel: 'Article', title: 'Hello', updatedAt: '2026-07-14T00:00:00Z' }]),
  quickActions: ref([{ kind: 'uploadMedia' as const }]),
  greetingKey: ref('morning' as const),
  loading: ref(false),
  error: ref(''),
  load: vi.fn().mockResolvedValue(undefined),
}
vi.mock('../composables/useDashboardData', () => ({ useDashboardData: () => state }))

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en, 'zh-TW': zhTW } })

async function mountView() {
  const DashboardView = (await import('./DashboardView.vue')).default
  const w = mount(DashboardView, { global: { plugins: [i18n, PrimeVue] } })
  await Promise.resolve()
  return w
}

describe('DashboardView', () => {
  it('renders stat cards for readable stats and hides null ones (users)', async () => {
    const w = await mountView()
    expect(w.text()).toContain('15') // items
    expect(w.text()).toContain('Content items')
    expect(w.text()).toContain('3') // media
    expect(w.text()).not.toContain('Users') // users stat is null -> hidden
  })
  it('navigates to the item when a recent row is selected', async () => {
    const w = await mountView()
    await w.get('[data-test="recent-row"]').trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: 'a1' } })
  })
  it('navigates to media for the upload quick action', async () => {
    const w = await mountView()
    await w.get('[data-test="quick-action"]').trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'media' })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/views/DashboardView.test.ts`
Expected: FAIL — current `DashboardView.vue` renders only the placeholder.

- [ ] **Step 3: Write the implementation**

Replace the entire contents of `frontend/src/views/DashboardView.vue` with:

```vue
<script setup lang="ts">
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import StatCard from '../components/dashboard/StatCard.vue'
import RecentUpdatesTable from '../components/dashboard/RecentUpdatesTable.vue'
import QuickActions from '../components/dashboard/QuickActions.vue'
import { useDashboardData } from '../composables/useDashboardData'
import type { RecentRow } from '../lib/aggregateRecentUpdates'
import type { QuickAction } from '../lib/quickActions'

const { t } = useI18n()
const router = useRouter()
const { stats, recent, quickActions, greetingKey, loading, error, load } = useDashboardData()

onMounted(load)

function onSelect(row: RecentRow): void {
  void router.push({ name: 'collection-item', params: { name: row.collection, id: row.id } })
}
function onRun(action: QuickAction): void {
  if (action.kind === 'uploadMedia') {
    void router.push({ name: 'media' })
  } else {
    void router.push({ name: 'collection-create', params: { name: action.collection } })
  }
}
</script>

<template>
  <section class="dashboard">
    <header class="dashboard__head">
      <h1>{{ t('dashboard.title') }}</h1>
      <p class="dashboard__greeting">{{ t(`dashboard.greeting.${greetingKey}`) }}</p>
    </header>

    <p v-if="error" class="dashboard__error" role="alert">{{ t('dashboard.error') }}</p>
    <p v-if="loading" class="dashboard__loading">{{ t('dashboard.loading') }}</p>

    <template v-if="!loading">
      <div class="dashboard__stats">
        <StatCard :caption="t('dashboard.stats.items')" :value="stats.items" />
        <StatCard :caption="t('dashboard.stats.collections')" :value="stats.collections" />
        <StatCard v-if="stats.media !== null" :caption="t('dashboard.stats.media')" :value="stats.media" />
        <StatCard v-if="stats.users !== null" :caption="t('dashboard.stats.users')" :value="stats.users" />
      </div>

      <div class="dashboard__grid">
        <div class="card dashboard__recent">
          <h2>{{ t('dashboard.recent.title') }}</h2>
          <RecentUpdatesTable :rows="recent" @select="onSelect" />
        </div>
        <div class="card dashboard__quick">
          <h2>{{ t('dashboard.quick.title') }}</h2>
          <QuickActions :actions="quickActions" @run="onRun" />
        </div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.dashboard {
  display: grid;
  gap: 20px;
}
.dashboard__head h1 {
  margin: 0;
}
.dashboard__greeting {
  color: var(--muted);
  margin: 4px 0 0;
}
.dashboard__stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 16px;
}
.dashboard__grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 20px;
  align-items: start;
}
.card {
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--surface);
  padding: 20px;
}
.card h2 {
  margin: 0 0 12px;
  font-size: 1.05rem;
}
.dashboard__error {
  color: var(--danger);
}
.dashboard__loading {
  color: var(--muted);
}
@media (max-width: 860px) {
  .dashboard__grid {
    grid-template-columns: 1fr;
  }
}
</style>
```

- [ ] **Step 4: Run the view test to verify it passes**

Run: `pnpm exec vitest run src/views/DashboardView.test.ts`
Expected: PASS (all three cases).

- [ ] **Step 5: Run the full suite + build gate**

Run: `pnpm test`
Expected: PASS — 399 baseline + all FE-R3 new tests green.

Run: `pnpm build`
Expected: clean (vue-tsc + vite; pre-existing >500 kB chunk advisory only). Fix any type errors surfaced here (vitest transpile can miss them — 9a-fe lesson).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/views/DashboardView.vue frontend/src/views/DashboardView.test.ts
git commit -m "feat(frontend): rebuild DashboardView on design system with real data (FE-R3)"
```

---

## Live Smoke (recommended — after all tasks green)

Run the app against real PostgreSQL and drive it with Playwright MCP, logged in (per prior FE-slice convention; env notes from memory: `ASPNETCORE_URLS=http://localhost:5080` for the Vite proxy; `pnpm dev --host 127.0.0.1` to avoid the Vite `[::1]`/Playwright IPv4 mismatch; bootstrap admin `admin@admin.com`). Confirm:

1. Stat cards show real counts — items total = sum of content-collection totals; media; users.
2. Recent-updates table lists the most-recently-updated items across content collections, newest first, with resolved titles + collection labels + formatted `updatedAt`; clicking a row opens the item.
3. Quick actions navigate correctly and respect permissions (upload media only when writable; "New {label}" per writable content collection, capped at 3).
4. Dark and light both read correctly.
5. A non-super-admin user sees only permitted cards/rows with no 403 noise in the console.

---

## Self-Review

**Spec coverage:**
- §2 layout → Task 11 (page-head + stats + dash-grid) ✅
- §3.1 SYSTEM_COLLECTIONS + content filter → Task 2 ✅
- §3.2 fan-out (items total, collections count, recent, media, users) → Task 10 ✅
- §3.3 verified API facts → relied on by Task 10 (sort/`updatedAt`/`total`) ✅
- §4 component decomposition → Tasks 1–11 map 1:1 to the file table ✅
- §5 quick actions → Task 4 (logic) + Task 9 (UI) + Task 11 (wiring) ✅
- §6 i18n → Task 6 ✅
- §7 error/empty states → Task 10 (allSettled/error) + Tasks 8/9/11 (empty states, hidden cards) ✅
- §8 testing → each task's TDD steps + Task 11 full gate ✅
- §9 acceptance / live smoke → Task 11 gate + Live Smoke section ✅

**Placeholder scan:** No TBD/TODO; every code step shows full code; every command shows expected output. ✅

**Type consistency:** `RecentRow` (Task 3) consumed identically in Tasks 8/10/11; `QuickAction` (Task 4) in 9/10/11; `resolveItemTitle`/`readField`/`TitleRow` (Task 1) in Task 10; `contentCollections`/`SYSTEM_COLLECTIONS` (Task 2) in Tasks 4/10; `buildQuickActions(collections, canWrite, maxNewItems)` signature consistent between Task 4 definition and Task 10 call; `useDashboardData` return shape consistent between Task 10 definition and Task 11 mock. ✅
