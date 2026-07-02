# Phase 7c — Item Detail + Create/Edit Forms Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the admin SPA schema-driven create/edit/delete forms for scalar fields with per-locale i18n editing, plus one additive backend endpoint exposing enabled languages.

**Architecture:** Backend adds a thin `GET /api/languages` over the existing `ILanguageProvider`. Frontend follows the Phase 7a/7b layering (`api` → `stores` → pure helpers → components → router): `apiClient` gains `put`/`delete`; `itemsApi` gains `get`/`create`/`update`/`remove`; new pure helpers do all payload/validation logic; a `FieldInput` dispatcher maps interfaces to PrimeVue controls; `ItemForm` composes shared + per-locale-tab fields; `ItemFormView` orchestrates load/save/delete/permissions on routes `/collections/:name/new` and `/collections/:name/:id`.

**Tech Stack:** .NET 10 / ASP.NET Core Controllers · SqlSugar · Vue 3 + TypeScript + PrimeVue 4 (Aura) · Pinia · Vue Router · Vitest + Vue Test Utils · Playwright.

## Global Constraints

- Outbound JSON = camelCase (backend already configured). FieldInterface values arrive as camelCase strings (`richText`, `dateTime`, `multiSelect`, …).
- Dependency rule: backend change confined to `Struo.Api` (no Domain/Application contract change). Frontend wire/DSL confined to `itemsApi` + payload helpers; views/components never touch raw payload strings.
- TDD: failing test first; acceptance = verification gate with evidence (§17.2). Many small files; functions <50 lines.
- No new frontend runtime dependency in 7c (TipTap is Phase 7d). Any package added later uses `pnpm add` (never hand-authored versions).
- Immutability applies to domain data; local Vue form-model state is mutated in place via `reactive` + `v-model` (idiomatic). Payloads are always built fresh by `buildItemPayload`.
- Spec: `docs/superpowers/specs/2026-07-02-phase7c-item-forms-design.md`.
- Run frontend commands from `frontend/`. Run backend commands from repo root.

---

### Task 1: Backend — `GET /api/languages`

**Files:**
- Create: `src/Struo.Api/Controllers/LanguagesController.cs`
- Test: `tests/Struo.Tests/Api/LanguagesEndpointTests.cs`

**Interfaces:**
- Consumes: `Struo.Application.Localization.ILanguageProvider` (`Enabled(): IReadOnlyList<LanguageInfo>` where `LanguageInfo(Code, Name, IsDefault, Enabled, Sort)`); `Struo.Api.Auth.AuthSchemes.CookieOrBearer`.
- Produces: `GET /api/languages` → `{ "data": [ { "code", "name", "isDefault" } ] }`; anonymous → 401.

- [ ] **Step 1: Write the failing test.** Mirror the class setup (factory, SQLite, login helper) of the existing `tests/Struo.Tests/Api/AuthMePermissionsTests.cs`. Add:

```csharp
[Fact]
public async Task Languages_ReturnsEnabledLocales_WithExactlyOneDefault()
{
    var client = Factory.CreateClient();
    await LoginAsSuperAdminAsync(client); // same helper AuthMePermissionsTests uses

    var res = await client.GetAsync("/api/languages");
    res.StatusCode.Should().Be(HttpStatusCode.OK);

    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    var arr = doc.RootElement.GetProperty("data");
    arr.GetArrayLength().Should().BeGreaterThan(0);
    arr.EnumerateArray().Count(e => e.GetProperty("isDefault").GetBoolean()).Should().Be(1);
    arr.EnumerateArray().Select(e => e.GetProperty("code").GetString())
       .Should().Contain("en");
}

[Fact]
public async Task Languages_Anonymous_Returns401()
{
    var client = Factory.CreateClient();
    var res = await client.GetAsync("/api/languages");
    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
```

- [ ] **Step 2: Run it and confirm it fails.**
Run: `dotnet test tests/Struo.Tests --filter LanguagesEndpointTests`
Expected: FAIL (404 — controller not found).

- [ ] **Step 3: Implement the controller.**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Application.Localization;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/languages")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class LanguagesController(ILanguageProvider languages) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
    {
        var data = languages.Enabled()
            .Select(l => new { code = l.Code, name = l.Name, isDefault = l.IsDefault })
            .ToList();
        return Ok(new { data });
    }
}
```

- [ ] **Step 4: Run tests and confirm pass.**
Run: `dotnet test tests/Struo.Tests --filter LanguagesEndpointTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Full build + suite.**
Run: `dotnet build` then `dotnet test`
Expected: clean build (warnings-as-errors); all green (prior + 2 new).

- [ ] **Step 6: Commit.**

```bash
git add src/Struo.Api/Controllers/LanguagesController.cs tests/Struo.Tests/Api/LanguagesEndpointTests.cs
git commit -m "feat(api): GET /api/languages exposes enabled locales"
```

---

### Task 2: Frontend — `apiClient` gains `put` / `delete`

**Files:**
- Modify: `frontend/src/api/apiClient.ts`
- Test: `frontend/src/api/apiClient.test.ts` (extend)

**Interfaces:**
- Produces: `apiClient.put<T>(path, body?): Promise<T>` (unwrap `{data}`, throw on non-2xx, invoke 401 handler); `apiClient.delete<T>(path): Promise<T>` (tolerates 204/empty → `undefined`).

- [ ] **Step 1: Write failing tests.** Append to `apiClient.test.ts`, following the existing `fetch` mock pattern in that file:

```ts
it('put unwraps the data envelope', async () => {
  globalThis.fetch = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ data: { id: '1' } }), { status: 200 }))
  const c = new ApiClient('/api')
  await expect(c.put('/items/article/1', { x: 1 })).resolves.toEqual({ id: '1' })
})

it('delete tolerates 204 no-content', async () => {
  globalThis.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
  const c = new ApiClient('/api')
  await expect(c.delete('/items/article/1')).resolves.toBeUndefined()
})

it('put throws the server error message on non-2xx', async () => {
  globalThis.fetch = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ error: { message: 'nope' } }), { status: 400 }))
  const c = new ApiClient('/api')
  await expect(c.put('/x', {})).rejects.toThrow('nope')
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/api/apiClient.test.ts`
Expected: FAIL (`put`/`delete` not a function).

- [ ] **Step 3: Add the methods** after `getRaw` in `apiClient.ts`:

```ts
  put<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', path, body)
  }

  delete<T>(path: string): Promise<T> {
    return this.request<T>('DELETE', path)
  }
```

(`request` already returns `undefined as T` for 204/empty, so `delete` needs no special handling.)

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/api/apiClient.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/api/apiClient.ts frontend/src/api/apiClient.test.ts
git commit -m "feat(frontend): apiClient put/delete"
```

---

### Task 3: Frontend — `LanguageInfo` type + `languagesApi`

**Files:**
- Modify: `frontend/src/types/schema.ts`
- Create: `frontend/src/api/languagesApi.ts`
- Test: `frontend/src/api/languagesApi.test.ts`

**Interfaces:**
- Produces: `type LanguageInfo = { code: string; name: string; isDefault: boolean }`; `languagesApi.getEnabled(): Promise<LanguageInfo[]>`.

- [ ] **Step 1: Add the type.** Append to `frontend/src/types/schema.ts`:

```ts
export type LanguageInfo = { code: string; name: string; isDefault: boolean }
```

- [ ] **Step 2: Write the failing test** `frontend/src/api/languagesApi.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { languagesApi } from './languagesApi'
import { apiClient } from './apiClient'

describe('languagesApi', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('getEnabled fetches /languages and returns the list', async () => {
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue([
      { code: 'en', name: 'English', isDefault: true },
    ])
    const res = await languagesApi.getEnabled()
    expect(spy).toHaveBeenCalledWith('/languages')
    expect(res).toEqual([{ code: 'en', name: 'English', isDefault: true }])
  })
})
```

- [ ] **Step 3: Run and confirm fail.**
Run: `pnpm test src/api/languagesApi.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 4: Implement** `frontend/src/api/languagesApi.ts`:

```ts
import { apiClient } from './apiClient'
import type { LanguageInfo } from '../types/schema'

export const languagesApi = {
  getEnabled(): Promise<LanguageInfo[]> {
    return apiClient.get<LanguageInfo[]>('/languages')
  },
}
```

- [ ] **Step 5: Run and confirm pass.**
Run: `pnpm test src/api/languagesApi.test.ts`
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add frontend/src/types/schema.ts frontend/src/api/languagesApi.ts frontend/src/api/languagesApi.test.ts
git commit -m "feat(frontend): LanguageInfo type + languagesApi"
```

---

### Task 4: Frontend — `languageStore`

**Files:**
- Create: `frontend/src/stores/languageStore.ts`
- Test: `frontend/src/stores/languageStore.test.ts`

**Interfaces:**
- Consumes: `languagesApi.getEnabled`.
- Produces: `useLanguageStore()` with state `languages: LanguageInfo[]`, `loaded`, `loadError`; getter `defaultCode: string`; action `load()` (fetch once, cache).

- [ ] **Step 1: Write failing test** `frontend/src/stores/languageStore.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useLanguageStore } from './languageStore'
import { languagesApi } from '../api/languagesApi'

describe('languageStore', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.restoreAllMocks() })

  it('load caches and exposes defaultCode', async () => {
    const spy = vi.spyOn(languagesApi, 'getEnabled').mockResolvedValue([
      { code: 'en', name: 'English', isDefault: true },
      { code: 'zh-TW', name: '繁體中文', isDefault: false },
    ])
    const store = useLanguageStore()
    await store.load()
    await store.load() // cached, no second call
    expect(spy).toHaveBeenCalledTimes(1)
    expect(store.defaultCode).toBe('en')
    expect(store.languages).toHaveLength(2)
  })

  it('records loadError on failure', async () => {
    vi.spyOn(languagesApi, 'getEnabled').mockRejectedValue(new Error('boom'))
    const store = useLanguageStore()
    await store.load()
    expect(store.loadError).toBe('boom')
    expect(store.loaded).toBe(false)
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/stores/languageStore.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement** `frontend/src/stores/languageStore.ts`:

```ts
import { defineStore } from 'pinia'
import { languagesApi } from '../api/languagesApi'
import type { LanguageInfo } from '../types/schema'

export const useLanguageStore = defineStore('language', {
  state: () => ({
    languages: [] as LanguageInfo[],
    loaded: false,
    loadError: '',
  }),
  getters: {
    defaultCode: (state): string =>
      state.languages.find((l) => l.isDefault)?.code ?? state.languages[0]?.code ?? '',
  },
  actions: {
    async load(): Promise<void> {
      if (this.loaded) return
      try {
        this.languages = await languagesApi.getEnabled()
        this.loaded = true
        this.loadError = ''
      } catch (e) {
        this.loadError = e instanceof Error ? e.message : 'Failed to load languages.'
      }
    },
  },
})
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/stores/languageStore.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/stores/languageStore.ts frontend/src/stores/languageStore.test.ts
git commit -m "feat(frontend): languageStore (cached enabled locales + defaultCode)"
```

---

### Task 5: Frontend — `itemsApi` gains `get` / `create` / `update` / `remove`

**Files:**
- Modify: `frontend/src/api/itemsApi.ts`
- Test: `frontend/src/api/itemsApi.test.ts` (extend)

**Interfaces:**
- Consumes: `apiClient.get/post/put/delete`.
- Produces: `itemsApi.get(collection, id, opts?: { locale?: string }): Promise<Record<string,unknown>>`; `create(collection, payload): Promise<Record<string,unknown>>`; `update(collection, id, payload): Promise<Record<string,unknown>>`; `remove(collection, id): Promise<void>`.

- [ ] **Step 1: Write failing tests.** Append to `itemsApi.test.ts` (follow its existing mock style):

```ts
it('get fetches a single item by id', async () => {
  const spy = vi.spyOn(apiClient, 'get').mockResolvedValue({ id: '1', status: 'draft' })
  const res = await itemsApi.get('article', '1')
  expect(spy).toHaveBeenCalledWith('/items/article/1')
  expect(res).toEqual({ id: '1', status: 'draft' })
})

it('get passes locale query when provided', async () => {
  const spy = vi.spyOn(apiClient, 'get').mockResolvedValue({})
  await itemsApi.get('article', '1', { locale: 'zh-TW' })
  expect(spy).toHaveBeenCalledWith('/items/article/1?locale=zh-TW')
})

it('create posts the payload', async () => {
  const spy = vi.spyOn(apiClient, 'post').mockResolvedValue({ id: '9' })
  await itemsApi.create('article', { status: 'draft' })
  expect(spy).toHaveBeenCalledWith('/items/article', { status: 'draft' })
})

it('update puts the payload', async () => {
  const spy = vi.spyOn(apiClient, 'put').mockResolvedValue({ id: '1' })
  await itemsApi.update('article', '1', { status: 'published' })
  expect(spy).toHaveBeenCalledWith('/items/article/1', { status: 'published' })
})

it('remove deletes by id', async () => {
  const spy = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined)
  await itemsApi.remove('article', '1')
  expect(spy).toHaveBeenCalledWith('/items/article/1')
})
```

Ensure the top of the file imports `apiClient`: `import { apiClient } from './apiClient'` (already present).

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/api/itemsApi.test.ts`
Expected: FAIL (`get`/`create`/… not functions).

- [ ] **Step 3: Add the methods** inside the `itemsApi` object in `itemsApi.ts`:

```ts
  async get(collection: string, id: string, opts?: { locale?: string }): Promise<Record<string, unknown>> {
    const qs = opts?.locale ? `?locale=${encodeURIComponent(opts.locale)}` : ''
    return apiClient.get<Record<string, unknown>>(`/items/${collection}/${id}${qs}`)
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
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/api/itemsApi.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/api/itemsApi.ts frontend/src/api/itemsApi.test.ts
git commit -m "feat(frontend): itemsApi get/create/update/remove"
```

---

### Task 6: Frontend — `authStore` gains `canWrite` / `canDelete`

**Files:**
- Modify: `frontend/src/stores/authStore.ts`
- Test: `frontend/src/stores/authStore.test.ts` (extend)

**Interfaces:**
- Produces: getters `canWrite(collection): boolean`, `canDelete(collection): boolean` (super-admin short-circuit; else `permissions[name].write|delete === true`).

- [ ] **Step 1: Write failing tests.** Append to `authStore.test.ts`:

```ts
it('canWrite: super-admin true; limited user by grant', () => {
  const store = useAuthStore()
  store.user = { id: '1', isSuperAdmin: true, permissions: {} }
  expect(store.canWrite('article')).toBe(true)
  store.user = { id: '2', isSuperAdmin: false, permissions: { article: { read: true, write: true, delete: false } } }
  expect(store.canWrite('article')).toBe(true)
  expect(store.canDelete('article')).toBe(false)
  expect(store.canWrite('category')).toBe(false)
})

it('canWrite false when unauthenticated', () => {
  const store = useAuthStore()
  store.user = null
  expect(store.canWrite('article')).toBe(false)
})
```

(Uses the same `setActivePinia(createPinia())` setup already present in the file.)

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/stores/authStore.test.ts`
Expected: FAIL.

- [ ] **Step 3: Add the getters** after `canRead` in `authStore.ts`:

```ts
    canWrite: (state) => (collection: string): boolean =>
      !!state.user && (state.user.isSuperAdmin || state.user.permissions?.[collection]?.write === true),
    canDelete: (state) => (collection: string): boolean =>
      !!state.user && (state.user.isSuperAdmin || state.user.permissions?.[collection]?.delete === true),
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/stores/authStore.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/stores/authStore.ts frontend/src/stores/authStore.test.ts
git commit -m "feat(frontend): authStore canWrite/canDelete getters"
```

---

### Task 7: Frontend — `fieldInputKind` pure classifier

**Files:**
- Create: `frontend/src/lib/fieldInputKind.ts`
- Test: `frontend/src/lib/fieldInputKind.test.ts`

**Interfaces:**
- Produces: `type InputKind = 'text'|'textarea'|'richtext'|'number'|'boolean'|'date'|'time'|'datetime'|'select'|'radio'|'divider'|'readonly'`; `fieldInputKind(iface: string): InputKind`.

- [ ] **Step 1: Write the failing test** `fieldInputKind.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { fieldInputKind } from './fieldInputKind'

describe('fieldInputKind', () => {
  it('maps scalar text-family to text', () => {
    for (const i of ['text', 'slug', 'email', 'url', 'color', 'phone', 'password'])
      expect(fieldInputKind(i)).toBe('text')
  })
  it('maps textarea family and richText', () => {
    expect(fieldInputKind('textarea')).toBe('textarea')
    expect(fieldInputKind('markdown')).toBe('textarea')
    expect(fieldInputKind('code')).toBe('textarea')
    expect(fieldInputKind('richText')).toBe('richtext')
  })
  it('maps number, boolean, date families', () => {
    expect(fieldInputKind('number')).toBe('number')
    expect(fieldInputKind('boolean')).toBe('boolean')
    expect(fieldInputKind('checkbox')).toBe('boolean')
    expect(fieldInputKind('date')).toBe('date')
    expect(fieldInputKind('time')).toBe('time')
    expect(fieldInputKind('dateTime')).toBe('datetime')
  })
  it('maps select/radio and divider', () => {
    expect(fieldInputKind('select')).toBe('select')
    expect(fieldInputKind('radio')).toBe('radio')
    expect(fieldInputKind('divider')).toBe('divider')
  })
  it('falls back to readonly for deferred/unknown interfaces', () => {
    for (const i of ['file', 'image', 'files', 'multiSelect', 'checkboxGroup', 'tags', 'json', 'keyValue', 'repeater', 'uuid', 'somethingNew'])
      expect(fieldInputKind(i)).toBe('readonly')
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/lib/fieldInputKind.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement** `fieldInputKind.ts`:

```ts
export type InputKind =
  | 'text' | 'textarea' | 'richtext' | 'number' | 'boolean'
  | 'date' | 'time' | 'datetime' | 'select' | 'radio' | 'divider' | 'readonly'

const MAP: Record<string, InputKind> = {
  text: 'text', slug: 'text', email: 'text', url: 'text', color: 'text', phone: 'text', password: 'text',
  textarea: 'textarea', markdown: 'textarea', code: 'textarea',
  richText: 'richtext',
  number: 'number', slider: 'number', rating: 'number',
  boolean: 'boolean', checkbox: 'boolean',
  date: 'date', time: 'time', dateTime: 'datetime',
  select: 'select', radio: 'radio',
  divider: 'divider',
}

export function fieldInputKind(iface: string): InputKind {
  return MAP[iface] ?? 'readonly'
}
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/lib/fieldInputKind.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/lib/fieldInputKind.ts frontend/src/lib/fieldInputKind.test.ts
git commit -m "feat(frontend): fieldInputKind classifier"
```

---

### Task 8: Frontend — form-model types + `splitFields`

**Files:**
- Create: `frontend/src/types/itemForm.ts`
- Create: `frontend/src/lib/splitFields.ts`
- Test: `frontend/src/lib/splitFields.test.ts`

**Interfaces:**
- Produces: `type FormModel = { shared: Record<string,unknown>; translations: Record<string, Record<string,unknown>> }`; `splitFields(meta: CollectionMeta): { shared: FieldMeta[]; translatable: FieldMeta[] }` — excludes `isSystem`, orders both by `sort`.

- [ ] **Step 1: Create the types** `frontend/src/types/itemForm.ts`:

```ts
export type LocaleValues = Record<string, unknown>
export type FormModel = {
  shared: Record<string, unknown>
  translations: Record<string, LocaleValues>
}
```

- [ ] **Step 2: Write the failing test** `splitFields.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { splitFields } from './splitFields'
import type { CollectionMeta, FieldMeta } from '../types/schema'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta = (fields: FieldMeta[]): CollectionMeta => ({ name: 'article', label: 'Article', fields })

describe('splitFields', () => {
  it('partitions by translatable, excludes system, orders by sort', () => {
    const m = meta([
      field('id', { isSystem: true, sort: 0 }),
      field('body', { translatable: true, sort: 2 }),
      field('status', { sort: 1 }),
      field('title', { translatable: true, sort: 1 }),
    ])
    const { shared, translatable } = splitFields(m)
    expect(shared.map((f) => f.name)).toEqual(['status'])
    expect(translatable.map((f) => f.name)).toEqual(['title', 'body'])
  })
})
```

- [ ] **Step 3: Run and confirm fail.**
Run: `pnpm test src/lib/splitFields.test.ts`
Expected: FAIL.

- [ ] **Step 4: Implement** `splitFields.ts`:

```ts
import type { CollectionMeta, FieldMeta } from '../types/schema'

export type SplitFields = { shared: FieldMeta[]; translatable: FieldMeta[] }

export function splitFields(meta: CollectionMeta): SplitFields {
  const editable = meta.fields
    .filter((f) => !f.isSystem)
    .slice()
    .sort((a, b) => a.sort - b.sort)
  return {
    shared: editable.filter((f) => !f.translatable),
    translatable: editable.filter((f) => f.translatable),
  }
}
```

- [ ] **Step 5: Run and confirm pass.**
Run: `pnpm test src/lib/splitFields.test.ts`
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add frontend/src/types/itemForm.ts frontend/src/lib/splitFields.ts frontend/src/lib/splitFields.test.ts
git commit -m "feat(frontend): FormModel types + splitFields"
```

---

### Task 9: Frontend — `parseItemToForm` + `blankItemForm`

**Files:**
- Create: `frontend/src/lib/parseItemToForm.ts`
- Test: `frontend/src/lib/parseItemToForm.test.ts`

**Interfaces:**
- Consumes: `splitFields`; types `CollectionMeta`, `LanguageInfo`, `FormModel`.
- Produces: `parseItemToForm(meta, item: Record<string,unknown>, locales: LanguageInfo[]): FormModel`; `blankItemForm(meta, locales): FormModel`.

- [ ] **Step 1: Write the failing test** `parseItemToForm.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { parseItemToForm, blankItemForm } from './parseItemToForm'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('id', { isSystem: true }),
  field('status', { sort: 1 }),
  field('title', { translatable: true, sort: 2 }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]

describe('parseItemToForm', () => {
  it('inflates shared + per-locale translations, seeding missing locales', () => {
    const item = { id: '1', status: 'published', translations: { en: { title: 'Hello' } } }
    const model = parseItemToForm(meta, item, locales)
    expect(model.shared).toEqual({ status: 'published' })
    expect(model.translations.en).toEqual({ title: 'Hello' })
    expect(model.translations['zh-TW']).toEqual({ title: '' })
  })
})

describe('blankItemForm', () => {
  it('seeds empty shared + empty per-locale entries', () => {
    const model = blankItemForm(meta, locales)
    expect(model.shared).toEqual({ status: '' })
    expect(model.translations.en).toEqual({ title: '' })
    expect(model.translations['zh-TW']).toEqual({ title: '' })
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/lib/parseItemToForm.test.ts`
Expected: FAIL.

- [ ] **Step 3: Implement** `parseItemToForm.ts`:

```ts
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'

export function parseItemToForm(
  meta: CollectionMeta,
  item: Record<string, unknown>,
  locales: LanguageInfo[],
): FormModel {
  const { shared, translatable } = splitFields(meta)
  const sharedModel: Record<string, unknown> = {}
  for (const f of shared) sharedModel[f.name] = item[f.name] ?? ''

  const itemTranslations = (item.translations ?? {}) as Record<string, Record<string, unknown>>
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const src = itemTranslations[loc.code] ?? {}
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = src[f.name] ?? ''
    translations[loc.code] = entry
  }
  return { shared: sharedModel, translations }
}

export function blankItemForm(meta: CollectionMeta, locales: LanguageInfo[]): FormModel {
  return parseItemToForm(meta, {}, locales)
}
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/lib/parseItemToForm.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/lib/parseItemToForm.ts frontend/src/lib/parseItemToForm.test.ts
git commit -m "feat(frontend): parseItemToForm + blankItemForm"
```

---

### Task 10: Frontend — `buildItemPayload`

**Files:**
- Create: `frontend/src/lib/buildItemPayload.ts`
- Test: `frontend/src/lib/buildItemPayload.test.ts`

**Interfaces:**
- Consumes: `splitFields`; types `CollectionMeta`, `LanguageInfo`, `FormModel`.
- Produces: `buildItemPayload(meta, model: FormModel, locales: LanguageInfo[], mode: 'create'|'update'): Record<string,unknown>`.

- [ ] **Step 1: Write the failing test** `buildItemPayload.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { buildItemPayload } from './buildItemPayload'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('id', { isSystem: true }),
  field('status'),
  field('title', { translatable: true }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]

describe('buildItemPayload (create)', () => {
  it('puts shared at top level and always includes default locale; omits empty non-default', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect(p.status).toBe('draft')
    expect(p.translations).toEqual({ en: { title: 'Hi' } })
  })
  it('includes a non-default locale when it has content', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } } }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect(p.translations).toEqual({ en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } })
  })
  it('omits empty optional shared fields on create', () => {
    const model: FormModel = { shared: { status: '' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect('status' in p).toBe(false)
  })
})

describe('buildItemPayload (update)', () => {
  it('keeps shared keys (partial) and only touched locales', () => {
    const model: FormModel = { shared: { status: 'published' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    const p = buildItemPayload(meta, model, locales, 'update')
    expect(p.status).toBe('published')
    expect(p.translations).toEqual({ en: { title: 'Hi' } })
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/lib/buildItemPayload.test.ts`
Expected: FAIL.

- [ ] **Step 3: Implement** `buildItemPayload.ts`:

```ts
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'

function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}

export function buildItemPayload(
  meta: CollectionMeta,
  model: FormModel,
  locales: LanguageInfo[],
  mode: 'create' | 'update',
): Record<string, unknown> {
  const { shared, translatable } = splitFields(meta)
  const payload: Record<string, unknown> = {}

  for (const f of shared) {
    const v = model.shared[f.name]
    if (mode === 'update' || !isEmpty(v)) payload[f.name] = v
  }

  const defaultCode = locales.find((l) => l.isDefault)?.code
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const values = model.translations[loc.code] ?? {}
    const hasContent = translatable.some((f) => !isEmpty(values[f.name]))
    const isDefault = loc.code === defaultCode
    // create: always send default locale; else only when it has content.
    // update: send only locales the user actually filled.
    if (mode === 'create' && !isDefault && !hasContent) continue
    if (mode === 'update' && !hasContent) continue
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = values[f.name]
    translations[loc.code] = entry
  }
  if (Object.keys(translations).length > 0) payload.translations = translations
  return payload
}
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/lib/buildItemPayload.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/lib/buildItemPayload.ts frontend/src/lib/buildItemPayload.test.ts
git commit -m "feat(frontend): buildItemPayload (shared + per-locale translations)"
```

---

### Task 11: Frontend — `validateItem`

**Files:**
- Create: `frontend/src/lib/validateItem.ts`
- Test: `frontend/src/lib/validateItem.test.ts`

**Interfaces:**
- Consumes: `splitFields`; types `CollectionMeta`, `FormModel`.
- Produces: `validateItem(meta, model: FormModel, defaultCode: string): Record<string,string>` (field→message; empty map = valid).

- [ ] **Step 1: Write the failing test** `validateItem.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { validateItem } from './validateItem'
import type { CollectionMeta, FieldMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('status', { required: true }),
  field('title', { translatable: true, required: true }),
]}

describe('validateItem', () => {
  it('flags empty required shared and default-locale required translatable', () => {
    const model: FormModel = { shared: { status: '' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } } }
    const errs = validateItem(meta, model, 'en')
    expect(errs.status).toMatch(/required/i)
    expect(errs.title).toMatch(/required/i)
  })
  it('does not block on missing non-default locale', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    expect(validateItem(meta, model, 'en')).toEqual({})
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/lib/validateItem.test.ts`
Expected: FAIL.

- [ ] **Step 3: Implement** `validateItem.ts`:

```ts
import type { CollectionMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'

function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}

export function validateItem(
  meta: CollectionMeta,
  model: FormModel,
  defaultCode: string,
): Record<string, string> {
  const { shared, translatable } = splitFields(meta)
  const errors: Record<string, string> = {}
  for (const f of shared) {
    if (f.required && isEmpty(model.shared[f.name])) errors[f.name] = `${f.label} is required.`
  }
  const defaultValues = model.translations[defaultCode] ?? {}
  for (const f of translatable) {
    if (f.required && isEmpty(defaultValues[f.name])) errors[f.name] = `${f.label} is required.`
  }
  return errors
}
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/lib/validateItem.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/lib/validateItem.ts frontend/src/lib/validateItem.test.ts
git commit -m "feat(frontend): validateItem (required + default-locale translatable)"
```

---

### Task 12: Frontend — `FieldInput` dispatcher component

**Files:**
- Create: `frontend/src/components/fields/FieldInput.vue`
- Test: `frontend/src/components/fields/FieldInput.test.ts`

**Interfaces:**
- Consumes: `fieldInputKind`; type `FieldMeta`; PrimeVue `InputText`, `Textarea`, `InputNumber`, `Checkbox`, `DatePicker`, `Select`, `RadioButton`.
- Produces: component `FieldInput` with props `{ field: FieldMeta; modelValue: unknown; disabled?: boolean }`, `v-model` (emits `update:modelValue`). Unsupported/`readonly` kind → read-only display; `field.readOnly` → disabled control.

- [ ] **Step 1: Write the failing test** `FieldInput.test.ts` (stub PrimeVue controls to keep the test independent of PrimeVue plugin setup; assert the right stub renders per kind):

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FieldInput from './FieldInput.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> = {}): FieldMeta {
  return { name: 'f', label: 'F', interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const stubs = {
  InputText: { template: '<input class="stub-text" />' },
  Textarea: { template: '<textarea class="stub-textarea" />' },
  InputNumber: { template: '<input class="stub-number" />' },
  Checkbox: { template: '<input class="stub-checkbox" />' },
  DatePicker: { template: '<input class="stub-date" />' },
  Select: { template: '<select class="stub-select" />' },
  RadioButton: { template: '<input class="stub-radio" />' },
}

describe('FieldInput', () => {
  it('renders InputText for text interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'text' }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-text').exists()).toBe(true)
  })
  it('renders Textarea for richText (fallback)', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'richText' }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-textarea').exists()).toBe(true)
  })
  it('renders Select for select interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'A' }] }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-select').exists()).toBe(true)
  })
  it('renders read-only display for unsupported interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'json' }), modelValue: '{}' }, global: { stubs } })
    expect(w.find('.readonly-field').exists()).toBe(true)
    expect(w.find('.stub-text').exists()).toBe(false)
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/components/fields/FieldInput.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement** `FieldInput.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import InputNumber from 'primevue/inputnumber'
import Checkbox from 'primevue/checkbox'
import DatePicker from 'primevue/datepicker'
import Select from 'primevue/select'
import RadioButton from 'primevue/radiobutton'
import { fieldInputKind } from '../../lib/fieldInputKind'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const kind = computed(() => fieldInputKind(props.field.interface))
const isDisabled = computed(() => props.disabled === true || props.field.readOnly)
function update(v: unknown): void { emit('update:modelValue', v) }
</script>

<template>
  <InputText v-if="kind === 'text'" :model-value="(modelValue as string)" :disabled="isDisabled"
    @update:model-value="update" />

  <Textarea v-else-if="kind === 'textarea' || kind === 'richtext'" :model-value="(modelValue as string)"
    :disabled="isDisabled" :rows="6" @update:model-value="update" />

  <InputNumber v-else-if="kind === 'number'" :model-value="(modelValue as number)" :disabled="isDisabled"
    @update:model-value="update" />

  <Checkbox v-else-if="kind === 'boolean'" :model-value="(modelValue as boolean)" :binary="true"
    :disabled="isDisabled" @update:model-value="update" />

  <DatePicker v-else-if="kind === 'date'" :model-value="(modelValue as Date)" :disabled="isDisabled"
    @update:model-value="update" />
  <DatePicker v-else-if="kind === 'time'" :model-value="(modelValue as Date)" time-only :disabled="isDisabled"
    @update:model-value="update" />
  <DatePicker v-else-if="kind === 'datetime'" :model-value="(modelValue as Date)" show-time :disabled="isDisabled"
    @update:model-value="update" />

  <Select v-else-if="kind === 'select'" :model-value="modelValue" :options="field.options ?? []"
    option-label="label" option-value="value" :disabled="isDisabled" @update:model-value="update" />

  <div v-else-if="kind === 'radio'" class="radio-group">
    <label v-for="opt in field.options ?? []" :key="opt.value" class="radio-option">
      <RadioButton :model-value="modelValue" :value="opt.value" :disabled="isDisabled"
        @update:model-value="update" />
      <span>{{ opt.label }}</span>
    </label>
  </div>

  <hr v-else-if="kind === 'divider'" />

  <span v-else class="readonly-field">{{ modelValue ?? '—' }}</span>
</template>
```

> Note: `date`/`time`/`datetime` bind a `Date`; backend sends ISO strings. The sample `article` collection has no editable date field, so coercion is out of scope for 7c — a later phase adds string↔Date handling when a date field ships in a form.

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/components/fields/FieldInput.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/components/fields/FieldInput.vue frontend/src/components/fields/FieldInput.test.ts
git commit -m "feat(frontend): FieldInput dispatcher (interface -> PrimeVue control)"
```

---

### Task 13: Frontend — `ItemForm` component

**Files:**
- Create: `frontend/src/components/ItemForm.vue`
- Test: `frontend/src/components/ItemForm.test.ts`

**Interfaces:**
- Consumes: `FieldInput`; `splitFields`; types `CollectionMeta`, `LanguageInfo`, `FormModel`; PrimeVue `Button`, `Tabs`, `TabList`, `Tab`, `TabPanels`, `TabPanel`.
- Produces: component `ItemForm` with props `{ meta: CollectionMeta; model: FormModel; locales: LanguageInfo[]; errors: Record<string,string>; serverError?: string; disabled?: boolean; submitting?: boolean }`; emits `submit`, `cancel`.

- [ ] **Step 1: Write the failing test** `ItemForm.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ItemForm from './ItemForm.vue'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('status', { sort: 1 }),
  field('title', { translatable: true, sort: 2 }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]
const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } } }
const stubs = {
  FieldInput: { props: ['field', 'modelValue', 'disabled'], template: '<div class="field-input" :data-name="field.name" />' },
  Button: { props: ['label'], template: '<button :data-label="label" @click="$emit(\'click\')">{{ label }}</button>' },
  Tabs: { template: '<div><slot /></div>' },
  TabList: { template: '<div><slot /></div>' },
  Tab: { template: '<button class="tab"><slot /></button>' },
  TabPanels: { template: '<div><slot /></div>' },
  TabPanel: { template: '<div class="tab-panel"><slot /></div>' },
}

describe('ItemForm', () => {
  it('renders shared fields once and a tab per locale', () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
    expect(w.findAll('.tab')).toHaveLength(2)
    // 1 shared (status) + translatable title rendered per locale panel (2) = 3 FieldInputs
    expect(w.findAll('.field-input')).toHaveLength(3)
  })
  it('shows a server error banner', () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {}, serverError: 'boom' }, global: { stubs } })
    expect(w.find('.error').text()).toContain('boom')
  })
  it('emits submit on form submit and cancel on Cancel click', async () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
    await w.find('form').trigger('submit')
    expect(w.emitted('submit')).toBeTruthy()
    await w.find('button[data-label="Cancel"]').trigger('click')
    expect(w.emitted('cancel')).toBeTruthy()
  })
  it('hides Save when disabled (read-only)', () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {}, disabled: true }, global: { stubs } })
    expect(w.find('button[data-label="Save"]').exists()).toBe(false)
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/components/ItemForm.test.ts`
Expected: FAIL.

- [ ] **Step 3: Implement** `ItemForm.vue`:

```vue
<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import FieldInput from './fields/FieldInput.vue'
import { splitFields } from '../lib/splitFields'
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const props = defineProps<{
  meta: CollectionMeta
  model: FormModel
  locales: LanguageInfo[]
  errors: Record<string, string>
  serverError?: string
  disabled?: boolean
  submitting?: boolean
}>()
const emit = defineEmits<{ (e: 'submit'): void; (e: 'cancel'): void }>()

const fields = computed(() => splitFields(props.meta))
const activeLocale = ref(props.locales[0]?.code ?? '')
</script>

<template>
  <form class="item-form" @submit.prevent="emit('submit')">
    <p v-if="serverError" class="error" role="alert">{{ serverError }}</p>

    <div v-for="f in fields.shared" :key="f.name" class="field">
      <label :for="f.name">{{ f.label }}<span v-if="f.required" class="req">*</span></label>
      <FieldInput :field="f" v-model="model.shared[f.name]" :disabled="disabled" />
      <small v-if="f.helpText" class="help">{{ f.helpText }}</small>
      <small v-if="errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
    </div>

    <Tabs v-if="fields.translatable.length" v-model:value="activeLocale">
      <TabList>
        <Tab v-for="loc in locales" :key="loc.code" :value="loc.code">
          {{ loc.name }}<span v-if="loc.isDefault"> *</span>
        </Tab>
      </TabList>
      <TabPanels>
        <TabPanel v-for="loc in locales" :key="loc.code" :value="loc.code">
          <div v-for="f in fields.translatable" :key="f.name" class="field">
            <label>{{ f.label }}<span v-if="f.required && loc.isDefault" class="req">*</span></label>
            <FieldInput :field="f" v-model="model.translations[loc.code][f.name]" :disabled="disabled" />
            <small v-if="loc.isDefault && errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <div class="actions">
      <Button type="button" label="Cancel" severity="secondary" @click="emit('cancel')" />
      <Button v-if="!disabled" type="submit" label="Save" :loading="submitting" />
    </div>
  </form>
</template>
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/components/ItemForm.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/components/ItemForm.vue frontend/src/components/ItemForm.test.ts
git commit -m "feat(frontend): ItemForm (shared fields + per-locale tabs)"
```

---

### Task 14: Frontend — `ItemFormView` orchestrator

**Files:**
- Create: `frontend/src/views/ItemFormView.vue`
- Test: `frontend/src/views/ItemFormView.test.ts`

**Interfaces:**
- Consumes: `ItemForm`; stores `useAuthStore`, `useSchemaStore`, `useLanguageStore`; `itemsApi.get/create/update/remove`; `blankItemForm`, `parseItemToForm`, `buildItemPayload`, `validateItem`; PrimeVue `Button`, `ConfirmDialog`, `useConfirm`; `vue-router` `useRoute`/`useRouter`.
- Produces: route component for `collection-create` (no `:id`) and `collection-item` (`:id`). Exposes (for tests) `init`, `onSubmit`, `onDelete`, `onCancel`, `model`, `errors`, `serverError`, `notFound`, `loading`.

- [ ] **Step 1: Write the failing test** `ItemFormView.test.ts`. Mock the router, stores, and `itemsApi`; drive the exposed methods:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import ItemFormView from './ItemFormView.vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'

const push = vi.fn()
let routeParams: Record<string, string> = {}
let routeName = 'collection-item'
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams, name: routeName }),
  useRouter: () => ({ push }),
}))
const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

const meta = { name: 'article', label: 'Article', fields: [
  { name: 'status', label: 'Status', interface: 'text', required: true, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
]}
const stubs = { ItemForm: true, Button: true, ConfirmDialog: true }

function setupStores(opts: { superAdmin?: boolean } = {}) {
  const auth = useAuthStore()
  auth.user = { id: '1', isSuperAdmin: opts.superAdmin ?? true, permissions: {} }
  const schema = useSchemaStore()
  schema.load = vi.fn().mockResolvedValue(undefined)
  schema.get = vi.fn().mockReturnValue(meta) as never
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return { auth, schema, lang }
}

describe('ItemFormView', () => {
  beforeEach(() => { setActivePinia(createPinia()); push.mockClear(); confirmRequire.mockClear(); routeParams = {}; routeName = 'collection-item' })

  it('edit path loads the item and inflates the model', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const spy = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {} })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect(spy).toHaveBeenCalledWith('article', '5')
    expect((w.vm as any).model.shared.status).toBe('published')
  })

  it('create path builds a blank model and calls create on submit', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    const spy = vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: '9' })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft'
    await (w.vm as any).onSubmit()
    expect(spy).toHaveBeenCalledWith('article', expect.objectContaining({ status: 'draft' }))
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  it('blocks submit and shows errors when required field empty', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    const spy = vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: '9' })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    await (w.vm as any).onSubmit()
    expect(spy).not.toHaveBeenCalled()
    expect((w.vm as any).errors.status).toMatch(/required/i)
  })

  it('marks notFound when the item is missing', async () => {
    routeParams = { name: 'article', id: '404' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new Error('Item not found.'))
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect((w.vm as any).notFound).toBe(true)
  })

  it('delete requires confirmation then removes and routes back', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const rm = vi.spyOn(itemsApi, 'remove').mockResolvedValue(undefined)
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).onDelete()
    expect(confirmRequire).toHaveBeenCalled()
    // invoke the accept callback the component passed to confirm.require
    await confirmRequire.mock.calls[0][0].accept()
    expect(rm).toHaveBeenCalledWith('article', '5')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })
})
```

- [ ] **Step 2: Run and confirm fail.**
Run: `pnpm test src/views/ItemFormView.test.ts`
Expected: FAIL.

- [ ] **Step 3: Implement** `ItemFormView.vue`:

```vue
<script setup lang="ts">
import { reactive, ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import ConfirmDialog from 'primevue/confirmdialog'
import Button from 'primevue/button'
import ItemForm from '../components/ItemForm.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { blankItemForm, parseItemToForm } from '../lib/parseItemToForm'
import { buildItemPayload } from '../lib/buildItemPayload'
import { validateItem } from '../lib/validateItem'
import type { FormModel } from '../types/itemForm'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const confirm = useConfirm()

const name = computed(() => route.params.name as string)
const id = computed(() => (route.params.id as string | undefined) ?? undefined)
const isCreate = computed(() => id.value === undefined)
const meta = computed(() => schema.get(name.value))
const canWrite = computed(() => auth.canWrite(name.value))
const canDelete = computed(() => auth.canDelete(name.value))

const model = reactive<FormModel>({ shared: {}, translations: {} })
const errors = ref<Record<string, string>>({})
const serverError = ref('')
const loading = ref(true)
const submitting = ref(false)
const notFound = ref(false)

function setModel(next: FormModel): void {
  model.shared = next.shared
  model.translations = next.translations
}

async function init(): Promise<void> {
  loading.value = true
  serverError.value = ''
  notFound.value = false
  errors.value = {}
  await Promise.all([schema.load(), langStore.load()])
  if (!meta.value) { loading.value = false; return }
  if (isCreate.value) {
    setModel(blankItemForm(meta.value, langStore.languages))
  } else {
    try {
      const item = await itemsApi.get(name.value, id.value!)
      setModel(parseItemToForm(meta.value, item, langStore.languages))
    } catch (e) {
      if (e instanceof Error && /not found/i.test(e.message)) notFound.value = true
      else serverError.value = e instanceof Error ? e.message : 'Failed to load item.'
    }
  }
  loading.value = false
}

async function onSubmit(): Promise<void> {
  if (!meta.value) return
  errors.value = validateItem(meta.value, model, langStore.defaultCode)
  if (Object.keys(errors.value).length > 0) return
  submitting.value = true
  serverError.value = ''
  try {
    const payload = buildItemPayload(meta.value, model, langStore.languages, isCreate.value ? 'create' : 'update')
    if (isCreate.value) await itemsApi.create(name.value, payload)
    else await itemsApi.update(name.value, id.value!, payload)
    router.push({ name: 'collection-list', params: { name: name.value } })
  } catch (e) {
    serverError.value = e instanceof Error ? e.message : 'Save failed.'
  } finally {
    submitting.value = false
  }
}

function onDelete(): void {
  confirm.require({
    header: 'Confirm delete',
    message: 'Delete this item? This cannot be undone.',
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

function onCancel(): void {
  router.push({ name: 'collection-list', params: { name: name.value } })
}

onMounted(init)
defineExpose({ init, onSubmit, onDelete, onCancel, model, errors, serverError, notFound, loading })
</script>

<template>
  <section class="item-form-view">
    <ConfirmDialog />
    <p v-if="loading" class="notice">Loading…</p>
    <p v-else-if="!meta" class="notice">Collection not found.</p>
    <p v-else-if="notFound" class="notice">Item not found.</p>
    <p v-else-if="isCreate && !canWrite" class="notice">You don't have permission to create items here.</p>
    <template v-else>
      <header class="form-header">
        <h2>{{ isCreate ? `New ${meta.label}` : `Edit ${meta.label}` }}</h2>
        <Button v-if="!isCreate && canDelete" label="Delete" severity="danger" @click="onDelete" />
      </header>
      <ItemForm
        :meta="meta"
        :model="model"
        :locales="langStore.languages"
        :errors="errors"
        :server-error="serverError"
        :disabled="!canWrite"
        :submitting="submitting"
        @submit="onSubmit"
        @cancel="onCancel"
      />
    </template>
  </section>
</template>
```

- [ ] **Step 4: Run and confirm pass.**
Run: `pnpm test src/views/ItemFormView.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts
git commit -m "feat(frontend): ItemFormView (create/edit/delete orchestration + permissions)"
```

---

### Task 15: Frontend — routes + list integration + ConfirmationService

**Files:**
- Modify: `frontend/src/router/index.ts`
- Modify: `frontend/src/main.ts`
- Modify: `frontend/src/views/CollectionListView.vue`
- Test: `frontend/src/views/CollectionListView.test.ts` (extend)

**Interfaces:**
- Consumes: `ItemFormView`; `useRouter`; `authStore.canWrite`; PrimeVue `Button` + `ConfirmationService`.
- Produces: routes `collection-create` (`collections/:name/new`) and `collection-item` (`collections/:name/:id`); list row-click nav + "New" button.

- [ ] **Step 1: Add the routes.** In `router/index.ts`, import the view and add two children. Declare `new` (static) before `:id` (dynamic) for clarity:

```ts
import ItemFormView from '../views/ItemFormView.vue'
```
Add inside `children` (after the existing `collection-list` entry):
```ts
        { path: 'collections/:name/new', name: 'collection-create', component: ItemFormView },
        { path: 'collections/:name/:id', name: 'collection-item', component: ItemFormView },
```

- [ ] **Step 2: Register ConfirmationService.** In `main.ts`, add the import and `app.use`:

```ts
import ConfirmationService from 'primevue/confirmationservice'
```
After `app.use(PrimeVue, { theme: { preset: Aura } })`:
```ts
app.use(ConfirmationService)
```

- [ ] **Step 3: Write the failing list test.** Append to `CollectionListView.test.ts`. If the file does not yet mock `useRouter`, add at the top of the file (outside `describe`):

```ts
const pushMock = vi.fn()
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { name: 'article' } }),
  useRouter: () => ({ push: pushMock }),
}))
```
Then add these cases (the existing suite already mounts the component with an authenticated store granting read on `article`; ensure that store also grants `write` — set `permissions: { article: { read: true, write: true, delete: true } }` or `isSuperAdmin: true` in the shared setup, and `pushMock.mockClear()` in `beforeEach`):

```ts
it('navigates to the item on row click', () => {
  const vm: any = wrapper.vm
  vm.onRowClick({ data: { id: '42' } })
  expect(pushMock).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: '42' } })
})

it('New button navigates to create when canWrite', () => {
  const vm: any = wrapper.vm
  vm.onNew()
  expect(pushMock).toHaveBeenCalledWith({ name: 'collection-create', params: { name: 'article' } })
})
```

- [ ] **Step 4: Run and confirm fail.**
Run: `pnpm test src/views/CollectionListView.test.ts`
Expected: FAIL (`onRowClick`/`onNew` not defined).

- [ ] **Step 5: Implement the list changes.** In `CollectionListView.vue` `<script setup>`:
  - Add imports: `import { useRouter } from 'vue-router'` and `import Button from 'primevue/button'`.
  - Add `const router = useRouter()` and `const canWrite = computed(() => auth.canWrite(name.value))`.
  - Add handlers:

```ts
function onRowClick(e: { data: Record<string, unknown> }): void {
  const rid = e.data.id
  if (rid != null) router.push({ name: 'collection-item', params: { name: name.value, id: String(rid) } })
}
function onNew(): void {
  router.push({ name: 'collection-create', params: { name: name.value } })
}
```
  - Extend `defineExpose({ ... })` to include `onRowClick, onNew, canWrite`.

In the template:
  - Add a New button in `.list-header` (after the search `InputText`):
```html
<Button v-if="canWrite" label="New" icon="pi pi-plus" @click="onNew" />
```
  - Add `@row-click="onRowClick"` to the `<DataTable>` opening tag.

- [ ] **Step 6: Run and confirm pass.**
Run: `pnpm test src/views/CollectionListView.test.ts`
Expected: PASS.

- [ ] **Step 7: Full frontend gate.**
Run: `pnpm test` then `pnpm build`
Expected: all tests green; type-check + build succeed.

- [ ] **Step 8: Commit.**

```bash
git add frontend/src/router/index.ts frontend/src/main.ts frontend/src/views/CollectionListView.vue frontend/src/views/CollectionListView.test.ts
git commit -m "feat(frontend): item form routes + list row-click/New + ConfirmationService"
```

---

### Task 16: E2E — create / edit / delete an article

**Files:**
- Create: `frontend/e2e/items.spec.ts`
- Modify: `frontend/e2e/README.md` (note the create flow + seed expectations)

**Interfaces:**
- Consumes: running API on `:5080` (live-ish or dev) + Vite dev server; admin creds via `E2E_EMAIL`/`E2E_PASSWORD` (defaults as in existing specs).

- [ ] **Step 1: Write the E2E spec** `frontend/e2e/items.spec.ts` (follow the login helper style in `auth.spec.ts`/`collections.spec.ts`):

```ts
import { test, expect } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'
const stamp = process.env.E2E_STAMP ?? 'e2e' // caller passes a unique value per run

async function login(page) {
  await page.goto('/login')
  await page.getByLabel(/email/i).fill(EMAIL)
  await page.getByLabel(/password/i).fill(PASSWORD)
  await page.getByRole('button', { name: /sign in|log in|login/i }).click()
  await expect(page).toHaveURL(/\/$|\/collections/)
}

test('create, edit, then delete an article', async ({ page }) => {
  await login(page)

  // Browse to the article collection, then create.
  await page.goto('/collections/article')
  await page.getByRole('button', { name: /new/i }).click()
  await expect(page).toHaveURL(/\/collections\/article\/new/)

  // Shared field + default-locale required translatable fields.
  // (Field labels come from schema: Status, Title, Body.)
  const title = `E2E Title ${stamp}`
  await page.getByLabel(/^status/i).fill('draft')
  await page.getByLabel(/^title/i).first().fill(title)
  await page.getByLabel(/^body/i).first().fill('E2E body content.')
  await page.getByRole('button', { name: /^save$/i }).click()

  // Back on the list; the new row is present.
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title)).toBeVisible()

  // Open it, edit the shared field, save.
  await page.getByText(title).click()
  await expect(page).toHaveURL(/\/collections\/article\/[^/]+$/)
  await page.getByLabel(/^status/i).fill('published')
  await page.getByRole('button', { name: /^save$/i }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Delete it.
  await page.getByText(title).click()
  await page.getByRole('button', { name: /^delete$/i }).click()
  await page.getByRole('button', { name: /^(yes|delete|confirm)$/i }).click() // PrimeVue confirm accept
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title)).toHaveCount(0)
})
```

> If the article schema's exact field labels/selectors differ (e.g., Status is a Select, not a text input), adjust the selectors to match the rendered controls — the flow (New → fill default-locale required fields → Save → row visible → edit → delete) is the contract. Document any selector adjustments in `e2e/README.md`.

- [ ] **Step 2: Update `e2e/README.md`** with a short "Items CRUD" section: the spec creates a uniquely-stamped article, so it is self-cleaning (creates then deletes); pass `E2E_STAMP` for parallel-safe unique titles; requires write+delete grants (bootstrap super-admin satisfies this).

- [ ] **Step 3: Run the E2E locally** (API on `:5080` + `pnpm dev` per `frontend/README.md`).
Run: `pnpm e2e`
Expected: `items.spec.ts` passes (plus existing `auth`/`collections` specs).

- [ ] **Step 4: Commit.**

```bash
git add frontend/e2e/items.spec.ts frontend/e2e/README.md
git commit -m "test(frontend): E2E article create/edit/delete (i18n-gated create)"
```

---

### Task 17: Verification gate + docs

**Files:**
- Modify: `docs/ROADMAP.md` (mark 7c done; add successor note)

**Interfaces:** none (documentation + evidence).

- [ ] **Step 1: Backend gate.**
Run: `dotnet build` then `dotnet test`
Expected: clean build (warnings-as-errors); all green (prior + `LanguagesEndpointTests`). Record counts.

- [ ] **Step 2: Frontend gate.**
Run (from `frontend/`): `pnpm test` then `pnpm build`
Expected: all unit/component tests green; build succeeds. Record counts.

- [ ] **Step 3: Live gate (evidence required).** With the dev API pointed at **live Postgres + Redis** (per the Phase 7b live recipe) and `pnpm dev` running:
  - Run `pnpm e2e` (article create/edit/delete) against the live stack.
  - Manually confirm the i18n contract: creating an article **without** a default-locale translation is rejected (400 "default locale … required"); creating **with** `en` + `zh-TW` round-trips (`GET /api/items/article/{id}` returns both locales under `translations`).
  - Capture evidence (test output + one `GET …/{id}` JSON body showing both locales).

- [ ] **Step 4: Update `docs/ROADMAP.md`.** Flip the Phase 7 / 7c rows: add a `7c` row (Status ✅ done, live-verified) linking this plan + the spec; update the "Status at a glance" / "Next up" to point to Phase 7d (relation pickers, File/Image upload, TipTap, multi-value selects). Keep the verification-baseline line current (new backend + frontend test counts).

- [ ] **Step 5: Commit the docs + evidence note.**

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 7c item-forms done + live gate evidence"
```

- [ ] **Step 6: Open the merge.** Push the branch and open a PR to `main` summarizing 7c (per the git-workflow PR process): additive `/api/languages`, apiClient put/delete, itemsApi CRUD, form helpers + FieldInput/ItemForm/ItemFormView, routes + list integration, E2E. Include the test plan and live-gate evidence.

---

## Self-Review

**Spec coverage (spec §→task):**
- §1 backend `/api/languages` → Task 1. ✅
- §2 apiClient put/delete → Task 2; languagesApi + `LanguageInfo` → Task 3; languageStore → Task 4; itemsApi CRUD → Task 5; authStore canWrite/canDelete → Task 6; helpers (`fieldInputKind`, `splitFields`, `buildItemPayload`, `parseItemToForm`, `validateItem`) → Tasks 7–11; `FieldInput` → Task 12; `ItemForm` → Task 13; `ItemFormView` → Task 14; router + list + ConfirmationService → Task 15. ✅
- §3 field split & wire mapping → Tasks 8/9/10 (splitFields + parse + payload). ✅
- §4 interface→control mapping → Tasks 7 (classifier) + 12 (renderer). ✅
- §5 data flow (create/edit/delete/list) → Tasks 14/15. ✅
- §6 error handling (client validation, 400/403/404/409/401, load failure) → Task 11 (validation), Task 4/14 (load errors), Task 14 (server errors surfaced in banner/notFound). ✅
- §7 testing (backend, pure helpers, components, api, E2E) → every task's tests + Task 16. ✅
- §8 verification gate → Task 17. ✅

**Placeholder scan:** No TBD/TODO/"add error handling" — each step carries real code and exact commands. The E2E selectors carry an explicit "adjust to match rendered controls" note (not a placeholder — the flow is fixed; only label strings may differ per the live schema).

**Type consistency:** `FormModel` (`{ shared, translations }`) defined in Task 8, consumed identically in Tasks 9–14. `LanguageInfo` defined Task 3, used Tasks 3/4/9/10/13/14. `fieldInputKind`/`InputKind` (Task 7) consumed by `FieldInput` (Task 12). `splitFields` signature stable across Tasks 8–13. `itemsApi.get/create/update/remove` signatures (Task 5) match calls in Task 14. `canWrite/canDelete` (Task 6) match usage in Tasks 14/15. Route names `collection-create`/`collection-item`/`collection-list` consistent across Tasks 14/15.
</content>
