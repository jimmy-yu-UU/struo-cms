# 13. REST API Endpoint Reference

This chapter gives one table per controller, listing every endpoint's method, path and required
permission; the shared response envelope, status codes, authentication and CSRF rules are left to
[Chapter 12: REST API Conventions](12-rest-conventions.md).

## How to read this chapter

In every table below, the Permission column uses only these phrasings:

- **Anonymous** — no identity check at all, and no collection-level grant check either.
- **Signed in** — only requires passing authentication with a cookie or bearer token; no specific
  collection grant is checked.
- **Read / Write / Delete** — checks the caller's matching RBAC grant on that collection; writing,
  deleting and restoring additionally always require signing in first.
- **Super-admin** — requires signing in first, plus the caller being a super-admin, regardless of
  what any delegated collection grant says.
- **Super-admin or the user themself** — the caller is a super-admin, or is the very user named in
  the path; being that user needs no admin identity, but does require proving the current password.

Before any of these checks run, the route's own type constraints reject a malformed request first:
the item endpoint's `{id}` accepts any type, every other endpoint's id segment is constrained to a
GUID, and a revision number is constrained to an integer. When the type doesn't match, the route
never matches at all: the response is a plain 404 with an empty body, not this chapter's error
envelope. Collection names are case-insensitive.

Items and files read `purge` differently: the item endpoint only recognizes `?purge=true`
(case-insensitive), and treats every other spelling as false; the file endpoint binds `purge` as a
real boolean, so a misspelling is a `VALIDATION` 400 outright.

## Items `api/items/{collection}`

The complete semantics of filtering, sorting, projection and pagination are left to
[Chapter 10: Querying: Filters, Sorting and Pagination](10-query-basics.md) and
[Chapter 11: Querying: Projection, Deep Expansion, Facets and Aggregates](11-query-advanced.md).

| Method and path | Permission | Purpose |
|---|---|---|
| `GET api/items/{collection}` | Read | List items |
| `POST api/items/{collection}/query` | Read | Same query, as a JSON body |
| `GET api/items/{collection}/{id}` | Read | Read a single item |
| `POST api/items/{collection}` | Write | Create a new item |
| `PUT api/items/{collection}/{id}` | Write | Update an item |
| `DELETE api/items/{collection}/{id}` | Delete | Trash or purge, per `?purge=` |
| `POST api/items/{collection}/{id}/restore` | Delete | Restore from trash, `200` with the item |
| `GET api/items/{collection}/{id}/revisions` | Read | List revisions |
| `GET api/items/{collection}/{id}/revisions/{n}` | Read | Read one revision snapshot |
| `POST api/items/{collection}/{id}/revisions/{n}/revert` | Write | Revert to a given version |

On a collection that carries a `version`, an update body's `version` must be an integer, an integer
string, `null`, or absent; any other value is a `400` `INVALID_VERSION` ([Chapter 12](12-rest-conventions.md)).

Requesting a non-default `deleted=` mode needs the collection's delete grant, not just its read
grant — for a single-item read as much as for a list; an unknown collection is always a 404. Writing
to or deleting from any junction a fork has marked `AdminOnly` additionally requires the caller to be a
super-admin — a delegated collection grant doesn't count. The built-in `user`, `role`, `permission` and
`userRole` are ordinary collections: the two hidden junctions follow the grant of `user` and `role`, and a
write that touches a super-admin account or role is refused to a caller who is not a super-admin
([Chapter 17](17-roles-and-permissions.md)).
A JSON body that names the same top-level property twice is a `400` `BAD_USER_INPUT` on create and update;
names that differ only in casing are different properties.

Two collections cannot be created through `POST api/items/{collection}` at all: `file` and `user`.
Both return `400` `BAD_USER_INPUT` with a generic message that names the collection, and GraphQL's
`createFile` and `createUser` mutations get the same error. The refusal comes from the collection's
`Creatable = false` flag ([Chapter 5](05-collections.md#cmscollection-options)). A file is created by uploading it to `POST api/files`, and a user by `POST api/users`,
which sets the password. The refusal comes before any check of the body, so a duplicate email is a `400` there, not a
`409`. Reading, updating and deleting both collections still go through the generic routes.

The complete rules for revisions and the trash — `DELETE`'s idempotent behavior, the details of
`?purge=`, and a revision snapshot's field shape — are left to
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md); for a collection with revisions
off, listing revisions returns an empty array, not a 404; an unknown item id, or a trashed item the
caller lacks delete permission for, is a 404.

The example below uses the Blog sample's `article` collection (revisions on) to show restore
returning `200` rather than `204`; the ids that appear are specific to this batch of test data, and
your own environment will differ:

```text
$ DELETE /api/items/article/01a08f92-4137-72e2-afeb-a0451167539c

HTTP_STATUS:204

$ POST /api/items/article/01a08f92-4137-72e2-afeb-a0451167539c/restore
{"success":true,"data":{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":2,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394Z","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508Z","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"}}
HTTP_STATUS:200
```

Delete returns `204` with no body; restore returns `200`, with `data` being the restored item
itself.

## Files `api/files`

| Method and path | Permission | Purpose |
|---|---|---|
| `POST api/files` | Write | Upload a file, returns `201` |
| `GET api/files/{id}` | Anonymous | Read file metadata |
| `GET api/files/{id}/content` | Anonymous | Read content, with optional transform parameters |
| `DELETE api/files/{id}` | Delete | Delete, trashed by default |
| `POST api/files/{id}/restore` | Delete | Restore from trash, returns `204` |

Neither read action checks any grant at all: a published file is readable by anyone, and an
unpublished file is readable only by a caller who is signed in and holds a `file` write grant;
everyone else gets a `404`, not a `403` — even a fork that revokes `file`'s public read grant
still leaves a published file readable by everyone.

Upload returns `201`, with `Location` pointing at the file just created, and a body of
`{ id, fileName, contentType, size, width, height, status, folderId }`; a malformed multipart shape
is rejected outright — a missing part named `file` produces `Missing 'file' part.`, and a
`folderId` that isn't a valid GUID produces `Invalid 'folderId'.`, both as `BAD_USER_INPUT`.

`GET api/files/{id}/content` streams the raw bytes directly by default, and returns `302` instead
when configured for presigned storage; adding the transform parameters `width`, `height`, `format`,
`fit` and `quality` usually returns the transformed bytes, but a transform failure is caught, and
the endpoint returns the original file rather than failing the whole request — the complete
transform rules are left to the files chapter.

Delete and restore both need a `file` delete grant, and both return `204` or `404`.

## Users `api/users`

| Method and path | Permission | Purpose |
|---|---|---|
| `POST api/users` | Write on `user` | Create a user, returns `201` |
| `PUT api/users/{id}/password` | Write on `user`, or the user themself | Change password; self-service proves the current one |
| `POST api/users/{id}/access-token` | Write on `user` | Issue an access token, shown only once |
| `GET api/users/{id}/access-token` | Read on `user` | Read the token's issue and last-use times |
| `DELETE api/users/{id}/access-token` | Write on `user` | Revoke the access token |
| `GET api/users/{id}/effective-permissions` | Read on `user` | Preview effective permissions, supports `?roles=` |

Creating a user returns `201`, with `Location` pointing at `api/items/user/{id}` — that's the
actual read route for the user row; the body is `{ id, email, name }`. A blank email, a password
the password rules reject, or a duplicate email (`409 CONFLICT`) are all rejected. A caller without the
grant gets `403` `Write permission on the user collection is required.` (`Read permission …` for the two
`GET`s). Resetting the password of a super-admin account, and issuing or revoking its token, are
refused to a caller who is not a super-admin: `403` `Only a super-admin can …`.

Changing password is the only rate-limited endpoint besides login, partitioned by the caller's own
user id; success revokes that user's other sessions and their access token, and a revocation
failure comes back as `SESSION_REVOCATION_FAILED` rather than being swallowed silently, because the
password change itself already succeeded. An issued token is shown only this once; only its hash is stored
afterward. Revoking returns `204` or `404`.

The `?roles=` parameter on `effective-permissions` can carry a list of role ids, to preview a role
combination that hasn't been saved yet; passing it with an empty string previews the public role's
permission floor, which differs from omitting the parameter entirely. A malformed or nonexistent
role id is always rejected, never silently dropped from the count.

Listing, updating and deleting users all go through the generic item API's `user` collection — the
create response's `Location` points exactly there. Creating a user there is refused with `400`:
the error message is `Items cannot be created in collection 'user' through the generic items API.`

## Roles `api/roles`

| Method and path | Permission | Purpose |
|---|---|---|
| `GET api/roles/{id}/permissions` | Read on `role` | Read the role's current grant rows |
| `PUT api/roles/{id}/permissions` | Write on `role` | Overwrite the whole set of grants |

`GET` returns a `{ collection, canRead, canWrite, canDelete }` array ordered by collection name, and
a nonexistent role is a `404`. `PUT` overwrites the whole set rather than merging row by row — a row
where all three flags are false is never stored at all, and the response only shows the rows that
genuinely remain; the request is rejected if the body isn't a JSON array, names a collection twice,
or names a collection that doesn't exist, and collection-name casing is normalized.

A caller without the grant gets `403` `Read permission on the role collection is required.` or `Write
permission on the role collection is required.`. Writing the grants of a super-admin role is refused to a
caller who is not a super-admin: `403` `Only a super-admin can change the grants of a super-admin role.`
A role is itself an ordinary collection, so `POST api/items/role` creates a new one. The `isSuperAdmin`
flag cannot be set through it; a body that carries it is accepted and the field is ignored.

## Login locks `api/login-locks`

| Method and path | Permission | Purpose |
|---|---|---|
| `POST api/login-locks/query` | Read on `user` | Read whether up to 100 accounts' logins are locked |
| `DELETE api/login-locks?email=…` | Write on `user` | Clear the lock, returns `204` |

The `POST` takes `{ "emails": [...] }`, one to 100 addresses, each non-blank and at most 255 characters;
anything else returns `400` `BAD_USER_INPUT`, and a caller without read on `user` gets `403`. It answers
one entry per address, in the order sent, so an address sent twice is answered twice:

```text
$ POST /api/login-locks/query
body:
{"emails":["admin@admin.com","nobody@example.com"]}
{"success":true,"data":[{"email":"admin@admin.com","locked":false,"retryAfterSeconds":0,"clearable":true},{"email":"nobody@example.com","locked":false,"retryAfterSeconds":0,"clearable":true}]}
```

`locked` is `true` when the account has used up its per-account login attempts for the current window,
and `retryAfterSeconds` is the time left in that window, `0` when not locked. `clearable` is `false` for a
super-admin account when the caller is not a super-admin, and `true` otherwise. The email is trimmed and
lowercased the way login does it, and the entry echoes it as sent. Reading never counts as an attempt. The
accounts are looked up in one query for the whole list, and the throttle is asked once per distinct address.
The `email` travels in the body, not in the URL, so it stays out of access logs.

The `DELETE` needs write on `user`; without it the answer is `403`, and a missing, blank or over-long
(more than 255 characters) `email` returns `400`. The answer does not depend on whether an account with
that email exists: an address with no account is answered in the same shape as a known one, and `DELETE`
returns `204` for it too. The one exception is a `DELETE` for a super-admin account, active or not, by a
caller who is not a super-admin: it returns `403` with `Only a super-admin can clear the lock on a
super-admin account.` and leaves the lock. A cookie-signed-in `DELETE` needs the CSRF header like any
other mutation. Each `DELETE` writes one `Information` log line with the caller's user id and the target
user id, or a note that no such account exists; a refused one writes a `Warning` line naming the caller
and the target by id. The API never logs the email, but the `DELETE` carries it in the URL, so access
logs in front of the API, such as a reverse proxy's, record it. An operator who must keep emails out of
those logs filters the `login-locks` path. There is no single-address `GET`. See [Chapter
16](16-authentication.md) and [Chapter 17](17-roles-and-permissions.md).

## Languages `api/languages`

| Method and path | Permission | Purpose |
|---|---|---|
| `GET api/languages` | Signed in | List enabled languages |

Returns a `{ code, name, isDefault }` array.

## Settings `api/settings`

| Method and path | Permission | Purpose |
|---|---|---|
| `PUT api/settings/branding` | Super-admin | Update the brand name and logo |

The body is `{ brandName, logoFileId? }`, and the response is `{ brandName, brandLogoUrl }`. A blank
or over-100-character brand name, or a logo file that's missing or not yet published, is rejected.
Saving immediately clears the settings cache, so the very next `GET api/config` sees the new value
without waiting out the 30-second TTL. This is the only `PUT` here, with no matching `GET` — to read
it back, see `GET api/config` in the Config section below.

## Schema `api/schema`

| Method and path | Permission | Purpose |
|---|---|---|
| `GET api/schema` | Signed in | List every collection's full schema |
| `GET api/schema/{collection}` | Signed in | Read one collection's schema, 404 if unknown |

A `Hidden` field never appears in either response. Each collection carries a `creatable` flag: `false`
when the collection's `[CmsCollection]` sets `Creatable = false`, which is what makes the admin UI hide
the New button.

## Auth `api/auth`

| Method and path | Permission | Purpose |
|---|---|---|
| `POST api/auth/login` | Anonymous | Log in, sets the session cookie |
| `POST api/auth/logout` | Signed in | Log out, returns `204` |
| `GET api/auth/me` | Signed in | Read the caller's identity and permissions |
| `GET api/auth/login/oidc/{provider}` | Anonymous | Redirect to that OIDC provider |

The login body is `{ email, password }`; success returns `200` with `{ id }` and sets the session
cookie, and failure returns `401`. Two independent defenses each throttle calls made too often:
per-account throttling, on by default, checked before the password is actually verified; and
per-caller-IP throttling, off by default. Both share the same status code, error code and message —
only `Retry-After` differs, because the two windows aren't the same length.

An actual login, followed by a read of `me`:

```text
$ POST /api/auth/login
body:
{"email":"admin@admin.com","password":"<your-admin-password>"}
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Fri, 06 Nov 2026 06:17:45 GMT
Server: Kestrel
Cache-Control: no-cache,no-store
Expires: Thu, 01 Jan 1970 00:00:00 GMT
Pragma: no-cache
Set-Cookie: struo.session=…; path=/; samesite=lax; httponly
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":true,"data":{"id":"01a1af7f-4385-770a-a8f8-0fba654633ae"}}
HTTP_STATUS:200

$ GET /api/auth/me
{"success":true,"data":{"id":"01a1af7f-4385-770a-a8f8-0fba654633ae","email":"admin@admin.com","name":"Administrator","isSuperAdmin":true,"permissions":{}}}
HTTP_STATUS:200
```

`Set-Cookie`'s value is long and elided here; what matters is that the header itself appears,
carrying `path=/`, `samesite=lax`, `httponly`. This output comes from a Development host; where the
cookie is always `Secure`, the name is `__Host-struo.session` and `secure` is added. `me` returns an
empty `permissions` for a super-admin, because every grant is already implied by that identity.

`login/oidc/{provider}` takes a provider id from `Oidc:Providers`. It returns `404` for an unknown or
disabled provider, and a `302` redirect to that provider when it is enabled, with
`?returnUrl=` kept only when it is a site-relative path or an absolute URL on an origin listed in
`Struo:Cors:AllowedOrigins` (otherwise `Oidc:ReturnUrlDefault` is used);
reading `me` right after logging out is `401` immediately, with no need to wait for the cookie to
expire. The details of the OIDC callback are left to the authentication chapter.

## Config `api/config`

| Method and path | Permission | Purpose |
|---|---|---|
| `GET api/config` | Anonymous | Read public settings, cached for 30 seconds |

This endpoint allows anonymous calls. It returns
`{ oidcProviders, brandName, brandLogoUrl, passwordMinLength }`, and the response is cached
server-side for 30 seconds. `oidcProviders` is an array of `{ id, displayName, icon }`, one per enabled
OIDC provider in button order, and empty when none is enabled; `icon` is `null` when the provider
sets none. If a stored logo file is later unpublished or deleted, this still falls
back to the logo recorded in settings, rather than letting the anonymous login page see a dead URL.

## Ping `api/ping`

| Method and path | Permission | Purpose |
|---|---|---|
| `GET api/Ping` | Anonymous | Health probe, returns `{ status, service, utc }` |

The route template is `api/[controller]`, which spells out to `api/Ping`; matching itself is
case-insensitive, so `api/ping` hits it just the same.

## GraphQL, OpenAPI and health checks

The framework mounts a few more routes at startup that don't belong to any controller above. Two
of the rows below are gated a different way than by a permission, so the column carries two
phrasings the legend doesn't define:

| Route | Permission/gate | Purpose |
|---|---|---|
| `POST /graphql` | Anonymous for queries, sign-in for mutations | GraphQL query and mutation entry point |
| `GET /graphql?sdl` | Gated by `GraphQl:ExposeSchema` | Read the current SDL text, Development-only by default |
| `GET /health/live` | Anonymous | Plain-text liveness probe |
| `GET /health/ready` | Anonymous | Plain-text readiness probe |
| `GET /openapi/v1.json` and `/scalar` | Development only by default (`OpenApi:Enabled`) | OpenAPI document and the Scalar interface |

Both health probes return plain text (`Healthy`, for example), not this chapter's JSON envelope.
GraphQL's own documentation is in [Chapter 14: GraphQL API](14-graphql.md); the gating rules for the
two routes gated by `OpenApi:Enabled`, `/openapi/v1.json` and `/scalar`, are already covered in
[Chapter 3: Getting Started](03-getting-started.md) and
[Chapter 4: Configuration Reference](04-configuration.md) — this chapter only lists the routes.

## What's next

With the endpoints all listed, the next chapter changes the angle: how the same query semantics
are expressed in GraphQL — see [Chapter 14: GraphQL API](14-graphql.md).
