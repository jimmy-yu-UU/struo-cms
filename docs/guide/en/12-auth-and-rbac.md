# 12. Authentication, SSO & RBAC

Every request either carries the session cookie, a bearer access token, or neither — chapter 9 already
documents the wire-level mechanics of both schemes, the `X-Struo-CSRF` header, and the endpoint tables
for `AuthController`/`UsersController`/`RolesController`. This chapter covers the subsystem those wire
contracts sit on: how a password is hashed and verified, how a session is stored and revoked, how an
external identity provider is wired in and JIT-provisions a local account, and the RBAC data model that
decides what an authenticated (or anonymous) caller may actually do once a request is authenticated.

## Password authentication (Argon2id)

`Argon2idPasswordHasher` (`src/Struo.Infrastructure/Identity/Argon2idPasswordHasher.cs`) is the only
shipped `IPasswordHasher`. It produces a self-contained PHC-encoded string — salt and parameters embedded
in the hash itself, so no separate salt column exists on `User` — via `Isopoh.Cryptography.Argon2` with
fixed parameters: `timeCost: 3`, `memoryCost: 65536` (64 MiB), `parallelism: 1`,
`type: Argon2Type.HybridAddressing`, `hashLength: 32`. Live-verified against the running database (not
merely read from source):

```
$ docker exec struo-postgres psql -U struo -d struo -c \
    "select email, left(password,30) as pw_prefix from users limit 2;"
       email        |           pw_prefix
--------------------+--------------------------------
 editor@example.com | $argon2id$v=19$m=65536,t=3,p=1
 admin@admin.com    | $argon2id$v=19$m=65536,t=3,p=1
```

`AuthController.Login` (`src/Struo.Api/Controllers/AuthController.cs`) calls
`IAuthService.AuthenticateAsync`, which verifies the submitted password against the stored hash via
`Argon2idPasswordHasher.Verify`; on success it signs a `Cookie`-scheme `ClaimsPrincipal` in directly —
password verification and cookie issuance happen in the same request, there is no separate "exchange a
password for a token" step.

## Session cookies and the distributed ticket store

The **cookie** scheme (`AuthSchemes.Cookie`, constant `"Cookies"`) is the one the default
`AuthSchemes.Adaptive` policy scheme forwards to automatically on every request lacking an
`Authorization: Bearer` header, regardless of `[Authorize]` — `AuthWiring.AddStruoAuth`
(`src/Struo.Api/Auth/AuthWiring.cs`) registers `Adaptive` itself, not `Cookie` directly, as the default
authentication scheme (`services.AddAuthentication(AuthSchemes.Adaptive)`); see Bearer tokens below for
the forwarding rule and the bearer-header case. Its cookie is named
`struo.session` (`AuthSchemes.SessionCookieName`), `HttpOnly`, `SameSite=Lax`, 8-hour sliding expiration.
`SecurePolicy` is `Always` in `Production` and `SameAsRequest` otherwise (the local dev/test host runs
over plain HTTP, and `Always` would silently stop the cookie being sent back). If CORS is configured with
any allowed origins (`CorsWiring.HasConfiguredOrigins`) the cookie options are reconfigured to
`SameSite=None` + `SecurePolicy=Always` instead — cross-origin cookies require `SameSite=None`, which
browsers only honor alongside `Secure`.

Ticket storage — the actual session state behind the cookie's opaque key — is
`DistributedCacheTicketStore` (`src/Struo.Api/Auth/DistributedCacheTicketStore.cs`), an `ITicketStore`
backed by `IDistributedCache`: **StackExchange.Redis when `Redis:ConnectionString` is set**, an
**in-memory distributed cache otherwise** (the `Redis:ConnectionString` branch in
`AuthWiring.AddStruoAuth`, `AuthWiring.cs`). Both branches use identical sliding
8-hour expiry. The practical difference (chapter 3 states this plainly) is that the in-memory fallback
loses every session on process restart — fine for a quick local run, not for anything longer-lived or
multi-instance — while Redis persists sessions across restarts and shares them across replicas. Using a
server-side ticket store rather than encoding claims straight into the cookie is what makes immediate
revocation possible: `AuthController.Logout` (`SignOutAsync`) removes the ticket from the store, so a
logged-out cookie is dead immediately rather than merely expiring on its own schedule.

## Bearer tokens

The **bearer** scheme (`AuthSchemes.Bearer`, constant `"Bearer"`) is verified by
`BearerTokenAuthenticationHandler` (`src/Struo.Api/Auth/BearerTokenAuthenticationHandler.cs`) against a
hashed token store — a token is minted once via `POST /api/users/{id}/access-token` (super-admin only,
chapter 9), shown in that one response only (`AccessTokenHasher.Generate`; only the hash is persisted),
and never expires on its own (there is no TTL — a token is permanent until explicitly revoked via
`DELETE /api/users/{id}/access-token`, or rotated by generating a new one, which overwrites the stored
hash). Every bearer-authenticated request updates `AccessTokenLastUsedAt`, throttled to once per minute
per token so a busy integration doesn't turn every call into a write.

**No `[Authorize]` attribute names Bearer by default** — every write action across
`ItemsController`/`FilesController`/`UsersController`/`RolesController`/etc. is explicit about it
(`[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]`), so a bearer token has always worked
identically to a cookie there. What used to be missing was the actions naming **no** scheme at all:
`ItemsController`'s read actions, and `/graphql` (chapter 10), carry no `[Authorize]` attribute, and
before `AuthSchemes.Adaptive` existed as the default authenticate scheme, ASP.NET Core only ever ran the
Cookie handler automatically for those — a bearer-only caller hitting one of them was resolved as
anonymous. `Adaptive` (`src/Struo.Api/Auth/AuthWiring.cs`) closes that gap: it forwards to `Bearer`
whenever the request carries `Authorization: Bearer …`, on **every** endpoint, attributed or not — so a
bearer-only caller now reads through `ItemsController` (and `/graphql`) as itself, with its own roles'
grants unioned with the `public` floor below, exactly as a cookie session would. Live-verified that a
bearer request needs no `X-Struo-CSRF` header to succeed (see below) where a cookie request to the
same endpoint would be rejected without it:

```
$ curl -s -i -X PUT http://localhost:5221/api/items/file/5b4de227-0997-4bb1-b1e7-9565b3cffce6 \
    -H "Authorization: Bearer <token>" \
    -H "Content-Type: application/json" -d '{"status":"published"}'
HTTP/1.1 200 OK
{"success":true,"data":{"id":"5b4de227-0997-4bb1-b1e7-9565b3cffce6","version":5, ...}}
```
(No `X-Struo-CSRF` header, no session cookie, and the request still succeeds — see below for why.)

## The CSRF header rule

`CsrfProtectionMiddleware` (`src/Struo.Api/Auth/CsrfProtectionMiddleware.cs`) requires the
`X-Struo-CSRF` header, checked for **presence only**, on every non-safe HTTP method — but **only when the
request rides on the session cookie**. A `Bearer`-authenticated request is exempt (no ambient browser
credential exists for a cross-site page to ride along with), and so is a request that carries no session
cookie at all. This is why the bearer `PUT` above needed no CSRF header while the equivalent
cookie-authenticated call would 403 without one — chapter 8 and chapter 9 both show that exact rejection
live; this chapter only restates the rule as it bears on the two auth schemes above, not the mechanism
itself (see either chapter for the full OWASP rationale and the middleware's `RequiresCsrfHeader` logic).

## Login rate limiting

`POST /api/auth/login` is guarded by an in-app fixed-window limiter, partitioned by client IP —
`RateLimiting:Login` (`LoginRateLimitOptions`, `src/Struo.Application/Configuration/LoginRateLimitOptions.cs`):
`Enabled` (default `true`), `PermitLimit` (default `5`), `WindowSeconds` (default `60`). Every anonymous
login attempt burns full Argon2id CPU regardless of outcome, so an unbounded brute-force attempt is also
a CPU-exhaustion DoS vector — this limiter exists specifically to bound that, applied to the login
action only (logout/me/the OIDC challenge are deliberately not limited). Chapter 9 shows the live `429`
response (`Retry-After: 60`, error code `TOO_MANY_REQUESTS`) this produces once the window is exhausted;
this chapter does not re-trigger it.

**When to disable it:** set `Enabled` to `false` only in a multi-replica deployment (e.g. Kubernetes)
where per-IP rate limiting is instead enforced at the ingress/edge/WAF layer — that layer sees the real
client IP and sits in front of every pod, whereas this limiter's state is in-memory and per-pod and
therefore cannot enforce a true global limit across replicas (chapter 3). Leaving it enabled behind a
load balancer that doesn't itself rate-limit would under-count attempts per pod without actually
protecting the deployment as a whole — the flag exists so an operator can make that trade-off
consciously rather than the default silently doing the wrong thing in either topology.

## OIDC/external login

`Oidc:Enabled` (default `false`) gates the entire external-login scheme registration —
`OidcWiring.AddStruoOidc` (`src/Struo.Api/Auth/OidcWiring.cs`) returns early, before calling
`AddOpenIdConnect`, whenever `Enabled` is false or `Authority` is blank — when disabled, no OIDC
`AuthenticationScheme` is added at all, and `GET /api/auth/login/oidc` returns a plain `404` rather than
attempting a challenge (live-verified, this host has OIDC disabled by default):

```
$ curl -s -i http://localhost:5221/api/auth/login/oidc
HTTP/1.1 404 Not Found
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}

$ curl -s http://localhost:5221/api/config
{"success":true,"data":{"oidcEnabled":false,"brandName":"StruoCMS","brandLogoUrl":null}}
```

When enabled, `Oidc:Authority`/`ClientId`/`ClientSecret` are all required at startup
(`ValidateOnStart`); the handler uses authorization-code + PKCE, keeps short JWT claim names
(`MapInboundClaims = false`, so `OidcClaimsMapper` reads `email`/`name`/`iss`/`tid`/`email_verified`
directly rather than their long ASP.NET Core claim-type equivalents), and fetches additional claims from
the userinfo endpoint. On the `OnTokenValidated` handler in `OidcWiring.AddStruoOidc`
(`OidcWiring.cs`) the external principal is mapped to an `ExternalIdentity` and handed to
`IExternalLoginService.ResolveOrProvisionAsync` (`ExternalLoginService`,
`src/Struo.Application/Security/ExternalLoginService.cs`) — **not** signed in directly: the OIDC
principal is discarded and replaced with a local `Cookie`-scheme identity carrying just the resolved
user's id, which is what actually gets persisted into the Redis-backed ticket store above. A production
deployment therefore ends up with exactly the same session mechanics as password login, regardless of
how the user authenticated.

**Email-based JIT provisioning:** resolution is anchored on the external identity's email, matched
**case-insensitively at the store layer** against existing local users. If no local user matches, one is
created on the spot (`store.CreateExternalUserAsync`) — this is what "JIT" (just-in-time) means here: no
separate admin-driven provisioning step is required for a first-time external sign-in to work. Three
guards are each independently checked before the email match runs, inside
`ExternalLoginService.ResolveOrProvisionAsync`; only two of them are permissive by default —
tenant pinning ships **fail-closed**:

| Guard | Config key | Default | Effect when set |
|---|---|---|---|
| Tenant pinning | `Oidc:AllowedTenantId` | ships as the non-matching placeholder `REPLACE_TENANT_ID` (`appsettings.json`) — fails closed, rejecting every real tenant until replaced | Rejects (`TenantNotAllowed`) unless the token's `tid` claim matches exactly. |
| Verified email | `Oidc:RequireEmailVerified` | `false` | Rejects (`EmailNotVerified`) unless the token's `email_verified` claim is `true`. |
| Domain allow-list | `Oidc:AllowedEmailDomains` | `[]` (unrestricted) | Rejects (`DomainNotAllowed`) unless the email's domain is in the list. |

The source itself documents this as an **accepted risk**, not an oversight
(`OidcOptions.RequireEmailVerified`/`AllowedTenantId`/`AllowedEmailDomains`,
`src/Struo.Application/Security/OidcOptions.cs`): because linking is by email
equality, a deployment that enables OIDC without pinning at least one of these could have a password
account taken over by any identity provider identity presenting a matching email. A production OIDC
deployment is expected to constrain it explicitly — a single-tenant `Authority` plus `AllowedTenantId`
and/or `AllowedEmailDomains`, and `RequireEmailVerified = true` — rather than rely on the zero-config
defaults that keep local development frictionless.

**`public` is the floor for every caller:** `SqlSugarRolePermissionStore.LoadForUserAsync`
(`src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs`) unions the `public` role's
own grants into **every** caller's effective permissions — anonymous, role-less, and role-holding alike
— not merely as a fallback for a user whose role set comes back empty. A caller's own roles can only
*add* to what `public` already grants, never subtract: the model has no deny semantics —
`PermissionResolver.Resolve` folds every role's read/write/delete grants together with `OR` and nothing
else — so a role could never have meaningfully narrowed the floor even before this union existed. That
matters because, before it existed, a signed-in user's grants came *only* from their own assigned roles:
a user with roles could therefore read **less** than an anonymous visitor, whenever `public` held a
grant their roles didn't happen to repeat — logged in, and worse off. Unioning `public` into every
result fixes that asymmetry: a freshly JIT-provisioned user, a user with no `UserRole` rows, and a user
holding a full set of roles all see at least what `public` grants, and never less.

## Users, roles, permissions: the data model

Four framework collections model RBAC, all `[CmsCollection(..., AdminOnly = true)]`
(`src/Struo.Infrastructure/Identity/*.cs`):

| Collection | Table | Key columns | Notes |
|---|---|---|---|
| `user` | `users` | `email` (unique), `password` (Argon2id hash, `Hidden`+`ReadOnly`), `name`, `isActive`, `accessToken` (SHA-256 of the bearer token, `Hidden`+`ReadOnly`) | `Roles` is a `TagSelect` many-to-many to `role` via the `userRole` junction — edited on the User form as tag-picked role names, not by hand-crafting junction rows. |
| `role` | `roles` | `name` (unique), `isSuperAdmin`, `description` | `isSuperAdmin = true` short-circuits every permission check to allow-all (see `EffectivePermissions` below). |
| `permission` | `permissions` | `roleId`, `collection`, `canRead`, `canWrite`, `canDelete` | Unique on `(roleId, collection)`; `Hidden` (no dedicated admin screen — edited only through the Role permission matrix, see below). |
| `userRole` | `user_roles` | `userId`, `roleId` | Unique on `(userId, roleId)`; `Hidden`, pure junction. |

Both `permission` and `userRole` also carry `AdminOnly` on top of `Hidden` — a collection can be `Hidden`
(no sidebar entry) without being `AdminOnly`, and vice versa; here they're both, because these two are
purely internal to the RBAC mechanism and letting anyone with an ordinary per-collection write grant
touch them would be exactly the self-escalation `AdminOnly` (below) exists to prevent.

Seeding is first-boot-only and idempotent (`RbacSeeder.SeedAsync`,
`src/Struo.Infrastructure/Identity/RbacSeeder.cs`), invoked only when the `roles` table is created: it
creates the `admin` (`isSuperAdmin = true`) and `public` roles, assigns the bootstrap admin
(`Auth:BootstrapAdmin:Email`) to `admin`, and grants `public` read access on each
`Rbac:PublicReadCollections` entry — the same first-boot-only caveat chapter 3 documents in detail
(editing that config key and restarting does **not** retroactively grant anything against an existing
database; the grant has to be made directly against a live one instead, which is exactly what the next
section demonstrates).

## Per-collection read/write/delete grants

`EffectivePermissions` (`src/Struo.Application/Security/EffectivePermissions.cs`) is the resolved
per-request snapshot: `IsSuperAdmin` short-circuits every `CanRead`/`CanWrite`/`CanDelete` check to `true`
unconditionally; otherwise each check looks up the collection in a folded read/write/delete triple built
by `PermissionResolver.Resolve` from every role the caller holds — a grant from **any** held role is
enough (`OR`, not `AND`, across roles), computed once and cached on the scoped `ICurrentPermissions` for
the rest of the request (`PermissionResolutionMiddleware`). An absent collection entry denies all three
outright — the safe default.

The Role permission matrix (`PUT /api/roles/{id}/permissions`, chapter 9) is a **full replace** of a
role's grant set in one transaction (delete-all + insert-all); an all-`false` row is treated as absent
rather than stored. Live-verified: granting `public` read on `file`, then reverting it, demonstrates the
grant taking effect immediately against the running database, with no restart:

```
$ curl -s -X PUT http://localhost:5221/api/roles/<public-role-id>/permissions \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '[{"collection":"file","canRead":true,"canWrite":false,"canDelete":false}]'
{"success":true,"data":[{"collection":"file","canRead":true,"canWrite":false,"canDelete":false}]}

$ curl -s -i "http://localhost:5221/api/items/file?sort=fileName&limit=1"
HTTP/1.1 200 OK
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}],"meta":{"total":4,"limit":1,"offset":0}}

$ curl -s -X PUT http://localhost:5221/api/roles/<public-role-id>/permissions \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[]'
{"success":true,"data":[]}

$ curl -s -i "http://localhost:5221/api/items/file?limit=1"
HTTP/1.1 401 Unauthorized
```

(The anonymous request before the grant, and again after reverting it, both correctly came back
`401 UNAUTHORIZED` — "Authentication required" — since `file` carries no public grant on this host
otherwise.)

## `AdminOnly` collections and super-admin

`CmsCollectionAttribute.AdminOnly` (`src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs`)
marks a collection's **writes** (create/update/delete through the generic CRUD path) as requiring
super-admin regardless of any delegated per-collection grant — the four identity/authorization
collections above are the only ones that set it. `ItemService.RequireSuperAdminForAdminOnly`
(`src/Struo.Application/Query/ItemService.cs`) throws `PermissionDeniedException` with the
message `"Writes to '{collection}' require a super-admin."` when it fails — but the **ordinary**
per-collection permission check runs *first* at each of the five call sites, and it is not the same
check every time: `CreateAsync`, `UpdateCoreAsync`, and `RevertAsync` check `CanWrite`
and fail with `"Write not permitted."`; `DeleteAsync` and `RestoreAsync` check
`CanDelete` and fail with `"Delete not permitted."` instead — `RequireSuperAdminForAdminOnly` runs
immediately after each of those five checks, so a caller
with **no** ordinary grant at all on an `AdminOnly` collection sees the generic per-verb message, and the
AdminOnly-specific message only surfaces for a caller that *does* hold the relevant per-collection grant
but isn't super-admin. Both are live-verified for the write case, deliberately isolating each check:

```
# editor@example.com has NO grant on 'role' at all:
$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"description":"hacked"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Write not permitted."}}

# public role (editor's floor) temporarily granted write on 'role', still not super-admin:
$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"description":"hacked"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Writes to 'role' require a super-admin."}}
```

`RolesController` enforces the same super-admin requirement directly on every one of its own actions
(`RequireAdmin()`, checked first thing in each action — e.g. `GetPermissions` and `PutPermissions`,
`src/Struo.Api/Controllers/RolesController.cs`) rather than going
through `ItemService` at all — live-verified the same rejection shape from a completely different code
path:

```
$ curl -s -i -X PUT http://localhost:5221/api/roles/<id>/permissions -H "X-Struo-CSRF: 1" \
    -b editor-cookies.txt -d '[]'
{"success":false,"error":{"code":"FORBIDDEN","message":"Admin role required."}}
```

**`UsersController` does the same for every action except one deliberate exception:**
`PUT /api/users/{id}/password` only calls `RequireAdmin()` when the caller is changing **someone else's**
password (`ChangePassword`, `src/Struo.Api/Controllers/UsersController.cs`) — a non-admin
authenticated user may change their **own** password by supplying `currentPassword`, which is verified
against the stored hash before the write proceeds, in `ChangePassword`'s self-service branch. This is the one self-service write path in
the entire identity/RBAC surface; every other `UsersController`/`RolesController` action (creating a
user, issuing/revoking an access token, the effective-permissions preview, the role permission matrix)
requires super-admin unconditionally, with no self-service exception. Live-verified against
`editor@example.com` (no admin grant of any kind), changing their own password: a wrong `currentPassword`
is rejected as `401` — proof-of-knowledge, not an admin gate, so it is `UNAUTHORIZED` rather than
`FORBIDDEN` — and the correct one succeeds:

```
$ curl -s -i -X PUT http://localhost:5221/api/users/<self-id>/password -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"newPassword":"tempPassword3","currentPassword":"wrongpass"}'
HTTP/1.1 401 Unauthorized
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Current password is incorrect."}}

$ curl -s -i -X PUT http://localhost:5221/api/users/<self-id>/password -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"newPassword":"tempPassword3","currentPassword":"editorpass1"}'
HTTP/1.1 204 No Content
```

A threat model built on "every `UsersController` action requires super-admin" would be wrong on exactly
this one endpoint — worth stating plainly rather than leaving as an implicit exception.

Reads of `AdminOnly` collections are **not** specially restricted — they go through the same
per-collection `CanRead` grant as any other collection; only writes carry the super-admin requirement.

## Public read grants (`Rbac:PublicReadCollections`)

Covered above as part of seeding; restated here because it's the one config key this chapter's RBAC
model most directly governs. It is consulted **only** the first time the `roles` table is created
(chapter 3) — changing it later and restarting has no retroactive effect on an existing database. The
only way to grant public read against a live database is directly, either through the Role permission
matrix in the admin (or its underlying `PUT /api/roles/{id}/permissions` endpoint, demonstrated above).

## Hidden fields never being projected or accepted

A field's `Hidden` flag (`FieldMetadata.Hidden`, distinct from `CmsCollectionAttribute.Hidden`'s
sidebar-presentation meaning) is unconditionally excluded from the outbound projection —
`ItemProjector.Project` skips it before permission/field-selection are even consulted
(`src/Struo.Application/Query/Projection/ItemProjector.cs`: `if (field.Hidden) continue;`) — so no
combination of `fields=`, RBAC field-readability, or a `deep`-expanded relation can ever surface a
`Hidden` field's value through the API. It is also excluded from the query-DSL whitelist entirely
(chapter 8) — a filter/sort/`fields=` naming a `Hidden` field is rejected as an unknown field, precisely
so a credential-shaped `Hidden` column can't be turned into a character-at-a-time extraction oracle via
`meta.total`.

On the **write** side, `Hidden` alone is not itself the mechanism: the `IsSystem`/`ReadOnly`
field-stripping loop in `ItemDeserializer.Deserialize`
(`src/Struo.Application/Query/Write/ItemDeserializer.cs`) strips any field flagged `IsSystem` **or**
`ReadOnly` from the incoming body before validation runs, nulling it back out on the freshly-deserialized
entity. In the shipped schema, every `Hidden` field (`User.Password`, `User.AccessToken`) also happens to
be declared `ReadOnly`, so a client-supplied value for either is silently discarded rather than persisted
— but that discarding comes from the `ReadOnly` flag, not from `Hidden` by itself. Live-verified: a
generic `PUT` attempting to overwrite a user's password succeeds (the request itself is not rejected —
the field is simply dropped) and the stored hash is provably unchanged:

```
$ curl -s -X PUT http://localhost:5221/api/items/user/<editor-id> -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"email":"editor@example.com","password":"IGNORED-VALUE"}'
{"success":true,"data":{"id":"...","version":2,"email":"editor@example.com", ...}}   # no "password" key in the response — Hidden, never projected

$ docker exec struo-postgres psql -U struo -d struo -t -c "select password from users where email='editor@example.com';"
 $argon2id$v=19$m=65536,t=3,p=1$8OQS79u9b0SQAywU5GXKFQ$ZZlbXg1/mIGKjRZlveHSs4jSGyhJ0GKP5z8UibRPOWs
```

(The hash above is identical before and after the write — the submitted `"IGNORED-VALUE"` never reached
the database. The only supported way to change a password is `PUT /api/users/{id}/password` — either as
a super-admin changing someone else's, or as the account's own owner supplying a correct
`currentPassword` (see the self-service exception above); chapter 9 documents the endpoint itself.)

## Effective-permission preview in the admin

`GET /api/users/{id}/effective-permissions` (super-admin only, chapter 9) exists specifically so the
admin's User-edit form can show what a role selection *would* grant without saving it first. It reuses
the exact same resolution pair (`IRolePermissionStore` + `PermissionResolver`) that a real request goes
through, so the preview is by construction identical to what would actually be enforced — not a
separately-maintained approximation. This includes the `public` floor above: `LoadForRolesAsync` unions
the same `public` grants into a hypothetical role set that `LoadForUserAsync` unions into a real
caller's stored roles, so the preview can never disagree with what the request pipeline would actually
resolve — previewing an empty or unsaved role selection still shows at least the floor, never an
artificially empty result. Three distinct request shapes, all live-verified against a role-less
`editor@example.com`:

```
# absent `roles=` -> the user's actually-STORED roles, unioned with the public floor (editor holds
# none, so this is just the floor itself, currently empty)
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions"
{"success":true,"data":{"isSuperAdmin":false,"permissions":{}}}

# `roles=` present but EMPTY -> hypothetical preview of an empty role set, unioned with the same
# public floor
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions?roles="
{"success":true,"data":{"isSuperAdmin":false,"permissions":{}}}

# hypothetical: "what if this user were assigned the admin role?" -- an UNSAVED selection
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions?roles=<admin-role-id>"
{"success":true,"data":{"isSuperAdmin":true,"permissions":{}}}

# an unknown role id in the hypothetical set is rejected outright, not silently dropped
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions?roles=00000000-0000-0000-0000-000000000000"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown role ids: 00000000-0000-0000-0000-000000000000"}}
```

The distinction between an **absent** `roles=` parameter and one present-but-**empty** is deliberate and
implemented deliberately, via the `Request.Query.TryGetValue` check in
`UsersController.GetEffectivePermissions` (`UsersController.cs`): ASP.NET Core's default model
binding would collapse both to `null` for a plain string parameter, so the controller reads
`Request.Query` directly instead, specifically so "preview the stored roles" and "preview a role-less
selection" remain distinguishable request shapes. `isSuperAdmin: true` with an empty `permissions` map
(third example above) is `EffectivePermissions`' documented shape for a super-admin — every grant is
implied, so no per-collection map is ever populated; the same shape `GET /api/auth/me` returns for an
actual super-admin session (chapter 9).

## Next steps

- Chapter 8, [Query DSL](08-query-dsl.md), and chapter 9, [REST API](09-rest-api.md), for the
  `X-Struo-CSRF` mechanism itself, the full cookie/bearer endpoint tables, and how the default
  `Adaptive` authentication scheme resolves a bearer-only caller identically on every endpoint, reads
  included.
- Chapter 3, [Configuration Reference](03-configuration-reference.md), for every config key named in this
  chapter — `Auth:BootstrapAdmin`, `Rbac:PublicReadCollections`, `RateLimiting:Login`, `Redis`, `Oidc` —
  in full, including their first-boot-only and restart caveats.
- Chapter 11, [Files, Media & Image Transforms](11-files-and-media.md), for `IFileAccessPolicy` — the
  one place RBAC is enforced outside the generic `ItemService` path.
- Chapter 13, [Revisions & Soft Delete](13-revisions-and-soft-delete.md), for `DeletedAccessGuard` — the
  one read-side permission check stricter than plain `CanRead`.
