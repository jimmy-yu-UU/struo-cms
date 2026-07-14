# Phase 9a-fe — Frontend response-envelope alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Vue frontend explicitly aware of the 9a REST response envelope — `apiClient` branches on `success` and throws a typed `ApiError` carrying `code`/`details`/`status`, and `ItemFormView` detects 404 via `code === 'NOT_FOUND'` instead of matching the error message text.

**Architecture:** Purely `frontend/`. Introduce an `ApiError extends Error` class + `ApiErrorBody`/`ValidationDetail` types in `apiClient.ts`; rewrite `request`'s error path to throw `ApiError` and its success path to unwrap on the `success` discriminator; fix the single consumer (`ItemFormView`) that relied on message-text matching. No backend, GraphQL, or `itemsApi`-signature change.

**Tech Stack:** Vue 3 + TypeScript, Vitest + @vue/test-utils, Playwright (live smoke). Package manager: `pnpm`. Type gate: `vue-tsc`.

## Global Constraints

- Frontend only (`frontend/`). No change to `Struo.*` backend, GraphQL, DB, or any NuGet/`pnpm` dependency.
- The public API of `itemsApi` and every other `*Api` module stays identical (signatures + return types).
- `validateItem` remains the client-side form-validation source of truth; server `VALIDATION` `details` are carried on the thrown `ApiError` but NOT rendered per-field this slice.
- `apiClient` behaviour that must be preserved unchanged: `credentials: 'include'`, the `X-Struo-CSRF` header on unsafe methods, the 401 unauthorized-handler callback, and the `204`/empty-body → `undefined` short-circuit.
- Outbound envelope shapes (from 9a, verbatim): success `{ success:true, data, meta? }`; error `{ success:false, error:{ code, message, details? } }` where `details` is `[{ field, message }]` and appears only on `VALIDATION`.
- Every commit message uses conventional-commit form with a `(9a-fe)` scope.
- Run all commands from the `frontend/` directory (where `package.json` lives).

---

### Task 1: `apiClient` error types + typed `ApiError` on the error path

**Files:**
- Modify: `frontend/src/api/apiClient.ts` (lines 1, 64-71 — the `ApiError` type + the `!res.ok` block)
- Test: `frontend/src/api/apiClient.test.ts`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `export type ValidationDetail = { field: string; message: string }`
  - `export type ApiErrorBody = { code?: string; message: string; details?: ValidationDetail[] }` (replaces the old `export type ApiError = { message: string }`)
  - `export class ApiError extends Error { readonly status: number; readonly code?: string; readonly details?: ValidationDetail[]; constructor(status: number, message: string, code?: string, details?: ValidationDetail[]) }`
  - `apiClient.request` now throws `ApiError` (an `instanceof Error`) on every non-2xx response.

- [ ] **Step 1: Write the failing tests**

Replace the two existing error-path tests (`'throws error.message on failure'`, `'put throws the server error message on non-2xx'`) so they assert against the 9a envelope, and add the new `ApiError`-shape tests. Add these to `frontend/src/api/apiClient.test.ts` (keep the existing `mockFetch` helper and the other tests):

```ts
import { ApiClient, ApiError } from './apiClient'

it('throws an ApiError carrying status/code/message from the error envelope', async () => {
  vi.stubGlobal('fetch', mockFetch(401, { success: false, error: { code: 'UNAUTHORIZED', message: 'Invalid credentials.' } }))
  const c = new ApiClient('/api')
  const err = await c.get('/auth/me').catch((e) => e)
  expect(err).toBeInstanceOf(ApiError)
  expect(err).toBeInstanceOf(Error)
  expect(err.status).toBe(401)
  expect(err.code).toBe('UNAUTHORIZED')
  expect(err.message).toBe('Invalid credentials.')
  expect(err.details).toBeUndefined()
})

it('carries VALIDATION details on the thrown ApiError', async () => {
  vi.stubGlobal('fetch', mockFetch(400, {
    success: false,
    error: { code: 'VALIDATION', message: 'One or more validation errors occurred.', details: [{ field: 'email', message: 'Email is required.' }] },
  }))
  const err = await new ApiClient('/api').post('/auth/login', {}).catch((e) => e)
  expect(err).toBeInstanceOf(ApiError)
  expect(err.code).toBe('VALIDATION')
  expect(err.details).toEqual([{ field: 'email', message: 'Email is required.' }])
})

it('falls back to a default message on a non-JSON error body', async () => {
  const c = new ApiClient('/api')
  globalThis.fetch = vi.fn().mockResolvedValue({
    status: 500, ok: false,
    json: async () => { throw new Error('not json') },
    text: async () => 'oops',
  } as unknown as Response)
  const err = await c.get('/x').catch((e) => e)
  expect(err).toBeInstanceOf(ApiError)
  expect(err.status).toBe(500)
  expect(err.message).toBe('Request failed (500)')
  expect(err.code).toBeUndefined()
})

it('put throws an ApiError with the server code on non-2xx', async () => {
  globalThis.fetch = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ success: false, error: { code: 'BAD_USER_INPUT', message: 'nope' } }), { status: 400 }))
  const err = await new ApiClient('/api').put('/x', {}).catch((e) => e)
  expect(err).toBeInstanceOf(ApiError)
  expect(err.code).toBe('BAD_USER_INPUT')
  expect(err.message).toBe('nope')
})
```

Also update the existing `'invokes the unauthorized handler on 401'` test's body literal from `{ error: { message: 'x' } }` to `{ success: false, error: { code: 'UNAUTHORIZED', message: 'x' } }` (behaviour asserted is unchanged — the handler still fires). Delete the now-superseded `'throws error.message on failure'` and `'put throws the server error message on non-2xx'` tests (replaced above).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pnpm test -- apiClient`
Expected: FAIL — `ApiError` is not exported as a class (import error / `is not a constructor`), and `err.code`/`err.status`/`err.details` are `undefined` on the plain `Error`.

- [ ] **Step 3: Write the implementation**

In `frontend/src/api/apiClient.ts`, replace line 1 (`export type ApiError = { message: string }`) with the new types + class:

```ts
export type ValidationDetail = { field: string; message: string }

// The shape of the server's error envelope body (9a).
export type ApiErrorBody = {
  code?: string
  message: string
  details?: ValidationDetail[]
}

// The Error thrown by apiClient on any non-2xx response.
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly details?: ValidationDetail[]
  constructor(status: number, message: string, code?: string, details?: ValidationDetail[]) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.details = details
  }
}
```

Then replace the `if (!res.ok) { … }` block (currently lines 64-71) with:

```ts
    if (!res.ok) {
      let body: ApiErrorBody | undefined
      try {
        body = (await res.json())?.error as ApiErrorBody | undefined
      } catch { /* non-JSON error body: leave body undefined */ }
      throw new ApiError(
        res.status,
        body?.message ?? `Request failed (${res.status})`,
        body?.code,
        body?.details,
      )
    }
```

Leave the success path (lines 73-78) unchanged in this task.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `pnpm test -- apiClient`
Expected: PASS (all apiClient tests, including the unchanged success/getRaw/204/CSRF tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/apiClient.ts frontend/src/api/apiClient.test.ts
git commit -m "feat(9a-fe): throw typed ApiError with code/details on the error path"
```

---

### Task 2: `apiClient` explicit `success`-discriminated success unwrap

**Files:**
- Modify: `frontend/src/api/apiClient.ts` (the success tail of `request`, currently lines 73-78)
- Test: `frontend/src/api/apiClient.test.ts`

**Interfaces:**
- Consumes: the types/class from Task 1.
- Produces: `request` unwraps success responses by branching on the `success` discriminator (returns `payload.data` when `'success' in payload`), keeps `getRaw` (`unwrap:false`) returning the full envelope, and retains a defensive `data ?? payload` fallback for any non-enveloped JSON.

- [ ] **Step 1: Write the failing tests**

Add to `frontend/src/api/apiClient.test.ts`:

```ts
it('unwraps data by branching on the success discriminator', async () => {
  vi.stubGlobal('fetch', mockFetch(200, { success: true, data: { id: 'u1' } }))
  const result = await new ApiClient('/api').get<{ id: string }>('/auth/me')
  expect(result).toEqual({ id: 'u1' })
})

it('returns a null data payload as null (not the whole envelope)', async () => {
  vi.stubGlobal('fetch', mockFetch(200, { success: true, data: null }))
  const result = await new ApiClient('/api').get('/auth/me')
  expect(result).toBeNull()
})

it('getRaw returns the whole success envelope including meta', async () => {
  vi.stubGlobal('fetch', mockFetch(200, { success: true, data: [{ id: '1' }], meta: { total: 42, limit: 25, offset: 0 } }))
  const result = await new ApiClient('/api').getRaw<{ data: unknown[]; meta: { total: number } }>('/items/article')
  expect(result).toEqual({ success: true, data: [{ id: '1' }], meta: { total: 42, limit: 25, offset: 0 } })
})

it('falls back to data ?? payload for a non-enveloped JSON response', async () => {
  vi.stubGlobal('fetch', mockFetch(200, { data: { id: 'legacy' } }))
  const result = await new ApiClient('/api').get('/auth/me')
  expect(result).toEqual({ id: 'legacy' })
})
```

Update the two existing success tests that use the old body shape:
- `'unwraps the data envelope on success'`: change its mock body from `{ data: { id: 'u1' } }` to `{ success: true, data: { id: 'u1' } }`.
- `'getRaw returns the full envelope without unwrapping data'`: change its mock body to `{ success: true, data: [{ id: '1' }], meta: { total: 42 } }` and its expected to match (it now includes `success`). (This overlaps the new getRaw test — keep whichever asserts the full object; delete the duplicate.)
- `'put unwraps the data envelope'`: change its `Response` body to `{ success: true, data: { id: '1' } }`.
- `'sends credentials: include'` and `'attaches the CSRF header on mutations'` and `'does not attach the CSRF header on GET'`: change their `mockFetch(200, { data: {} })` to `mockFetch(200, { success: true, data: {} })`.

- [ ] **Step 2: Run the tests to verify the new ones fail (or characterize)**

Run: `pnpm test -- apiClient`
Expected: the `'returns a null data payload as null'` test FAILS — with the current `payload?.data ?? payload`, `data: null` returns the whole `{ success, data:null }` envelope instead of `null`. (The other new tests may already pass via the fallback; this null case is the one that proves the explicit branch is needed.)

- [ ] **Step 3: Write the implementation**

In `frontend/src/api/apiClient.ts`, replace the success tail (currently lines 73-78) with:

```ts
    if (res.status === 204) return undefined as T
    const text = await res.text()
    if (!text) return undefined as T
    const payload = JSON.parse(text)
    if (opts?.unwrap === false) return payload as T
    if (payload && typeof payload === 'object' && 'success' in payload) {
      return (payload as { data?: unknown }).data as T
    }
    return (payload?.data ?? payload) as T
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `pnpm test -- apiClient`
Expected: PASS (all apiClient tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/apiClient.ts frontend/src/api/apiClient.test.ts
git commit -m "feat(9a-fe): unwrap success responses on the success discriminator"
```

---

### Task 3: `ItemFormView` — code-driven 404 detection

**Files:**
- Modify: `frontend/src/views/ItemFormView.vue` (add an `ApiError` import; line 68 — the not-found branch)
- Test: `frontend/src/views/ItemFormView.test.ts` (line 106-113 — the `'marks notFound when the item is missing'` test)

**Interfaces:**
- Consumes: `ApiError` from `../api/apiClient` (Task 1).
- Produces: no exported change; the view sets `notFound` iff the rejection is an `ApiError` with `code === 'NOT_FOUND'`.

- [ ] **Step 1: Update the failing test**

In `frontend/src/views/ItemFormView.test.ts`, add the `ApiError` import and rewrite the not-found test to reject with a typed `ApiError`, plus add a companion test proving a non-404 error does NOT trip `notFound`:

```ts
import { ApiError } from '../api/apiClient'
```

```ts
it('marks notFound when the server returns code NOT_FOUND', async () => {
  routeParams = { name: 'article', id: '404' }
  setupStores()
  // CJK message that does NOT match the old /not found/i regex — only the code branch can pass this.
  vi.spyOn(itemsApi, 'get').mockRejectedValue(new ApiError(404, '找不到資源', 'NOT_FOUND'))
  const w = mount(ItemFormView, { global: { stubs } })
  await w.vm.init()
  expect((w.vm as any).notFound).toBe(true)
})

it('shows serverError (not notFound) on a non-404 load error', async () => {
  routeParams = { name: 'article', id: '5' }
  setupStores()
  vi.spyOn(itemsApi, 'get').mockRejectedValue(new ApiError(500, 'Boom', 'INTERNAL_SERVER_ERROR'))
  const w = mount(ItemFormView, { global: { stubs } })
  await w.vm.init()
  expect((w.vm as any).notFound).toBe(false)
  expect((w.vm as any).serverError).toBe('Boom')
})
```

Delete the old `'marks notFound when the item is missing'` test (line 106-113) — it is replaced by the two above.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pnpm test -- ItemFormView`
Expected: FAIL — the old code matches `/not found/i` against `e.message`; `new ApiError(404, 'Resource not found.', 'NOT_FOUND')` has message `'Resource not found.'` which *happens* to still match `/not found/i`, so the first test may pass by luck, but the import of `ApiError` and the explicit `code` intent are not yet wired. To make the test genuinely drive the change, the decisive assertion is the **non-404** case combined with a NOT_FOUND message that does NOT contain "not found": change the first test's message to `new ApiError(404, '找不到資源', 'NOT_FOUND')` so it FAILS under the old `/not found/i` match and only passes once the code branches on `code`. Use that CJK message in the test.

(Concretely: with the old regex branch, `'找不到資源'` does not match `/not found/i`, so `notFound` stays `false` → the first test FAILS. This is the RED that proves the fix.)

- [ ] **Step 3: Write the implementation**

In `frontend/src/views/ItemFormView.vue`, add the import near the other `../api/*` imports:

```ts
import { ApiError } from '../api/apiClient'
```

Replace line 68's branch:

```ts
      if (e instanceof ApiError && e.code === 'NOT_FOUND') notFound.value = true
      else serverError.value = e instanceof Error ? e.message : 'Failed to load item.'
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `pnpm test -- ItemFormView`
Expected: PASS — the CJK-message NOT_FOUND now trips `notFound` (via `code`), and the 500 error sets `serverError` without tripping `notFound`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts
git commit -m "fix(9a-fe): detect 404 via error code, not message text, in ItemFormView"
```

---

### Task 4: Full regression + type gate + live smoke

**Files:**
- No source change (verification task). May add: `frontend/e2e/not-found.spec.ts` if step 4 is not already covered by an existing spec.

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: evidence that the whole frontend suite, the type checker, and the production build are green, plus a live smoke against the real backend.

- [ ] **Step 1: Full unit/component suite**

Run: `pnpm test`
Expected: PASS — all tests green (the 261 baseline, net of the replaced/added apiClient + ItemFormView tests). Record the final count.

- [ ] **Step 2: Type gate**

Run: `pnpm vue-tsc --noEmit` (or the project's `pnpm build` which runs `vue-tsc`)
Expected: 0 errors. This is what proves the `ApiError` type → `ApiErrorBody` rename and the new `ApiError` class compile across the whole app (confirmed by grep: the old type was only referenced inside `apiClient.ts`, so no importer needs re-pointing — but the gate is the guarantee).

- [ ] **Step 3: Production build**

Run: `pnpm build`
Expected: succeeds (pre-existing >500 kB chunk-size advisory only; no error).

- [ ] **Step 4: Live smoke (real backend + real Postgres, §17.2)**

Prereqs (per project memory): run the dev API with `ASPNETCORE_URLS` on `:5080` (the Vite proxy target; default is `:5000`) against live `web-struo-cms-db` + Redis + MinIO; bootstrap super-admin `admin@admin.com`. Start the Vite dev server. Then drive Playwright (extend an existing `frontend/e2e/*.spec.ts` or add `not-found.spec.ts`):

1. Login → dashboard renders.
2. Browse a collection list (e.g. Article) — rows load and pagination reflects `meta.total` (exercises the `getRaw` explicit-envelope path).
3. CRUD round-trip: create an item (or open an existing one) → edit → save → back to list.
4. **Navigate to a non-existent id** — visit `/collections/article/00000000-0000-0000-0000-000000000000` → the **not-found view** renders (driven by the new `code === 'NOT_FOUND'` branch, confirming the live envelope carries the code end-to-end).
5. Any CJK content in the round-trip stays code-point-exact.

Expected: all steps pass. If step 4 has no existing coverage, add a minimal `not-found.spec.ts` asserting the not-found view appears for a random GUID id.

- [ ] **Step 5: Commit (if an E2E spec was added) + update ROADMAP**

```bash
git add frontend/e2e/ docs/ROADMAP.md
git commit -m "test(9a-fe): live-smoke frontend envelope alignment; roadmap update"
```

Update `docs/ROADMAP.md`: add the 9a-fe row to the phase table and a status bullet (done & live-verified), mirroring the 9b-fe entry style, and note the final test count.

---

## Self-Review

**1. Spec coverage:**
- Spec §2.1 error types (`ApiErrorBody`, `ValidationDetail`, `ApiError` class) → Task 1. ✅
- Spec §2.2 error path (throw `ApiError` with code/details, non-JSON fallback, 401 handler) → Task 1. ✅
- Spec §2.2 success path (explicit `success` unwrap, `getRaw` full envelope, 204 short-circuit, defensive fallback) → Task 2. ✅
- Spec §2.3 consumer fix (`ItemFormView` 404 via `code`) → Task 3. ✅
- Spec §3 "what does NOT change" (`itemsApi` untouched, `validateItem` untouched, 401/CSRF/204 preserved) → enforced by Global Constraints + the preserved apiClient tests in Task 1/2. ✅
- Spec §4 testing strategy (apiClient tests, ItemFormView tests, full regression) → Tasks 1-3 + Task 4. ✅
- Spec §5 verification gate (`pnpm test` + `vue-tsc` + `pnpm build` + live smoke incl. the 404-code decisive check + CJK) → Task 4. ✅
- Spec §7 out-of-scope (no per-field VALIDATION rendering, no other consumer refactor, no backend change) → respected; no task adds these. ✅

**2. Placeholder scan:** No TBD/TODO; every code step shows the actual code; every command shows expected output. ✅

**3. Type consistency:** `ApiError(status, message, code?, details?)` constructor signature is identical in Task 1 (definition), Task 1/2 tests (usage), and Task 3 (`new ApiError(404, '找不到資源', 'NOT_FOUND')`). `ApiErrorBody`/`ValidationDetail` names match across the spec and Task 1. `getRaw`/`unwrap:false` behaviour is consistent between Task 2 and the `itemsApi.list` consumer (unchanged). ✅
