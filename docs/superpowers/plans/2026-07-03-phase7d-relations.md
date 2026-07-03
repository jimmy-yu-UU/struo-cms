# Phase 7d — Relation Editing + List Translated Columns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add schema-driven relation editing (M2O Dropdown, M2M TagSelect, self-referencing TreeSelect, read-only RelatedList) to the admin SPA, fix the collection list so translatable columns render, and land a full create→edit→delete UI E2E.

**Architecture:** Front-end only. Mirror Phase 7c's pure-classifier + dispatcher pattern: a pure `relationInputKind` maps a `RelationInterface` to a rendering kind; `RelationInput.vue` dispatches to a single generic `RelationPicker.vue` (props switch single/multi/tree) or a read-only `RelatedList.vue`. Relations render in a shared section of `ItemForm` (never per-locale). The backend relation write path (M2O FK scalar, `SyncM2MAsync`), schema `Relations`, deep expansion, and list translation overlay already exist. One sample-only change adds an `Article↔Tag` M2M so `TagSelect` has a real relation to exercise.

**Tech Stack:** Vue 3 + TypeScript (`<script setup>`), Pinia, Vue Router, PrimeVue (`Select`, `MultiSelect`, `TreeSelect`, `DataTable`), Vitest + Vue Test Utils, Playwright. Backend sample: .NET 10 / C# + SqlSugar attributes. pnpm.

## Global Constraints

- Outbound/inbound JSON is camelCase; enum values are camelCase strings (`RelationInterface.Dropdown` → `"dropdown"`, `TreeSelect` → `"treeSelect"`, `RelatedList` → `"relatedList"`, `TagSelect` → `"tagSelect"`).
- No new frontend runtime dependency — use PrimeVue controls already installed. Any package that proves necessary is installed via `pnpm add` (never a hand-authored version) (§17.5).
- Immutability: helpers return new objects; never mutate inputs (user coding-style rule).
- Errors are never silently swallowed; surface inline or in the form banner (CLAUDE.md §8, coding-style).
- No `console.log` in committed code.
- Framework code never references `samples/*`; the M2M change lives only in `samples/Struo.Sample.Blog` (CLAUDE.md §2).
- All DB access via SqlSugar ORM; zero vendor SQL (§17.4).
- The query/wire DSL stays confined to `itemsApi` + the query/payload helpers; views/components never build raw query strings.
- TDD: failing test first, watch it fail, minimal implementation, watch it pass, commit (§17.2).
- Run frontend commands from `frontend/`: `pnpm test`, `pnpm build`. Run a single test file with `pnpm test <path>` (Vitest). Backend from repo root: `dotnet build`, `dotnet test`.

---

## File structure

**Frontend — new files**
- `frontend/src/lib/relationInputKind.ts` (+ `.test.ts`) — pure classifier.
- `frontend/src/lib/resolveDisplayLabel.ts` (+ `.test.ts`) — pure label resolver.
- `frontend/src/lib/relationTargetQuery.ts` (+ `.test.ts`) — pure query-param builder for targets.
- `frontend/src/lib/buildRelationTree.ts` (+ `.test.ts`) — pure tree builder + cycle guard.
- `frontend/src/components/fields/RelationInput.vue` (+ `.test.ts`) — dispatcher.
- `frontend/src/components/fields/RelationPicker.vue` (+ `.test.ts`) — generic lazy picker.
- `frontend/src/components/fields/RelatedList.vue` (+ `.test.ts`) — read-only inbound list.

**Frontend — modified files**
- `frontend/src/types/schema.ts` — add `RelationMeta`, `relations` on `CollectionMeta`.
- `frontend/src/lib/buildListQuery.ts` (+ `.test.ts`) — `filter` + `locale`.
- `frontend/src/api/itemsApi.ts` (+ `.test.ts`) — `get` `deep`/`locale`; `list` `filter`/`locale`.
- `frontend/src/lib/buildItemPayload.ts` (+ `.test.ts`) — merge relation values.
- `frontend/src/lib/parseItemToForm.ts` (+ `.test.ts`) — inflate relation values from deep expansion.
- `frontend/src/components/ItemForm.vue` (+ `.test.ts`) — relations section.
- `frontend/src/views/ItemFormView.vue` (+ `.test.ts`) — deep fetch + parentId.
- `frontend/src/views/CollectionListView.vue` (+ `.test.ts`) — translatable columns.

**Sample — new/modified files**
- `samples/Struo.Sample.Blog/Tag.cs` — new `Tag` collection.
- `samples/Struo.Sample.Blog/ArticleTag.cs` — new M2M junction.
- `samples/Struo.Sample.Blog/Article.cs` — add `Tags` M2M relation.
- Seed data location: follow the existing sample seeding path (verify at implementation; see Task 16).

**E2E**
- `frontend/e2e/relations.spec.ts` — full CRUD flow.

---

## Task 1: Front-end relation types

**Files:**
- Modify: `frontend/src/types/schema.ts`

**Interfaces:**
- Produces: `RelationMeta` type; `CollectionMeta.relations: RelationMeta[]`.

- [ ] **Step 1: Add the types**

Append to `frontend/src/types/schema.ts` and add `relations` to `CollectionMeta`:

```typescript
export type RelationMeta = {
  name: string
  label: string
  kind: string // camelCase RelationKind, e.g. "manyToOne" | "manyToMany" | "oneToMany"
  targetCollection: string
  interface: string // camelCase RelationInterface: "dropdown" | "tagSelect" | "treeSelect" | "relatedList"
  foreignKey?: string | null // CLR property name on the owning/child entity, e.g. "CategoryId"
  displayTemplate?: string | null // e.g. "{Name}"
  editable: boolean
  selfReferencing: boolean
}
```

Change `CollectionMeta` to add:

```typescript
  relations: RelationMeta[]
```

- [ ] **Step 2: Verify it compiles**

Run: `cd frontend && pnpm build`
Expected: build succeeds (type-only change; existing `schemaApi` returns whatever the server sends, so `relations` is populated at runtime — no mapping code needed).

> Note: `schemaApi`/`schemaStore` pass the server payload through, so `relations` flows automatically once the type allows it. If `schemaApi` maps fields explicitly, mirror that mapping for `relations` (verify the file; if it does an explicit `.map`, add `relations: c.relations ?? []`).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/types/schema.ts
git commit -m "feat(frontend): RelationMeta type + relations on CollectionMeta"
```

---

## Task 2: `relationInputKind` classifier

**Files:**
- Create: `frontend/src/lib/relationInputKind.ts`
- Test: `frontend/src/lib/relationInputKind.test.ts`

**Interfaces:**
- Produces: `RelationInputKind` type; `relationInputKind(iface: string): RelationInputKind`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/relationInputKind.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { relationInputKind } from './relationInputKind'

describe('relationInputKind', () => {
  it('maps the four supported interfaces', () => {
    expect(relationInputKind('dropdown')).toBe('dropdown')
    expect(relationInputKind('tagSelect')).toBe('tagSelect')
    expect(relationInputKind('treeSelect')).toBe('treeSelect')
    expect(relationInputKind('relatedList')).toBe('relatedList')
  })
  it('maps file relation interfaces and unknowns to readonly', () => {
    expect(relationInputKind('filePicker')).toBe('readonly')
    expect(relationInputKind('imagePicker')).toBe('readonly')
    expect(relationInputKind('filesPicker')).toBe('readonly')
    expect(relationInputKind('whatever')).toBe('readonly')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/lib/relationInputKind.test.ts`
Expected: FAIL — cannot resolve `./relationInputKind`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/relationInputKind.ts`:

```typescript
export type RelationInputKind = 'dropdown' | 'tagSelect' | 'treeSelect' | 'relatedList' | 'readonly'

const MAP: Record<string, RelationInputKind> = {
  dropdown: 'dropdown',
  tagSelect: 'tagSelect',
  treeSelect: 'treeSelect',
  relatedList: 'relatedList',
}

export function relationInputKind(iface: string): RelationInputKind {
  return MAP[iface] ?? 'readonly'
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/lib/relationInputKind.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/relationInputKind.ts frontend/src/lib/relationInputKind.test.ts
git commit -m "feat(frontend): relationInputKind classifier"
```

---

## Task 3: `resolveDisplayLabel` helper

**Files:**
- Create: `frontend/src/lib/resolveDisplayLabel.ts`
- Test: `frontend/src/lib/resolveDisplayLabel.test.ts`

**Interfaces:**
- Consumes: `RelationMeta`, `CollectionMeta`, `FieldMeta` from `../types/schema`.
- Produces: `resolveDisplayLabel(row, relation, targetMeta, locale): string`.

Behavior: interpolate `relation.displayTemplate` tokens (`{Field}`) against the row; a token whose target field is `translatable` reads `row.translations[locale]?.[camelField]`, else the top-level camelField. Falls back to `targetMeta.defaultDisplayField`, then to the row `id`. Field tokens are matched case-insensitively and read as their camelCase key (e.g. `{Name}` → `row.name`).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/resolveDisplayLabel.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { resolveDisplayLabel } from './resolveDisplayLabel'
import type { CollectionMeta, RelationMeta } from '../types/schema'

function meta(over: Partial<CollectionMeta> = {}): CollectionMeta {
  return { name: 't', label: 'T', fields: [], relations: [], defaultDisplayField: null, ...over }
}
const rel = (over: Partial<RelationMeta> = {}): RelationMeta => ({
  name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category',
  interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}',
  editable: true, selfReferencing: false, ...over,
})

describe('resolveDisplayLabel', () => {
  it('interpolates a template against top-level (non-translatable) fields', () => {
    const target = meta({ fields: [{ name: 'name', label: 'Name', interface: 'text', required: false, searchable: true, sortable: true, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }] })
    expect(resolveDisplayLabel({ id: '1', name: 'Tech' }, rel(), target, 'en')).toBe('Tech')
  })
  it('reads a translatable template field from translations[locale]', () => {
    const target = meta({ fields: [{ name: 'title', label: 'Title', interface: 'text', required: true, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: true, sort: 1, isSystem: false }] })
    const row = { id: '9', translations: { en: { title: 'Hello' }, 'zh-TW': { title: '哈囉' } } }
    expect(resolveDisplayLabel(row, rel({ displayTemplate: '{Title}' }), target, 'zh-TW')).toBe('哈囉')
  })
  it('falls back to defaultDisplayField then id', () => {
    const target = meta({ defaultDisplayField: 'name', fields: [{ name: 'name', label: 'Name', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }] })
    expect(resolveDisplayLabel({ id: '3', name: 'Fallback' }, rel({ displayTemplate: null }), target, 'en')).toBe('Fallback')
    expect(resolveDisplayLabel({ id: '3' }, rel({ displayTemplate: null }), meta(), 'en')).toBe('3')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/lib/resolveDisplayLabel.test.ts`
Expected: FAIL — cannot resolve module.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/resolveDisplayLabel.ts`:

```typescript
import type { CollectionMeta, RelationMeta } from '../types/schema'

type Row = Record<string, unknown> & { id?: unknown; translations?: Record<string, Record<string, unknown>> }

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

function readField(row: Row, targetMeta: CollectionMeta, locale: string, fieldKey: string): unknown {
  const field = targetMeta.fields.find((f) => f.name.toLowerCase() === fieldKey.toLowerCase())
  const key = field?.name ?? camel(fieldKey)
  if (field?.translatable) {
    const t = row.translations?.[locale]
    return t?.[key]
  }
  return row[key]
}

export function resolveDisplayLabel(
  row: Row,
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
  if (targetMeta.defaultDisplayField) {
    const v = readField(row, targetMeta, locale, targetMeta.defaultDisplayField)
    if (v != null && v !== '') return String(v)
  }
  return row.id != null ? String(row.id) : ''
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/lib/resolveDisplayLabel.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/resolveDisplayLabel.ts frontend/src/lib/resolveDisplayLabel.test.ts
git commit -m "feat(frontend): resolveDisplayLabel (template + i18n + fallbacks)"
```

---

## Task 4: `buildListQuery` — filter + locale

**Files:**
- Modify: `frontend/src/lib/buildListQuery.ts`
- Test: `frontend/src/lib/buildListQuery.test.ts`

**Interfaces:**
- Produces: `buildListQuery(page, rows, sort?, search?, filter?, locale?)` where
  `filter?: Record<string, { op: string; value: string }>`. Emits `filter[field][op]=value` and `locale=...`.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/lib/buildListQuery.test.ts` (create if absent; keep existing cases):

```typescript
import { describe, it, expect } from 'vitest'
import { buildListQuery } from './buildListQuery'

describe('buildListQuery filter + locale', () => {
  it('emits filter[field][op]=value', () => {
    const p = buildListQuery(0, 25, undefined, undefined, { categoryId: { op: '_eq', value: 'abc' } })
    expect(p['filter[categoryId][_eq]']).toBe('abc')
  })
  it('emits locale when provided', () => {
    const p = buildListQuery(0, 25, undefined, undefined, undefined, 'zh-TW')
    expect(p.locale).toBe('zh-TW')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/lib/buildListQuery.test.ts`
Expected: FAIL — extra params ignored; keys absent.

- [ ] **Step 3: Write minimal implementation**

Replace `frontend/src/lib/buildListQuery.ts`:

```typescript
export type FilterSpec = Record<string, { op: string; value: string }>

export function buildListQuery(
  page: number,
  rows: number,
  sort?: string,
  search?: string,
  filter?: FilterSpec,
  locale?: string,
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
  return params
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/lib/buildListQuery.test.ts`
Expected: PASS (existing cases still pass — new params are optional).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/buildListQuery.ts frontend/src/lib/buildListQuery.test.ts
git commit -m "feat(frontend): buildListQuery supports filter + locale"
```

---

## Task 5: `relationTargetQuery` helper

**Files:**
- Create: `frontend/src/lib/relationTargetQuery.ts`
- Test: `frontend/src/lib/relationTargetQuery.test.ts`

**Interfaces:**
- Consumes: `buildListQuery`, `FilterSpec` from `./buildListQuery`.
- Produces: `relationTargetQuery(opts: { page: number; rows: number; search?: string; locale?: string; filter?: FilterSpec }): Record<string, string>`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/relationTargetQuery.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { relationTargetQuery } from './relationTargetQuery'

describe('relationTargetQuery', () => {
  it('builds paginated search params with locale', () => {
    const p = relationTargetQuery({ page: 1, rows: 20, search: 'foo', locale: 'en' })
    expect(p.limit).toBe('20')
    expect(p.offset).toBe('20')
    expect(p.search).toBe('foo')
    expect(p.locale).toBe('en')
  })
  it('passes a filter through (RelatedList inbound)', () => {
    const p = relationTargetQuery({ page: 0, rows: 10, filter: { categoryId: { op: '_eq', value: 'x' } } })
    expect(p['filter[categoryId][_eq]']).toBe('x')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/lib/relationTargetQuery.test.ts`
Expected: FAIL — module missing.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/relationTargetQuery.ts`:

```typescript
import { buildListQuery, type FilterSpec } from './buildListQuery'

export function relationTargetQuery(opts: {
  page: number
  rows: number
  search?: string
  locale?: string
  filter?: FilterSpec
}): Record<string, string> {
  return buildListQuery(opts.page, opts.rows, undefined, opts.search, opts.filter, opts.locale)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/lib/relationTargetQuery.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/relationTargetQuery.ts frontend/src/lib/relationTargetQuery.test.ts
git commit -m "feat(frontend): relationTargetQuery param builder"
```

---

## Task 6: `buildRelationTree` helper

**Files:**
- Create: `frontend/src/lib/buildRelationTree.ts`
- Test: `frontend/src/lib/buildRelationTree.test.ts`

**Interfaces:**
- Produces: `TreeNode = { key: string; label: string; data: string; children: TreeNode[] }`;
  `buildRelationTree(rows: LabeledRow[], parentKey: string, excludeId?: string): TreeNode[]` where
  `LabeledRow = { id: string; label: string; [k: string]: unknown }` and `parentKey` is the camelCase self-FK field (e.g. `parentId`).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/buildRelationTree.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { buildRelationTree } from './buildRelationTree'

const rows = [
  { id: 'a', label: 'A', parentId: null },
  { id: 'b', label: 'B', parentId: 'a' },
  { id: 'c', label: 'C', parentId: 'b' },
  { id: 'd', label: 'D', parentId: null },
]

describe('buildRelationTree', () => {
  it('nests rows by parent FK', () => {
    const tree = buildRelationTree(rows, 'parentId')
    expect(tree.map((n) => n.key)).toEqual(['a', 'd'])
    expect(tree[0].children[0].key).toBe('b')
    expect(tree[0].children[0].children[0].key).toBe('c')
  })
  it('excludes the node and its descendants when excludeId is set (cycle guard)', () => {
    const tree = buildRelationTree(rows, 'parentId', 'a')
    // a, b, c all removed; only d remains
    expect(tree.map((n) => n.key)).toEqual(['d'])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/lib/buildRelationTree.test.ts`
Expected: FAIL — module missing.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/buildRelationTree.ts`:

```typescript
export type LabeledRow = { id: string; label: string; [k: string]: unknown }
export type TreeNode = { key: string; label: string; data: string; children: TreeNode[] }

export function buildRelationTree(rows: LabeledRow[], parentKey: string, excludeId?: string): TreeNode[] {
  // Collect the excluded subtree (excludeId + all descendants) first.
  const excluded = new Set<string>()
  if (excludeId) {
    excluded.add(excludeId)
    let grew = true
    while (grew) {
      grew = false
      for (const r of rows) {
        const parent = r[parentKey]
        if (typeof parent === 'string' && excluded.has(parent) && !excluded.has(r.id)) {
          excluded.add(r.id)
          grew = true
        }
      }
    }
  }

  const usable = rows.filter((r) => !excluded.has(r.id))
  const byId = new Map<string, TreeNode>()
  for (const r of usable) byId.set(r.id, { key: r.id, label: r.label, data: r.id, children: [] })

  const roots: TreeNode[] = []
  for (const r of usable) {
    const node = byId.get(r.id)!
    const parent = r[parentKey]
    if (typeof parent === 'string' && byId.has(parent)) byId.get(parent)!.children.push(node)
    else roots.push(node)
  }
  return roots
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/lib/buildRelationTree.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/buildRelationTree.ts frontend/src/lib/buildRelationTree.test.ts
git commit -m "feat(frontend): buildRelationTree with self/descendant cycle guard"
```

---

## Task 7: `itemsApi` — `deep`/`locale` on get, `filter`/`locale` on list

**Files:**
- Modify: `frontend/src/api/itemsApi.ts`
- Test: `frontend/src/api/itemsApi.test.ts`

**Interfaces:**
- Consumes: `FilterSpec` from `../lib/buildListQuery`.
- Produces: `list(collection, opts)` where `opts: { page; rows; sort?; search?; filter?: FilterSpec; locale? }`; `get(collection, id, opts?: { locale?: string; deep?: string[] })`.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/api/itemsApi.test.ts` (extend existing; mock pattern follows the current file — mock `./apiClient`):

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { itemsApi } from './itemsApi'
import { apiClient } from './apiClient'

vi.mock('./apiClient', () => ({
  apiClient: { getRaw: vi.fn(), get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

describe('itemsApi deep + filter + locale', () => {
  beforeEach(() => vi.clearAllMocks())

  it('get appends deep and locale', async () => {
    ;(apiClient.get as any).mockResolvedValue({})
    await itemsApi.get('article', '1', { locale: 'zh-TW', deep: ['category', 'tags'] })
    expect(apiClient.get).toHaveBeenCalledWith('/items/article/1?locale=zh-TW&deep=category%2Ctags')
  })

  it('list forwards filter and locale to the query string', async () => {
    ;(apiClient.getRaw as any).mockResolvedValue({ data: [], meta: { total: 0 } })
    await itemsApi.list('article', { page: 0, rows: 10, filter: { categoryId: { op: '_eq', value: 'x' } }, locale: 'en' })
    const path = (apiClient.getRaw as any).mock.calls[0][0] as string
    expect(path).toContain('filter%5BcategoryId%5D%5B_eq%5D=x')
    expect(path).toContain('locale=en')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/api/itemsApi.test.ts`
Expected: FAIL — `deep`/`filter` not applied.

- [ ] **Step 3: Write minimal implementation**

Replace `frontend/src/api/itemsApi.ts`:

```typescript
import { apiClient } from './apiClient'
import { buildListQuery, type FilterSpec } from '../lib/buildListQuery'

export type ListOptions = {
  page: number
  rows: number
  sort?: string
  search?: string
  filter?: FilterSpec
  locale?: string
}
export type ListResult = { data: Record<string, unknown>[]; total: number }

type ListEnvelope = { data: Record<string, unknown>[]; meta: { total: number } }

export const itemsApi = {
  async list(collection: string, opts: ListOptions): Promise<ListResult> {
    const params = buildListQuery(opts.page, opts.rows, opts.sort, opts.search, opts.filter, opts.locale)
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
  async remove(collection: string, id: string): Promise<void> {
    await apiClient.delete<void>(`/items/${collection}/${id}`)
  },
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/api/itemsApi.test.ts`
Expected: PASS (existing cases still pass — `list` params are appended after existing ones).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/itemsApi.ts frontend/src/api/itemsApi.test.ts
git commit -m "feat(frontend): itemsApi get deep/locale + list filter/locale"
```

---

## Task 8: `buildItemPayload` — merge relation values

**Files:**
- Modify: `frontend/src/lib/buildItemPayload.ts`
- Modify: `frontend/src/types/itemForm.ts`
- Test: `frontend/src/lib/buildItemPayload.test.ts`

**Interfaces:**
- Consumes: `RelationMeta`; `relationInputKind`.
- Produces: `FormModel` gains `relations: Record<string, unknown>` (relation name → id | id[] | null). `buildItemPayload` merges: M2O/Tree → `payload[camelCase(foreignKey)] = value ?? null`; M2M (`tagSelect`) → `payload[relationName] = value` (array). Untouched relation keys (absent from `model.relations`) are omitted. `relatedList`/`readonly` never write.

- [ ] **Step 1: Extend the FormModel type**

Modify `frontend/src/types/itemForm.ts`:

```typescript
export type LocaleValues = Record<string, unknown>
export type FormModel = {
  shared: Record<string, unknown>
  translations: Record<string, LocaleValues>
  relations: Record<string, unknown> // relation name -> id | id[] | null
}
```

- [ ] **Step 2: Write the failing test**

Add to `frontend/src/lib/buildItemPayload.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { buildItemPayload } from './buildItemPayload'
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const langs: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]

function metaWithRelations(): CollectionMeta {
  return {
    name: 'article', label: 'Article', fields: [], defaultDisplayField: null,
    relations: [
      { name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false },
      { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect', foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false },
      { name: 'comments', label: 'Comments', kind: 'oneToMany', targetCollection: 'comment', interface: 'relatedList', foreignKey: 'ArticleId', displayTemplate: '{Body}', editable: false, selfReferencing: false },
    ],
  }
}

describe('buildItemPayload relations', () => {
  it('writes M2O FK (camelCased) and M2M id array; omits relatedList and untouched relations', () => {
    const model: FormModel = {
      shared: {}, translations: {},
      relations: { category: 'cat-1', tags: ['t1', 't2'] }, // comments untouched
    }
    const payload = buildItemPayload(metaWithRelations(), model, langs, 'update')
    expect(payload.categoryId).toBe('cat-1')
    expect(payload.tags).toEqual(['t1', 't2'])
    expect(payload).not.toHaveProperty('comments')
  })
  it('sends empty M2M array to clear, and null FK to clear', () => {
    const model: FormModel = { shared: {}, translations: {}, relations: { category: null, tags: [] } }
    const payload = buildItemPayload(metaWithRelations(), model, langs, 'update')
    expect(payload.categoryId).toBeNull()
    expect(payload.tags).toEqual([])
  })
})
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd frontend && pnpm test src/lib/buildItemPayload.test.ts`
Expected: FAIL — relations not merged; also TS errors in existing tests where `FormModel` literals now need `relations`.

> Fix existing `FormModel` literals in this and other test files by adding `relations: {}` (do it as you hit compile errors).

- [ ] **Step 4: Write minimal implementation**

Modify `frontend/src/lib/buildItemPayload.ts` — add imports at the top:

```typescript
import { relationInputKind } from './relationInputKind'

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}
```

Then, before `if (Object.keys(translations).length > 0)`:

```typescript
  const relations = model.relations ?? {}
  for (const rel of meta.relations ?? []) {
    if (!(rel.name in relations)) continue // untouched -> partial update
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      if (rel.foreignKey) payload[camel(rel.foreignKey)] = relations[rel.name] ?? null
    } else if (kind === 'tagSelect') {
      payload[rel.name] = relations[rel.name] ?? []
    }
    // relatedList / readonly: never written
  }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd frontend && pnpm test src/lib/buildItemPayload.test.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/buildItemPayload.ts frontend/src/types/itemForm.ts frontend/src/lib/buildItemPayload.test.ts
git commit -m "feat(frontend): buildItemPayload merges relation values (M2O FK / M2M array)"
```

---

## Task 9: `parseItemToForm` — inflate relations from deep expansion

**Files:**
- Modify: `frontend/src/lib/parseItemToForm.ts`
- Test: `frontend/src/lib/parseItemToForm.test.ts`

**Interfaces:**
- Produces: `parseItemToForm`/`blankItemForm` also fill `model.relations`: for editable M2O/Tree → `item[relationName]?.id ?? null`; M2M → `(item[relationName] ?? []).map(r => r.id)`; blank → M2O/Tree `null`, M2M `[]`. `relatedList`/`readonly` are not seeded.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/lib/parseItemToForm.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { parseItemToForm, blankItemForm } from './parseItemToForm'
import type { CollectionMeta, LanguageInfo } from '../types/schema'

const langs: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]
function meta(): CollectionMeta {
  return {
    name: 'article', label: 'Article', fields: [], defaultDisplayField: null,
    relations: [
      { name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false },
      { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect', foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false },
    ],
  }
}

describe('parseItemToForm relations', () => {
  it('inflates M2O id and M2M id array from deep-expanded item', () => {
    const item = { id: '1', category: { id: 'cat-1', name: 'Tech' }, tags: [{ id: 't1' }, { id: 't2' }] }
    const model = parseItemToForm(meta(), item, langs)
    expect(model.relations.category).toBe('cat-1')
    expect(model.relations.tags).toEqual(['t1', 't2'])
  })
  it('blank form seeds M2O null and M2M empty array', () => {
    const model = blankItemForm(meta(), langs)
    expect(model.relations.category).toBeNull()
    expect(model.relations.tags).toEqual([])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/lib/parseItemToForm.test.ts`
Expected: FAIL — `model.relations` missing.

- [ ] **Step 3: Write minimal implementation**

Modify `frontend/src/lib/parseItemToForm.ts` — add import:

```typescript
import { relationInputKind } from './relationInputKind'
```

Change the `return` of `parseItemToForm` to build relations:

```typescript
  const relations: Record<string, unknown> = {}
  for (const rel of meta.relations ?? []) {
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      const nested = item[rel.name] as { id?: unknown } | null | undefined
      relations[rel.name] = nested?.id ?? null
    } else if (kind === 'tagSelect') {
      const arr = (item[rel.name] as Array<{ id?: unknown }> | undefined) ?? []
      relations[rel.name] = arr.map((r) => r.id)
    }
  }
  return { shared: sharedModel, translations, relations }
```

(`blankItemForm` already delegates to `parseItemToForm(meta, {}, locales)`, so blank M2O → `null`, M2M → `[]` fall out automatically.)

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/lib/parseItemToForm.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/parseItemToForm.ts frontend/src/lib/parseItemToForm.test.ts
git commit -m "feat(frontend): parseItemToForm inflates relations from deep expansion"
```

---

## Task 10: `RelationPicker.vue` — generic lazy picker

**Files:**
- Create: `frontend/src/components/fields/RelationPicker.vue`
- Test: `frontend/src/components/fields/RelationPicker.test.ts`

**Interfaces:**
- Consumes: `itemsApi.list`/`itemsApi.get`, `resolveDisplayLabel`, `buildRelationTree`, `schemaStore` (target meta), `languageStore` (locale).
- Props: `{ relation: RelationMeta; modelValue: unknown; multiple?: boolean; tree?: boolean; disabled?: boolean; excludeId?: string }`. Emits `update:modelValue`.
- Produces: a component that lazy-loads target options, resolves labels, and v-model round-trips a single id (single/tree) or an id array (multiple). Exposes `loadOptions`, `ensureSelectedLabels`, `onChange`, `options`, `loading`, `loadError` for testing.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/fields/RelationPicker.test.ts`:

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createTestingPinia } from '@pinia/testing'
import PrimeVue from 'primevue/config'
import RelationPicker from './RelationPicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import type { RelationMeta } from '../../types/schema'

vi.mock('../../api/itemsApi', () => ({
  itemsApi: { list: vi.fn(), get: vi.fn() },
}))

const rel: RelationMeta = {
  name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category',
  interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false,
}

function mountPicker(props: Record<string, unknown>) {
  const wrapper = mount(RelationPicker, {
    props: { relation: rel, modelValue: null, ...props },
    global: {
      plugins: [PrimeVue, createTestingPinia({ createSpy: vi.fn })],
    },
  })
  const schema = useSchemaStore()
  ;(schema as any).get = () => ({ name: 'category', label: 'Category', fields: [{ name: 'name', label: 'Name', interface: 'text', required: false, searchable: true, sortable: true, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }], relations: [], defaultDisplayField: 'name' })
  return wrapper
}

describe('RelationPicker', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lazy-loads options from the target collection on open', async () => {
    ;(itemsApi.list as any).mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const wrapper = mountPicker({})
    await (wrapper.vm as any).loadOptions()
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('category', expect.objectContaining({ page: 0 }))
    expect((wrapper.vm as any).options).toHaveLength(1)
    expect((wrapper.vm as any).options[0].label).toBe('Tech')
  })

  it('emits the selected id (single mode)', async () => {
    ;(itemsApi.list as any).mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const wrapper = mountPicker({})
    ;(wrapper.vm as any).onChange('c1')
    expect(wrapper.emitted('update:modelValue')![0]).toEqual(['c1'])
  })

  it('back-fills a preselected label via itemsApi.get', async () => {
    ;(itemsApi.list as any).mockResolvedValue({ data: [], total: 0 })
    ;(itemsApi.get as any).mockResolvedValue({ id: 'c9', name: 'Preselected' })
    const wrapper = mountPicker({ modelValue: 'c9' })
    await (wrapper.vm as any).ensureSelectedLabels()
    await flushPromises()
    expect(itemsApi.get).toHaveBeenCalledWith('category', 'c9', expect.any(Object))
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/components/fields/RelationPicker.test.ts`
Expected: FAIL — component missing.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/fields/RelationPicker.vue`:

```vue
<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import Select from 'primevue/select'
import MultiSelect from 'primevue/multiselect'
import TreeSelect from 'primevue/treeselect'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { resolveDisplayLabel } from '../../lib/resolveDisplayLabel'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
import type { RelationMeta } from '../../types/schema'

const props = defineProps<{
  relation: RelationMeta
  modelValue: unknown
  multiple?: boolean
  tree?: boolean
  disabled?: boolean
  excludeId?: string
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const schema = useSchemaStore()
const langStore = useLanguageStore()

type Option = { id: string; label: string; raw: Record<string, unknown> }
const options = ref<Option[]>([])
const labelById = ref<Record<string, string>>({})
const loading = ref(false)
const loadError = ref('')
const search = ref('')

const targetMeta = computed(() => schema.get(props.relation.targetCollection))

function toOption(row: Record<string, unknown>): Option {
  const tm = targetMeta.value
  const label = tm ? resolveDisplayLabel(row, props.relation, tm, langStore.defaultCode) : String(row.id)
  return { id: String(row.id), label, raw: row }
}

async function loadOptions(): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    const res = await itemsApi.list(props.relation.targetCollection, {
      page: 0, rows: 25, search: search.value || undefined, locale: langStore.defaultCode || undefined,
    })
    options.value = res.data.map(toOption)
    for (const o of options.value) labelById.value[o.id] = o.label
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : 'Failed to load options.'
  } finally {
    loading.value = false
  }
}

async function ensureSelectedLabels(): Promise<void> {
  const ids = props.multiple
    ? ((props.modelValue as string[] | null) ?? [])
    : props.modelValue != null ? [String(props.modelValue)] : []
  for (const id of ids) {
    if (labelById.value[id]) continue
    try {
      const row = await itemsApi.get(props.relation.targetCollection, id, { locale: langStore.defaultCode })
      labelById.value[id] = toOption(row).label
    } catch {
      labelById.value[id] = id // deleted / inaccessible -> show id
    }
  }
}

const treeNodes = computed<TreeNode[]>(() => {
  if (!props.tree) return []
  const parentKey = props.relation.foreignKey
    ? props.relation.foreignKey[0].toLowerCase() + props.relation.foreignKey.slice(1)
    : 'parentId'
  return buildRelationTree(
    options.value.map((o) => ({ id: o.id, label: o.label, ...o.raw })),
    parentKey,
    props.excludeId,
  )
})

function onChange(v: unknown): void {
  emit('update:modelValue', v)
}

// TreeSelect binds an object keyed by node key; map to a single id.
const treeValue = computed(() => (props.modelValue != null ? { [String(props.modelValue)]: true } : {}))
function onTreeChange(selection: Record<string, boolean>): void {
  const keys = Object.keys(selection)
  emit('update:modelValue', keys.length ? keys[0] : null)
}

watch(search, loadOptions)
onMounted(async () => {
  await loadOptions()
  await ensureSelectedLabels()
})

defineExpose({ loadOptions, ensureSelectedLabels, onChange, options, loading, loadError })
</script>

<template>
  <div class="relation-picker">
    <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>

    <TreeSelect
      v-if="tree"
      :model-value="treeValue"
      :options="treeNodes"
      selection-mode="single"
      :disabled="disabled"
      :loading="loading"
      @update:model-value="onTreeChange"
    />

    <MultiSelect
      v-else-if="multiple"
      :model-value="modelValue"
      :options="options"
      option-label="label"
      option-value="id"
      filter
      :disabled="disabled"
      :loading="loading"
      @filter="(e: { value: string }) => (search = e.value)"
      @update:model-value="onChange"
    />

    <Select
      v-else
      :model-value="modelValue"
      :options="options"
      option-label="label"
      option-value="id"
      filter
      show-clear
      :disabled="disabled"
      :loading="loading"
      @filter="(e: { value: string }) => (search = e.value)"
      @update:model-value="onChange"
    />
  </div>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/components/fields/RelationPicker.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/RelationPicker.vue frontend/src/components/fields/RelationPicker.test.ts
git commit -m "feat(frontend): RelationPicker (lazy single/multi/tree relation control)"
```

---

## Task 11: `RelatedList.vue` — read-only inbound list

**Files:**
- Create: `frontend/src/components/fields/RelatedList.vue`
- Test: `frontend/src/components/fields/RelatedList.test.ts`

**Interfaces:**
- Consumes: `itemsApi.list`, `resolveDisplayLabel`, `schemaStore`, `languageStore`, `vue-router`.
- Props: `{ relation: RelationMeta; parentId?: string }`.
- Produces: a paginated read-only list of target rows filtered by `foreignKey _eq parentId`; row click routes to that item; no query when `parentId` is absent. Exposes `load`, `onPage`, `rows`, `total`, `loading`, `error`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/fields/RelatedList.test.ts`:

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createTestingPinia } from '@pinia/testing'
import PrimeVue from 'primevue/config'
import RelatedList from './RelatedList.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import type { RelationMeta } from '../../types/schema'

vi.mock('../../api/itemsApi', () => ({ itemsApi: { list: vi.fn() } }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

const rel: RelationMeta = {
  name: 'articles', label: 'Articles', kind: 'oneToMany', targetCollection: 'article',
  interface: 'relatedList', foreignKey: 'CategoryId', displayTemplate: '{Title}', editable: false, selfReferencing: false,
}

function mountList(props: Record<string, unknown>) {
  const wrapper = mount(RelatedList, {
    props: { relation: rel, ...props },
    global: { plugins: [PrimeVue, createTestingPinia({ createSpy: vi.fn })] },
  })
  const schema = useSchemaStore()
  ;(schema as any).get = () => ({ name: 'article', label: 'Article', fields: [{ name: 'title', label: 'Title', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: true, sort: 1, isSystem: false }], relations: [], defaultDisplayField: null })
  return wrapper
}

describe('RelatedList', () => {
  beforeEach(() => vi.clearAllMocks())

  it('queries the target filtered by foreignKey _eq parentId', async () => {
    ;(itemsApi.list as any).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mountList({ parentId: 'p1' })
    await (wrapper.vm as any).load()
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', expect.objectContaining({
      filter: { categoryId: { op: '_eq', value: 'p1' } },
    }))
  })

  it('does not query when parentId is absent (create mode)', async () => {
    const wrapper = mountList({ parentId: undefined })
    await (wrapper.vm as any).load()
    expect(itemsApi.list).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/components/fields/RelatedList.test.ts`
Expected: FAIL — component missing.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/fields/RelatedList.vue`:

```vue
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { resolveDisplayLabel } from '../../lib/resolveDisplayLabel'
import type { RelationMeta } from '../../types/schema'

const props = defineProps<{ relation: RelationMeta; parentId?: string }>()
const router = useRouter()
const schema = useSchemaStore()
const langStore = useLanguageStore()

type Row = { id: string; label: string }
const rows = ref<Row[]>([])
const total = ref(0)
const page = ref(0)
const perPage = ref(10)
const loading = ref(false)
const error = ref('')

const targetMeta = computed(() => schema.get(props.relation.targetCollection))

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

async function load(): Promise<void> {
  if (!props.parentId || !props.relation.foreignKey) return
  loading.value = true
  error.value = ''
  try {
    const res = await itemsApi.list(props.relation.targetCollection, {
      page: page.value,
      rows: perPage.value,
      locale: langStore.defaultCode || undefined,
      filter: { [camel(props.relation.foreignKey)]: { op: '_eq', value: props.parentId } },
    })
    const tm = targetMeta.value
    rows.value = res.data.map((r) => ({
      id: String(r.id),
      label: tm ? resolveDisplayLabel(r, props.relation, tm, langStore.defaultCode) : String(r.id),
    }))
    total.value = res.total
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load related items.'
  } finally {
    loading.value = false
  }
}

function onPage(e: { page: number; rows: number }): void {
  page.value = e.page
  perPage.value = e.rows
  load()
}

function openItem(id: string): void {
  router.push({ name: 'collection-item', params: { name: props.relation.targetCollection, id } })
}

onMounted(load)
defineExpose({ load, onPage, rows, total, loading, error })
</script>

<template>
  <div class="related-list">
    <p v-if="!parentId" class="hint">Visible after saving.</p>
    <template v-else>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <DataTable
        :value="rows"
        lazy
        paginator
        :rows="perPage"
        :total-records="total"
        :loading="loading"
        @page="onPage"
        @row-click="(e: { data: Row }) => openItem(e.data.id)"
      >
        <Column field="label" :header="relation.label" />
        <template #empty>No related items.</template>
      </DataTable>
    </template>
  </div>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/components/fields/RelatedList.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/RelatedList.vue frontend/src/components/fields/RelatedList.test.ts
git commit -m "feat(frontend): RelatedList (read-only inbound paginated list)"
```

---

## Task 12: `RelationInput.vue` — dispatcher

**Files:**
- Create: `frontend/src/components/fields/RelationInput.vue`
- Test: `frontend/src/components/fields/RelationInput.test.ts`

**Interfaces:**
- Consumes: `relationInputKind`, `RelationPicker`, `RelatedList`.
- Props: `{ relation: RelationMeta; modelValue: unknown; disabled?: boolean; parentId?: string; excludeId?: string }`. Emits `update:modelValue`.
- Produces: dispatches `dropdown` → single picker; `tagSelect` → multi picker; `treeSelect` → tree picker; `relatedList` → `RelatedList`; else read-only.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/fields/RelationInput.test.ts`:

```typescript
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createTestingPinia } from '@pinia/testing'
import PrimeVue from 'primevue/config'
import RelationInput from './RelationInput.vue'
import RelationPicker from './RelationPicker.vue'
import RelatedList from './RelatedList.vue'
import type { RelationMeta } from '../../types/schema'

vi.mock('../../api/itemsApi', () => ({ itemsApi: { list: vi.fn().mockResolvedValue({ data: [], total: 0 }), get: vi.fn() } }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

function rel(over: Partial<RelationMeta>): RelationMeta {
  return { name: 'r', label: 'R', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false, ...over }
}
function mountInput(relation: RelationMeta) {
  return mount(RelationInput, {
    props: { relation, modelValue: null },
    global: { plugins: [PrimeVue, createTestingPinia({ createSpy: vi.fn })] },
  })
}

describe('RelationInput dispatcher', () => {
  it('renders RelationPicker for dropdown/tagSelect/treeSelect', () => {
    expect(mountInput(rel({ interface: 'dropdown' })).findComponent(RelationPicker).exists()).toBe(true)
    expect(mountInput(rel({ interface: 'tagSelect' })).findComponent(RelationPicker).exists()).toBe(true)
    expect(mountInput(rel({ interface: 'treeSelect' })).findComponent(RelationPicker).exists()).toBe(true)
  })
  it('renders RelatedList for relatedList', () => {
    expect(mountInput(rel({ interface: 'relatedList', editable: false })).findComponent(RelatedList).exists()).toBe(true)
  })
  it('renders read-only for unknown interface', () => {
    const w = mountInput(rel({ interface: 'filePicker' }))
    expect(w.findComponent(RelationPicker).exists()).toBe(false)
    expect(w.find('.readonly-relation').exists()).toBe(true)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/components/fields/RelationInput.test.ts`
Expected: FAIL — component missing.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/fields/RelationInput.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import RelationPicker from './RelationPicker.vue'
import RelatedList from './RelatedList.vue'
import { relationInputKind } from '../../lib/relationInputKind'
import type { RelationMeta } from '../../types/schema'

const props = defineProps<{
  relation: RelationMeta
  modelValue: unknown
  disabled?: boolean
  parentId?: string
  excludeId?: string
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const kind = computed(() => relationInputKind(props.relation.interface))
const isDisabled = computed(() => props.disabled === true || props.relation.editable === false)
function update(v: unknown): void { emit('update:modelValue', v) }
</script>

<template>
  <RelationPicker
    v-if="kind === 'dropdown'"
    :relation="relation" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelationPicker
    v-else-if="kind === 'tagSelect'"
    :relation="relation" :model-value="modelValue" multiple :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelationPicker
    v-else-if="kind === 'treeSelect'"
    :relation="relation" :model-value="modelValue" tree :exclude-id="excludeId" :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelatedList
    v-else-if="kind === 'relatedList'"
    :relation="relation" :parent-id="parentId"
  />
  <span v-else class="readonly-relation">{{ relation.label }} (read-only)</span>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/components/fields/RelationInput.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/RelationInput.vue frontend/src/components/fields/RelationInput.test.ts
git commit -m "feat(frontend): RelationInput dispatcher (picker / related-list / readonly)"
```

---

## Task 13: `ItemForm.vue` — relations section

**Files:**
- Modify: `frontend/src/components/ItemForm.vue`
- Test: `frontend/src/components/ItemForm.test.ts`

**Interfaces:**
- Consumes: `RelationInput`, `RelationMeta`, `FormModel.relations`.
- Produces: a shared "Relations" section rendering `meta.relations` via `RelationInput`, bound to `model.relations[rel.name]`, passed `parentId` (the item id when editing) + `excludeId` (same id) for cycle-guarding self-referencing trees. New optional prop `itemId?: string`.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/components/ItemForm.test.ts`:

```typescript
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createTestingPinia } from '@pinia/testing'
import PrimeVue from 'primevue/config'
import ItemForm from './ItemForm.vue'
import RelationInput from './fields/RelationInput.vue'
import type { CollectionMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'

vi.mock('../api/itemsApi', () => ({ itemsApi: { list: vi.fn().mockResolvedValue({ data: [], total: 0 }), get: vi.fn() } }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

function meta(): CollectionMeta {
  return {
    name: 'article', label: 'Article', defaultDisplayField: null,
    fields: [{ name: 'status', label: 'Status', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }],
    relations: [{ name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false }],
  }
}

describe('ItemForm relations section', () => {
  it('renders a RelationInput per relation in the shared section', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: {}, relations: { category: null } }
    const wrapper = mount(ItemForm, {
      props: { meta: meta(), model, locales: [{ code: 'en', name: 'English', isDefault: true }], errors: {} },
      global: { plugins: [PrimeVue, createTestingPinia({ createSpy: vi.fn })] },
    })
    expect(wrapper.findComponent(RelationInput).exists()).toBe(true)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/components/ItemForm.test.ts`
Expected: FAIL — no `RelationInput` rendered.

- [ ] **Step 3: Write minimal implementation**

Modify `frontend/src/components/ItemForm.vue`. Add to `<script setup>` imports:

```typescript
import RelationInput from './fields/RelationInput.vue'
```

Add `itemId` to props:

```typescript
const props = defineProps<{
  meta: CollectionMeta
  model: FormModel
  locales: LanguageInfo[]
  errors: Record<string, string>
  serverError?: string
  disabled?: boolean
  submitting?: boolean
  itemId?: string
}>()
```

Add a relations section in the template, after the shared fields `<div v-for>` and before the `<Tabs>`:

```vue
    <section v-if="meta.relations && meta.relations.length" class="relations">
      <h3>Relations</h3>
      <div v-for="rel in meta.relations" :key="rel.name" class="field">
        <label>{{ rel.label }}</label>
        <RelationInput
          :relation="rel"
          v-model="model.relations[rel.name]"
          :disabled="disabled"
          :parent-id="itemId"
          :exclude-id="rel.selfReferencing ? itemId : undefined"
        />
      </div>
    </section>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/components/ItemForm.test.ts`
Expected: PASS (existing ItemForm cases still pass — the section is additive and guarded by `meta.relations.length`).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/ItemForm.vue frontend/src/components/ItemForm.test.ts
git commit -m "feat(frontend): ItemForm renders a shared relations section"
```

---

## Task 14: `ItemFormView.vue` — deep fetch + parentId

**Files:**
- Modify: `frontend/src/views/ItemFormView.vue`
- Test: `frontend/src/views/ItemFormView.test.ts`

**Interfaces:**
- Consumes: `itemsApi.get` with `deep`; `relationInputKind` to compute editable relation names.
- Produces: on edit, `itemsApi.get(name, id, { deep: <editable relation names>, locale })`; passes `itemId` to `ItemForm`.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/views/ItemFormView.test.ts` (follow the existing mock setup in that file; key assertion below):

```typescript
it('fetches with deep = editable relation names on edit', async () => {
  // Reuse this file's existing edit-mode harness for mounting ItemFormView.
  // Arrange schemaStore.get('article') to return a meta with:
  //   relations: [ {name:'category', interface:'dropdown', editable:true, ...},
  //                {name:'comments', interface:'relatedList', editable:false, ...} ]
  // and the route to carry :id = '123', with itemsApi.get mocked.
  await (view.vm as any).init()
  expect(itemsApiGetMock).toHaveBeenCalledWith(
    'article',
    '123',
    expect.objectContaining({ deep: ['category'] }), // relatedList excluded (not editable)
  )
})
```

> Test author: extend the file's existing edit-mode setup so `schemaStore.get('article')` returns the two relations above; assert `deep: ['category']`.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/views/ItemFormView.test.ts`
Expected: FAIL — `get` called without `deep`.

- [ ] **Step 3: Write minimal implementation**

Modify `frontend/src/views/ItemFormView.vue`. Add import:

```typescript
import { relationInputKind } from '../lib/relationInputKind'
```

Add a computed for editable relation names (after `meta`):

```typescript
const editableRelations = computed(() =>
  (meta.value?.relations ?? [])
    .filter((r) => ['dropdown', 'tagSelect', 'treeSelect'].includes(relationInputKind(r.interface)))
    .map((r) => r.name),
)
```

Change the edit fetch in `init()`:

```typescript
      const item = await itemsApi.get(name.value, id.value!, {
        deep: editableRelations.value,
        locale: langStore.defaultCode,
      })
```

Pass `item-id` to `ItemForm` in the template:

```vue
      <ItemForm
        :meta="meta"
        :model="model"
        :locales="langStore.languages"
        :errors="errors"
        :server-error="serverError"
        :disabled="!canWrite"
        :submitting="submitting"
        :item-id="id"
        @submit="onSubmit"
        @cancel="onCancel"
      />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/views/ItemFormView.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts
git commit -m "feat(frontend): ItemFormView deep-fetches editable relations + passes itemId"
```

---

## Task 15: `CollectionListView.vue` — translatable columns

**Files:**
- Modify: `frontend/src/views/CollectionListView.vue`
- Test: `frontend/src/views/CollectionListView.test.ts`

**Interfaces:**
- Consumes: `languageStore.defaultCode`.
- Produces: list cells for a `translatable` column read `row.translations[defaultLocale]?.[field]`; the list load passes `locale: defaultCode`.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/views/CollectionListView.test.ts`:

```typescript
it('renders a translatable column from translations[locale] instead of "—"', async () => {
  // Reuse the file's harness: meta has a translatable "title" column;
  // languageStore.defaultCode === 'en'.
  itemsApiListMock.mockResolvedValue({
    data: [{ id: '1', translations: { en: { title: 'Hello' } } }],
    total: 1,
  })
  await (view.vm as any).loadItems()
  await flushPromises()
  expect(view.text()).toContain('Hello')
})
```

> Test author: reuse the file's existing harness that mounts `CollectionListView` with a mocked `schemaStore` (meta with a translatable `title` column) and `languageStore` (`defaultCode: 'en'`).

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test src/views/CollectionListView.test.ts`
Expected: FAIL — cell shows the top-level (undefined → `—`), not `Hello`.

- [ ] **Step 3: Write minimal implementation**

Modify `frontend/src/views/CollectionListView.vue`. Import + use the language store:

```typescript
import { useLanguageStore } from '../stores/languageStore'
```

```typescript
const langStore = useLanguageStore()
```

In `loadItems`, ensure languages are loaded and pass the locale:

```typescript
    await langStore.load()
    const res = await itemsApi.list(name.value, {
      page: page.value,
      rows: perPage.value,
      sort,
      search: search.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
```

Add a cell-value resolver:

```typescript
function cellValue(row: Record<string, unknown>, field: FieldMeta): unknown {
  if (field.translatable) {
    const t = (row.translations as Record<string, Record<string, unknown>> | undefined)?.[langStore.defaultCode]
    return t?.[field.name]
  }
  return row[field.name]
}
```

Change the column body template:

```vue
          <template #body="{ data }">
            {{ formatCell(cellValue(data, fieldOf(col.field)!), fieldOf(col.field)!) }}
          </template>
```

Add `cellValue` to `defineExpose`.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test src/views/CollectionListView.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/views/CollectionListView.vue frontend/src/views/CollectionListView.test.ts
git commit -m "feat(frontend): list resolves translatable columns from translations[locale]"
```

---

## Task 16: Sample — `Tag` collection + `Article↔Tag` M2M + seed

**Files:**
- Create: `samples/Struo.Sample.Blog/Tag.cs`
- Create: `samples/Struo.Sample.Blog/ArticleTag.cs`
- Modify: `samples/Struo.Sample.Blog/Article.cs`
- Modify: sample seed (verify path first — search the sample host for existing InitTables/seed code).

**Interfaces:**
- Produces: an M2M relation `Article.Tags` with `RelationInterface.TagSelect` so the front-end `TagSelect` has a real relation. Mirror the existing M2M junction pattern already used in the codebase for junction wiring (`IM2MDescriptorSource` conventions).

- [ ] **Step 1: Verify the M2M convention (do NOT invent one)**

Read the backend M2M junction convention before writing.

Run: `grep -rn "SyncManyToManyAsync\|JunctionType\|ParentFkProperty\|TargetFkProperty\|SortProperty\|Navigate(" src tests --include=*.cs`
Expected: shows the exact `[Navigate]`/junction property conventions `IM2MDescriptorSource` expects. Match them exactly in `ArticleTag` and the `Article.Tags` navigation.

- [ ] **Step 2: Write a backend metadata test (failing)**

Add a test in `tests/Struo.Tests` (mirroring existing metadata-scan tests) asserting the sample scan yields an `article` relation named `tags` with `Interface == RelationInterface.TagSelect` and `Kind == ManyToMany`.

Run: `dotnet test --filter FullyQualifiedName~Tag`
Expected: FAIL — no `Tag`/`tags` relation yet.

- [ ] **Step 3: Create `Tag.cs`**

```csharp
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("tags")]
[CmsCollection("Tag", Icon = "tag", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Tag : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Create `ArticleTag.cs`** (match the junction convention confirmed in Step 1)

```csharp
using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_tags")]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Guid TagId { get; set; }
    public int Sort { get; set; }
}
```

> Adjust property names / attributes to whatever `IM2MDescriptorSource` requires per Step 1. Do not invent a second convention.

- [ ] **Step 5: Add the M2M relation on `Article.cs`**

Add to `Article` (after the `Category` relation):

```csharp
    [Navigate(typeof(ArticleTag), nameof(ArticleTag.ArticleId), nameof(ArticleTag.TagId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<Tag> Tags { get; set; } = [];
```

> Use whatever `[Navigate]` M2M overload the codebase already uses for M2M (confirm in Step 1); match it exactly.

- [ ] **Step 6: Seed rows**

Add seed rows for a few `Tag`s (and, if the sample seeds `Article`s, wire a couple of `ArticleTag` links) in the sample's existing seed path. If the sample has no seed path, add tags via the running API at gate time instead and note it (do not fabricate a seed mechanism).

- [ ] **Step 7: Run tests to verify green**

Run: `dotnet build && dotnet test`
Expected: PASS — new metadata test green; full suite still green.

- [ ] **Step 8: Commit**

```bash
git add samples/Struo.Sample.Blog/Tag.cs samples/Struo.Sample.Blog/ArticleTag.cs samples/Struo.Sample.Blog/Article.cs tests/
git commit -m "test(sample): add Tag collection + Article<->Tag M2M for TagSelect"
```

---

## Task 17: E2E — full create→edit→delete with relations

**Files:**
- Create: `frontend/e2e/relations.spec.ts`

**Interfaces:**
- Consumes: the running dev app (same-origin dev-proxy), seeded admin creds + data per `frontend/e2e/README.md`, the sample `Tag`/M2M from Task 16.

- [ ] **Step 1: Write the E2E spec**

Create `frontend/e2e/relations.spec.ts` (mirror the login/selector helpers used in the existing `items.spec.ts`/`collections.spec.ts` — adjust the helper import to the repo's actual path):

```typescript
import { test, expect } from '@playwright/test'
import { login } from './helpers'

const TITLE = `Rel E2E ${Date.now()}`

test('create → list shows translated title → edit relations → related-list → delete', async ({ page }) => {
  await login(page)

  // Create
  await page.goto('/collections/article')
  await page.getByRole('button', { name: 'New' }).click()
  await page.getByLabel('Title', { exact: false }).first().fill(TITLE)
  await page.getByLabel('Body', { exact: false }).first().fill('Body text')
  // pick a category (Dropdown) and a tag (TagSelect)
  await page.getByLabel('Category').click()
  await page.getByRole('option').first().click()
  await page.getByLabel('Tags').click()
  await page.getByRole('option').first().click()
  await page.keyboard.press('Escape')
  await page.getByRole('button', { name: 'Save' }).click()

  // List shows the translated Title (not "—")
  await expect(page.getByText(TITLE)).toBeVisible()

  // Edit: open the row, change a tag, save
  await page.getByText(TITLE).click()
  await page.getByLabel('Tags').click()
  await page.getByRole('option').nth(1).click()
  await page.keyboard.press('Escape')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(TITLE)).toBeVisible()

  // Delete
  await page.getByText(TITLE).click()
  await page.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('button', { name: /accept|yes|confirm/i }).click()
  await expect(page.getByText(TITLE)).toHaveCount(0)
})
```

> Selector note: match the actual labels/roles your components render (PrimeVue `Select`/`MultiSelect` open a listbox with `role=option`). Adjust `getByLabel`/`getByRole` to the real DOM; the flow, not the exact selectors, is the contract.

- [ ] **Step 2: Run the E2E**

Run: `cd frontend && pnpm e2e` (with the dev API on live Postgres+Redis per `e2e/README.md`)
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/e2e/relations.spec.ts
git commit -m "test(frontend): E2E relations create/edit/delete + translated list title"
```

---

## Task 18: Full verification gate + docs

**Files:**
- Modify: `docs/ROADMAP.md` (flip Phase 7d row to done; add spec/plan links).

- [ ] **Step 1: Backend gate**

Run: `dotnet build && dotnet test`
Expected: build clean (warnings-as-errors); all tests pass.

- [ ] **Step 2: Frontend gate**

Run: `cd frontend && pnpm test && pnpm build`
Expected: all unit/component tests pass; build succeeds.

- [ ] **Step 3: Live gate (real Postgres + Redis)**

Follow `frontend/e2e/README.md` to run the dev API against live Postgres + Redis, then:
- Create an `article` with a `category` + one or more `tags`; confirm 201.
- `GET /api/items/article/{id}?deep=category,tags&locale=en` returns the nested `category` + `tags`.
- Edit to change the category and add/remove a tag; confirm the M2M junction rows are replaced (query `article_tags` or re-GET with deep).
- `GET /api/items/article?filter[categoryId][_eq]=<catId>&locale=en` (the RelatedList query) returns the article with its translated title present.
- Confirm the list's translatable Title column renders (not `—`).
- Record evidence (commands + responses) as in prior phases' live-gate notes.

- [ ] **Step 4: Update ROADMAP + commit**

Flip the Phase 7d row to done with the live-verified note and link the spec/plan.

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 7d done + live-verified (relations + list translated columns)"
```

---

## Self-Review

**Spec coverage:**
- Four relation interfaces — Dropdown (Task 10/12), TagSelect (10/12), TreeSelect (6/10/12), RelatedList (11/12). ✓
- Relation types on `CollectionMeta` — Task 1. ✓
- `relationInputKind` — Task 2. ✓
- `resolveDisplayLabel` (template + i18n + fallbacks) — Task 3. ✓
- `relationTargetQuery` + `buildListQuery` filter/locale — Tasks 4-5. ✓
- `buildRelationTree` cycle guard — Task 6. ✓
- `itemsApi` deep/filter/locale — Task 7. ✓
- Payload merge + inflation — Tasks 8-9. ✓
- Shared relations section (not per-locale) — Task 13. ✓
- Edit deep-fetch of current values — Task 14. ✓
- List translated columns — Task 15. ✓
- Sample M2M for TagSelect — Task 16. ✓
- Full UI E2E — Task 17. ✓
- Verification gate incl. live PG+Redis — Task 18. ✓
- Error handling (§5): inline load/permission errors (Task 10/11 `loadError`/`error`), preselected-deleted → id (Task 10 `ensureSelectedLabels` catch), M2M 400 surfaces via the existing form banner (Task 14/existing `onSubmit`), RelatedList create → hidden (Task 11), TreeSelect cycle (Task 6). ✓ Known limitation (translatable non-searchable search) is recorded in the spec and re-checked at the gate (Task 18).

**Placeholder scan:** No TBD/TODO in code steps; each code step shows full content. Tasks 14/15 reference "reuse the file's existing harness" for test *setup* only, with the exact assertion given — acceptable since the harness already exists and reproducing it verbatim would be guesswork about the current file; the behavioral assertion is concrete. Task 16 Steps 4-6 flag "confirm the existing convention" because inventing a second M2M/junction convention would violate a global constraint — the confirming command is given.

**Type consistency:** `FormModel.relations: Record<string, unknown>` (Task 8) is read by Tasks 9/13; `RelationMeta` (Task 1) fields used consistently (`interface`, `foreignKey`, `editable`, `selfReferencing`, `targetCollection`, `displayTemplate`); `FilterSpec` (Task 4) consumed by Tasks 5/7/11; `relationInputKind` kinds (`dropdown`/`tagSelect`/`treeSelect`/`relatedList`/`readonly`) used identically in Tasks 8/9/12/14; `TreeNode` (Task 6) consumed by Task 10; the camelCase FK helper is duplicated intentionally in `buildItemPayload`/`RelatedList`/`RelationPicker` (tiny, local — a shared import is not worth it here). ✓
