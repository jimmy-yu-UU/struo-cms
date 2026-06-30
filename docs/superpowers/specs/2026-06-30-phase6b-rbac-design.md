# StruoCMS — Phase 6b (Collection-based Authorization / RBAC) Design

> Date: 2026-06-30
> Source of truth: the StruoCMS master spec (§0–§18) + Phase 0–6a specs.
> Depends on Phase 6a (merged, `4d02a2e`): authentication core — `User` collection, cookie+Redis
> session, bearer token, real `ICurrentUserAccessor`, auth enforcement on mutations.
> Each phase runs its own brainstorm → write-plan (TDD) → execute → verify cycle (§17.1).

## 0. Position in the Phase 6 decomposition

Phase 6 (auth / session / SSO / RBAC / Redis) was split in the 6a spec into:

- **6a — Authentication core (merged):** identity, session, bearer token, `ICurrentUserAccessor`,
  authentication *enforcement* on mutations. `IPermissionService` left as `AllowAll`.
- **6b — Collection-based authorization (this spec):** the real `IPermissionService` backed by
  role/permission data; per-collection read/write/delete rules **including public (anonymous) read**
  so a public website can consume designated collections. Replaces `AllowAllPermissionService`.
- **6c — SSO (later):** external OIDC identity providers, layered on 6a.

## 1. Goal & scope

Replace `AllowAllPermissionService` with real RBAC: **roles → per-collection read/write/delete rules**,
including **anonymous (public) read**, so a public front-end can consume designated data collections.
Reuse the existing metadata engine and the Phase 3a relation foundation; keep **`ItemService`
effectively unchanged** (one exception-type swap, see §5).

**In scope:**
- Role / permission / user-role data model, exposed as first-class CMS collections (Infrastructure,
  beside `User`/`File`), so RBAC is administered through the same generic CRUD path.
- A **per-request effective-permissions snapshot**: resolved once per request, cached for the request
  lifetime; `IPermissionService` keeps its current **synchronous, ambient signature** (no change to
  `ItemService`).
- `RbacPermissionService : IPermissionService` reading the snapshot; replaces `AllowAllPermissionService`.
- **Anonymous = the `public` role**: anonymous requests apply the `public` role's permissions.
- **Multi-role** users (M2M); effective permission = the **union (OR)** of all the user's roles.
- An **`admin` super-role** via an `IsSuperAdmin` flag (all collections, all operations) — no per-collection
  permission rows required for it.
- Dev `RbacSeeder` (idempotent): seed `admin` + `public` roles, assign the bootstrap admin user to
  `admin`, and seed `public` read rows from config (`Rbac:PublicReadCollections`).
- `UsersController` TODO(6b) closure: admin gate on managing other users / access tokens; self
  password-change requires a verified `currentPassword`.
- Remove the temporary `ProtectedCollections { "user" }` guard (subsumed by the permission model).
- TDD coverage + live Postgres verification gate.

**Out of scope (deferred):**
- **Field-level per-role read control.** `ReadableFields` stays "return all readable fields" (YAGNI).
- **Record/row-level (ownership) rules** as a general framework. The only ownership check introduced
  is the narrow `id == current userId` test for self password-change (no generic per-row scope engine).
- **Cross-request permission caching** (Redis / in-memory keyed by user). One DB load per request only;
  noted as a future refinement.
- SSO / external IdPs (Phase 6c). Self-service registration, lockout, rate-limiting (as in 6a).

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Model richness | **Roles → per-collection read/write/delete rules.** Not a single admin flag; not row-level scopes. |
| Permission resolution | **Per-request snapshot**, resolved in middleware after authentication, held in a scoped service. `IPermissionService` keeps its **synchronous, ambient** signature → `ItemService` unchanged. |
| Role cardinality | **Multi-role (M2M).** Effective permission = union (OR) of all the user's roles. |
| Anonymous / public read | **A real `public` role row.** Anonymous requests apply its permissions. Public scope is data (permission rows), not code. |
| `admin` semantics | **`IsSuperAdmin` flag on `role`.** A super-role short-circuits to allow-all; no per-collection rows needed. |
| Field-level read | **Not in 6b (YAGNI).** `ReadableFields` returns all readable fields. |
| Where RBAC data lives | **CMS collections** (`role`/`permission`/`userRole`) in Infrastructure, governed by RBAC itself (default-deny → only super-admin manages them). |
| `UsersController` TODO(6b) | **Closed:** admin gate on managing others + access tokens; self password-change verifies `currentPassword`; ownership = `id == current userId`. |

## 3. Data model (three new Infrastructure entities, as CMS collections)

```
user ──< userRole >── role ──< permission
```

- **`role`** (`[SugarTable("roles")]`, collection `role`, group `System`):
  - `Id` (Guid/UUIDv7) · `Name` (string, **unique index**) · `IsSuperAdmin` (bool, default `false`)
  - `Description` (string?, optional)
- **`permission`** (`[SugarTable("permissions")]`, collection `permission`, group `System`):
  - `Id` (Guid) · `RoleId` (Guid, **M2O → role**) · `Collection` (string) ·
    `CanRead` / `CanWrite` / `CanDelete` (bool, default `false`)
  - **unique index** (`RoleId`, `Collection`)
- **`userRole`** (`[SugarTable("user_roles")]`, junction, collection `userRole`):
  - `Id` (Guid) · `UserId` (Guid, **M2O → user**) · `RoleId` (Guid, **M2O → role**)
  - **unique index** (`UserId`, `RoleId`) — models the user↔role M2M

All three are framework collections in Infrastructure (like `User`), carrying `[Cms*]` + `[Sugar*]`
attributes. They are **governed by RBAC itself**: with default-deny and no permission rows granted to
non-super roles, only a super-admin can read/write them — self-protecting, no special-casing needed.

## 4. Components (by layer)

### Domain
- No new types. (Authorization is an Application/Infrastructure concern.)

### Application (ports + pure logic; depends only on Domain)
- `EffectivePermissions` — the resolved snapshot: `bool IsSuperAdmin` + a per-collection map of
  `(canRead, canWrite, canDelete)`. Methods `CanRead/CanWrite/CanDelete(string collection)`:
  super-admin short-circuits to `true`; otherwise look up the collection (**default-deny** when absent).
- `IRolePermissionStore` (port) — `Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken)`.
  Queries role/permission/user-role data **directly, bypassing the generic projection / permission
  gating** (mirrors the 6a `IUserCredentialStore` pattern; avoids the chicken-and-egg of gating the
  very query that resolves the gate). For `userId == null`, loads the `public` role's permissions.
  Returns roles (with their `IsSuperAdmin` flags) + permission rows.
- `PermissionResolver` (pure) — folds `RolePermissionData` into an `EffectivePermissions`:
  any super-admin role → `IsSuperAdmin = true` (short-circuit); else union (OR) the permission rows
  across all the user's roles.
- `ICurrentPermissions` (scoped holder port) — exposes the resolved `EffectivePermissions` for the
  current request (set by the resolution middleware, read by the permission service and by controllers
  needing an admin check).
- `RbacPermissionService : IPermissionService` — **signature unchanged**; delegates `CanRead/CanWrite/
  CanDelete(collection)` to `ICurrentPermissions.Current`. `ReadableFields` returns all readable fields
  (YAGNI, §1).
- `PermissionDeniedException` — typed denial carrying whether the caller was anonymous (→ 401) or
  authenticated-but-unauthorized (→ 403). See §5.

### Infrastructure (implementations)
- Entities `Role`, `Permission`, `UserRole` (`Identity/`), as in §3.
- `SqlSugarRolePermissionStore : IRolePermissionStore` — direct SqlSugar queries (join user_roles →
  roles → permissions; or the `public` role for anonymous).
- `RbacSeeder` (dev, idempotent) — runs in the dev startup block after `AdminUserSeeder`:
  1. Ensure `admin` (`IsSuperAdmin = true`) and `public` roles exist.
  2. Ensure the bootstrap admin user has the `admin` role (a `user_roles` row).
  3. For each collection in `Rbac:PublicReadCollections` (config array), ensure a `public`-role
     permission row with `CanRead = true`. Keeps the blog sample's public pages readable **without**
     coupling the framework seeder to `samples/*` (the collection list is config, not a code reference).

### Api (composition root + web-coupled glue; per the 6a placement decision)
- `PermissionResolutionMiddleware` — placed **after** `UseAuthentication()`: reads the current user id
  (`ICurrentUserAccessor`, null when anonymous) → `IRolePermissionStore.LoadForUserAsync` →
  `PermissionResolver` → stores the snapshot into the scoped `ICurrentPermissions`. **One DB load per
  request.**
- DI: `services.Replace(AllowAllPermissionService → RbacPermissionService)`; register the store,
  resolver, scoped holder, and middleware.
- Remove `ProtectedCollections` and its read-path guard in `ItemsController` (subsumed by RBAC).
- `UsersController` — inject `ICurrentPermissions`; enforce the §5 rules.
- Map `PermissionDeniedException` → 401/403 in the existing exception-handling path.

## 5. Enforcement & status codes

- **Writes / deletes** keep `[Authorize]` (authentication required) **plus** the permission check.
- **Reads** can no longer use a blanket `[Authorize]` (anonymous callers with a `public` grant must be
  able to read). Read gating flows through `ItemService`'s existing `permissions.CanRead(collection)`
  call.
- On denial, the permission layer throws `PermissionDeniedException`: **anonymous → 401**,
  **authenticated-but-unauthorized → 403**. Mapped in the global exception handler (exact mapping point
  confirmed during planning).
- **`ItemService` change (the only one):** the existing `throw new QueryException("… not permitted")`
  at the three permission checks becomes `throw new PermissionDeniedException(...)` (~3 lines). No other
  `ItemService` change.
- **Behavior change (recorded explicitly):** in 6a content reads were unconditionally anonymous; from
  6b a content read requires the `public` role to hold a `CanRead` permission row for that collection.
  The dev `RbacSeeder` + `Rbac:PublicReadCollections` config keep the sample's public pages readable.

**`UsersController` TODO(6b) closure:**
- `POST /api/users` (create), `PUT /api/users/{id}/password` for **another** user, and the access-token
  generate/revoke endpoints → require an **admin** (`ICurrentPermissions.Current.IsSuperAdmin`); else **403**.
- `PUT /api/users/{id}/password` where `id == current userId` (the **owner**) → allowed without admin,
  **but** the body must carry a `currentPassword` that `IPasswordHasher.Verify` confirms against the
  stored hash; wrong/absent → **401**.
- A non-admin attempting to change another user's password → **403**.

## 6. Data flow

1. Request arrives → 6a authentication middleware builds `HttpContext.User`.
2. `PermissionResolutionMiddleware` reads the user id (null for anonymous) →
   `IRolePermissionStore.LoadForUserAsync` → `PermissionResolver` → snapshot into scoped
   `ICurrentPermissions`.
3. `ItemService` calls `permissions.CanRead/CanWrite/CanDelete(collection)` → `RbacPermissionService`
   reads the snapshot. Super-admin → `true`; otherwise the collection's rule row; **absent → deny**.
4. Controllers needing an admin gate (`UsersController`) inject `ICurrentPermissions` and check
   `.Current.IsSuperAdmin`.

## 7. Error handling & edge cases

- Anonymous read of a non-public collection → **401**; authenticated read of an ungranted collection → **403**.
- `role` / `permission` / `userRole` / `user` collections: no grants for non-super roles → editors **403**,
  anonymous **401**, super-admin **200** — without special-casing.
- A logged-in user with **no roles** → empty snapshot → default-deny everywhere. A logged-in user does
  **not** inherit the `public` role (which applies to anonymous callers only; see Open Questions §11).
- Multi-role union: a `CanWrite` from any one role grants write.
- Self password-change without `currentPassword` → 401; admin changing another user's password needs no
  `currentPassword`.
- Resolution query must never be gated by RBAC → dedicated `IRolePermissionStore` (direct query).
- Tests use SQLite + in-memory cache (hermetic), mirroring 6a.

## 8. Testing (TDD — RED first)

**Unit:** (1) `PermissionResolver` — super-admin short-circuit; multi-role union; anonymous → `public`;
default-deny on absent collection. (2) `RbacPermissionService` — reads the snapshot; `ReadableFields`
returns all.

**Integration** (`WebApplicationFactory` + SQLite + in-memory cache):
(3) anonymous read of a `public`-granted collection → 200; of an ungranted collection → 401.
(4) editor role — read granted ok; write to an ungranted collection → 403; write to a granted one → ok.
(5) super-admin — all operations allowed.
(6) `role`/`permission`/`userRole`/`user` collections — editor → 403, anonymous → 401, admin → 200.
(7) `UsersController` — non-admin create user → 403; admin → 201; self password-change wrong
`currentPassword` → 401, correct → 200; non-admin changing another's password → 403; access-token
endpoints require admin.
(8) the removed `ProtectedCollections` guard's intent still holds: `user` read requires permission.

**Live verification gate** (memory: *SQLite green ≠ Postgres correct*) — real PG:
(9) `roles` / `permissions` / `user_roles` tables created with the right columns + unique indexes.
(10) `RbacSeeder` creates `admin` + `public`; the bootstrap user holds the `admin` role (`user_roles` row).
(11) permission resolution round-trip on live PG; public read on a configured collection works; an
ungranted collection denies.

## 9. Migration

Dev-only `InitTables`: register `Role`, `Permission`, `UserRole` in the initializer entity list and the
metadata scan assembly; recreate to add `roles` / `permissions` / `user_roles` (with unique indexes on
`roles.name`, (`permissions.role_id`,`collection`), (`user_roles.user_id`,`role_id`)). `RbacSeeder` is
idempotent. No data migration (greenfield). No new external packages anticipated (uses the existing
metadata/relation/SqlSugar stack); confirm during planning.

## 10. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Chicken-and-egg: gating the very query that resolves the gate | Dedicated `IRolePermissionStore` queries directly, bypassing projection/gating (mirrors 6a credential store). |
| Content-read behavior change breaks sample public pages | Dev `RbacSeeder` grants `public` read from `Rbac:PublicReadCollections` config; documented in §5. |
| One permission DB load per request (perf) | Resolved once per request into a cached snapshot; cross-request caching (Redis) deferred (YAGNI), noted here. |
| New collection added → forgotten grants | Super-admin flag always works without per-collection rows; `public`/editor use safe default-deny. |
| RBAC tables editable through the generic CRUD path | Governed by RBAC itself (default-deny) → only super-admin manages them; covered by test (6). |

## 11. Open questions (settle in planning or flag to user)

- **Does a logged-in user with no roles inherit `public`?** Current design: **no** — `public` applies to
  *anonymous* callers only; an authenticated user sees only their explicit roles' grants (default-deny
  otherwise). Revisit if a "every authenticated user can at least read public collections" rule is wanted.
- Exact `PermissionDeniedException` → status mapping point in the existing exception pipeline (confirm by
  reading the current handler during planning).

## 12. Acceptance (verification gate, with evidence)

- `dotnet build` clean (warnings-as-errors); all unit/integration tests green on SQLite + in-memory cache.
- Live PG checks (9)(10)(11) pass with captured evidence (table columns/indexes, seeded roles, bootstrap
  user's `admin` role, resolution round-trip, public read allow + ungranted deny).
- §2 dependency rule intact: entities in Infrastructure with persistence attributes; ports in Application;
  `ItemService` unchanged except the single `PermissionDeniedException` swap.
- `AllowAllPermissionService` removed from the live DI graph; `RbacPermissionService` in its place.
- `ProtectedCollections` guard removed; its intent preserved by RBAC (test 8).
- `UsersController` TODO(6b) closed (admin gate + self `currentPassword`); no secrets committed.
