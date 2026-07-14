# API Response Envelope

Every StruoCMS **REST** endpoint under `/api/*` returns its JSON in one consistent envelope, so a
client can handle success and failure the same way everywhere. (GraphQL at `/graphql` keeps its own
spec-mandated `{ data, errors }` shape — this page is about the REST API.)

## Success

```json
{ "success": true, "data": <payload> }
```

`data` is always present on success. It may be an object, an array, a scalar, or `null`.

**List / paginated** endpoints additionally carry `meta`:

```json
{
  "success": true,
  "data": [ { "id": "…", "…": "…" } ],
  "meta": { "total": 42, "limit": 25, "offset": 0 }
}
```

`meta` is **offset-based** (`total` / `limit` / `offset`) and appears **only** on list responses.

```bash
curl -s /api/items/article?limit=2
# { "success": true, "data": [ … ], "meta": { "total": 42, "limit": 2, "offset": 0 } }
```

## Error

```json
{ "success": false, "error": { "code": "BAD_USER_INPUT", "message": "…" } }
```

`error.code` is a stable, machine-readable string; `error.message` is a human-readable, client-safe
string. A success response never has an `error` key, and an error response never has a `data` key — so
a client can branch on `success` alone.

**Validation** errors additionally carry `details` (present only when `code` is `VALIDATION`):

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION",
    "message": "One or more validation errors occurred.",
    "details": [ { "field": "email", "message": "Email is required." } ]
  }
}
```

### Error codes

| `error.code` | HTTP | When |
|---|---|---|
| `UNAUTHORIZED` | 401 | The operation needs authentication and the caller is anonymous. |
| `FORBIDDEN` | 403 | The authenticated caller lacks permission for the operation. |
| `NOT_FOUND` | 404 | Unknown collection, or the requested resource does not exist. |
| `CONFLICT` | 409 | A relation constraint or an optimistic-concurrency (`version`) conflict. |
| `BAD_USER_INPUT` | 400 | Invalid query, filter, or request the engine rejected. |
| `VALIDATION` | 400 | Model-binding / request-shape validation failed (carries `details`). |
| `INTERNAL_SERVER_ERROR` | 500 | Unexpected server error. The message is masked; details are logged server-side. |

The REST codes mirror the GraphQL error `code` extensions (`StruoErrorFilter`); REST additionally
distinguishes `UNAUTHORIZED` (401) from `FORBIDDEN` (403) and `VALIDATION` from `BAD_USER_INPUT`.

## Responses that are *not* enveloped

- **`204 No Content`** — a success with no body (e.g. logout, delete). It stays a bare `204`.
- **Binary file streams** — `GET /api/files/{id}/content` returns the file bytes directly.
- **`302` redirects** — the same endpoint returns a presigned-URL redirect when the storage backend is
  S3/MinIO.

## How it works (for contributors)

Enveloping is centralized in `Struo.Api/Http/`, so controllers stay simple and can't drift:

- **`EnvelopeResultFilter`** (an `IAlwaysRunResultFilter`) wraps a controller's raw success return in the
  success envelope, expands a `PagedResult` into `data` + `meta`, and turns a bare `404` into a
  `NOT_FOUND` error envelope. It leaves `204`, file streams, and redirects untouched.
- **`StruoExceptionHandler`** (an `IExceptionHandler`) maps domain exceptions to the error envelope and
  status code (401-vs-403 from the caller's auth state), masking and logging anything unexpected.
- **`InvalidModelStateResponseFactory`** turns model-binding failures into a `VALIDATION` error with
  `details`.
- **`ApiResults.Fail(status, code, message)`** builds an error envelope for the few hand-written error
  returns (e.g. invalid login credentials).

A controller therefore just returns raw data (`Ok(item)`), a `PagedResult` for lists, `Created(url,
created)`, `NotFound()` / `NoContent()`, or `ApiResults.Fail(...)` — the envelope is applied for it.
