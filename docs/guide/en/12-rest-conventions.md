# 12. REST API Conventions

Every REST endpoint shares the same response envelope, status codes, error codes, authentication
rules, and write semantics — that shared surface is this chapter's subject. The next chapter lists
the endpoints themselves.

## The response envelope

Every JSON response, success or failure, is wrapped in the same shape. On success:

```json
{ "success": true, "data": … }
```

On failure:

```json
{ "success": false, "error": { "code": "…", "message": "…", "details": … } }
```

`details` appears only when the error code is `VALIDATION`; every other code omits the key
entirely. `meta` is omitted the same way: it appears only on a paginated list result. A single-item
read, a create, and an update all omit the key outright rather than sending `null`. When `meta` is
present, it can also carry two more keys, `facets` and `aggregate`, each showing up only when the
request actually asked for it; the two are independent of each other.

Three different sources all end up in the same envelope: an ordinary controller response, an
exception the framework catches on its own, and the handful of requests that end early during
validation or authorization — a missing CSRF header, for example. Whichever one produced the
response, the shape is identical, and the caller never has to tell them apart.

A controller response whose content is already shaped like the envelope passes through untouched.
A paginated result is wrapped into a success envelope carrying `meta`. A `201` create is re-wrapped
so its body sits in the envelope, and the `Location` header still goes out unchanged. A `204`
always passes through untouched too, with no body at all.

Every other error response gets a default message keyed off its status code — `400` is
`Bad request.`, `401` is `Authentication required.`, `403` is `Forbidden.`, `404` is
`Resource not found.`, `409` is `Conflict.`, and anything else falls back to `An error occurred.`.
When the controller attaches its own message text, that text replaces the default.

A `201` create, with the complete set of response headers:

```text
$ POST /api/items/tag
body:
{"name":"launch"}
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Fri, 11 Sep 2026 08:25:38 GMT
Server: Kestrel
Location: /api/items/tag/01a08f92-8470-718c-888b-1714ffaef71e
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":true,"data":{"id":"01a08f92-8470-718c-888b-1714ffaef71e","version":0,"name":"launch","createdAt":"2026-09-11T08:25:39.185162Z","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:39.1853366Z","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"}}
HTTP_STATUS:201
```

The ids in the example belong to this batch of test data; your own environment will differ.
Notice that `Location` points at the row just created, and that `id`, `version`, and `createdAt`
are all assigned by the server — the caller sent none of them.

An exception no handler catches is logged server-side, and the caller always gets back the same
masked message, `An internal error occurred.` — never the exception's own content. A failure in
authentication itself goes through the same masking and the same envelope.

GraphQL is the one exception — see [Chapter 14: GraphQL API](14-graphql.md) for its own spelling:
a `/graphql` response is native GraphQL shape (`data`/`errors`), not this envelope at all, unless
the request is stopped before it ever reaches GraphQL — a missing CSRF header, for instance — in
which case it still comes back in this chapter's envelope.

## Status codes

Most reads and writes return `200`, with the body carrying the result. `201` only appears on
three endpoints — creating an item, creating a user, and uploading a file — all three carrying a
relative `Location` pointing at the standard read route for the row just created. `204` shows up
in six places — deleting an item, deleting a file, restoring a file, logging out, changing a
password, and revoking an access token — always with no body.

Restoring an item from the trash is the one exception: `POST /api/items/{collection}/{id}/restore`
returns `200` with the restored item as its body, rather than the `204` every other delete/restore
pair uses; restoring a file really is `204` — the two interfaces don't agree here. Every other
status code maps one for one to the error codes below.

## Error codes

Here is the complete set of error codes. REST and GraphQL share the same list — the same string
means the same thing on both protocols:

| Code | HTTP | When |
|---|---|---|
| `UNAUTHORIZED` | 401 | No or invalid credentials; a denial while unauthenticated |
| `FORBIDDEN` | 403 | Authenticated but not permitted; a missing CSRF header |
| `NOT_FOUND` | 404 | An unknown collection, a missing file, or another plain 404 |
| `CONFLICT` | 409 | Relation-blocked delete, duplicate email, or a plain 409 |
| `VERSION_CONFLICT` | 409 | An optimistic-concurrency `version` mismatch |
| `BAD_USER_INPUT` | 400 | A malformed query parameter or write body |
| `VALIDATION` | 400 | Model-binding or data-annotation failure |
| `INTERNAL_SERVER_ERROR` | 500 | An unclassified exception, message masked |
| `TOO_MANY_REQUESTS` | 429 | Rate limiting triggered by login or password change |
| `PAYLOAD_TOO_LARGE` | 413 | Upload bytes over the configured cap |
| `INVALID_CURRENT_PASSWORD` | 400 | Wrong current password on self-service change |
| `NO_LOCAL_PASSWORD` | 400 | External OIDC account, no local password |
| `ACCOUNT_INACTIVE` | 401 | Correct password, deactivated account |
| `SESSION_REVOCATION_FAILED` | 500 | Revocation failure after password change or user delete |
| `SEARCH_UNAVAILABLE` | 503 | The registered search provider's own failure to respond |

- `TOO_MANY_REQUESTS` is written directly at three sites rather than going through the error
  mapping above: the per-account login throttle, the per-caller-IP login throttle, and the
  password-change throttle.
- `SESSION_REVOCATION_FAILED` means the password change or user deletion itself already
  succeeded — only the follow-up step of revoking that user's other sessions failed, so some
  sessions may still be alive. It has no status code of its own, so it falls to 500, but the code
  and message stay specific to it.
- `SEARCH_UNAVAILABLE`'s message is always `Search is temporarily unavailable.`; whatever reason
  the provider itself reports only goes into the server-side log.

`PAYLOAD_TOO_LARGE` and `SEARCH_UNAVAILABLE` are both hard to trigger on an ordinary host — the
first needs an upload that genuinely exceeds the cap, and the second needs a search provider that
actively fails; `INTERNAL_SERVER_ERROR` only shows up for a truly unhandled error.

## Validation error `details`

`details` only appears with the `VALIDATION` code — it's what model binding or data-annotation
validation produces, and it happens before the action itself ever runs. Each array entry
corresponds to one validation failure, not one field: a field that violates two checks at once
produces two entries sharing the same `field`. When ASP.NET Core gives no message of its own, it
falls back to the fixed `Invalid value.`.

```text
$ POST /api/users
body:
{"password":"whatever123"}
{
  "success": false,
  "error": {
    "code": "VALIDATION",
    "message": "One or more validation errors occurred.",
    "details": [
      {
        "field": "Email",
        "message": "The Email field is required."
      }
    ]
  }
}
HTTP_STATUS:400
```

The required `email` field is missing, and that is caught before the action ever runs; `details`
carries exactly one entry, naming `Email`.

An error the application layer works out for itself is a different matter — it's always
`BAD_USER_INPUT`, with no `details` key at all; the message itself carries the whole story:

```text
$ POST /api/users
body:
{"email":"nobody@example.com","password":"short"}
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Password must be at least 8 characters."}}
HTTP_STATUS:400
```

Same endpoint — this time `email` is present, and a too-short password is an application-layer
rule, so it comes back as `BAD_USER_INPUT` instead of `VALIDATION`.

An update can leave a required field out entirely and keep its existing value; but explicitly
sending `null` or blank still gets rejected — the merged item as a whole still has to pass the
required check. A required list or map field follows the same logic: omit it and the stored value
stands; send it — even as an empty array or empty object — and it must pass full validation.
Create has no such concern, since there's no existing row to merge into.

A translatable field's required check runs on a separate path, per locale against the translation
content, and isn't affected by this rule. On update, field-level checks such as a length cap run
before this merged-required check does; when both are violated, whichever one runs first is the one
reported — which can differ from create, where the required check runs first.

An error that can name its field reads like `Field 'weight' has an invalid value.`; one that fails
during parsing itself, with no field to point at, falls back to the generic
`Request body could not be parsed.`. A top-level body that isn't a JSON object at all is rejected
outright too, with `Request body must be a JSON object.`.

## Authentication: cookie or bearer

A caller can authenticate with the session cookie the login endpoint sets, or with a bearer token
issued separately for some user, carried in the `Authorization: Bearer <token>` header. The rule
for choosing is simple: whenever `Authorization` starts with `Bearer `, the request is a bearer
identity; only when it's absent does the cookie count — the decision looks only at the header,
never falling back to try the cookie afterward.

But this rule only holds on endpoints that don't require sign-in — reads, `/graphql`,
`GET /api/files/{id}`, and `/content`. On these, a caller sending both credentials at once still
gets only the bearer one honored; an invalid or malformed token drops straight to anonymous rather
than falling back to the cookie identity. This is deliberate — better to let the request fall to
anonymous and be denied than to quietly swap in a different identity that happens to pass.

A write endpoint that requires sign-in accepts either one — cookie or bearer — and both get
validated and folded into the caller's identity. Most read endpoints require neither credential at
all, since a read only checks the collection's own read grant. The full detail on SSO and password
rules is left to the authentication chapter.

Issue a token for the current account, read once with it, then revoke it:

```text
$ POST /api/users/01a08f90-893a-7cdb-bb01-a1b46c5ed248/access-token
{"success":true,"data":{"token":"LbUeNnhuvBYeEYt9uFU1izO6eyQDmUAL57hIxchHMvw"}}
HTTP_STATUS:200

$ GET /api/items/article?limit=1  Authorization: Bearer <token>
{"success":true,"data":[{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":2,"limit":1,"offset":0}}
HTTP_STATUS:200

$ DELETE /api/users/01a08f90-893a-7cdb-bb01-a1b46c5ed248/access-token

HTTP_STATUS:204

$ GET /api/items/article?limit=1  Authorization: Bearer <revoked token>
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Read not permitted."}}
HTTP_STATUS:401
```

The token is shown exactly once, at the moment it's issued — only its hash is stored afterward.
Reading with it produces the exact same `data` shape as reading with a cookie. Once revoked, that
same token stops working immediately, not only from the next request onward.

## CSRF: `X-Struo-CSRF`

When a call authenticates with the session cookie, every unsafe method — anything other than
`GET`/`HEAD`/`OPTIONS`/`TRACE` — must carry an `X-Struo-CSRF` header. The server only checks
whether the header is present; it never looks at the value itself, so any string at all counts.

That's enough to defend against CSRF on its own, resting on the browser's own rules rather than the
header's value: a cross-site page has no way to attach a custom header to a credentialed request
unless the target site's CORS policy has already allowed that origin, and StruoCMS's CORS is closed
by default, recognizing only allowlisted origins. A fork that relaxes CORS to unknown origins
weakens this defense along with it.

This check only activates when the request carries the session cookie: a call carrying a bearer
header is exempt, and so is a request that carries no session cookie at all — login itself, for
example. The bearer exemption is checked first, so a request carrying both is still exempt.

This check covers every unsafe-method path, not just `/api/*` — `POST /graphql` is an unsafe
method too, so a GraphQL request authenticated with a cookie needs this header whether it's a
query or a mutation. Without it, the response is:

```text
$ POST /api/items/article/query (no CSRF header)
body:
{}
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}
HTTP_STATUS:403
```

Even though `POST .../query` is itself only a read, it still needs this header — the CSRF check
looks only at the method, not at whether this particular call actually writes anything.

## Optimistic concurrency: `version`

Only a collection that derives from the auditable base carries a `version` integer on its rows — a
collection that only implements the most basic auditing interface has no such column; see
[Chapter 11](11-query-advanced.md) for the detail.

A collection with `version` returns it on every read and list response, and an update body can send
that value back for the server to compare against the row's current value; the write lands only when
the two match, and is rejected whole when they don't:

```text
$ PUT /api/items/article/01a08f92-3a18-7c5d-99a3-5b14cd1279ea
body:
{"status":"published","version":0}
{"success":false,"error":{"code":"VERSION_CONFLICT","message":"The record was modified by someone else since you loaded it. Reload and try again."}}
HTTP_STATUS:409
```

The caller's `version` has already fallen behind the row's real current value, so the write is
rejected; this message is exactly what a client's "reload and try again" path is built on. Leaving
`version` out of the body entirely is also allowed — doing so skips the comparison altogether,
giving up this layer of protection while staying compatible with an older caller that doesn't
send it.

A revert strips `version` from the snapshot before applying it, so a stale version number sitting
in that snapshot never causes a needless conflict (see
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md)). `version` itself can never be
set directly through a create or update body either — it's only ever used as the concurrency
comparison value, never written into that column.

## What a `POST`/`PUT` body can set

Create and update bind the body only against the collection's own declared writable fields and
its many-to-one foreign keys; every other key is silently dropped, with no error reported — that
covers `id`, `version`, and any system-managed field. Neither create nor update lets the caller
pick the item's `id`.

Read-only and system fields are also cleared back to empty after binding, so even a body that
deliberately reuses one of those key names to smuggle in a value can't reach that field.

`translations` and a many-to-many relation's key sit outside this binding step but still take
effect — the server reads them separately from the raw body and applies them inside the same
transaction; see "The many-to-many write shape" below for the detail.

A `Json`-typed field takes its value directly from the body's raw JSON text and accepts any JSON
shape. A non-translatable rich-text field is sanitized before validation ever runs; the length cap
measures the sanitized HTML, and the sanitized HTML is what gets stored too.

An update only overwrites keys that genuinely appear in the body — the collection's own fields and
its many-to-one foreign keys alike — and that presence check is case-insensitive. A partial `PUT`
therefore never accidentally clears a server-managed field the caller never sent. A few common body
mistakes and the messages they actually produce:

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'publishedAt' has an invalid value."}}
HTTP_STATUS:400
```

Sending `{"publishedAt":""}` produces the field-level error above; sending a top-level body that
isn't an object at all (`[]`, say) produces the earlier `Request body must be a JSON object.`;
sending `{"name":null}` against a required field produces `Field 'name' is required.`.

The `file` collection has to go through its own dedicated upload endpoint — sending
`POST /api/items/file` against it is always refused, whatever the body says:

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Files cannot be created through the generic items API. Upload one with POST /api/files instead."}}
HTTP_STATUS:400
```

Upload detail is left to the files chapter; reading, updating, and deleting the `file` collection
all follow exactly the same general rules as any other collection here.

### The many-to-many write shape

A bare id or a payload-carrying object, the dedup rule for a mixed array, an object outranking a
bare id, and the junction's own write grant are all covered in
[Chapter 8: Relations](08-relations.md); this section only looks at what a many-to-many value
looks like written into a `PUT` body.

Here is an actual write and response, with one bare id and one payload-carrying object in the
`tags` array:

```text
$ PUT /api/items/article/01a08f92-3a18-7c5d-99a3-5b14cd1279ea
body:
{"tags":["01a08f92-38f5-7f1b-a478-f8a1140f0b0b",{"id":"01a08f92-396e-7bf7-a77c-149c9aa732b1","note":"editor pick"}]}
{"success":true,"data":{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","version":2,"status":"draft", …}}
HTTP_STATUS:200
```

The rest of `data`'s fields are elided, in the same shape as any other update response in this
chapter. The first target is only linked; the second target's `note` is replaced with
`editor pick`.

When a payload field fails validation, the message names the relation and the target, so a form
editing several links at once can pin down exactly where the problem is:

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation 'tags', target '01a08f92-396e-7bf7-a77c-149c9aa732b1': Field 'note' exceeds maximum length 200."}}
HTTP_STATUS:400
```

The message names both the relation and that target's id, so the caller knows exactly which row
to flag. A key inside the object that isn't declared as this relation's payload, along with the
junction's own foreign keys and structural fields, is always ignored.

An object element sent to a relation that has no declared payload, or an invalid shape in the
array — a boolean, an array, `null`, a non-integer — always gets the same general-purpose message:

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"One or more ids in 'tags' are not valid."}}
HTTP_STATUS:400
```

The message doesn't distinguish which element is at fault — it only says this relation's ids have
a problem. An object element sent to a relation that does declare payload, but with no `id` key,
gets a different message instead: `Each object in '<relation>' must carry an 'id'.` That check,
too, runs before any payload value is bound.

Every target id must genuinely exist; one that doesn't produces
`One or more ids in '<relation>' do not exist in '<target>'.`. A revert is the one exception — it
accepts even a target already sitting in the trash, because the target may not yet have been
trashed at the moment the snapshot was captured, a point
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md) already makes.

## What's next

With the rules every endpoint shares now covered, the next step is listing exactly which endpoints
each controller actually has — see
[Chapter 13: REST API Endpoint Reference](13-rest-endpoints.md).
