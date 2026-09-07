# 9. REST API

Every framework controller lives in `src/Struo.Api/Controllers/*.cs` — ten of them: `ItemsController`,
`FilesController`, `UsersController`, `RolesController`, `LanguagesController`, `SettingsController`,
`SchemaController`, `AuthController`, `ConfigController`, `PingController` — plus the two health-check
endpoints mapped directly in `Program.cs` (no controller class). This chapter documents the wire
contract each one exposes: the envelope every response is wrapped in, the stable error-code catalog,
authentication and CSRF, optimistic concurrency, and every endpoint grouped by controller. Query
semantics — the filter/sort/pagination/fields/deep/deleted/locale surface that `ItemsController`'s
`GET`/`POST query` actions share with GraphQL — are chapter 8's subject; this chapter only covers the
endpoints themselves.

## The response envelope

Every JSON response (success or error) is wrapped by `EnvelopeResultFilter`
(`src/Struo.Api/Http/EnvelopeResultFilter.cs`), an `IAlwaysRunResultFilter` registered on the MVC
pipeline in `Program.cs`. A success response is `{ "success": true, "data": ... }`, with an optional
`meta` object present only for a paginated list result (`Envelope.cs`'s `SuccessEnvelope.Meta` is
omitted from the JSON entirely when null — it is never emitted as `null`):

```
$ curl -s http://localhost:5221/api/ping
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-07-29T07:23:58.4897657Z"}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=fileName&limit=2"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}, ...],"meta":{"total":3,"limit":2,"offset":0}}
```

`meta` grows two more optional keys, `facets` and `aggregate`, only when the request asked for them
(`facets=`/`aggregate[<op>]=`, chapter 8) — each is its own `[JsonIgnore(Condition =
WhenWritingNull)]` property on `MetaInfo` (`src/Struo.Api/Http/Envelope.cs`), so a request that names
neither omits both, one that names only one omits the other, exactly like `meta` itself omits from a
non-paginated response:

```
"meta": {
  "total": 42, "limit": 25, "offset": 0,
  "facets": {
    "status": [{"value": "published", "count": 30}, {"value": "draft", "count": 12}],
    "category.name": [{"value": "Tech", "count": 25}, {"value": null, "count": 3}]
  },
  "aggregate": {"sum": {"price": 1234.5}, "max": {"price": 99, "rating": 5}, "count": {"publishedAt": 30}}
}
```

`facets` is an object keyed by the request's facet path strings, in the order they were requested; each
value is an array of `{ "value", "count" }` pairs, already ordered and capped per chapter 8's
"Ordering and the value cap". `aggregate` is an object keyed by op, each holding an object keyed by
field name — chapter 8's "Facets and aggregates" section has the full syntax, semantics, and live
transcripts (query string, JSON envelope, and four validation-error examples) for both.

An error response is always `{ "success": false, "error": { "code": "...", "message": "...", "details": [...] } }`
— `details` (a list of `{ "field", "message" }` pairs) is present only for the `VALIDATION` code and
otherwise omitted the same way `meta` is:

```
$ curl -s http://localhost:5221/api/languages
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Authentication required."}}
```

This envelope is produced by three independent layers that all funnel through the same
`Envelope.Success`/`Envelope.Error` helpers (`src/Struo.Api/Http/Envelope.cs`), so the shape is
identical regardless of which layer answered:

- **`EnvelopeResultFilter`** wraps whatever an MVC action returned: an `ObjectResult` in the 2xx range
  becomes `Envelope.Success`; a bare `NotFound()`/`StatusCode(4xx)` with no body becomes
  `Envelope.Error` with a default per-status message (`400` → "Bad request.", `401` → "Authentication
  required.", `403` → "Forbidden.", `404` → "Resource not found.", `409` → "Conflict."); a `204
  NoContent` result passes through untouched (stays bare, no envelope at all — see Status-code
  conventions below); a result that is already an `ErrorEnvelope`/`SuccessEnvelope` (built by the
  `ApiResults.Fail` helper or the validation factory) is left untouched, so wrapping is idempotent.
- **`StruoExceptionHandler`** (`src/Struo.Api/Http/StruoExceptionHandler.cs`, registered via
  `AddExceptionHandler<T>()`) catches any exception the action itself didn't handle, maps it through
  `DomainErrorMap` (below), and writes the envelope directly via `WriteAsJsonAsync` — this runs
  **outside** the MVC pipeline, so `EnvelopeResultFilter` never sees it, and the exception is logged
  server-side first when it collapses to `INTERNAL_SERVER_ERROR`.
- **Pre-MVC middleware** (`CsrfProtectionMiddleware`'s 403, the cookie scheme's `OnRedirectToLogin`/
  `OnRedirectToAccessDenied` 401/403, the login and password-change rate limiters' 429s — both route
  through the same `OnRejected` callback) writes the same envelope shape by hand, using the shared
  camelCase `JsonSerializerOptions` in `EnvelopeJsonOptionsHolder` — because these run before MVC's own
  `JsonOptions` (also camelCase, configured in `Program.cs`) would apply.

## Error codes

`ErrorCodes` (`src/Struo.Api/Http/ErrorCodes.cs`) declares exactly these fifteen stable `code` values. This
is the same catalog GraphQL's `StruoErrorFilter` uses (chapter 10) — a `PermissionDeniedException`
maps to the identical code on both protocols, for example — so a client that already handles GraphQL
errors recognizes REST errors by the same string.

| Code | Typical status | Meaning |
|---|---|---|
| `UNAUTHORIZED` | 401 | No/invalid credentials, or a `PermissionDeniedException` reached while unauthenticated (`DomainErrorMap` picks this over `FORBIDDEN` specifically because the caller isn't authenticated at all). A login with a *correct* password on a deactivated account does **not** land here — that gets its own code below (`ACCOUNT_INACTIVE`), since the caller has already proven the credential. |
| `FORBIDDEN` | 403 | Authenticated but not permitted — a per-collection RBAC denial, an `AdminOnly`-collection write attempted without super-admin, or a missing `X-Struo-CSRF` header. |
| `NOT_FOUND` | 404 | Unknown id, unknown collection (`CollectionNotFoundException`), or any bare `NotFound()` result. |
| `CONFLICT` | 409 | A `RelationConflictException` — deleting a row another row still references under `OnDelete.Restrict` (chapter 7) — or any other bare `409` result. |
| `VERSION_CONFLICT` | 409 | `ConcurrencyConflictException` — the optimistic-concurrency `version` the caller sent no longer matches the stored row. Split out from plain `CONFLICT` so a client's "reload and retry" recovery path can key on this one code alone (see Optimistic concurrency below). |
| `BAD_USER_INPUT` | 400 | A `QueryException` — malformed query parameters, an unknown filter field, a failed application-level check (weak password, duplicate/unknown role-permission collection, malformed id, a required-field-missing write — see below), etc. |
| `VALIDATION` | 400 | ASP.NET Core model-binding/model-state failure (a request-body property that fails `[Required]`/data-annotation validation before the action even runs) — the only code that carries `details`. |
| `INTERNAL_SERVER_ERROR` | 500 | Any exception `DomainErrorMap` doesn't recognize. The client-facing message is always the masked generic string `"An internal error occurred."`; the real exception is logged server-side, never leaked to the response. |
| `TOO_MANY_REQUESTS` | 429 | One of three independent producers rejected the request, none of them through `DomainErrorMap` (no exception is thrown for any of them): `POST /api/auth/login`'s per-account throttle (keyed by the account in the request body; chapter 3's `RateLimiting:LoginAccount` section), `POST /api/auth/login`'s per-client-IP limiter (chapter 3's `RateLimiting:Login` section), or `PUT /api/users/{id}/password`'s limiter (partitioned by the authenticated caller's user id; chapter 3's `RateLimiting:Password` section). The two client-IP-and-user-id limiters write this code from a shared `OnRejected` callback that picks its message wording ("login attempts" vs. "password change attempts") from whichever policy rejected; the per-account throttle writes it directly from `AuthController.Login` using the identical "login attempts" wording, so the two login-endpoint producers share the same status, code, and message. `Retry-After` is the one thing that differs between them: the per-client-IP limiter's is bounded by `RateLimiting:Login:WindowSeconds` (`60` by default) when that layer is even enabled, while the per-account throttle's is bounded by `RateLimiting:LoginAccount:WindowSeconds` (`900` by default) and so can be far larger — see the live example below. |
| `PAYLOAD_TOO_LARGE` | 413 | A streamed upload whose actual bytes exceed `Struo:Files:MaxUploadBytes` even though the declared `Content-Length` passed the up-front check (a "lying" or chunked upload). Not exercised live in this chapter — triggering it needs an upload past the configured 25 MB default — but the mapping is real: `DomainErrorMap.StatusFor` → 413. |
| `INVALID_CURRENT_PASSWORD` | 400 | `PUT /api/users/{id}/password`, self-service branch: the caller supplied a missing or wrong `currentPassword`. Deliberately not `401` — the caller already holds a valid session, and the SPA's global 401 handler clears the session on every `401` it sees, so reusing `UNAUTHORIZED` here would log the caller out on a plain typo. |
| `NO_LOCAL_PASSWORD` | 400 | Self-service password change attempted on an account provisioned entirely through external OIDC, whose stored hash is the empty string because it never had a local password. |
| `ACCOUNT_INACTIVE` | 401 | `POST /api/auth/login` with a *correct* password on a deactivated account. Only reachable after a successful hash verify, so surfacing it leaks nothing the caller hadn't already proven — unlike splitting "wrong password" from "no such account", which stays merged under `UNAUTHORIZED` above (chapter 12 covers why). |
| `SESSION_REVOCATION_FAILED` | 500 | `SessionRevocationFailedException` — a password change or a user delete already committed successfully, but the follow-up session-revocation step then failed, so some of that user's existing sessions may still be live (chapter 12). `DomainErrorMap.StatusFor` has no dedicated arm for this code, so it falls through to the default 500 status even though the code and message are specific, not the masked generic string `INTERNAL_SERVER_ERROR` otherwise carries. |
| `SEARCH_UNAVAILABLE` | 503 | `SearchUnavailableException` — a registered `ISearchProvider` (chapter 8's [Search providers](08-query-dsl.md#search-providers)) threw because its own search engine could not answer (connection refused, timeout, missing index). The client-facing message is always the fixed string `"Search is temporarily unavailable."` (`DomainErrorMap.SearchUnavailableMessage`) — the provider's own exception message, which may name an internal host, only reaches the server-side Warning log, never the response. |

`DomainErrorMap` (`src/Struo.Api/Http/DomainErrorMap.cs`) is the single source of the exception→code
mapping, shared verbatim with GraphQL's error filter:

```csharp
PermissionDeniedException when !authenticated => (ErrorCodes.Unauthorized, exception.Message),
PermissionDeniedException => (ErrorCodes.Forbidden, exception.Message),
CollectionNotFoundException => (ErrorCodes.NotFound, exception.Message),
FileBlobNotFoundException => (ErrorCodes.NotFound, exception.Message),
ConcurrencyConflictException => (ErrorCodes.VersionConflict, exception.Message),
RelationConflictException => (ErrorCodes.Conflict, exception.Message),
QueryException => (ErrorCodes.BadUserInput, exception.Message),
PayloadTooLargeException => (ErrorCodes.PayloadTooLarge, exception.Message),
SessionRevocationFailedException => (ErrorCodes.SessionRevocationFailed, exception.Message),
SearchUnavailableException => (ErrorCodes.SearchUnavailable, SearchUnavailableMessage),
_ => (ErrorCodes.Internal, "An internal error occurred."),
```

Live-triggered examples below cover every code above except `PAYLOAD_TOO_LARGE` and `SEARCH_UNAVAILABLE` (both excused above, in their own row — the latter needs a registered `ISearchProvider` that actively fails, which the sample host does not carry), `INTERNAL_SERVER_ERROR` (excused below), and three more codes: `INVALID_CURRENT_PASSWORD` gets its own live example in the Users section further down instead; `NO_LOCAL_PASSWORD` and `ACCOUNT_INACTIVE` are not live-triggered anywhere in this chapter:

```
$ curl -s http://localhost:5221/api/languages
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Authentication required."}}

$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" -b cookies.txt -d '{}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/nope"
{"success":false,"error":{"code":"NOT_FOUND","message":"Unknown collection 'nope'."}}

$ curl -s -X DELETE http://localhost:5221/api/items/mediaFolder/<guides-id> -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediaFolder/<guides-id>': referenced by 'file'."}}

$ curl -s -X PUT http://localhost:5221/api/items/file/<alpha-id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"status":"published","version":1}'
{"success":false,"error":{"code":"VERSION_CONFLICT","message":"The record was modified by someone else since you loaded it. Reload and try again."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bbogus%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogus' on collection 'file'."}}

$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"password":"whatever123"}'
{"success":false,"error":{"code":"VALIDATION","message":"One or more validation errors occurred.","details":[{"field":"Email","message":"The Email field is required."}]}}

$ for i in 1 2 3 4 5 6 7 8 9 10 11; do curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"nobody@example.com","password":"wrong"}'; done
401 401 401 401 401 401 401 401 401 401 429
$ curl -s -i -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"nobody@example.com","password":"wrong"}'
HTTP/1.1 429 Too Many Requests
Content-Type: application/json; charset=utf-8
Retry-After: 897
X-Content-Type-Options: nosniff
{"success":false,"error":{"code":"TOO_MANY_REQUESTS","message":"Too many login attempts. Please try again later."}}
```

That is the per-account throttle (`RateLimiting:LoginAccount`, on by default, `PermitLimit` `10` —
chapter 3), not the per-client-IP limiter: the eleventh failure against `nobody@example.com` is what
trips it, and its `Retry-After` reflects however much of the `900`-second window remained at that
moment (`897` here — three seconds had already elapsed). The per-client-IP limiter (`RateLimiting:Login`)
ships disabled, so nothing stops the same client IP from failing logins against a *different* account
with no `429` at all — buckets are keyed by account, not by IP:

```
$ for i in 1 2 3 4 5 6; do curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"someone-else@example.com","password":"wrong"}'; done
401 401 401 401 401 401
```

(`INTERNAL_SERVER_ERROR` is deliberately not demonstrated with a crafted request — provoking one means
finding an actual unhandled server bug, which this chapter has no business doing; the mapping above and
the masked/logged behavior are read directly from `StruoExceptionHandler`/`DomainErrorMap` source.)

## Status-code conventions

| Status | When |
|---|---|
| `200 OK` | A read, or a write whose result is meaningfully returned in the body (create/update/get/list all return `200` — note `Created`'s `201` below is the one exception). |
| `201 Created` | `POST /api/items/{collection}`, `POST /api/users`, and `POST /api/files` — the response body carries the created row, and all three send a relative `Location` header pointing at the created row's canonical `GET` route: `/api/items/{collection}/{id}`, `/api/items/user/{id}` (the generic items route, not a dedicated users one — that's where the row actually reads back), and `/api/files/{id}` respectively. `ItemsController.Create`, `UsersController.Create`, and `FilesController.Upload` all call `Created(uri, value)` (a `CreatedResult`, which normally writes `Location` during its own `ExecuteResultAsync`). `EnvelopeResultFilter` special-cases `CreatedResult` ahead of its generic `ObjectResult` branch and rebuilds it as a **new** `CreatedResult` carrying the enveloped body, so the `Location`-writing behavior still runs when ASP.NET Core formats the response — unlike the generic branch, which would otherwise flatten it to a plain `ObjectResult` and lose the header. `CreatedAtActionResult`/`CreatedAtRouteResult` are deliberately **not** covered by this rebuild: their `Location` is computed from `IUrlHelper` at formatting time, after the filter has already run, so it can't be reconstructed here. Nothing in this template uses either of those two result types; a fork that wants one should return `Created(uri, value)` instead. |
| `204 No Content` | Every delete (trash or purge), restore, and logout — no body at all; `EnvelopeResultFilter` explicitly leaves a `NoContentResult` bare rather than wrapping it in an envelope. |
| `400 Bad Request` | `BAD_USER_INPUT` or `VALIDATION` (see the error-code table). |
| `401 Unauthorized` | `UNAUTHORIZED`. |
| `403 Forbidden` | `FORBIDDEN`. |
| `404 Not Found` | `NOT_FOUND`. |
| `409 Conflict` | `CONFLICT` or `VERSION_CONFLICT`. |
| `413 Payload Too Large` | `PAYLOAD_TOO_LARGE`. |
| `429 Too Many Requests` | `TOO_MANY_REQUESTS` (either login-endpoint defense or the password-change limiter — see above). |
| `503 Service Unavailable` | `SEARCH_UNAVAILABLE`. |
| `500 Internal Server Error` | `INTERNAL_SERVER_ERROR`. |

```
$ curl -s -i -X DELETE http://localhost:5221/api/files/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
```

## Validation error details shape

A request body that fails ASP.NET Core's own model binding/data-annotation validation (not a
domain-level `QueryException`) never reaches the controller action at all — the
`InvalidModelStateResponseFactory` configured in `Program.cs` intercepts it and returns `400` with code
`VALIDATION`, `details` populated from `ModelState`, one `{ "field", "message" }` entry per invalid
property (message falls back to `"Invalid value."` when ASP.NET Core supplies no message of its own):

```
$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"password":"whatever123"}'
{"success":false,"error":{"code":"VALIDATION","message":"One or more validation errors occurred.","details":[{"field":"Email","message":"The Email field is required."}]}}
```

This is distinct from an application-level `QueryException`, which surfaces as `BAD_USER_INPUT` with no
`details` array at all — the message is the whole story:

```
$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"email":"nobody@example.com","password":"short"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Password must be at least 8 characters."}}
```

**A `Required` `[CmsField]` must be resent on every write, including a partial update.** This is easy to
miss: `ItemService.UpdateCoreAsync`'s "only overlay fields the client sent" merge only decides whether a
sent field *overwrites* the existing row — but `ItemDeserializer.Deserialize` (shared by create and
update, `src/Struo.Application/Query/Write/ItemDeserializer.cs`) validates every `Required` field against
the **freshly parsed request body** before that merge ever runs, regardless of whether the field was
part of this particular call's intent. Omitting a `Required` field from an otherwise-valid partial `PUT`
fails with `BAD_USER_INPUT`, not silently keeping the existing value:

```
$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"description":"partial update, no name"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'name' is required."}}

$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"name":"Demo9","description":"partial update, name resent"}'
{"success":true,"data":{"id":"...","version":1,"name":"Demo9","isSuperAdmin":false,"description":"partial update, name resent", ...}}
```

Translatable `Required` fields are exempt from this — they validate per-locale inside the translation
sidecar sync, not on the parent deserialize (see `ItemWriteSideSync` and chapter 6).

## Authentication: cookie or bearer

Two schemes are accepted, both registered in `AuthWiring.AddStruoAuth` (`src/Struo.Api/Auth/AuthWiring.cs`):

- **Cookie** (`AuthSchemes.Cookie`, the session cookie named `struo.session`) — set by
  `POST /api/auth/login`, an 8-hour sliding-expiration ticket backed by Redis (or an in-memory
  fallback; chapter 3).
- **Bearer** (`AuthSchemes.Bearer`, a per-user access token from `POST /api/users/{id}/access-token`,
  sent as `Authorization: Bearer <token>`) — verified by `BearerTokenAuthenticationHandler`
  (`src/Struo.Api/Auth/BearerTokenAuthenticationHandler.cs`) against the hashed token store.

Neither of those two is itself ASP.NET Core's *default* authenticate scheme — a third, forwarding
scheme is: `AuthSchemes.Adaptive` (`"Adaptive"`, registered via `AddAuthentication(AuthSchemes.Adaptive)`
plus `AddPolicyScheme` in `AuthWiring.AddStruoAuth`) forwards to `Bearer` whenever the request's
`Authorization` header starts with `Bearer `, and to `Cookie` otherwise. Because `Adaptive` is the
default, ASP.NET Core authenticates **every request** this way — with or without an `[Authorize]`
attribute on the action — as whichever of the two real schemes actually matches the request. The
header alone decides, and the cookie is never consulted by this selector: a caller presenting BOTH a
session cookie and an `Authorization: Bearer` header is resolved as the bearer identity on every
`[Authorize]`-free action, so a bad or revoked token downgrades that caller to the `public` floor
rather than falling back to the cookie's own grants — fail-closed by design, not a bug (chapter 12
covers the `public` floor).

Actions that carry `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]` (a comma-joined
scheme list: `"Cookies,Bearer"`) — every write on `ItemsController`/`FilesController`, and every action
on `UsersController`/`RolesController`/`LanguagesController`/`SettingsController`/`SchemaController`,
plus `AuthController`'s `logout`/`me` — additionally name **both** schemes explicitly: ASP.NET Core's
`PolicyEvaluator` calls `AuthenticateAsync` against each named scheme in turn and merges whichever
principals succeed, on top of driving `[Authorize]`'s own challenge/forbid logic — so on these actions a
bearer token has always probed and worked identically to a cookie, regardless of which scheme `Adaptive`
would have forwarded to by default:

```
$ curl -s -i -X PUT http://localhost:5221/api/items/file/<id> -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"status":"published"}'
HTTP/1.1 200 OK
{"success":true,"data":{"id":"...","version":2, ...}}
```

**`ItemsController`'s read actions (`GET`/`POST query` on `/api/items/{collection}` and
`/api/items/{collection}/{id}`, plus the `.../revisions` actions) still carry no `[Authorize]` attribute
at all** — deliberately, since a read only needs the ordinary per-collection `CanRead` RBAC check, not a
blanket authentication requirement (chapter 8), and `Adaptive` doesn't change that. What it does change
is which identity resolves for a bearer-only caller hitting one of these actions: `Adaptive` still
authenticates a `Bearer` header even on an action that names no scheme at all, so the caller is
resolved as **itself** — with its own roles' grants unioned with the `public` floor (chapter 12) — the
same as a cookie-authenticated caller would be, not as anonymous. An anonymous request (no credential
at all) still gets whatever `public`'s own grants allow, unchanged. The same bearer token therefore now
works identically whether it hits a plain read on `ItemsController` or a sibling `[Authorize]`-attributed
endpoint on a different controller:

```
$ curl -s -H "Authorization: Bearer <token>" "http://localhost:5221/api/languages"
{"success":true,"data":[{"code":"en","name":"English","isDefault":true}, ...]}
```

`LanguagesController` and `SchemaController` are notable for requiring only *authentication*, not a
collection-specific `CanRead` grant — any signed-in user (cookie or bearer) can read the full language
list or the full schema, regardless of their `language`/per-collection RBAC grants (see the source
comments on each controller for why).

## CSRF: the `X-Struo-CSRF` header

`CsrfProtectionMiddleware` (`src/Struo.Api/Auth/CsrfProtectionMiddleware.cs`) requires the
`X-Struo-CSRF` header — checked for **presence only**, its value is never inspected — on every
non-safe HTTP method (`POST`/`PUT`/`DELETE`/... — `GET`/`HEAD`/`OPTIONS`/`TRACE` are exempt), but
**only when the request rides on the session cookie**: a `Bearer`-authenticated request is exempt (no
ambient browser credential to forge), and so is a request carrying no session cookie at all (nothing
yet to attack, e.g. `POST /api/auth/login` before a session exists). This is the OWASP
"custom-request-header" CSRF defense: a cross-site page cannot attach a custom header to a credentialed
request unless the target's CORS policy already allows that origin, and StruoCMS CORS is allowlist-only
and default-off.

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" -b cookies.txt -d '{}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}
```

The middleware guards **every** non-safe request path, not just `/api/*` — `POST /graphql` is a
non-safe method too, so a cookie-authenticated GraphQL request (query *or* mutation alike) needs the
same header (chapter 10 covers this in the GraphQL context).

## Optimistic concurrency (`version` round-trip)

Every collection's rows carry a `version` integer (from `AuditableEntity`). A `GET`/list response
always includes it; an `UPDATE` may echo it back in the request body. `ItemService.UpdateCoreAsync`
overlays the client's `version` onto the entity it's about to save, and the repository's compare-and-
swap update runs as `WHERE version = <echoed value>`: zero affected rows raises
`ConcurrencyConflictException` → `VERSION_CONFLICT` / `409`. Omitting `version` entirely falls back to
whatever was just loaded — no protection, but backward compatible for callers that don't track it:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>"
{"success":true,"data":{"id":"...","version":2,"fileName":"alpha-report.txt", ...}}

$ curl -s -X PUT http://localhost:5221/api/items/file/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"status":"published","version":1}'
{"success":false,"error":{"code":"VERSION_CONFLICT","message":"The record was modified by someone else since you loaded it. Reload and try again."}}
```

## What a `POST`/`PUT` body may set

`ItemDeserializer.Deserialize` (`src/Struo.Application/Query/Write/ItemDeserializer.cs`), shared by
create and update, binds a request body against an allowlist rather than the whole CLR entity type:
only declared, writable `[CmsField]`s and declared `ManyToOne` relation foreign keys (e.g.
`categoryId`) bind onto the parent entity. The `translations` sidecar payload and M2M relation keys
(e.g. `tags`) are excluded from that bind — `ItemDeserializer` strips them out before deserializing the
parent entity — but they still take effect: `ItemService` reads them from the original request body and
applies them separately, in the same transaction as the parent row, via
`ItemWriteSideSync.SyncTranslationsAsync`/`SyncM2MAsync`
(`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`). Every other top-level key — including `id`,
`version`, and the soft-delete/audit columns (`deletedAt`, `deletedBy`, `createdAt`, `createdBy`,
`updatedAt`, `updatedBy`) — is silently dropped, not rejected: the write still succeeds, just without
that key taking effect. `POST` and `PUT` share this allowlist, so a create request cannot set a
client-chosen id or seed the optimistic-concurrency version any more than an update request can.

### Many-to-many write shape: bare ids and junction payload

A many-to-many relation's array (`"tags": [...]`) accepts a **mixed list**: each element is either a
bare id or, for a relation whose junction declares payload (chapter 7), an object carrying `id` plus
payload field values. `ItemWriteSideSync.SyncM2MAsync`
(`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`) merges the array into an ordered, id-keyed
list of links before syncing, following three rules:

- A **bare id** means membership only — the target is linked (or stays linked), and any existing
  payload on its junction row is left untouched.
- An **object** (`{ "id": ..., "<payloadField>": ... }`) means membership *and* a merge of the payload
  fields it names into the junction row — fields the object doesn't mention keep their current value.
  When the same id appears more than once in the array, an object always outranks a bare id (whichever
  came first or last), and between two objects the later one wins — it **replaces**, not merges with,
  the earlier object's payload.
- The **order of first appearance** in the array is the sort order (when the relation declares a
  `SortField`), regardless of which later duplicate of that id ends up winning.

Unknown keys inside a payload object are ignored, and so are the junction's own foreign-key, sort, and
`id`-adjacent structural keys if a caller includes them in the payload portion — only the relation's
declared payload fields (chapter 7) are ever merged in. An element carrying a payload additionally
requires the caller to hold the **write** grant on the junction collection itself (and, if the junction
is `AdminOnly`, to be a super-admin) — checked before any payload is parsed, so a caller lacking that
grant gets `403 FORBIDDEN` (`"Write to '{junction}' not permitted."`, or `"Writes to '{junction}'
require a super-admin."`) rather than a validation error about the payload's own fields. A bare id
needs only the parent collection's ordinary write grant, exactly as before this feature. This same
gate applies to `.../revisions/{n}/revert` (chapter 13) whenever the reverted-to snapshot carries
junction payload — a revert re-applies `[{id, ...payload}]` elements exactly like a direct write, so it
also requires the junction collection's write grant, not just the parent's. Illustrative
mixed-array body against the sample's `article` (`Article.Tags`, chapter 16 — `Note` is `ArticleTag`'s
payload field):

```json
{
  "tags": [
    "<tag-id-1>",
    { "id": "<tag-id-2>", "note": "editor pick" }
  ]
}
```

`<tag-id-1>` is linked with its existing payload untouched (or defaulted, if newly linked); `<tag-id-2>`
is linked and its junction row's `note` column is set to `"editor pick"`.

The junction rows themselves now survive a re-sync: `ManyToManySync`
(`src/Struo.Infrastructure/Query/ManyToManySync.cs`) diffs the incoming link list against what is
already stored for the parent — deleting rows for targets no longer present, inserting rows for newly
added targets, and updating rows for targets that remain in place — rather than the "delete everything,
reinsert everything" approach an earlier version of this sync used; a junction row's own primary key is
therefore stable across a write that keeps its target linked, which matters for anything that
references a junction row by id (a duplicate `(parent, target)` pair left over from data written before
this behavior existed is reduced to its lowest-primary-key row the next time that parent is synced,
with one warning logged).

## Endpoint reference, by controller

Required permission below always means the per-collection RBAC grant checked by `ItemService`/
`FileAccessPolicy` (`CanRead`/`CanWrite`/`CanDelete` — chapter 12 covers RBAC's own admin surface),
**in addition to** whatever `[Authorize]` attribute (if any) is noted. Writing to an `AdminOnly`
collection (`permission`, `role`, `user`, `userRole`) additionally requires the caller to be a
super-admin regardless of any delegated per-collection grant
(`ItemService.RequireSuperAdminForAdminOnly`) — a plain per-collection write grant on one of these four
is not enough:

```
$ curl -s -i -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"description":"hacked"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Writes to 'role' require a super-admin."}}
```

**Route constraints fail before any of this runs.** `ItemsController`'s generic `{id}` segment is a
plain, unconstrained route parameter (a collection's id column isn't always a `Guid`), but every
`FilesController`/`UsersController`/`RolesController` id route is declared `{id:guid}`, and
`ItemsController`'s two revision routes are `{id}/revisions/{revisionNumber:long}` — a value that fails
the constraint never reaches the action at all: ASP.NET Core's routing simply doesn't match, so the
response is a **bare, empty-body `404`** (no envelope, `Content-Length: 0`) rather than the domain
`{"success":false,"error":{"code":"NOT_FOUND",...}}` shape a genuinely unknown-but-well-formed id
produces:

```
$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/not-a-guid"
HTTP/1.1 404 Not Found
Content-Length: 0

$ curl -s -i -b cookies.txt "http://localhost:5221/api/items/file/<id>/revisions/not-a-number"
HTTP/1.1 404 Not Found
Content-Length: 0
```

### Items (`ItemsController`, `api/items/{collection}`)

The generic CRUD surface over every `[CmsCollection]` — `language`, `permission`, `role`, `user`,
`userRole`, `file`, `mediaFolder` on this host (no sample opted in). See chapter 8 for the full
`filter`/`sort`/`limit`/`offset`/`fields`/`deep`/`search`/`locale`/`deleted`/`facets`/`aggregate` query
surface.

| Method & path | Query params | Body | Response | Auth | Permission |
|---|---|---|---|---|---|
| `GET /api/items/{collection}` | `filter[...]`, `sort`, `limit`, `offset`, `fields`, `deep`, `search`, `locale`, `deleted`, `facets`, `aggregate[<op>]` | — | `200`, list + `meta` | none (`[Authorize]`-absent; `Adaptive` still authenticates a cookie or bearer credential if present — see above) | `CanRead` |
| `POST /api/items/{collection}/query` | `locale`, `deleted` (read from the URL even here) | JSON envelope (chapter 8), including `facets`/`aggregate` | `200`, list + `meta` | none (same caveat) | `CanRead` |
| `GET /api/items/{collection}/{id}` | `deep`, `locale`, `deleted` | — | `200` item, or `404` | none (same caveat) | `CanRead` (`deleted=only\|with` additionally needs `CanDelete`) |
| `POST /api/items/{collection}` | — | JSON object of writable fields (allowlisted — see above) | `201` created item, `Location: /api/items/{collection}/{id}` (see Status-code conventions above) | Cookie or Bearer | `CanWrite` (+ super-admin if `AdminOnly`) |
| `PUT /api/items/{collection}/{id}` | — | JSON object, partial (only sent keys overlay — but see the `Required`-field caveat above) | `200` updated item, or `404` | Cookie or Bearer | `CanWrite` (+ super-admin if `AdminOnly`) |
| `DELETE /api/items/{collection}/{id}` | `purge` (bool, default `false`) | — | `204`, or `404` | Cookie or Bearer | `CanDelete` (+ super-admin if `AdminOnly`) |
| `POST /api/items/{collection}/{id}/restore` | — | — | `200` restored item, or `404` | Cookie or Bearer | `CanDelete` (+ super-admin if `AdminOnly`) |
| `GET /api/items/{collection}/{id}/revisions` | — | — | `200`, array of `{ revisionNumber, operation, createdAt, createdBy, sourceRevisionNumber }` (`[]` when the collection has no `Revisions=true`) | none (same caveat) | `CanRead` |
| `GET /api/items/{collection}/{id}/revisions/{n}` | — | — | `200`, the entry above plus `snapshot` (hidden fields redacted), or `404` | none (same caveat) | `CanRead` |
| `POST /api/items/{collection}/{id}/revisions/{n}/revert` | — | — | `200` reverted item (re-applies the snapshot as an update, recorded as a new `"revert"` revision), or `404` | Cookie or Bearer | `CanWrite` (+ super-admin if `AdminOnly`) |

`GET /api/items/{collection}/{id}` parses `?deleted=` through the identical `DeletedMode`/
`DeletedAccessGuard` path the list/query actions use (chapter 8) — an invalid value is rejected the
same way regardless of which of the three actions it reaches:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/e2269ae7-094d-448d-bcb9-b484418de9b2?deleted=bogus"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Query parameter 'deleted' must be exclude|only|with."}}
```

None of the seven live framework collections declares `[CmsCollection(Revisions = true)]`, so on this
host every revisions/revert action above is live-reachable but always resolves to the "no revisions"
branch:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>/revisions"
{"success":true,"data":[]}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/items/file/<id>/revisions/1"
HTTP/1.1 404 Not Found
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
```

`DELETE` on a soft-deletable collection (only `file`, of the seven — `softDelete: true` in its schema)
trashes by default and permanently purges with `?purge=true`; every other collection has no soft-delete
tier at all, so `DELETE` is always a purge regardless of the query string:

```
$ curl -s -i -X DELETE http://localhost:5221/api/items/file/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=only"
{"success":true,"data":[{"fileName":"...", ...}],"meta":{"total":1, ...}}
```

A delete blocked by another row's `OnDelete.Restrict` (chapter 7) surfaces as plain `CONFLICT`, not
`VERSION_CONFLICT`:

```
$ curl -s -i -X DELETE http://localhost:5221/api/items/mediaFolder/<guides-id> -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediaFolder/<guides-id>': referenced by 'file'."}}
```

### Files (`FilesController`, `api/files`)

The dedicated upload/storage/image-transform pipeline for the `file` collection — mutations go through
`FileService`/`IFileStorage` rather than the generic `ItemService`, so RBAC is enforced here via
`IFileAccessPolicy` instead.

| Method & path | Query params | Body | Response | Auth | Permission |
|---|---|---|---|---|---|
| `POST /api/files` | — | `multipart/form-data`: `file` (required), `folderId` (optional) | `201`, `Location: /api/files/{id}`, `{ id, fileName, contentType, size, width, height, status, folderId }` | Cookie or Bearer | `IFileAccessPolicy.CanWrite()` |
| `GET /api/files/{id}` | — | — | `200` metadata, or `404` (a non-published file also `404`s unless the caller has read-unpublished access) | none (`[Authorize]`-absent; `Adaptive` still authenticates a cookie or bearer credential if present — needed for the permission check at right) | none for a published file; for a non-published one, an **authenticated identity plus a `file` write grant** (`IFileAccessPolicy.CanReadUnpublished` — chapter 11 — not a read grant) |
| `GET /api/files/{id}/content` | `width`, `height`, `format`, `fit`, `quality` (image transform, chapter 11) | — | `200` bytes (streamed, or `302` when `Struo:Files:PresignedRedirect` is on), or `404` | same as `Get` above | same as `Get` above |
| `DELETE /api/files/{id}` | `purge` (bool, default `false`) | — | `204`, or `404` | Cookie or Bearer | `CanDelete()` |
| `POST /api/files/{id}/restore` | — | — | `204`, or `404` | Cookie or Bearer | `CanDelete()` |

```
$ curl -s -X POST http://localhost:5221/api/files -H "X-Struo-CSRF: 1" -b cookies.txt -F "file=@pixel.png;type=image/png"
{"success":true,"data":{"id":"...","fileName":"pixel.png","contentType":"image/png","size":70,"width":1,"height":1,"status":"published","folderId":null}}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=1&format=webp"
HTTP/1.1 200 OK
Content-Type: image/webp

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?format=bogus"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unsupported format 'bogus'."}}

$ curl -s -i -X DELETE http://localhost:5221/api/files/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
$ curl -s -i -X POST http://localhost:5221/api/files/<id>/restore -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
$ curl -s -i -X DELETE "http://localhost:5221/api/files/<id>?purge=true" -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
```

### Users (`UsersController`, `api/users`) — all actions require Cookie or Bearer

| Method & path | Body | Response | Permission |
|---|---|---|---|
| `POST /api/users` | `{ email, password, name? }` | `201`, `Location: /api/items/user/{id}`, `{ id, email, name }` | super-admin |
| `PUT /api/users/{id}/password` | `{ newPassword, currentPassword? }` | `204`, or `404` | super-admin (changing another user) — or self, proving `currentPassword` |
| `POST /api/users/{id}/access-token` | — | `200`, `{ token }` (shown once — only the hash is stored) | super-admin |
| `DELETE /api/users/{id}/access-token` | — | `204`, or `404` | super-admin |
| `GET /api/users/{id}/effective-permissions` | — | `200`, `{ isSuperAdmin, permissions }` — accepts `?roles=` (a comma list of role ids) to preview a *hypothetical*, unsaved role selection instead of the user's stored roles; `?roles=` (present but empty) previews the public-role floor, distinct from the parameter being absent entirely | super-admin |

```
$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"email":"editor@example.com","password":"editorpass1"}'
{"success":true,"data":{"id":"...","email":"editor@example.com","name":null}}

$ curl -s -i -X PUT http://localhost:5221/api/users/<self-id>/password -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"newPassword":"newpassword2"}'
HTTP/1.1 400 Bad Request
{"success":false,"error":{"code":"INVALID_CURRENT_PASSWORD","message":"Current password is incorrect."}}

$ curl -s -X POST http://localhost:5221/api/users/<id>/access-token -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":true,"data":{"token":"<token>"}}

$ curl -s -b cookies.txt "http://localhost:5221/api/users/<id>/effective-permissions?roles="
{"success":true,"data":{"isSuperAdmin":false,"permissions":{}}}
```

### Roles (`RolesController`, `api/roles`) — all actions require Cookie or Bearer + super-admin

| Method & path | Body | Response |
|---|---|---|
| `GET /api/roles/{id}/permissions` | — | `200`, array of `{ collection, canRead, canWrite, canDelete }`, or `404` |
| `PUT /api/roles/{id}/permissions` | JSON array of the same shape — a **full replace** of the role's grant set (delete-all + insert-all in one transaction); an all-`false` entry is stored as absent | `200`, the saved (non-`false`) rows, or `400`/`404` |

```
$ curl -s -X PUT http://localhost:5221/api/roles/<id>/permissions -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[{"collection":"role","canRead":true,"canWrite":true,"canDelete":false}]'
{"success":true,"data":[{"collection":"role","canRead":true,"canWrite":true,"canDelete":false}]}

$ curl -s -X PUT http://localhost:5221/api/roles/<id>/permissions -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[{"collection":"file","canRead":true,"canWrite":false,"canDelete":false},{"collection":"file","canRead":false,"canWrite":true,"canDelete":false}]'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Duplicate collection entries: file."}}

$ curl -s -X PUT http://localhost:5221/api/roles/<id>/permissions -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[{"collection":"bogus","canRead":true,"canWrite":false,"canDelete":false}]'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown collections: bogus."}}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/roles/00000000-0000-0000-0000-000000000000/permissions"
{"success":false,"error":{"code":"NOT_FOUND","message":"Role not found."}}

$ curl -s -i -b editor-cookies.txt -H "X-Struo-CSRF: 1" -X PUT "http://localhost:5221/api/roles/<id>/permissions" -d '[]'
{"success":false,"error":{"code":"FORBIDDEN","message":"Admin role required."}}
```

### Languages (`LanguagesController`, `api/languages`) — requires Cookie or Bearer only

| Method & path | Response |
|---|---|
| `GET /api/languages` | `200`, array of `{ code, name, isDefault }` — enabled languages only |

```
$ curl -s -b cookies.txt "http://localhost:5221/api/languages"
{"success":true,"data":[{"code":"en","name":"English","isDefault":true},{"code":"zh-TW","name":"繁體中文","isDefault":false}]}
```

### Settings (`SettingsController`, `api/settings`) — requires Cookie or Bearer + super-admin

| Method & path | Body | Response |
|---|---|---|
| `PUT /api/settings/branding` | `{ brandName, logoFileId? }` — `logoFileId`, if set, must reference a *published* file | `200`, `{ brandName, brandLogoUrl }`, or `400`/`403` |

Writing here evicts `ConfigController`'s cache key immediately, so the next `GET /api/config` reflects
it without waiting out the 30-second TTL:

```
$ curl -s -X PUT http://localhost:5221/api/settings/branding -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"brandName":"StruoCMS Docs Demo","logoFileId":null}'
{"success":true,"data":{"brandName":"StruoCMS Docs Demo","brandLogoUrl":null}}
$ curl -s "http://localhost:5221/api/config"
{"success":true,"data":{"oidcEnabled":false,"brandName":"StruoCMS Docs Demo","brandLogoUrl":null,"passwordMinLength":8}}
```

### Schema (`SchemaController`, `api/schema`) — requires Cookie or Bearer only

| Method & path | Response |
|---|---|
| `GET /api/schema` | `200`, array of every non-hidden-field `CollectionMetadata` |
| `GET /api/schema/{collection}` | `200`, one `CollectionMetadata`, or `404` |

On this host, seven collections come back: `language`, `permission`, `role`, `user`, `userRole`, `file`,
`mediaFolder`. `Hidden` fields never appear in either response (chapter 5).

### Auth (`AuthController`, `api/auth`)

| Method & path | Auth | Rate limit | Body | Response |
|---|---|---|---|---|
| `POST /api/auth/login` | anonymous | 10 failed/900s per account (`RateLimiting:LoginAccount`, on by default) + 5/60s per client IP (`RateLimiting:Login`, off by default) — chapter 3 | `{ email, password }` | `200`, `{ id }`, sets the session cookie; or `401` |
| `POST /api/auth/logout` | Cookie or Bearer | — | — | `204`, clears the session cookie |
| `GET /api/auth/me` | Cookie or Bearer | — | — | `200`, `{ id, email, name, isSuperAdmin, permissions }` (`permissions` is `{}` for a super-admin — every grant is implied) |
| `GET /api/auth/login/oidc` | anonymous | — | `?returnUrl=` | `302` challenge to the configured OIDC provider, or `404` when `Oidc:Enabled` is `false` |

```
$ curl -s -c cookies.txt -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"admin@admin.com","password":"admin"}'
{"success":true,"data":{"id":"019fa8b2-4d09-7155-b641-2c3e2519233b"}}

$ curl -s -b cookies.txt "http://localhost:5221/api/auth/me"
{"success":true,"data":{"id":"019fa8b2-4d09-7155-b641-2c3e2519233b","email":"admin@admin.com","name":"Administrator","isSuperAdmin":true,"permissions":{}}}

$ curl -s -i -X POST -H "X-Struo-CSRF: 1" -b cookies.txt "http://localhost:5221/api/auth/logout"
HTTP/1.1 204 No Content
$ curl -s -i -b cookies.txt "http://localhost:5221/api/auth/me"
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Authentication required."}}

$ curl -s -i "http://localhost:5221/api/auth/login/oidc"
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
```

### Config (`ConfigController`, `api/config`) — anonymous

| Method & path | Response |
|---|---|
| `GET /api/config` | `200`, `{ oidcEnabled, brandName, brandLogoUrl, passwordMinLength }` — cached 30s server-side; evicted immediately by a branding save |

```
$ curl -s "http://localhost:5221/api/config"
{"success":true,"data":{"oidcEnabled":false,"brandName":"StruoCMS Docs Demo","brandLogoUrl":null,"passwordMinLength":8}}
```

(`brandName` here is the saved override from the branding example above, not `Branding:Name`'s
appsettings.json default of `"StruoCMS"` — chapter 3 documents that default.)

### Ping (`PingController`, `api/ping`) — anonymous

| Method & path | Response |
|---|---|
| `GET /api/ping` | `200`, `{ status: "ok", service: "StruoCMS", utc }` |

```
$ curl -s "http://localhost:5221/api/ping"
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-07-29T07:23:58.4897657Z"}}
```

### Health (mapped in `Program.cs`, not a controller) — anonymous, unenveloped

`/health/live` and `/health/ready` are ASP.NET Core's own health-check middleware, mapped directly
rather than through a controller action — their responses are the health-check framework's own plain
text, **not** the REST envelope above:

| Path | Checks | Response |
|---|---|---|
| `GET /health/live` | none (`Predicate = _ => false` — always reports healthy, a pure liveness probe) | `200`, `Healthy` |
| `GET /health/ready` | `database` (`DbReadinessCheck`), `cache` (`CacheReadinessCheck`) — both tagged `"ready"` | `200 Healthy`, or a non-200 with the failing check's status when a dependency is down |

```
$ curl -s http://localhost:5221/health/ready
Healthy
```

## Next steps

- Chapter 8, [Query DSL](08-query-dsl.md), for the `filter`/`sort`/`limit`/`offset`/`fields`/`deep`/
  `search`/`locale`/`deleted` surface `ItemsController`'s list/get/query actions share with GraphQL.
- Chapter 7, [Relations](07-relations.md), for `OnDelete.Restrict` and the cross-relation dotted-path
  rules a delete/filter can hit.
- Chapter 10, [GraphQL API](10-graphql-api.md), for the same collections and permission model exposed
  as a typed schema, including the `X-Struo-CSRF` requirement on every cookie-authenticated
  `POST /graphql`.
- Chapter 12, [Authentication, SSO & RBAC](12-auth-and-rbac.md), for the full RBAC/`AdminOnly`/OIDC
  model behind the permission column in every table above.
