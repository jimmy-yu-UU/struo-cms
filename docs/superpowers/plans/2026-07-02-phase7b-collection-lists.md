# Phase 7b — Collection Lists Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a browse-only, schema-driven content surface to the admin SPA — an RBAC-filtered collection nav plus a server-paginated, sortable, globally-searchable data table per collection.

**Architecture:** One additive backend change exposes the caller's effective permissions on `GET /api/auth/me`. The frontend layers as in Phase 7a (`api` → `stores` → pure helpers → components → router): typed `schemaApi`/`itemsApi` over the existing `apiClient`, a `schemaStore`, four pure helpers (`buildListQuery`, `selectListColumns`, `formatCell`, `buildNav`), a `CollectionNav` (PrimeVue PanelMenu), and a `CollectionListView` (PrimeVue lazy DataTable). The query DSL is confined to `itemsApi` + `buildListQuery`; views call ergonomic methods only.

**Tech Stack:** .NET 10 / ASP.NET Core (backend, additive `/me` change); Vue 3, TypeScript, Vite, PrimeVue (DataTable/Column/PanelMenu/InputText — already installed in 7a), Pinia, Vue Router, Vitest + Vue Test Utils, Playwright. Package manager **pnpm**.

## Global Constraints

- **Package versions are NEVER inferred from model knowledge.** Install latest via the package manager itself (`pnpm add`, `dotnet add package`); any version string must be one the tool produced. (CLAUDE.md §17.5) — 7b adds **no** new packages; PrimeVue components ship in the already-installed `primevue`.
- **Backend change is additive and default-safe:** `GET /api/auth/me` gains `isSuperAdmin` + `permissions`; `id` is unchanged; all existing tests stay green. (spec §1)
- **Outbound JSON is camelCase; enums serialize as camelCase strings** (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`), e.g. `FieldInterface.RichText` → `"richText"`, `DateTime` → `"dateTime"`. Frontend matches these exact strings. (CLAUDE.md §1)
- **Query DSL never leaks into views/components** — it lives only in `itemsApi` + `buildListQuery`. (CLAUDE.md §2 query-DSL rule)
- **TDD on logic:** failing-test-first for the four pure helpers, `itemsApi`, `schemaStore`, `authStore`, and the backend `/me` projection. Components verified by mount + E2E. (spec §7)
- **Never swallow errors silently;** show user-facing messages, don't crash the app. (CLAUDE.md error-handling)
- **Backend layering:** the change is confined to `Struo.Api` (`AuthController` consuming the already-registered scoped `ICurrentPermissions` + `SchemaService`); no Domain/Application contract change. (spec §9)

---

### Task 1: Backend — expose effective permissions on `GET /api/auth/me`

**Files:**
- Modify: `src/Struo.Api/Controllers/AuthController.cs` (extend `Me()`)
- Test: `tests/Struo.Tests/Api/AuthMePermissionsTests.cs`

**Interfaces:**
- Consumes (existing): `ICurrentPermissions` (scoped, populated by `PermissionResolutionMiddleware`) exposing `EffectivePermissions Current`; `EffectivePermissions.IsSuperAdmin`, `.CanRead(name)`, `.CanWrite(name)`, `.CanDelete(name)`; `Struo.Application.Metadata.SchemaService.GetAll()` returning `CollectionMetadata` with `.Name`.
- Produces: `GET /api/auth/me` → `{ data: { id, isSuperAdmin: bool, permissions: { <name>: { read, write, delete } } } }`. `permissions` includes only collections with ≥1 grant; empty when super-admin.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Api/AuthMePermissionsTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public sealed class AuthMePermissionsTests
{
    [Fact]
    public async Task Me_for_super_admin_reports_isSuperAdmin_true()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateAuthenticatedClientAsync(); // admin role is IsSuperAdmin
        var doc = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var data = doc.GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Me_for_editor_lists_only_granted_collections()
    {
        using var factory = new ApiFactory();
        var (client, _) = await factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: ["article"]);

        var doc = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var data = doc.GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeFalse();

        var perms = data.GetProperty("permissions");
        // Role grant on "article":
        perms.GetProperty("article").GetProperty("read").GetBoolean().Should().BeTrue();
        perms.GetProperty("article").GetProperty("write").GetBoolean().Should().BeTrue();
        perms.GetProperty("article").GetProperty("delete").GetBoolean().Should().BeFalse();
        // "user" is neither role-granted nor public-read → absent from the map.
        perms.TryGetProperty("user", out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~AuthMePermissionsTests`
Expected: FAIL — `data` has no `isSuperAdmin`/`permissions` property (KeyNotFound / property missing).

- [ ] **Step 3: Extend `AuthController.Me()`**

Replace the existing `Me()` action in `src/Struo.Api/Controllers/AuthController.cs`:

```csharp
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    [HttpGet("me")]
    public IActionResult Me(
        [FromServices] Struo.Application.Security.ICurrentPermissions permissions,
        [FromServices] Struo.Application.Metadata.SchemaService schema)
    {
        var eff = permissions.Current;
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!eff.IsSuperAdmin)
        {
            foreach (var c in schema.GetAll())
            {
                var read = eff.CanRead(c.Name);
                var write = eff.CanWrite(c.Name);
                var del = eff.CanDelete(c.Name);
                if (read || write || del)
                    map[c.Name] = new { read, write, @delete = del };
            }
        }

        return Ok(new
        {
            data = new
            {
                id = User.FindFirstValue(ClaimTypes.NameIdentifier),
                isSuperAdmin = eff.IsSuperAdmin,
                permissions = map
            }
        });
    }
```

> `@delete` is the C# escaped identifier for the `delete` keyword; it serializes to the JSON property `"delete"` (camelCase policy leaves it as-is). `ClaimsPrincipal.FindFirstValue` and `ClaimTypes` are already imported in this file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~AuthMePermissionsTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the full backend suite (regression)**

Run: `dotnet test`
Expected: all previously-passing tests still pass + 2 new. 0 failed / 0 skipped.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Api/Controllers/AuthController.cs tests/Struo.Tests/Api/AuthMePermissionsTests.cs
git commit -m "feat(api): expose effective permissions on /api/auth/me"
```

---

### Task 2: Frontend types + `authStore` permission state

**Files:**
- Create: `frontend/src/types/schema.ts`
- Modify: `frontend/src/stores/authStore.ts`
- Modify: `frontend/src/stores/authStore.test.ts` (existing tests must keep typechecking against the widened `CurrentUser`)

**Interfaces:**
- Produces:
  - `types/schema.ts`: `FieldOption`, `FieldMeta`, `CollectionMeta`, `CollectionPermission`, `CurrentUserDto`.
  - `authStore`: `CurrentUser = { id; isSuperAdmin; permissions: Record<string, CollectionPermission> }`; getter `isAuthenticated`; getter `canRead(collection: string): boolean`; actions `login`/`logout`/`fetchCurrentUser` (contracts unchanged).

- [ ] **Step 1: Create the shared types**

```typescript
// frontend/src/types/schema.ts
// Mirrors backend DTOs. JSON is camelCase; enum values are camelCase strings
// (e.g. FieldInterface.RichText -> "richText", DateTime -> "dateTime").

export type FieldOption = { value: string; label: string }

export type FieldMeta = {
  name: string
  label: string
  interface: string // camelCase FieldInterface, e.g. "text" | "select" | "dateTime" | "richText"
  required: boolean
  searchable: boolean
  sortable: boolean
  readOnly: boolean
  hidden: boolean
  translatable: boolean
  sort: number
  helpText?: string | null
  group?: string | null
  options?: FieldOption[] | null
  isSystem: boolean
}

export type CollectionMeta = {
  name: string
  label: string
  icon?: string | null
  group?: string | null
  defaultDisplayField?: string | null
  fields: FieldMeta[]
}

export type CollectionPermission = { read: boolean; write: boolean; delete: boolean }

export type CurrentUserDto = {
  id: string
  isSuperAdmin: boolean
  permissions: Record<string, CollectionPermission>
}
```

- [ ] **Step 2: Update the existing `authStore` test first (widen the mocks) — write, expect current impl to still pass its behavior but fail typecheck until store widens**

Edit `frontend/src/stores/authStore.test.ts`: change every `CurrentUser` value in mocks/assignments to include the new fields. Specifically:

- In "login success" test, change both mock return values:
  ```typescript
  vi.mocked(apiClient.post).mockResolvedValue({ id: 'u1', isSuperAdmin: false, permissions: {} })
  vi.mocked(apiClient.get).mockResolvedValue({ id: 'u1', isSuperAdmin: false, permissions: {} })
  ```
  and the assertion:
  ```typescript
  expect(store.user).toEqual({ id: 'u1', isSuperAdmin: false, permissions: {} })
  ```
- In "fetchCurrentUser sets user on 200":
  ```typescript
  vi.mocked(apiClient.get).mockResolvedValue({ id: 'u9', isSuperAdmin: false, permissions: {} })
  ...
  expect(store.user).toEqual({ id: 'u9', isSuperAdmin: false, permissions: {} })
  ```
- In "logout clears the user":
  ```typescript
  store.user = { id: 'u1', isSuperAdmin: false, permissions: {} }
  ```

Then append a new test for the `canRead` getter:

```typescript
  it('canRead is true for super-admin on any collection', () => {
    const store = useAuthStore()
    store.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    expect(store.canRead('anything')).toBe(true)
  })

  it('canRead reflects per-collection read grant for non-super users', () => {
    const store = useAuthStore()
    store.user = { id: 'u1', isSuperAdmin: false, permissions: { article: { read: true, write: false, delete: false } } }
    expect(store.canRead('article')).toBe(true)
    expect(store.canRead('category')).toBe(false)
  })

  it('canRead is false when unauthenticated', () => {
    const store = useAuthStore()
    expect(store.canRead('article')).toBe(false)
  })
```

- [ ] **Step 3: Run test to verify it fails**

Run (inside `frontend/`): `pnpm test authStore`
Expected: FAIL — `store.canRead` is not a function (and TS type errors on the widened `user`).

- [ ] **Step 4: Widen `authStore`**

```typescript
// frontend/src/stores/authStore.ts
import { defineStore } from 'pinia'
import { apiClient } from '../api/apiClient'
import type { CollectionPermission } from '../types/schema'

export type CurrentUser = {
  id: string
  isSuperAdmin: boolean
  permissions: Record<string, CollectionPermission>
}

export const useAuthStore = defineStore('auth', {
  state: () => ({ user: null as CurrentUser | null }),
  getters: {
    isAuthenticated: (state) => state.user !== null,
    canRead: (state) => (collection: string): boolean =>
      !!state.user && (state.user.isSuperAdmin || state.user.permissions?.[collection]?.read === true),
  },
  actions: {
    async login(email: string, password: string): Promise<void> {
      // Login returns { id }; then confirm via /me for a canonical session (id + perms).
      await apiClient.post('/auth/login', { email, password })
      await this.fetchCurrentUser()
    },
    async logout(): Promise<void> {
      try {
        await apiClient.post('/auth/logout')
      } finally {
        this.user = null
      }
    },
    async fetchCurrentUser(): Promise<void> {
      try {
        this.user = await apiClient.get<CurrentUser>('/auth/me')
      } catch {
        this.user = null // unauthenticated / no session
      }
    },
  },
})
```

- [ ] **Step 5: Run test to verify it passes**

Run (inside `frontend/`): `pnpm test authStore`
Expected: PASS (existing 5 + 3 new = 8).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/types/schema.ts frontend/src/stores/authStore.ts frontend/src/stores/authStore.test.ts
git commit -m "feat(frontend): schema types + authStore permission state (canRead)"
```

---

### Task 3: `buildListQuery` (pure, TDD)

**Files:**
- Create: `frontend/src/lib/buildListQuery.ts`
- Test: `frontend/src/lib/buildListQuery.test.ts`

**Interfaces:**
- Produces: `buildListQuery(page: number, rows: number, sort?: string, search?: string): Record<string, string>` — always sets `limit`=rows, `offset`=page*rows; adds `sort` when truthy; adds `search` when non-empty (after trim).

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/lib/buildListQuery.test.ts
import { describe, it, expect } from 'vitest'
import { buildListQuery } from './buildListQuery'

describe('buildListQuery', () => {
  it('maps page/rows to limit/offset', () => {
    expect(buildListQuery(0, 25)).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(2, 10)).toEqual({ limit: '10', offset: '20' })
  })

  it('includes sort when provided (asc and desc tokens pass through)', () => {
    expect(buildListQuery(0, 25, 'title')).toEqual({ limit: '25', offset: '0', sort: 'title' })
    expect(buildListQuery(0, 25, '-createdAt')).toEqual({ limit: '25', offset: '0', sort: '-createdAt' })
  })

  it('includes search only when non-empty', () => {
    expect(buildListQuery(0, 25, undefined, 'hello')).toEqual({ limit: '25', offset: '0', search: 'hello' })
    expect(buildListQuery(0, 25, undefined, '')).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(0, 25, undefined, '   ')).toEqual({ limit: '25', offset: '0' })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test buildListQuery`
Expected: FAIL — `Cannot find module './buildListQuery'`.

- [ ] **Step 3: Implement**

```typescript
// frontend/src/lib/buildListQuery.ts
export function buildListQuery(
  page: number,
  rows: number,
  sort?: string,
  search?: string,
): Record<string, string> {
  const params: Record<string, string> = {
    limit: String(rows),
    offset: String(page * rows),
  }
  if (sort) params.sort = sort
  if (search && search.trim() !== '') params.search = search
  return params
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test buildListQuery`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/buildListQuery.ts frontend/src/lib/buildListQuery.test.ts
git commit -m "feat(frontend): buildListQuery (page/sort/search -> DSL params)"
```

---

### Task 4: `selectListColumns` (pure, TDD)

**Files:**
- Create: `frontend/src/lib/selectListColumns.ts`
- Test: `frontend/src/lib/selectListColumns.test.ts`

**Interfaces:**
- Consumes: `CollectionMeta`, `FieldMeta` from `types/schema`.
- Produces: `type ColumnDef = { field: string; header: string; sortable: boolean }`; `selectListColumns(meta: CollectionMeta): ColumnDef[]` — DefaultDisplayField first (if eligible), then other eligible fields, capped at 6. Eligible = `!isSystem && !hidden && scalar interface`. `sortable` from `field.sortable`.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/lib/selectListColumns.test.ts
import { describe, it, expect } from 'vitest'
import { selectListColumns } from './selectListColumns'
import type { CollectionMeta, FieldMeta } from '../types/schema'

function field(partial: Partial<FieldMeta> & { name: string; interface: string }): FieldMeta {
  return {
    label: partial.name, required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
    ...partial,
  } as FieldMeta
}

function meta(fields: FieldMeta[], defaultDisplayField?: string): CollectionMeta {
  return { name: 'article', label: 'Article', defaultDisplayField, fields }
}

describe('selectListColumns', () => {
  it('puts DefaultDisplayField first, keeps eligible scalars, carries sortable', () => {
    const cols = selectListColumns(meta([
      field({ name: 'title', label: 'Title', interface: 'text', sortable: true }),
      field({ name: 'status', label: 'Status', interface: 'select' }),
    ], 'status'))
    expect(cols.map((c) => c.field)).toEqual(['status', 'title'])
    expect(cols.find((c) => c.field === 'title')!.sortable).toBe(true)
    expect(cols.find((c) => c.field === 'status')!.header).toBe('Status')
  })

  it('excludes system, hidden, richText, relations and file/image interfaces', () => {
    const cols = selectListColumns(meta([
      field({ name: 'title', interface: 'text' }),
      field({ name: 'body', interface: 'richText' }),
      field({ name: 'cover', interface: 'image' }),
      field({ name: 'attachment', interface: 'file' }),
      field({ name: 'secret', interface: 'text', hidden: true }),
      field({ name: 'id', interface: 'uuid', isSystem: true }),
    ]))
    expect(cols.map((c) => c.field)).toEqual(['title'])
  })

  it('caps at 6 columns', () => {
    const many = Array.from({ length: 10 }, (_, i) => field({ name: `f${i}`, interface: 'text' }))
    expect(selectListColumns(meta(many))).toHaveLength(6)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test selectListColumns`
Expected: FAIL — `Cannot find module './selectListColumns'`.

- [ ] **Step 3: Implement**

```typescript
// frontend/src/lib/selectListColumns.ts
import type { CollectionMeta, FieldMeta } from '../types/schema'

export type ColumnDef = { field: string; header: string; sortable: boolean }

// camelCase FieldInterface values that render cleanly as a single table cell.
const SCALAR_INTERFACES = new Set<string>([
  'text', 'textarea', 'slug', 'email', 'url', 'phone', 'color',
  'number', 'slider', 'rating',
  'boolean', 'checkbox',
  'date', 'time', 'dateTime',
  'select', 'radio',
])

const MAX_COLUMNS = 6

export function selectListColumns(meta: CollectionMeta): ColumnDef[] {
  const eligible = meta.fields.filter(
    (f) => !f.isSystem && !f.hidden && SCALAR_INTERFACES.has(f.interface),
  )

  const ordered: FieldMeta[] = []
  const ddf = meta.defaultDisplayField
  if (ddf) {
    const hit = eligible.find((f) => f.name === ddf)
    if (hit) ordered.push(hit)
  }
  for (const f of eligible) {
    if (!ordered.includes(f)) ordered.push(f)
  }

  return ordered.slice(0, MAX_COLUMNS).map((f) => ({
    field: f.name,
    header: f.label,
    sortable: f.sortable,
  }))
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test selectListColumns`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/selectListColumns.ts frontend/src/lib/selectListColumns.test.ts
git commit -m "feat(frontend): selectListColumns (smart-subset table columns)"
```

---

### Task 5: `formatCell` (pure, TDD)

**Files:**
- Create: `frontend/src/lib/formatCell.ts`
- Test: `frontend/src/lib/formatCell.test.ts`

**Interfaces:**
- Consumes: `FieldMeta`.
- Produces: `formatCell(value: unknown, field: FieldMeta): string` — null/undefined/`''` → `"—"`; select/radio → option label (fallback raw); boolean/checkbox → `"Yes"`/`"No"`; date/time/dateTime → locale-formatted (raw on invalid); else `String(value)`.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/lib/formatCell.test.ts
import { describe, it, expect } from 'vitest'
import { formatCell } from './formatCell'
import type { FieldMeta } from '../types/schema'

function field(partial: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return {
    name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
    ...partial,
  } as FieldMeta
}

describe('formatCell', () => {
  it('renders em-dash for null/undefined/empty', () => {
    const f = field({ interface: 'text' })
    expect(formatCell(null, f)).toBe('—')
    expect(formatCell(undefined, f)).toBe('—')
    expect(formatCell('', f)).toBe('—')
  })

  it('maps a select value to its option label, falling back to the raw value', () => {
    const f = field({ interface: 'select', options: [{ value: 'draft', label: 'Draft' }] })
    expect(formatCell('draft', f)).toBe('Draft')
    expect(formatCell('unknown', f)).toBe('unknown')
  })

  it('renders booleans as Yes/No', () => {
    const f = field({ interface: 'boolean' })
    expect(formatCell(true, f)).toBe('Yes')
    expect(formatCell(false, f)).toBe('No')
  })

  it('formats dateTime values and passes plain text through', () => {
    expect(formatCell('2026-01-02T03:04:05Z', field({ interface: 'dateTime' }))).toContain('2026')
    expect(formatCell('not-a-date', field({ interface: 'dateTime' }))).toBe('not-a-date')
    expect(formatCell('hello', field({ interface: 'text' }))).toBe('hello')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test formatCell`
Expected: FAIL — `Cannot find module './formatCell'`.

- [ ] **Step 3: Implement**

```typescript
// frontend/src/lib/formatCell.ts
import type { FieldMeta } from '../types/schema'

export function formatCell(value: unknown, field: FieldMeta): string {
  if (value === null || value === undefined || value === '') return '—'

  const iface = field.interface
  if ((iface === 'select' || iface === 'radio') && field.options) {
    const opt = field.options.find((o) => o.value === String(value))
    return opt ? opt.label : String(value)
  }
  if (iface === 'boolean' || iface === 'checkbox') {
    return value ? 'Yes' : 'No'
  }
  if (iface === 'date' || iface === 'time' || iface === 'dateTime') {
    const d = new Date(String(value))
    return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleString()
  }
  return String(value)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test formatCell`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/formatCell.ts frontend/src/lib/formatCell.test.ts
git commit -m "feat(frontend): formatCell (select label, date, boolean, empty)"
```

---

### Task 6: `buildNav` (pure, TDD)

**Files:**
- Create: `frontend/src/lib/buildNav.ts`
- Test: `frontend/src/lib/buildNav.test.ts`

**Interfaces:**
- Consumes: `CollectionMeta`, `CollectionPermission`.
- Produces: `type NavItem = { name: string; label: string; icon?: string | null }`; `type NavGroup = { group: string; items: NavItem[] }`; `buildNav(collections, isSuperAdmin, permissions): NavGroup[]` — filter to readable (super-admin → all; else `permissions[name]?.read === true`), group by `group` (blank/missing → `"General"`), preserve collection order within groups.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/lib/buildNav.test.ts
import { describe, it, expect } from 'vitest'
import { buildNav } from './buildNav'
import type { CollectionMeta, CollectionPermission } from '../types/schema'

function coll(name: string, group?: string): CollectionMeta {
  return { name, label: name[0].toUpperCase() + name.slice(1), group, fields: [] }
}
const grant = (read: boolean): CollectionPermission => ({ read, write: false, delete: false })

describe('buildNav', () => {
  const collections = [coll('article', 'Content'), coll('category', 'Content'), coll('user')]

  it('super-admin sees every collection', () => {
    const nav = buildNav(collections, true, {})
    const names = nav.flatMap((g) => g.items.map((i) => i.name))
    expect(names.sort()).toEqual(['article', 'category', 'user'])
  })

  it('non-super users see only readable collections', () => {
    const nav = buildNav(collections, false, { article: grant(true), category: grant(false) })
    const names = nav.flatMap((g) => g.items.map((i) => i.name))
    expect(names).toEqual(['article'])
  })

  it('groups by group, defaulting blank/missing to General', () => {
    const nav = buildNav(collections, true, {})
    const content = nav.find((g) => g.group === 'Content')!
    const general = nav.find((g) => g.group === 'General')!
    expect(content.items.map((i) => i.name)).toEqual(['article', 'category'])
    expect(general.items.map((i) => i.name)).toEqual(['user'])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test buildNav`
Expected: FAIL — `Cannot find module './buildNav'`.

- [ ] **Step 3: Implement**

```typescript
// frontend/src/lib/buildNav.ts
import type { CollectionMeta, CollectionPermission } from '../types/schema'

export type NavItem = { name: string; label: string; icon?: string | null }
export type NavGroup = { group: string; items: NavItem[] }

const UNGROUPED = 'General'

export function buildNav(
  collections: CollectionMeta[],
  isSuperAdmin: boolean,
  permissions: Record<string, CollectionPermission>,
): NavGroup[] {
  const readable = collections.filter(
    (c) => isSuperAdmin || permissions[c.name]?.read === true,
  )

  const groups = new Map<string, NavItem[]>()
  for (const c of readable) {
    const key = c.group && c.group.trim() !== '' ? c.group : UNGROUPED
    if (!groups.has(key)) groups.set(key, [])
    groups.get(key)!.push({ name: c.name, label: c.label, icon: c.icon })
  }

  return Array.from(groups.entries()).map(([group, items]) => ({ group, items }))
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test buildNav`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/buildNav.ts frontend/src/lib/buildNav.test.ts
git commit -m "feat(frontend): buildNav (RBAC filter + group by Group)"
```

---

### Task 7: API layer + `schemaStore` (`apiClient.getRaw`, `schemaApi`, `itemsApi`, `schemaStore`)

**Files:**
- Modify: `frontend/src/api/apiClient.ts` (add `getRaw` — returns the full envelope without unwrapping `data`)
- Modify: `frontend/src/api/apiClient.test.ts` (test `getRaw`)
- Create: `frontend/src/api/schemaApi.ts`
- Create: `frontend/src/api/itemsApi.ts`
- Test: `frontend/src/api/itemsApi.test.ts`
- Create: `frontend/src/stores/schemaStore.ts`
- Test: `frontend/src/stores/schemaStore.test.ts`

**Interfaces:**
- Consumes: existing `ApiClient.get`/`request` (credentials + 401 handler + error throw); `buildListQuery` (Task 3); `CollectionMeta` (Task 2).
- Produces:
  - `ApiClient.getRaw<T>(path): Promise<T>` — like `get` but returns the parsed body verbatim (no `data` unwrap), so `{ data, meta }` survives. 401/error behavior identical to `get`.
  - `schemaApi = { getAll(): Promise<CollectionMeta[]>; get(name): Promise<CollectionMeta> }`.
  - `itemsApi = { list(collection, opts): Promise<ListResult> }`, `ListOptions = { page; rows; sort?; search? }`, `ListResult = { data: Record<string, unknown>[]; total: number }`.
  - `useSchemaStore` (Pinia): state `collections: CollectionMeta[]`, `loaded: boolean`, `loadError: string`; action `load()` (fetch once, cache; store `loadError` on failure); method `get(name): CollectionMeta | undefined`.

- [ ] **Step 1: Write the failing tests (apiClient.getRaw + itemsApi + schemaStore)**

Append to `frontend/src/api/apiClient.test.ts`:

```typescript
  it('getRaw returns the full envelope without unwrapping data', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { data: [{ id: '1' }], meta: { total: 42 } }))
    const c = new ApiClient('/api')
    const result = await c.getRaw<{ data: unknown[]; meta: { total: number } }>('/items/article')
    expect(result).toEqual({ data: [{ id: '1' }], meta: { total: 42 } })
  })
```

```typescript
// frontend/src/api/itemsApi.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { itemsApi } from './itemsApi'
import { apiClient } from './apiClient'

vi.mock('./apiClient', () => ({
  apiClient: { getRaw: vi.fn() },
}))

describe('itemsApi.list', () => {
  beforeEach(() => vi.clearAllMocks())

  it('builds the query string and unwraps { data, meta.total }', async () => {
    vi.mocked(apiClient.getRaw).mockResolvedValue({ data: [{ id: '1' }], meta: { total: 7 } })
    const res = await itemsApi.list('article', { page: 1, rows: 10, sort: '-createdAt', search: 'x' })
    expect(apiClient.getRaw).toHaveBeenCalledWith('/items/article?limit=10&offset=10&sort=-createdAt&search=x')
    expect(res).toEqual({ data: [{ id: '1' }], total: 7 })
  })

  it('omits sort/search when not provided', async () => {
    vi.mocked(apiClient.getRaw).mockResolvedValue({ data: [], meta: { total: 0 } })
    await itemsApi.list('article', { page: 0, rows: 25 })
    expect(apiClient.getRaw).toHaveBeenCalledWith('/items/article?limit=25&offset=0')
  })
})
```

```typescript
// frontend/src/stores/schemaStore.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSchemaStore } from './schemaStore'
import { schemaApi } from '../api/schemaApi'

vi.mock('../api/schemaApi', () => ({ schemaApi: { getAll: vi.fn() } }))

describe('schemaStore', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('loads collections once and caches', async () => {
    vi.mocked(schemaApi.getAll).mockResolvedValue([{ name: 'article', label: 'Article', fields: [] }])
    const store = useSchemaStore()
    await store.load()
    await store.load() // second call should not re-fetch
    expect(schemaApi.getAll).toHaveBeenCalledOnce()
    expect(store.get('article')?.label).toBe('Article')
  })

  it('records loadError on failure without throwing', async () => {
    vi.mocked(schemaApi.getAll).mockRejectedValue(new Error('boom'))
    const store = useSchemaStore()
    await store.load()
    expect(store.loadError).toBe('boom')
    expect(store.collections).toEqual([])
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pnpm test apiClient itemsApi schemaStore`
Expected: FAIL — `getRaw` undefined; `Cannot find module './itemsApi'` / `./schemaStore`.

- [ ] **Step 3: Add `getRaw` to `apiClient`**

In `frontend/src/api/apiClient.ts`, add an `unwrap` option to `request` and a `getRaw` method. Change the private `request` signature and its final return, and add the method next to `get`:

```typescript
  getRaw<T>(path: string): Promise<T> {
    return this.request<T>('GET', path, undefined, { unwrap: false })
  }
```

```typescript
  private async request<T>(
    method: string,
    path: string,
    body?: unknown,
    opts?: { unwrap?: boolean },
  ): Promise<T> {
```

And the tail of `request` (envelope handling) becomes:

```typescript
    if (res.status === 204) return undefined as T
    const text = await res.text()
    if (!text) return undefined as T
    const payload = JSON.parse(text)
    if (opts?.unwrap === false) return payload as T
    return (payload?.data ?? payload) as T
```

(Existing `get`/`post` call `request` without `opts`, so they keep unwrapping — no behavior change.)

- [ ] **Step 4: Implement `schemaApi`, `itemsApi`, `schemaStore`**

```typescript
// frontend/src/api/schemaApi.ts
import { apiClient } from './apiClient'
import type { CollectionMeta } from '../types/schema'

export const schemaApi = {
  getAll(): Promise<CollectionMeta[]> {
    return apiClient.get<CollectionMeta[]>('/schema')
  },
  get(name: string): Promise<CollectionMeta> {
    return apiClient.get<CollectionMeta>(`/schema/${name}`)
  },
}
```

```typescript
// frontend/src/api/itemsApi.ts
import { apiClient } from './apiClient'
import { buildListQuery } from '../lib/buildListQuery'

export type ListOptions = { page: number; rows: number; sort?: string; search?: string }
export type ListResult = { data: Record<string, unknown>[]; total: number }

type ListEnvelope = { data: Record<string, unknown>[]; meta: { total: number } }

export const itemsApi = {
  async list(collection: string, opts: ListOptions): Promise<ListResult> {
    const params = buildListQuery(opts.page, opts.rows, opts.sort, opts.search)
    const qs = new URLSearchParams(params).toString()
    const path = qs ? `/items/${collection}?${qs}` : `/items/${collection}`
    const res = await apiClient.getRaw<ListEnvelope>(path)
    return { data: res.data, total: res.meta.total }
  },
}
```

```typescript
// frontend/src/stores/schemaStore.ts
import { defineStore } from 'pinia'
import { schemaApi } from '../api/schemaApi'
import type { CollectionMeta } from '../types/schema'

export const useSchemaStore = defineStore('schema', {
  state: () => ({
    collections: [] as CollectionMeta[],
    loaded: false,
    loadError: '',
  }),
  actions: {
    async load(): Promise<void> {
      if (this.loaded) return
      try {
        this.collections = await schemaApi.getAll()
        this.loaded = true
        this.loadError = ''
      } catch (e) {
        this.loadError = e instanceof Error ? e.message : 'Failed to load schema.'
      }
    },
    get(name: string): CollectionMeta | undefined {
      return this.collections.find((c) => c.name === name)
    },
  },
})
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pnpm test apiClient itemsApi schemaStore`
Expected: PASS (apiClient 4+1, itemsApi 2, schemaStore 2).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/apiClient.ts frontend/src/api/apiClient.test.ts frontend/src/api/schemaApi.ts frontend/src/api/itemsApi.ts frontend/src/api/itemsApi.test.ts frontend/src/stores/schemaStore.ts frontend/src/stores/schemaStore.test.ts
git commit -m "feat(frontend): schemaApi/itemsApi + schemaStore + apiClient.getRaw"
```

---

### Task 8: `CollectionNav` component

**Files:**
- Create: `frontend/src/components/CollectionNav.vue`
- Test: `frontend/src/components/CollectionNav.test.ts`

**Interfaces:**
- Consumes: `useAuthStore` (`user.isSuperAdmin`, `user.permissions`), `useSchemaStore` (`collections`, `loadError`, `load`), `buildNav` (Task 6), `useRouter`.
- Produces: a component exposing (via `defineExpose`) `model` — the PanelMenu model array `[{ key, label, items: [{ key, label, command }] }]`; each leaf `command` navigates to `{ name: 'collection-list', params: { name } }`. Renders a retry affordance when `schemaStore.loadError` is set.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/components/CollectionNav.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import CollectionNav from './CollectionNav.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('primevue/panelmenu', () => ({ default: { name: 'PanelMenu', props: ['model'], template: '<div class="pm" />' } }))

describe('CollectionNav', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  function seed(isSuperAdmin: boolean, permissions: Record<string, { read: boolean; write: boolean; delete: boolean }>) {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin, permissions }
    const schema = useSchemaStore()
    schema.collections = [
      { name: 'article', label: 'Article', group: 'Content', fields: [] },
      { name: 'user', label: 'User', group: 'System', fields: [] },
    ]
  }

  it('builds a grouped model of readable collections and navigates on command', () => {
    seed(false, { article: { read: true, write: false, delete: false } })
    const wrapper = mount(CollectionNav)
    const model = (wrapper.vm as unknown as { model: any[] }).model
    const names = model.flatMap((g) => g.items.map((i: any) => i.key))
    expect(names).toEqual(['article']) // 'user' filtered out

    model[0].items[0].command()
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  it('super-admin model includes every collection', () => {
    seed(true, {})
    const wrapper = mount(CollectionNav)
    const names = (wrapper.vm as unknown as { model: any[] }).model.flatMap((g) => g.items.map((i: any) => i.key))
    expect(names.sort()).toEqual(['article', 'user'])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test CollectionNav`
Expected: FAIL — `Cannot find module './CollectionNav.vue'`.

- [ ] **Step 3: Implement**

```vue
<!-- frontend/src/components/CollectionNav.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import PanelMenu from 'primevue/panelmenu'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { buildNav } from '../lib/buildNav'

const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()

const model = computed(() =>
  buildNav(schema.collections, auth.user?.isSuperAdmin ?? false, auth.user?.permissions ?? {}).map(
    (g) => ({
      key: g.group,
      label: g.group,
      items: g.items.map((it) => ({
        key: it.name,
        label: it.label,
        command: () => router.push({ name: 'collection-list', params: { name: it.name } }),
      })),
    }),
  ),
)

defineExpose({ model })
</script>

<template>
  <div v-if="schema.loadError" class="nav-error" role="alert">
    <span>{{ schema.loadError }}</span>
    <button type="button" @click="schema.load()">Retry</button>
  </div>
  <PanelMenu v-else :model="model" />
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test CollectionNav`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/CollectionNav.vue frontend/src/components/CollectionNav.test.ts
git commit -m "feat(frontend): CollectionNav (RBAC-filtered PanelMenu)"
```

---

### Task 9: `CollectionListView` view

**Files:**
- Create: `frontend/src/views/CollectionListView.vue`
- Test: `frontend/src/views/CollectionListView.test.ts`

**Interfaces:**
- Consumes: `useRoute` (`params.name`), `useAuthStore` (`canRead`), `useSchemaStore` (`get`), `itemsApi.list` (Task 7), `selectListColumns` (Task 4), `formatCell` (Task 5), PrimeVue `DataTable`/`Column`, `InputText`.
- Produces: a view exposing (via `defineExpose`) `loadItems`, `onPage`, `onSort`, `onSearchInput`, and reactive `rows`/`total`/`loading`/`error`. On mount (readable) it calls `itemsApi.list` with page 0. Unreadable → permission message, **no** API call. Load failure → error message. `onSort` builds `-field` for descending.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/views/CollectionListView.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import CollectionListView from './CollectionListView.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { itemsApi } from '../api/itemsApi'

vi.mock('vue-router', () => ({ useRoute: () => ({ params: { name: 'article' } }) }))
vi.mock('../api/itemsApi', () => ({ itemsApi: { list: vi.fn() } }))
vi.mock('primevue/datatable', () => ({ default: { name: 'DataTable', template: '<div><slot /></div>' } }))
vi.mock('primevue/column', () => ({ default: { name: 'Column', template: '<div />' } }))
vi.mock('primevue/inputtext', () => ({ default: { name: 'InputText', template: '<input />' } }))

function seedSchema() {
  const schema = useSchemaStore()
  schema.collections = [{
    name: 'article', label: 'Article', defaultDisplayField: 'status',
    fields: [{ name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
      sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
      options: [{ value: 'draft', label: 'Draft' }] }],
  }]
}

describe('CollectionListView', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('loads items on mount for a readable collection', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
    mount(CollectionListView)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: undefined, search: undefined })
  })

  it('shows a permission message and makes no API call when not readable', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: false, permissions: {} }
    const wrapper = mount(CollectionListView)
    await flushPromises()
    expect(itemsApi.list).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain("don't have access")
  })

  it('shows an error message when the list load fails', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockRejectedValue(new Error('Server error.'))
    const wrapper = mount(CollectionListView)
    await flushPromises()
    expect(wrapper.text()).toContain('Server error.')
  })

  it('onSort builds a descending token and reloads', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mount(CollectionListView)
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(wrapper.vm as unknown as { onSort: (e: unknown) => void }).onSort({ sortField: 'status', sortOrder: -1 })
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: '-status', search: undefined })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test CollectionListView`
Expected: FAIL — `Cannot find module './CollectionListView.vue'`.

- [ ] **Step 3: Implement**

```vue
<!-- frontend/src/views/CollectionListView.vue -->
<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { itemsApi } from '../api/itemsApi'
import { selectListColumns } from '../lib/selectListColumns'
import { formatCell } from '../lib/formatCell'
import type { FieldMeta } from '../types/schema'

const route = useRoute()
const auth = useAuthStore()
const schema = useSchemaStore()

const name = computed(() => route.params.name as string)
const meta = computed(() => schema.get(name.value))
const canRead = computed(() => auth.canRead(name.value))
const columns = computed(() => (meta.value ? selectListColumns(meta.value) : []))

const rows = ref<Record<string, unknown>[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const page = ref(0)
const perPage = ref(25)
const sortField = ref<string | undefined>(undefined)
const sortOrder = ref<1 | -1 | undefined>(undefined)
const search = ref('')

function fieldOf(colField: string): FieldMeta | undefined {
  return meta.value?.fields.find((f) => f.name === colField)
}

async function loadItems(): Promise<void> {
  if (!meta.value || !canRead.value) return
  loading.value = true
  error.value = ''
  try {
    const sort = sortField.value
      ? sortOrder.value === -1 ? `-${sortField.value}` : sortField.value
      : undefined
    const res = await itemsApi.list(name.value, {
      page: page.value,
      rows: perPage.value,
      sort,
      search: search.value || undefined,
    })
    rows.value = res.data
    total.value = res.total
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load items.'
    rows.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

function onPage(e: { page: number; rows: number }): void {
  page.value = e.page
  perPage.value = e.rows
  loadItems()
}

function onSort(e: { sortField: string | null; sortOrder: number | null }): void {
  sortField.value = e.sortField ?? undefined
  sortOrder.value = (e.sortOrder as 1 | -1 | null) ?? undefined
  page.value = 0
  loadItems()
}

let searchTimer: ReturnType<typeof setTimeout> | undefined
function onSearchInput(value: string): void {
  search.value = value
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    page.value = 0
    loadItems()
  }, 300)
}

watch(name, () => {
  page.value = 0
  sortField.value = undefined
  sortOrder.value = undefined
  search.value = ''
  loadItems()
})

onMounted(loadItems)

defineExpose({ loadItems, onPage, onSort, onSearchInput, rows, total, loading, error })
</script>

<template>
  <section class="collection-list">
    <template v-if="!meta">
      <p class="notice">Collection not found.</p>
    </template>
    <template v-else-if="!canRead">
      <p class="notice">You don't have access to this collection.</p>
    </template>
    <template v-else>
      <header class="list-header">
        <h2>{{ meta.label }}</h2>
        <InputText
          type="text"
          placeholder="Search"
          @input="onSearchInput(($event.target as HTMLInputElement).value)"
        />
      </header>

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
      >
        <Column
          v-for="col in columns"
          :key="col.field"
          :field="col.field"
          :header="col.header"
          :sortable="col.sortable"
        >
          <template #body="{ data }">
            {{ formatCell(data[col.field], fieldOf(col.field)!) }}
          </template>
        </Column>
        <template #empty>No records.</template>
      </DataTable>
    </template>
  </section>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test CollectionListView`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/views/CollectionListView.vue frontend/src/views/CollectionListView.test.ts
git commit -m "feat(frontend): CollectionListView (lazy DataTable, permission/error states)"
```

---

### Task 10: Wire router + AppShell (build-verified)

**Files:**
- Modify: `frontend/src/router/index.ts` (add `collection-list` route)
- Modify: `frontend/src/layouts/AppShell.vue` (mount `CollectionNav`, trigger `schemaStore.load()`)
- Modify: `frontend/src/layouts/AppShell.test.ts` (stub `CollectionNav`)

**Interfaces:**
- Consumes: `CollectionListView` (Task 9), `CollectionNav` (Task 8), `useSchemaStore` (Task 7).
- Produces: route `{ path: 'collections/:name', name: 'collection-list', component: CollectionListView }` as a child of the `/` `AppShell` route; `AppShell` renders `<CollectionNav />` and calls `schemaStore.load()` on mount.

- [ ] **Step 1: Add the route**

```typescript
// frontend/src/router/index.ts
import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { authGuard } from './guard'
import AppShell from '../layouts/AppShell.vue'
import LoginView from '../views/LoginView.vue'
import DashboardView from '../views/DashboardView.vue'
import CollectionListView from '../views/CollectionListView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: LoginView, meta: { public: true } },
    {
      path: '/',
      component: AppShell,
      children: [
        { path: '', name: 'dashboard', component: DashboardView },
        { path: 'collections/:name', name: 'collection-list', component: CollectionListView },
      ],
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  return authGuard({ name: to.name as string, meta: to.meta }, auth.isAuthenticated)
})

export default router
```

- [ ] **Step 2: Wire AppShell + update its test (stub CollectionNav)**

Edit `frontend/src/layouts/AppShell.vue`:

```vue
<!-- frontend/src/layouts/AppShell.vue -->
<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import CollectionNav from '../components/CollectionNav.vue'

const auth = useAuthStore()
const schema = useSchemaStore()
const router = useRouter()

onMounted(() => {
  schema.load()
})

async function onLogout() {
  await auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <header>
      <span class="brand">StruoCMS</span>
      <button type="button" class="logout" @click="onLogout">Log out</button>
    </header>
    <nav><CollectionNav /></nav>
    <main><router-view /></main>
  </div>
</template>
```

Edit `frontend/src/layouts/AppShell.test.ts` — the `vi.mock('vue-router', …)` stays; add a stub for `CollectionNav` in the mount call so it doesn't pull in the real schema store/PanelMenu. Change the mount line to:

```typescript
    const wrapper = mount(AppShell, {
      global: { stubs: { RouterView: true, CollectionNav: true } },
    })
```

- [ ] **Step 3: Run the full frontend unit suite**

Run (inside `frontend/`): `pnpm test`
Expected: PASS — all prior 7a tests + Task 2–9 tests (authStore 8, buildListQuery 3, selectListColumns 3, formatCell 4, buildNav 3, apiClient 5, itemsApi 2, schemaStore 2, CollectionNav 2, CollectionListView 4, plus existing LoginView/AppShell/guard/smoke).

- [ ] **Step 4: Verify the production build compiles (router wiring + all components)**

Run (inside `frontend/`): `pnpm build`
Expected: succeeds (dist produced), no `vue-tsc` type errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/router/index.ts frontend/src/layouts/AppShell.vue frontend/src/layouts/AppShell.test.ts
git commit -m "feat(frontend): wire collection-list route + CollectionNav into AppShell"
```

---

### Task 11: Playwright E2E + verification gate + docs

**Files:**
- Create: `frontend/e2e/collections.spec.ts`
- Modify: `frontend/e2e/README.md` (seed note for browse E2E)
- Modify: `docs/ROADMAP.md` (mark Phase 7b done + link spec/plan)

**Interfaces:**
- Consumes: running API (dev, HTTP on the Vite-proxy port) with a seeded admin (from 7a's `E2E_EMAIL`/`E2E_PASSWORD`) and at least one sample `article` row; the running Vite dev server (from 7a `playwright.config.ts` `webServer`).

- [ ] **Step 1: Write the E2E spec**

```typescript
// frontend/e2e/collections.spec.ts
import { test, expect } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'

test('browse a collection list', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)

  // Open the Article collection from the nav (PanelMenu group must be expanded to reveal the leaf).
  await page.getByText('Content', { exact: true }).click()
  await page.getByText('Article', { exact: true }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // The list renders its column header (DefaultDisplayField "Status").
  await expect(page.getByText('Status', { exact: true })).toBeVisible()

  await page.click('button.logout')
  await expect(page).toHaveURL(/\/login$/)
})
```

> If the seeded admin is a super-admin, the "Content" group and "Article" leaf are present. Row
> assertions require ≥1 seeded `article`; seeding is documented in Step 2. The header assertion holds
> even for an empty collection (columns come from schema, not data).

- [ ] **Step 2: Document seeding in `frontend/e2e/README.md`**

Append a section:

```markdown
## Phase 7b browse E2E (collections.spec.ts)

Requires the same seeded admin as the auth E2E, plus at least one `article` row so the table is
non-empty (the header assertion passes even when empty). Seed one via the authenticated API after
login, e.g. from a REST client or `curl` against the dev API:

    POST /api/items/article   { "status": "published" }

(super-admin session cookie required). The sample blog collection `article` is served because
`Struo:ContentAssemblies` includes `Struo.Sample.Blog` in the dev configuration.
```

- [ ] **Step 3: Run the E2E**

Ensure the dev API is running (HTTP on the Vite-proxy port) with the seeded admin, then run (inside `frontend/`):

```bash
pnpm e2e
```

Expected: `collections.spec.ts` passes (and the existing `auth.spec.ts` still passes).

- [ ] **Step 4: Full verification gate (evidence, spec §8)**

- Backend: `dotnet build` clean (warnings-as-errors); `dotnet test` all green (prior + `AuthMePermissionsTests`).
- Frontend: `pnpm test` green; `pnpm build` succeeds; `pnpm e2e` passes.
- **Live gate:** with the API on **live Postgres + Redis**, log in as (a) a super-admin and (b) a limited editor; confirm `GET /api/auth/me` returns the correct `isSuperAdmin`/`permissions`, the nav shows only readable collections, and a collection list paginates/sorts. Record evidence (SQLite-green ≠ Postgres-correct).
- Version policy (§17.5): confirm no frontend package was added (7b uses only already-installed `primevue` components); if any was, it came from `pnpm add`.

- [ ] **Step 5: Update ROADMAP + commit**

Update `docs/ROADMAP.md`: add a Phase 7b row (Status done, link
`superpowers/specs/2026-07-02-phase7b-collection-lists-design.md` and
`superpowers/plans/2026-07-02-phase7b-collection-lists.md`); update the "Status at a glance" / "Next up"
lines to point past 7b (7c: item detail + create/edit forms).

```bash
git add frontend/e2e/collections.spec.ts frontend/e2e/README.md docs/ROADMAP.md
git commit -m "test(frontend): Phase 7b browse E2E + roadmap/docs update"
```

---

## Self-Review

**Spec coverage:**
- §1 `/me` permissions (additive) → Task 1.
- §2 architecture / file map: types+authStore → Task 2; pure helpers → Tasks 3–6; schemaApi/itemsApi/schemaStore (+`getRaw`) → Task 7; CollectionNav → Task 8; CollectionListView → Task 9; router+AppShell → Task 10.
- §3 data flow (boot → `fetchCurrentUser`; `schemaStore.load` in AppShell; nav from `buildNav`; list via `buildListQuery`→`itemsApi`; `total` binding; switch resets) → Tasks 2, 7, 8, 9, 10.
- §4 GET-now transport, DSL encapsulated in `itemsApi`+`buildListQuery` → Tasks 3, 7.
- §5 smart-subset columns + `formatCell` → Tasks 4, 5.
- §6 error handling (401 via existing handler; 403 permission card + no call; 404 "not found"; load-failure error; empty slot; schema-load error + retry) → Tasks 7 (schemaStore error), 8 (nav retry), 9 (view states).
- §7 testing (backend integration; four pure-helper unit suites; component tests; E2E) → Tasks 1, 3–9, 11.
- §8 verification gate (backend/frontend/live) → Task 11.
- Out-of-scope (mutations, detail, filter UI, locale switch, file/richtext render) correctly absent.

**Placeholder scan:** No TBD/TODO. Every code step shows complete code. The one cross-task ordering note (Task 10 build verifies the router wiring that imports Task 9's view) is explicit.

**Type consistency:** `CollectionMeta`/`FieldMeta`/`FieldOption`/`CollectionPermission`/`CurrentUser` defined in Task 2 and used identically in Tasks 4–9. `apiClient.getRaw<T>` (Task 7) consumed by `itemsApi` (Task 7). `ListOptions`/`ListResult` from Task 7 consumed by Task 9. `ColumnDef` from Task 4 consumed by Task 9. `NavGroup`/`NavItem` from Task 6 consumed by Task 8. Route name `collection-list` consistent across Tasks 8, 9 (indirect), 10, 11. `buildListQuery(page, rows, sort?, search?)` signature identical in Tasks 3, 7. `formatCell(value, field)` identical in Tasks 5, 9. `buildNav(collections, isSuperAdmin, permissions)` identical in Tasks 6, 8. Selectors used by E2E (`input[type=email/password]`, `button[type=submit]`, `button.logout`, text `Content`/`Article`/`Status`) align with 7a views + Task 9 output.
