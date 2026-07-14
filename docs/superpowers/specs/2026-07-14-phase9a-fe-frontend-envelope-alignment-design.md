# Phase 9a-fe — Frontend response-envelope alignment

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 9. The frontend companion to **9a** (unified REST response envelope, backend-only),
> mirroring how 9b shipped backend-first with the Vue UI following in 9b-fe.
> **Depends on:** 9a (done & live-verified). The backend already emits the
> `{ success, data, meta? }` / `{ success:false, error:{ code, message, details? } }` envelope on every
> REST `/api/*` JSON response.
> **Spec:** [9a](2026-07-14-phase9a-unified-response-envelope-design.md) (§6 defers this work here).

## 0. Summary

Phase 9a made the backend REST envelope consistent and machine-readable, but left the frontend
**untouched** — it kept working only because it was *accidentally* backward-compatible: `apiClient` unwraps
success as `payload?.data ?? payload` (a coincidence, not an assertion) and reads errors as
`payload?.error?.message`, discarding the new `error.code` and `error.details`.

9a-fe hardens that coincidence into an explicit contract:

1. **`apiClient` becomes explicitly envelope-aware** — the success path branches on `success` rather than
   relying on the `data ?? payload` fallback; the error path parses the full `error` object.
2. **A real `ApiError extends Error` class** carries `status` / `code?` / `details?` to callers (today a
   bare `Error` carries only `message`).
3. **The one fragile consumer is fixed** — `ItemFormView` currently detects a 404 by string-matching the
   error *message* (`/not found/i.test(e.message)`); it switches to the robust `code === 'NOT_FOUND'`.

**Scope fence.** Frontend only (`frontend/`). No backend change of any kind — 9a already shipped the
envelope. The public API of `itemsApi` (and every other `*Api` module) is **unchanged**; only `apiClient`'s
internals and its exported error types change, plus the single `ItemFormView` 404 branch. Client-side
`validateItem` stays as the form-validation source of truth; server `VALIDATION` `details` are *carried on
the error* for future use but **not** wired per-field into forms this slice (YAGNI).

## 1. Current state (what exists today)

`frontend/src/api/apiClient.ts`:

```ts
export type ApiError = { message: string }          // wire error-body type, message only
// ... on !res.ok:
message = (payload?.error as ApiError)?.message ?? message
throw new Error(message)                             // plain Error, code/details discarded
// ... on success:
return (payload?.data ?? payload) as T              // coincidental unwrap
```

- Every consumer catches with `e instanceof Error ? e.message : '<fallback>'` — they only read `message`.
- **`ItemFormView.vue:68`** is the exception: `if (e instanceof Error && /not found/i.test(e.message))
  notFound.value = true` — a fragile message-text match (breaks if the message wording or locale changes).
- `itemsApi.list` uses `apiClient.getRaw` (which returns the full envelope, `unwrap:false`) and reads
  `res.data` + `res.meta.total`.
- No consumer reads `code` or `details` today.

## 2. Target design

### 2.1 Error types (`apiClient.ts`)

Rename the existing message-only wire type and extend it to the full 9a error body; add a real thrown
error class.

```ts
// The shape of the server's error envelope body (9a §2).
export type ApiErrorBody = {
  code?: string
  message: string
  details?: ValidationDetail[]
}
export type ValidationDetail = { field: string; message: string }

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

Rationale for the class (vs. attaching props to a plain `Error`): `ApiError extends Error` satisfies the
existing `e instanceof Error` checks in every consumer **unchanged**, while also enabling the precise
`e instanceof ApiError && e.code === '…'` branch where a consumer wants it. `code`/`details` are typed, not
`(e as any).code`.

> **Naming migration:** the old exported `type ApiError = { message }` is renamed to `ApiErrorBody`. A grep
> confirms `apiClient.ts` is the only definer; if any other module imports the old `ApiError` *type* it is
> re-pointed to `ApiErrorBody`. (Consumers that only `catch` don't import the type at all.)

### 2.2 `apiClient.request` — the two paths

**Error path (`!res.ok`):**

```ts
if (res.status === 401) this.onUnauthorized?.()          // unchanged
if (!res.ok) {
  let body: ApiErrorBody | undefined
  try { body = (await res.json())?.error } catch { /* non-JSON */ }
  throw new ApiError(
    res.status,
    body?.message ?? `Request failed (${res.status})`,
    body?.code,
    body?.details,
  )
}
```

- Non-JSON / bodyless error → `ApiError(status, "Request failed (<status>)")` with `code`/`details`
  `undefined` (matches today's fallback message).
- The `error` object is read once from the parsed body; both `code` and `details` come from it.

**Success path:**

```ts
if (res.status === 204) return undefined as T           // unchanged
const text = await res.text()
if (!text) return undefined as T
const payload = JSON.parse(text)
if (opts?.unwrap === false) return payload as T         // getRaw: full envelope (itemsApi.list)
if (payload && typeof payload === 'object' && 'success' in payload) {
  return (payload as { data?: unknown }).data as T      // explicit envelope unwrap
}
return (payload?.data ?? payload) as T                  // defensive fallback (should not happen on /api/*)
```

- The **explicit** branch keys on the presence of `success` (the 9a discriminator), returning `data`.
- The trailing `data ?? payload` is a **defensive** fallback for any hypothetical non-enveloped JSON; under
  9a every `/api/*` JSON response is enveloped, so it is not expected to fire — it exists so the client
  degrades gracefully rather than throwing.
- `getRaw` (`unwrap:false`) still returns the whole `{ success, data, meta }` object; `itemsApi.list`
  continues to read `res.data` + `res.meta.total` with no change.

### 2.3 Consumer fix — `ItemFormView.vue`

```ts
// before:
if (e instanceof Error && /not found/i.test(e.message)) notFound.value = true
else serverError.value = e instanceof Error ? e.message : 'Failed to load item.'
// after:
if (e instanceof ApiError && e.code === 'NOT_FOUND') notFound.value = true
else serverError.value = e instanceof Error ? e.message : 'Failed to load item.'
```

This is the **only** consumer changed. Every other `catch (e) { … e.message … }` block keeps working
because `ApiError` is an `Error`.

## 3. What does NOT change

- **`itemsApi` and every other `*Api` module** — public method signatures/returns are identical. `itemsApi`
  need not even be edited (it reads `res.data`/`res.meta.total` off `getRaw`, unchanged).
- **`validateItem`** — client-side form validation remains the source of truth; server `VALIDATION`
  `details` are available on the thrown `ApiError` but not rendered per-field this slice.
- **The 401 unauthorized handler**, the CSRF header logic, `credentials: 'include'`, and the 204/empty-body
  short-circuits — all unchanged.
- **Backend / GraphQL** — nothing. 9a already shipped.

## 4. Testing strategy (TDD)

**`apiClient.test.ts`** (update + add):

- Existing error tests re-pointed to the 9a body shape `{ success:false, error:{ code, message } }`
  (currently `{ error:{ message } }`).
- Thrown value is an `instanceof ApiError` (and `instanceof Error`); `e.status`, `e.code`, `e.message`,
  `e.details` are populated from the envelope.
- A `VALIDATION` error carries `e.code === 'VALIDATION'` and `e.details` = `[{ field, message }]`.
- A non-JSON / bodyless error → `ApiError(status, 'Request failed (<status>)')`, `code`/`details`
  `undefined`.
- Success with `{ success:true, data }` → explicit unwrap returns `data`.
- `getRaw` on `{ success:true, data, meta }` → returns the whole envelope (unchanged behaviour).
- 204 → `undefined`; 401 still invokes the unauthorized handler.

**`ItemFormView.test.ts`** (add/update):

- `itemsApi.get` rejecting with `new ApiError(404, '…', 'NOT_FOUND')` → the not-found view renders.
- `itemsApi.get` rejecting with a non-404 `ApiError` (or generic `Error`) → `serverError` shows the
  message, not the not-found view.
- (Existing message-string-based not-found test, if any, is replaced by the code-based one.)

**Full regression:** `pnpm test` all green, `pnpm vue-tsc` clean (this enforces the `ApiError`/`ApiErrorBody`
type migration compiles across the app), `pnpm build` succeeds.

## 5. Verification gate

**Automated (pre-merge):** `pnpm test` green + `vue-tsc` clean + `pnpm build` succeeds. Backend untouched,
so `dotnet test` is unaffected (stays at the 9a baseline **635**).

**Live smoke (real backend + real Postgres, §17.2)** — Playwright against the running SPA (dev API on
`:5080` behind the Vite proxy, live `web-struo-cms-db` + Redis + MinIO):

1. Login → dashboard.
2. Browse a collection list — pagination still driven by `meta.total` (the explicit-unwrap `getRaw` path).
3. A CRUD round-trip (create → edit → back to list).
4. **Navigate to a non-existent item id** (`/collections/<c>/<random-guid>`) → the **not-found view**
   renders, driven by the new `code === 'NOT_FOUND'` branch (not the old message match). This is the
   decisive check that the code path is really wired end-to-end through the live envelope.
5. Any CJK content in the round-trip stays code-point-exact (regression guard on the unwrap change).

The existing `frontend/e2e/*.spec.ts` (auth/collections/trash) must still pass; a small addition covers
step 4 if not already present.

## 6. Risks & mitigations

- **Type migration ripple.** Renaming the `ApiError` *type* to `ApiErrorBody` could break an importer.
  Mitigation: `vue-tsc` is a hard gate; a grep for `ApiError` imports across `frontend/src` precedes the
  edit. (The new `ApiError` *class* is a different, additive export.)
- **Over-eager explicit unwrap.** If some `/api/*` JSON response legitimately lacks `success`, strict
  unwrap could drop it. Mitigation: the `'success' in payload` guard + the defensive `data ?? payload`
  fallback preserve today's behaviour for any such case; 9a's live gate already confirmed every `/api/*`
  JSON carries `success`.
- **Silent frontend breakage.** Mitigation: full `pnpm test` + `vue-tsc` + the live SPA smoke (§5) before
  merge — the same compatibility gate 9a promised (9a §7).

## 7. Out of scope (explicit)

- Per-field rendering of server `VALIDATION` `details` in forms — `validateItem` already covers client-side
  field validation; can be added later additively.
- Refactoring other consumers' `catch` blocks to branch on `code` (only `ItemFormView`'s 404 hack is a real
  defect; the rest read `message` correctly).
- Any backend, GraphQL, or `itemsApi`-signature change.
- A shared toast/notification service, retry/backoff, or request cancellation — none are envelope-related.
