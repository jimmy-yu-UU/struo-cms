# 17. Roles and Permissions

Once a caller is authenticated, this chapter covers what it can do on a given collection, how the
system computes that answer, and where to change it. How a caller proves who it is is covered in
[Chapter 16: Authentication and SSO](16-authentication.md).

## The three tables

The RBAC data model rests on three collections: the users themselves, roles, and per-collection
grants.

| Collection | Table | Content |
|---|---|---|
| `user` | `users` | Email, password hash, active status, token hash, plus roles |
| `role` | `roles` | Role name, `isSuperAdmin` flag, description |
| `permission` | `permissions` | One row per role-collection grant |

On `user`, `password` and `accessToken` are both `Hidden` and `ReadOnly` at once. The password and
token hashes neither show up in an ordinary query result nor can be written by a caller directly.

A user's roles are a `TagSelect` many-to-many relation to `role` — on the admin form, this is just
checking role names. The junction between user and role is a fourth table, `userRole` (table
`user_roles`), which no caller ever sees.

A `permission` row names a role and a collection, and carries exactly three grant flags:
`canRead`, `canWrite`, `canDelete`. There's no fourth flag, and no separately stored "can read
unpublished content" flag. The role itself carries its own `isSuperAdmin`, which isn't a
per-collection grant at all — it's a short-circuit for the whole check chain.

## How effective permissions are computed

A single request can involve several roles at once, so the permission actually applied isn't a
lookup against one role — it's recomputed on every request.

`PermissionResolutionMiddleware` runs this resolution right after authentication. It first unions
the caller's held roles with the `public` role's grants. `public`'s grants are always added in,
whether the caller is anonymous, holds no role, or already holds several — not only as a floor
that applies when the role set happens to be empty.

Skipping that union would let a signed-in user end up with less access than an anonymous visitor:
if `public` opens read on some collection and none of the user's own roles repeat that grant,
signing in becomes a regression. This union query reads directly, bypassing the ordinary
projection and permission check, because the query that computes the permission threshold can't
itself be blocked by that same threshold.

The union is then handed to `PermissionResolver.Resolve` to fold down. On a given collection, if
any held role has `canRead`, `canWrite`, or `canDelete`, the folded result is true — this is an OR,
not an AND. Collection names are compared case-insensitively.

A role carrying `isSuperAdmin` short-circuits straight to all three flags true for every
collection, without consulting any grant row at all. A collection with no grant row is treated as
all three flags false — a safe default, not an oversight.

The folded result is cached for the rest of this request: the controller and every later
permission check reads that same value, never recomputing it. The pipeline order is
authentication, authorization, CSRF, permission resolution, rate limiting, controller, so
permission resolution always sees an identity that has already passed authentication.

## Read, write, delete

What each of the three flags actually guards lines up with the permission column on each row of
[Chapter 13: REST API Endpoint Reference](13-rest-endpoints.md):

- `canRead` guards every kind of read on the collection: listing, a single `GET`, listing and
  reading a single revision, GraphQL reads, and the facets and aggregates returned with a list.
- `canWrite` guards create, update, and revision restore on the generic path, and also guards file
  upload.
- `canDelete` guards delete (trash or purge) and restore on the generic path, and also guards
  file delete and restore.

Requesting `deleted=` in a non-default mode needs the delete grant, not the read grant. Why this
check is deliberately stricter, and the full set of accepted spellings and messages, are in
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md).

There's no separate "read unpublished content" flag. Files are the one exception: an unpublished
file is only readable when both conditions hold — the caller is signed in, and it holds a write
grant on `file`. Every other case is `404`; the details are in
[Chapter 15: Files, Media and Image Transforms](15-files-and-media.md).

## `AdminOnly` collections

Among the framework's built-in collections, the ones marked `AdminOnly` are `user`, `role`,
`permission`, and `userRole`. What the attribute itself declares, and how to set it on a
collection of your own, are in [Chapter 5: Defining Collections](05-collections.md).

`AdminOnly` governs writes only: create, update, delete, restore, and revision restore on the
generic CRUD path all require the caller to be a super-admin, whatever the collection's own grants
say. Even a role granted `canWrite` is blocked here unless the caller is a super-admin. Reads are
unaffected and follow the ordinary `canRead` check.

Every write point runs the ordinary collection permission check first, with the super-admin check
right after it. Create, update, and revision restore check the write grant and fail with
`Write not permitted.`; delete and restore check the delete grant and fail with
`Delete not permitted.`.

A caller with no grant at all on the collection sees one of those two ordinary messages. Only a
caller that already holds the relevant grant, but isn't a super-admin, sees the dedicated
`Writes to '{collection}' require a super-admin.` — so that message appears only for someone who
would otherwise be allowed to write.

## Public read

The first time the `roles` table is created, the `admin` and `public` roles are created along with
it, and the first administrator is added to `admin`. `public`'s grants are every caller's floor —
how they're unioned in is covered above under "How effective permissions are computed". What
`public` is granted to read is decided by the `Rbac:PublicReadCollections` configuration key.

Each collection name in that list becomes one `canRead = true` grant row on `public`. This key
only applies the first time the `roles` table is created; changing it and restarting has no
retroactive effect on an existing database. The key name and this limit are in
[Chapter 4: Configuration Reference](04-configuration.md).

## `Hidden` fields and permissions

A field-level `Hidden` flag is a separate thing from the collection-level `AdminOnly`. What the
attribute declares, and its effect on the admin form, are in
[Chapter 6: Field Types and Editors](06-field-types.md); this section covers only what it means
for permissions.

A `Hidden` field disappears entirely from the query DSL's field allowlist, and from the
searchable-field allowlist along with it — not just left unprojected, but rejected as unknown
even as a filter or sort target. The core-computed list of searchable fields, itself only a hint
handed to built-in search and to a search provider, excludes it too.

Without this, a field that isn't returned but can still be filtered on — combined with
pagination's `meta.total` — could still be guessed out one character at a time.

`Hidden` is not a write gate. Field-override logic doesn't check this flag at all; what actually
stops a caller from writing an arbitrary value is the system-field and read-only-field check. The
two built-in `Hidden` fields, `password` and `accessToken`, are also declared `ReadOnly`, and it's
that flag that actually protects them.

To block a sensitive field on a collection of your own, set both flags together. `Hidden` alone
still lets a caller who can guess the field name write to it, and the value really is written —
only reading it back is blocked. Adding `ReadOnly` is what makes a submitted value get silently
dropped by the system-field check instead, rather than rejected outright.

## What a permission failure looks like

A permission check fails with only two status codes: a caller that hasn't passed authentication
gets `401`; a caller that has passed authentication but lacks the matching grant gets `403`. The
envelope shape is the same as any other error, covered in
[Chapter 12: REST API Conventions](12-rest-conventions.md).

Take `editor@example.com`, whose role is granted only `article` read, and use it to edit an
article. The request body comes first, the response after:

```text
$ PUT /api/items/article/01a08f92-3a18-7c5d-99a3-5b14cd1279ea
body:
{"status":"draft"}
{"success":false,"error":{"code":"FORBIDDEN","message":"Write not permitted."}}
HTTP_STATUS:403
pretty:
{
  "success": false,
  "error": {
    "code": "FORBIDDEN",
    "message": "Write not permitted."
  }
}
```

The same caller reading a collection it has no grant on at all, such as `tag`, is also `403`, with
the message changed to `Read not permitted.`. Sending that same request fully anonymous is `401`
instead — what's being distinguished is whether this particular request passed authentication,
which is a different question from whether this caller could pass authentication at all.

## Previewing effective permissions

Before saving a role selection, the user form in the admin UI needs to show what permissions that
selection would actually grant. `GET /api/users/{id}/effective-permissions` is for that screen.
Like `RolesController`, only a super-admin can call it; a signed-in caller that isn't a
super-admin always gets `403` `FORBIDDEN`, `Admin role required.`.

This endpoint runs the exact same two-stage resolution as a real request: union the role grants,
then fold. The preview's answer is computed by the same logic that computes what this user gets
when it actually makes a request, not a separately maintained approximation, and the `public`
floor is unioned in the same way even over a hypothetical role set.

The `roles=` query parameter decides whose roles are being previewed:

- omitted entirely, and the preview uses this user's actually stored roles;
- present but an empty string, and the preview uses an empty hypothetical role set;
- one or more role ids, and the preview uses that hypothetical set;
- an id that doesn't resolve to a real role, and the response is `400`
  `Unknown role ids: …`; a value that isn't even a well-formed GUID gets a different message,
  `400` `Malformed role id: {value}`.

Whichever case applies, `public`'s grants are still unioned in — switching to a hypothetical role
set never drops that floor. Telling "omitted" apart from "empty string" needs the raw query
string, since ordinary model binding collapses both to the same `null`, but the two previews are
not the same thing. A user id that doesn't exist at all returns `404` `User not found.`, and that
check runs before any role is resolved.

A super-admin's result is fixed at `isSuperAdmin: true` plus an empty `permissions`, because every
collection's grant is already implied true and there's nothing to list per collection.
`GET /api/auth/me` returns the same shape for an actual super-admin session. For anyone else, both
interfaces list only the collections with at least one true flag; a collection with no grant at
all is simply absent from `permissions`.

Take `editor@example.com` again, whose role only grants `article` read. First the result with no
`roles=`, then the result with a hypothetical super-admin role swapped in:

```text
$ GET /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/effective-permissions
{"success":true,"data":{"isSuperAdmin":false,"permissions":{"article":{"read":true,"write":false,"delete":false}}}}
HTTP_STATUS:200
pretty:
{
  "success": true,
  "data": {
    "isSuperAdmin": false,
    "permissions": {
      "article": {
        "read": true,
        "write": false,
        "delete": false
      }
    }
  }
}

$ GET /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/effective-permissions?roles=01a08f90-8c13-764d-84c1-11fd1380ac9a
{"success":true,"data":{"isSuperAdmin":true,"permissions":{}}}
HTTP_STATUS:200
pretty:
{
  "success": true,
  "data": {
    "isSuperAdmin": true,
    "permissions": {}
  }
}
```

The second call swaps in an id that holds `isSuperAdmin`, and the response switches straight to
the super-admin shape — entirely unrelated to what this user's roles actually are in storage,
because this is a hypothesis, not a real swap.

## Managing roles in the admin UI

The admin UI never lets an administrator edit the `permission` or `userRole` collections directly.
`Hidden` and `AdminOnly` are independent flags, and a collection normally carries at most one of
them, but these two carry both, because they're purely internal RBAC data — letting the ordinary
write grant reach them would be exactly the self-escalation `AdminOnly` exists to block.

To change a role's grants, use the role permission matrix instead: check `canRead`, `canWrite`,
and `canDelete` per collection, and submit the whole set in one go. The effective-permissions
preview panel on the user form updates live to show what the currently selected role set computes
to.

A role's whole grant set is written through `PUT /api/roles/{id}/permissions`, a full overwrite
completed in one transaction; the endpoint's own semantics are in
[Chapter 13: REST API Endpoint Reference](13-rest-endpoints.md). Rejections:

- the body isn't a JSON array: `400`;
- the role id doesn't exist: `404`;
- the same collection appears twice: `400`, `Duplicate collection entries: …`;
- a collection name doesn't exist or is blank: `400`, `Unknown collections: …`.

A row where all three flags are false isn't stored at all — it's equivalent to deleting that
grant. These changes call the same endpoint directly, so nothing needs restarting afterward: the
next request recomputes from whatever is now in the database.

## What's next

That covers roles and permissions. The next chapter,
[Chapter 18: Extension Points: Search Providers and Change Listeners](18-extension-points.md),
covers the two places the framework deliberately leaves for a fork to implement.
