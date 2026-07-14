# Phase 9a — Unified response envelope (REST, backend-only slice)

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 9 (soft delete / revisions / lifecycle hooks + unified response envelope). Phase 9
> was decomposed into four independent slices: 9b soft delete (done), **9a unified response envelope
> (this spec)**, 9c revisions, 9d lifecycle hooks.
> **Companion follow-up:** the frontend alignment (`apiClient` becoming explicitly envelope-aware,
> `ApiError` carrying `code`/`details`) is deferred to a later slice (**9a-fe**), mirroring how 9b shipped
> backend-first with the Vue UI following in 9b-fe. This is only safe because the current frontend is
> already **backward-compatible** with the new envelope (see §6) — the deferral preserves a working app.

## 0. Summary

Every REST `/api/*` JSON response is brought under one consistent envelope:

- **Success:** `{ "success": true, "data": <payload>, "meta"?: { total, limit, offset } }` — `meta` only
  on list/paginated responses.
- **Error:** `{ "success": false, "error": { "code": "...", "message": "...", "details"?: [ { field, message } ] } }`
  — `details` only on `VALIDATION` errors.

Today the REST surface is **inconsistent**: list returns `{ data, meta }`, get/create/update return
`{ data }`, `NotFound()`/`NoContent()` are **bare** (no envelope), model-binding validation returns
RFC-7807 `ValidationProblemDetails`, and an uncaught exception falls through to the default ASP.NET 500 —
none of these carry the `{ data }`/`{ error }` shape. Domain-exception errors are enveloped as
`{ error: { message } }` by an **inline `try/catch` middleware** in `Program.cs`, with **no
machine-readable `code`** (whereas GraphQL already carries a stable `code` via `StruoErrorFilter`).

9a closes those gaps by making the envelope **automatic and centralized**, adds a `success` discriminator
and a machine-readable `error.code` (symmetric with GraphQL), and folds the currently-bare cases (bare
404, validation, unhandled 500) into the envelope.

**Scope fence.** Backend only, `Struo.Api` only — **`Struo.Domain` / `Struo.Application` /
`Struo.Infrastructure` are untouched, no new NuGet packages, no DB migration.** **GraphQL is untouched**:
`/graphql` keeps its spec-mandated `{ data, errors }` envelope and its `StruoErrorFilter` code mapping.
**Frontend code is untouched this slice** (deferred to 9a-fe). Binary file streams and 302 redirects
(`GET /api/files/{id}/content`) and `204 No Content` responses stay as-is (see §3).

## 1. Where each concern attaches (current → change)

| Concern | Current behaviour | Change for 9a |
|---|---|---|
| Success shape | `Ok(new { data })` / `Ok(new { data, meta })` written by hand in every action | **auto-wrapped** by a result filter; controllers return raw data / a `PagedResult` |
| List pagination meta | `new { total, limit, offset }` inline | controller returns `PagedResult(data, total, limit, offset)`; filter emits `meta` |
| Error shape (domain exc.) | inline `app.Use(try/catch)` in `Program.cs` → `{ error: { message } }`, no `code` | **`IExceptionHandler`** → `{ success:false, error:{ code, message } }`; middleware block deleted |
| Bare `NotFound()` (null resource) | bare 404, no body | filter converts to `{ success:false, error:{ code:"NOT_FOUND", message } }` |
| `NoContent()` (logout, delete) | bare 204 | **unchanged** — stays bare 204 |
| Model-binding validation | RFC-7807 `ValidationProblemDetails` (400) | `InvalidModelStateResponseFactory` → `{ success:false, error:{ code:"VALIDATION", message, details } }` |
| Unhandled exception | default ASP.NET 500 (dev page / empty) | `IExceptionHandler` fallback → `{ success:false, error:{ code:"INTERNAL_SERVER_ERROR", message } }` (masked, logged) |
| Manual `BadRequest/Unauthorized(new{error})` in Auth/Users/Files | hand-written error objects | replaced with a `Fail(status, code, message)` helper returning an `ErrorBody`, enveloped by the filter |
| Binary/redirect (`files/{id}/content`) | `File(...)` / `Redirect(...)` | **untouched** — filter skips `FileResult`/`RedirectResult`/`EmptyResult`/`ChallengeResult` |
| Error `code` taxonomy | REST: none; GraphQL: 5 codes in `StruoErrorFilter` | REST gains a parallel taxonomy (§4); the two mapping points stay symmetric |

## 2. Envelope contract

### Success

```json
{ "success": true, "data": <payload> }
```

List / paginated responses additionally carry `meta`:

```json
{ "success": true, "data": [ … ], "meta": { "total": 42, "limit": 25, "offset": 0 } }
```

- `data` is **always present** on success (may be an object, array, scalar, or `null`).
- `meta` is present **only** on list/paginated responses; it stays **offset-based** `{ total, limit,
  offset }` — the existing engine contract. It is **not** changed to a page-based `{ total, page, limit }`.
- Success responses carry **no `error` key**.
- `204 No Content` (logout, soft/hard delete) is a **bare 204 with no body** — success-with-no-content;
  HTTP semantics preserved, no envelope.

### Error

```json
{ "success": false, "error": { "code": "BAD_USER_INPUT", "message": "…" } }
```

`VALIDATION` errors additionally carry `details`:

```json
{ "success": false, "error": { "code": "VALIDATION", "message": "One or more validation errors occurred.",
  "details": [ { "field": "email", "message": "Email is required." } ] } }
```

- `error.code` is a stable machine-readable string (§4); `error.message` is a client-safe human string.
- `error.details` is present **only** on `VALIDATION`; it is an array of `{ field, message }`.
- Error responses carry **no `data` key**.
- The response is a discriminated union on `success`: a client can branch on `success` alone.

## 3. What is and isn't enveloped

**Enveloped** — every MVC controller action under `/api/*` that returns JSON: `ItemsController`,
`AuthController`, `UsersController`, `FilesController` (metadata actions), `SchemaController`,
`LanguagesController`, `PingController`.

**Not enveloped:**

- **`204 No Content`** — stays bare (success, no body).
- **Binary file streams** — `FileResult` (`GET /api/files/{id}/content` local-serve path).
- **302 redirects** — `RedirectResult` (`GET /api/files/{id}/content` presigned S3/MinIO path).
- **`ChallengeResult` / `EmptyResult`** — auth challenges and no-op results.
- **GraphQL** (`/graphql`) — its own `{ data, errors }` spec envelope, its own `StruoErrorFilter`. Not an
  MVC action, so the result filter never sees it.
- **Health checks** (`/health/*`), **OpenAPI/Scalar** — not MVC actions; untouched.

The result filter runs only on MVC results, so GraphQL / health / Scalar are excluded *by construction*.
Within controllers, the filter inspects the concrete `IActionResult` type to skip binary/redirect/empty.

## 4. Error code taxonomy

| Exception / condition | HTTP | `error.code` |
|---|---|---|
| `PermissionDeniedException` (anonymous caller) | 401 | `UNAUTHORIZED` |
| `PermissionDeniedException` (authenticated caller) | 403 | `FORBIDDEN` |
| `CollectionNotFoundException`; bare `NotFound()` (null resource) | 404 | `NOT_FOUND` |
| `RelationConflictException`, `ConcurrencyConflictException` | 409 | `CONFLICT` |
| `QueryException` | 400 | `BAD_USER_INPUT` |
| Model-binding / `[ApiController]` validation failure | 400 | `VALIDATION` (+ `details`) |
| any unhandled exception | 500 | `INTERNAL_SERVER_ERROR` (message masked, exception logged) |

The 401-vs-403 split for `PermissionDeniedException` is decided by the exception handler from
`HttpContext.User.Identity?.IsAuthenticated` — exactly the split `Program.cs` does today. GraphQL's
`StruoErrorFilter` does not distinguish 401 (it has no `UNAUTHORIZED` and no `VALIDATION` split); that is
its existing surface behaviour and is **not** changed here. The two mapping points (REST exception handler,
GraphQL error filter) stay independent but symmetric on the shared codes
(`FORBIDDEN`/`NOT_FOUND`/`CONFLICT`/`BAD_USER_INPUT`/`INTERNAL_SERVER_ERROR`).

## 5. Backend mechanism (four parts, all in `Struo.Api`)

### 5.1 Envelope types (`Struo.Api/Http/`)

- `ErrorBody` — `record ErrorBody(string Code, string Message, IReadOnlyList<ValidationDetail>? Details = null)`.
- `ValidationDetail` — `record ValidationDetail(string Field, string Message)`.
- `PagedResult` — a lightweight marker a controller returns to signal "this is a list with pagination
  meta": `record PagedResult(object Data, long Total, int Limit, int Offset)`. The filter unwraps it to
  `data` + `meta`.
- The success envelope object is built by the filter (anonymous or a small `record` — implementer's
  choice) as `{ success:true, data, meta? }`; the error envelope as `{ success:false, error }`. JSON
  serialization uses the app's existing camelCase options, so `success`/`data`/`meta`/`error` serialize
  lowercase.

### 5.2 `EnvelopeResultFilter : IAlwaysRunResultFilter` (`Struo.Api/Http/`)

Registered globally in `AddControllers(o => o.Filters.Add<EnvelopeResultFilter>())`. In
`OnResultExecuting`, it rewrites `context.Result`:

- `ObjectResult` whose `Value is ErrorBody eb` → `ObjectResult({ success:false, error:eb })`, status code
  preserved.
- `ObjectResult` whose `Value is PagedResult pr` and status is 2xx → `{ success:true, data:pr.Data,
  meta:{ total, limit, offset } }`.
- `ObjectResult` with a 2xx status and any other value → `{ success:true, data:value }`.
- `ObjectResult` with a non-2xx status and a non-`ErrorBody` value (rare) → `{ success:false, error:{
  code: <by status>, message: value?.ToString() ?? <status reason> } }`.
- `NotFoundResult` (bare 404, no body) → `{ success:false, error:{ code:"NOT_FOUND", message:"Resource
  not found." } }` at 404.
- `StatusCodeResult` with another 4xx/5xx and no body → `{ success:false, error:{ code:<by status> } }`.
- `NoContentResult` (204) → **left unchanged**.
- `FileResult`, `RedirectResult`, `ChallengeResult`, `EmptyResult`, `SignInResult`, `SignOutResult` →
  **left unchanged**.

The status→code helper maps 400→`BAD_USER_INPUT`, 401→`UNAUTHORIZED`, 403→`FORBIDDEN`, 404→`NOT_FOUND`,
409→`CONFLICT`, else→`INTERNAL_SERVER_ERROR`.

### 5.3 `StruoExceptionHandler : IExceptionHandler` (`Struo.Api/Http/`)

Registered via `builder.Services.AddExceptionHandler<StruoExceptionHandler>()` +
`builder.Services.AddProblemDetails()` (framework plumbing only) and `app.UseExceptionHandler()`. The
inline `app.Use(async (context, next) => { try {…} catch {…} })` block in `Program.cs` is **deleted** and
its logic moved here. `TryHandleAsync` maps the exception (§4 table) to `(status, code)`, writes
`{ success:false, error:{ code, message } }` (message = `ex.Message` for the client-safe domain
exceptions; masked generic message for the 500 fallback, with `ILogger.LogError`), sets the status, and
returns `true`. Uses `httpContext.User` for the 401/403 split.

### 5.4 `InvalidModelStateResponseFactory` (`Program.cs` / `AddControllers`)

Override `ApiBehaviorOptions.InvalidModelStateResponseFactory` so `[ApiController]` model-binding failures
produce a `BadRequestObjectResult(new ErrorBody("VALIDATION", "One or more validation errors occurred.",
details))` where `details` is built from `ModelState` (`{ field, message }` per error). The result filter
then envelopes it (its `Value is ErrorBody` branch), so validation and every other error share one code
path.

### 5.5 Controller simplification

- Success returns become raw: `Ok(result.Data)`, `Created(uri, created)`, `Ok(item)`,
  `Ok(new PagedResult(result.Data, result.Total, result.Limit, result.Offset))` for lists.
- `NotFound()` stays `NotFound()` (filter envelopes it); `NoContent()` stays `NoContent()`.
- Hand-written `BadRequest(new { error = new { message } })` / `Unauthorized(new { error … })` in
  `AuthController`/`UsersController`/`FilesController` become `Fail(StatusCodes.Status400BadRequest,
  "BAD_USER_INPUT", "…")` / `Fail(401, "UNAUTHORIZED", "Invalid credentials.")`, where `Fail` is a small
  `ControllerBase` extension returning `new ObjectResult(new ErrorBody(code, message)) { StatusCode =
  status }`. No behaviour/status change — only the wire shape gains `success`/`code`.

## 6. Frontend — no change this slice (deferred to 9a-fe)

The frontend is **not modified in 9a**. It is already backward-compatible with the new envelope, which is
what makes the deferral safe:

- `apiClient.request` unwraps success as `payload?.data ?? payload` → `{ success, data }` still hits
  `data`; it reads errors as `payload?.error?.message` → the new error envelope still has that.
- `itemsApi.list` uses `getRaw` and reads `res.data` + `res.meta.total` → `meta` keeps its
  `{ total, limit, offset }` shape, so `.data` and `.meta.total` still resolve.
- `res.status === 204` short-circuit → 204 stays bare, still handled.

Deferred to **9a-fe**: make `apiClient` *explicitly* envelope-aware (branch on `success` rather than the
`data ?? payload` coincidence), extend `ApiError` to `{ message, code?, details? }`, attach `code`/`details`
to the thrown `Error` for UI use, and update the frontend tests. The compatibility above is a **contract
guarded by 9a's live gate** (§7), not an accident to rely on forever — 9a-fe hardens it.

## 7. Testing strategy

**Backend unit / integration (`tests/`):**

- `EnvelopeResultFilter`: success `ObjectResult` → `{ success:true, data }`; `PagedResult` → `data` +
  `meta{total,limit,offset}`; `ErrorBody` value → `{ success:false, error }` with status preserved; bare
  `NotFoundResult` → 404 `NOT_FOUND` envelope; `NoContentResult` → untouched; `FileResult`/`RedirectResult`
  → untouched.
- `StruoExceptionHandler`: each domain exception → correct `(status, code)`; `PermissionDeniedException`
  anonymous → 401 `UNAUTHORIZED`, authenticated → 403 `FORBIDDEN`; unknown exception → 500
  `INTERNAL_SERVER_ERROR` masked + logged.
- `InvalidModelStateResponseFactory`: invalid model → 400 `VALIDATION` with `details` populated from
  `ModelState`.
- Controller-level integration (via the existing `WebApplicationFactory` test host): real HTTP responses
  for list (`meta`), get (`{success,data}`), create (201 envelope), a validation failure (`VALIDATION` +
  `details`), a domain-exception path (`BAD_USER_INPUT`), a 404 (`NOT_FOUND`), and a file `/content`
  request (still 302/binary, **not** enveloped).

**Compatibility gate (guards the 9a-fe deferral):** run the existing frontend suite (`pnpm test`) — it
must stay green against the new backend contract — plus a live smoke that the running SPA still logs in,
browses a collection list (pagination via `meta.total`), and completes a CRUD round-trip.

**Live gate (real Postgres, §17.2)** — bootstrap super-admin, API-level, on `web-struo-cms-db`:

1. `GET /api/items/article` → `{ success:true, data:[…], meta:{ total, limit, offset } }`.
2. `GET /api/items/article/{id}` → `{ success:true, data:{…} }`; unknown id → 404 `{ success:false,
   error:{ code:"NOT_FOUND" } }`.
3. `POST` create → 201 `{ success:true, data:{…} }`; a payload that fails model binding → 400
   `{ code:"VALIDATION", details:[…] }`.
4. `?deleted=banana` → 400 `{ code:"BAD_USER_INPUT" }` (the `QueryException` path).
5. Anonymous read of a non-public collection → 401 `{ code:"UNAUTHORIZED" }`; authenticated-but-denied →
   403 `{ code:"FORBIDDEN" }`.
6. A `CONFLICT` path (stale `version` on update, or an inbound-Restrict) → 409 `{ code:"CONFLICT" }`.
7. `GET /api/files/{id}/content` → still **302** (presigned) or binary stream — **not** enveloped.
8. `DELETE …` success → still bare **204**.
9. Any CJK message round-trips code-point-exact.

## 8. Risks & mitigations

- **Double-wrapping.** If a controller still returns `new { data }` while the filter also wraps, the
  response nests. Mitigation: the controller simplification (§5.5) is part of the slice — a grep for
  `new { data` / `new { error` in `Controllers/` must come back empty at the end; a test asserts a
  single-level `data`.
- **Enveloping something that shouldn't be.** File streams / redirects / 204 must pass through untouched.
  Mitigation: explicit type checks in the filter + a live check (§7.7) and a filter unit test.
- **`IExceptionHandler` vs the old inline middleware ordering.** The exception handler must sit so it
  catches exceptions thrown by controllers and by the permission/CSRF middleware as today. Mitigation:
  register `app.UseExceptionHandler()` at the same point the inline block sat, and keep a test for each
  exception→status mapping (characterization of the deleted middleware).
- **Frontend silently breaks despite the compatibility argument.** Mitigation: the compatibility gate in
  §7 runs the real frontend suite + a live SPA smoke before merge; if anything breaks, 9a-fe is pulled
  forward instead of deferred.
- **`meta` regressions.** `itemsApi.list` depends on `meta.total`. Mitigation: `PagedResult` carries all
  three fields; an integration test asserts the exact `meta` shape.

## 9. Out of scope (explicit)

- Frontend `apiClient`/`ApiError` changes — **9a-fe**.
- GraphQL envelope or `StruoErrorFilter` changes — GraphQL keeps `{ data, errors }`.
- Changing `meta` to page-based, adding `requestId`/`timestamp`/tracing fields to the envelope, or an
  API version field — YAGNI; can be additive later.
- Any `Domain`/`Application`/`Infrastructure` change, new NuGet package, or DB migration.
- Revisions (9c) and lifecycle hooks (9d).
