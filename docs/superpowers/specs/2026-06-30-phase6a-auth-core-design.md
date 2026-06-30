# StruoCMS — Phase 6a (Authentication Core) Design

> Date: 2026-06-30
> Source of truth: the StruoCMS master spec (§0–§18) + Phase 0–5.6 specs.
> Depends on Phase 5.5 (merged): entity `Guid`/UUIDv7 PKs and `Guid?` audit identity.
> Each phase runs its own brainstorm → write-plan (TDD) → execute → verify cycle (§17.1).

## 0. Phase 6 decomposition

The master-spec "Phase 6 (auth / session / SSO / RBAC / Redis)" bundles several subsystems with a
dependency order: authentication establishes identity, on which authorization (RBAC) and SSO both
build. Phase 6 is therefore split:

- **6a — Authentication core (this spec):** identity (`User` collection), credential verification,
  cookie session (Redis-backed, revocable), a permanent per-user access token (bearer auth),
  the real `ICurrentUserAccessor`, and authentication *enforcement* on mutations.
- **6b — Collection-based authorization (RBAC):** real `IPermissionService` backed by role/permission
  data; per-collection read/write/delete rules **including public (anonymous) read** so a public
  website can consume designated data collections. Replaces `AllowAllPermissionService`.
- **6c — SSO (optional/later):** external OIDC identity providers, layered on 6a.

Redis is not a standalone subsystem; it is introduced here as the server-side session store.

## 1. Goal & scope

Stand up authentication so the system knows *who* the caller is, persist that across requests with a
revocable server-side session, and stamp real audit identities — without changing the generic engine
(`ItemService` already depends only on the `IPermissionService` / `ICurrentUserAccessor` ports).

**In scope:**
- `User` entity as a first-class CMS collection (Infrastructure, like `File`/`Language`), with
  sensitive fields (`Password`, `AccessToken`) `Hidden + ReadOnly` so the generic CRUD path can
  never project or accept them.
- Password hashing with **Argon2id** via a PHC-encoding library (encoded hash embeds salt + params;
  no separate salt column).
- **Cookie authentication** (ASP.NET Core built-in) with a **Redis-backed `ITicketStore`** for
  server-side, revocable sessions (logout / logout-all).
- A **permanent per-user access token** (bearer scheme) for programmatic data access: random
  ≥256-bit token, stored only as a `SHA-256` hash, shown once, rotatable/revocable.
- Real `ICurrentUserAccessor` (`HttpContextCurrentUserAccessor`) replacing `StubCurrentUserAccessor`,
  so audit `CreatedBy`/`UpdatedBy` carry the real user.
- Dedicated identity endpoints (login / logout / me / user-provisioning / password / access-token).
- Authentication **enforcement**: all writes/deletes and the `user` collection + identity endpoints
  require an authenticated caller (cookie *or* bearer). Content-collection **reads stay anonymous**.
- Dev bootstrap admin seeder (from config/env).
- Redis added to `/health/ready`.
- TDD coverage + live Postgres **and** Redis verification gate.

**Out of scope (deferred):**
- **Authorization / RBAC** — role/permission model, per-collection rules, public-read gating.
  `IPermissionService` stays `AllowAll` in 6a (see §0, Phase 6b). The future model is *collection-based
  permissions* enabling public data APIs for a front-end website.
- SSO / external IdPs (Phase 6c).
- Self-service registration, email verification, password-reset email flows, account lockout /
  rate-limiting (revisit when needed; not required for an admin CMS in 6a).
- Multiple access tokens per user / token scopes (single token column now; a token table is a later
  refinement if needed).
- Admin SPA login UI (Phase 7).

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Session strategy | **HttpOnly cookie session**, server-side ticket in **Redis** (revocable). Chosen over JWT/JWT+refresh: same-origin Vue admin SPA, simplest + safe, revocation built in. |
| `User` placement | **First-class CMS collection** in Infrastructure (consistent with `File`/`Language`), not a separate auth module. |
| Entity-in-Infrastructure | Kept (see §3). Dependency rule is **not** violated: the generic engine references entities only via metadata + reflection, never by type. |
| Password hashing | **Argon2id**, PHC-encoded (salt + params embedded) → **no separate salt column**. Library `Isopoh.Cryptography.Argon2` (latest via `dotnet add package`). |
| Login identifier | `Email` (unique), + password. No public registration; admin-created + dev bootstrap seed. |
| Access token | **Permanent per-user**, full bearer auth path in 6a, **stored as `SHA-256` hash**, generated random ≥256-bit, shown once, rotatable/revocable. |
| Auth enforcement (6a) | Writes/deletes + `user` collection + identity endpoints require auth (cookie or bearer). **Content reads stay anonymous**; per-collection read gating (incl. public) is 6b. |
| RBAC | Deferred to 6b; `IPermissionService` remains `AllowAll`. |

## 3. Architectural note — entities live in Infrastructure (kept)

`User` carries `[SugarColumn]`/`[SugarTable]` persistence attributes, so by §2 it cannot live in pure
Domain; it sits in Infrastructure beside `File`/`Language`. This is a conscious deviation from textbook
Clean Architecture (entities-at-core), but **the dependency rule holds**: the enterprise logic here is
the *metadata-driven engine* (scanner, query DSL, relation graph, translation overlay, permission
ports) which is pure and inward; entities are anemic data shapes that the engine manipulates via
reflection + the `IItemRepository`/`IEntityRegistry` ports — never referenced by type from inner layers.
"Has operations" (login, CRUD, token management) is satisfied by **services** (`AuthService`,
`IPasswordHasher`, controllers), exactly as `File`'s rich behavior lives in `FileService` while
`File` stays a data shape.

**Migration trigger (recorded so it isn't lost):** if entities later accumulate genuine invariants /
aggregate behavior that is awkward to keep in services, migrate **all** entities at once to pure
Domain POCOs (keeping `[Cms*]`, dropping `[SugarColumn]`) + SqlSugar **fluent/external mapping** in
Infrastructure. Not piecemeal, and not in 6a.

## 4. Components

### Domain
- No new types. (Authentication is an Application/Infrastructure concern.)

### Application (ports + pure orchestration; depends only on Domain)
- `ICurrentUserAccessor` (**exists**) — interface unchanged; real implementation swapped in.
- `IPasswordHasher` — `string Hash(string password)`, `bool Verify(string encoded, string password)`.
- `IUserCredentialStore` — credential lookups that deliberately **bypass the generic projection** so
  hashes never traverse the read path:
  - `Task<UserCredential?> FindByEmailAsync(string email, CancellationToken)`
  - `Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken)`
  - `record UserCredential(Guid Id, string PasswordEncoded, bool IsActive)`
- `IAuthService` — `Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken)`;
  returns success(userId) or a typed failure (`InvalidCredentials` / `Inactive`). Verifies only —
  issuing the cookie is an Api/HttpContext concern. Lives in Application (depends on the two ports above).
- `AuthOptions` (config-bound) — cookie name/secure/sameSite/expiry, password min length, token byte
  length, bootstrap admin, Redis connection.

### Infrastructure (implementations; + external packages)
- `User` entity (`Identity/User.cs`), collection `user`, `: AuditableEntity`:
  `Id` (Guid/UUIDv7) · `Email` (`[CmsField]`, required, **unique index**) · `Password`
  (Argon2id PHC string; `[CmsField(Hidden, ReadOnly)]`) · `Name` (`[CmsField]`) · `IsActive`
  (`[CmsField]`, default true) · `AccessToken` (string?,
  **stores the `SHA-256` of the issued token, never the plaintext**; `[CmsField(Hidden, ReadOnly)]`,
  indexed for lookup) · audit fields.
- `Argon2idPasswordHasher : IPasswordHasher` — wraps `Isopoh.Cryptography.Argon2`; fixed params
  (memory/iterations/parallelism) as constants; PHC-encoded output.
- `SqlSugarUserCredentialStore : IUserCredentialStore` — direct SqlSugar query by email
  (case-insensitive) and by access-token hash.
  *(Web-coupled auth components — `HttpContextCurrentUserAccessor`, `DistributedCacheTicketStore`,
  `BearerTokenAuthenticationHandler` — live in **Api** per the 2026-06-30 placement decision so
  Infrastructure stays web-free with no ASP.NET FrameworkReference; see the Api subsection.)*
- `AdminUserSeeder` (dev) — if no users exist, create an admin from `Auth:BootstrapAdmin:{Email,Password}`.
  Runs in the dev startup block after `LanguageSeeder`.
- Redis registration: when `Redis:ConnectionString` is configured →
  `AddStackExchangeRedisCache`; otherwise `AddDistributedMemoryCache` (tests/fallback, logged).
- `RedisReadinessCheck` (tag `ready`) — lightweight `IDistributedCache` round-trip.

### Api (composition root + endpoints; web-coupled auth lives here)
- `HttpContextCurrentUserAccessor : ICurrentUserAccessor` — reads `IHttpContextAccessor.HttpContext.User`
  `NameIdentifier` claim → `Guid?`; **replaces** the Infrastructure `StubCurrentUserAccessor` in DI
  (`Program.cs` via `services.Replace`).
- `DistributedCacheTicketStore : ITicketStore` — `Store/Renew/Retrieve/Remove` over `IDistributedCache`
  (prefixed keys; value = `TicketSerializer`-serialized `AuthenticationTicket`); server-side revocation + logout-all.
- `BearerTokenAuthenticationHandler` — reads `Authorization: Bearer <token>`, computes `SHA-256`,
  resolves the user via `IUserCredentialStore.FindByAccessTokenAsync`, builds the `ClaimsPrincipal`.
- `AuthController`:
  - `POST /api/auth/login` `{email, password}` → `AuthService` → `HttpContext.SignInAsync(Cookie)` → 200 + user summary; failure → 401.
  - `POST /api/auth/logout` → `SignOutAsync` (removes ticket from Redis) → 204.
  - `GET /api/auth/me` → current user summary, or 401.
- `UsersController` (password-aware writes; reads use the generic engine):
  - `POST /api/users` `{email, password, name}` → Argon2id hash → create → 201 (no hash returned).
  - `PUT /api/users/{id}/password` `{newPassword}` (self-service also requires `currentPassword`) → rehash.
  - `POST /api/users/{id}/access-token` → generate random ≥256-bit token, store `SHA-256`, return plaintext **once**.
  - `DELETE /api/users/{id}/access-token` → null `AccessToken` (revoke).
- Wiring in `Program.cs`: `AddHttpContextAccessor()`; Redis cache; `AddAuthentication()` with two
  schemes — Cookie (`SessionStore = DistributedCacheTicketStore`, `HttpOnly`, `SecurePolicy=Always`,
  `SameSite`, `OnRedirectToLogin`/`OnRedirectToAccessDenied` overridden to return **401/403 JSON**,
  not 302 HTML) + the bearer scheme; a composite policy accepting either; `UseAuthentication()` /
  `UseAuthorization()`.
- **Enforcement mechanism** (the generic `ItemsController` cannot use a per-collection `[Authorize]`
  attribute, so enforcement is two-layered):
  1. `[Authorize]` attribute on all explicitly-mutating/identity actions — `UsersController`,
     `AuthController` (except `login`), and the generic `ItemsController` **write/delete** actions.
  2. A **protected-collection guard** for the generic read path: a small configured set of *system
     collections* requiring authentication for **all** access (initially `{ "user" }`). The
     `ItemsController` read actions (and any generic access) reject unauthenticated callers when the
     requested collection is in that set → 401. Content collections are not in the set → reads stay
     anonymous. (This set is the seam 6b's per-collection permission model later subsumes.)

## 5. Data flow

1. **Login (cookie):** `POST /api/auth/login` → `AuthenticateAsync` → `FindByEmailAsync` →
   `IPasswordHasher.Verify(encoded, password)` → if valid & active → principal(`NameIdentifier=userId`)
   → `SignInAsync` → ticket written to Redis via `ITicketStore` → 200 + `Set-Cookie`. Failure → 401 (generic).
2. **Cookie request:** cookie → cookie middleware → `ITicketStore.RetrieveAsync` (Redis) →
   `HttpContext.User` → `HttpContextCurrentUserAccessor` → audit AOP stamps the real user.
3. **Bearer request:** `Authorization: Bearer <token>` → handler → `SHA-256` →
   `FindByAccessTokenAsync` → if found & active → principal → identical downstream.
4. **Logout:** `SignOutAsync` → `ITicketStore.RemoveAsync` (immediate server-side invalidation) → cookie cleared.
5. **Provisioning / password:** create hashes a fresh Argon2id PHC string; change-password rehashes;
   hash never returned or projected.
6. **Access-token management:** generate → store `SHA-256(token)`, return plaintext once; regenerate =
   rotation (old hash overwritten, old token dead); delete = revoke.

## 6. Error handling & edge cases

- Wrong password / unknown email / inactive account → **401 + single generic message** (no user enumeration).
- Unauthenticated access to a protected endpoint → **401 JSON** (no HTML redirect).
- Duplicate email on create → **409**; weak password (< configured min length) → **400**.
- Self-service change-password with wrong `currentPassword` → 401.
- Invalid / revoked bearer token → 401.
- Redis unavailable at runtime → session operations fail with **503**; surfaced by `/health/ready`.
- `Password` / `AccessToken` never appear in any projection (Hidden+ReadOnly **and** the dedicated
  credential store that bypasses projection — double guard); generic create/update cannot set them.
- Bootstrap seeder runs with no current user (accessor returns null → existing audit null-handling applies).
- Tests use `AddDistributedMemoryCache` → no Redis dependency (hermetic, mirrors SQLite-for-tests).

## 7. Testing (TDD — RED first)

**Unit:** (1) Argon2id hasher — distinct output per call, `Verify` true/false, encoded round-trip;
(2) token `SHA-256` hashing + lookup; (3) `AuthService` — valid / wrong-password / inactive /
unknown-email; (4) `user` schema — `password`/`accessToken` Hidden (absent from field list);
(5) generic CRUD can neither read nor write `password`/`accessToken`.

**Integration** (`WebApplicationFactory` + SQLite + in-memory cache): (6) login 200 + `Set-Cookie` /
401 / inactive 401; (7) authenticated write → audit `CreatedBy` == logged-in user; unauthenticated
write → 401; (8) logout removes ticket → reused cookie is unauthenticated; (9) bearer: generate →
authorized → revoke → 401, bad token → 401; (10) provisioning — hash works, duplicate email 409, weak
password 400, password never returned; (11) change-password — old fails, new works; (12) `user` read
requires auth (anonymous → 401), content-collection read stays anonymous; (13) dev bootstrap seeder
seeds admin when no users.

**Live verification gate** (memory: *SQLite green ≠ Postgres correct*) — real PG **and** Redis:
(14) `users` table columns (`email` unique, `password`, `accesstoken`, `isactive`); login
round-trip; audit stamps the real user; (15) Redis holds the session ticket; logout removes it;
revocation works; a host restart preserves the session (server-side store confirmed).

## 8. Migration

Dev-only `InitTables`: register `User` in the initializer entity list and the metadata scan assembly;
recreate to add the `users` table (`email` unique index, `accesstoken` index). New packages added
via `dotnet add package` (latest, centralized in `Directory.Packages.props`):
`Isopoh.Cryptography.Argon2` (Infrastructure) and `Microsoft.Extensions.Caching.StackExchangeRedis`
(Api). No new ASP.NET package needed (Api already references the framework). No data migration.

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Hash/token leaking via the generic read path | Hidden+ReadOnly **and** a dedicated credential store that bypasses projection (double guard); test (4)(5). |
| Permanent token is a long-lived credential | Stored only as `SHA-256`; high-entropy random; shown once; rotatable/revocable; bearer lookup by hash. |
| Bootstrap admin password committed | Sourced from config/env only; never hardcoded; verified in acceptance. |
| Redis outage breaks sessions | `/health/ready` includes Redis; 503 on session ops; tests use in-memory cache. |
| Web-framework coupling for auth | Web-coupled auth (accessor / ticket store / bearer handler) lives in **Api**; Infrastructure stays web-free (no ASP.NET FrameworkReference). |
| Adding a 2nd auth scheme increases surface | Both schemes converge on one `ClaimsPrincipal` shape; downstream identical; enforcement centralized in policy. |

## 10. Acceptance (verification gate, with evidence)

- `dotnet build` clean (warnings-as-errors); all unit/integration tests green on SQLite + in-memory cache.
- Live PG + Redis checks (14)(15) pass with captured evidence (table columns, login round-trip, audit
  user, Redis ticket present/removed, revocation, restart-survival).
- §2 intact: `User` in Infrastructure with persistence attributes; Domain untouched; ports in Application;
  `ItemService` unchanged.
- No secrets committed; bootstrap admin from config/env.
- `IPermissionService` still `AllowAll` (RBAC is 6b); content reads anonymous; `user`/identity endpoints
  and all mutations require auth.
