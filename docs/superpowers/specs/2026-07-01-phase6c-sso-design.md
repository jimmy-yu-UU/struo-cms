# StruoCMS — Phase 6c (SSO / external OIDC) Design

> Date: 2026-07-01
> Source of truth: the StruoCMS master spec (§0–§18) + Phase 6a/6b specs.
> Depends on Phase 6a (auth core — cookie session, Redis ticket store, `ICurrentUserAccessor`)
> and 6b (RBAC — `Role`/`Permission`/`UserRole`, `PermissionResolver`, per-request snapshot), both merged.
> Each phase runs its own brainstorm → write-plan (TDD) → execute → verify cycle (§17.1).

## 1. Goal & scope

Layer **external OIDC login** onto the existing 6a/6b auth pipeline: a user authenticates at the
organization's IdP (primary target: **Microsoft 365 / Entra ID**), and on success the system
resolves-or-provisions a local `User` and issues the **same** cookie session used by password login.
Everything downstream (Redis ticket, `ICurrentUserAccessor`, audit AOP, RBAC) is reused unchanged —
SSO's only job is *external handshake + local user resolution*.

**In scope**
- Generic OIDC integration via `Microsoft.AspNetCore.Authentication.OpenIdConnect` — discovery-driven
  (`.well-known/openid-configuration`), single configured authority, config-bound.
- Two endpoints: `GET /api/auth/login/oidc?returnUrl=` (challenge) and the OIDC callback handled inside
  the handler's `OnTokenValidated` event.
- `ExternalLoginService` (Application): given a normalized external identity, resolve-or-JIT-provision a
  local `User` and return the local `userId`.
- **JIT provisioning**: match a local user by email; if none, create an `IsActive=true` user with **no
  role** and **no usable password** (empty PHC hash → password login always fails for it).
- **Coexistence** with local email+password login (unchanged); SSO success uses the existing
  `SignInAsync(Cookie)`.
- **RBAC resolution change (Option B)**: `SqlSugarRolePermissionStore.LoadForUserAsync` returns the
  `public` role's permissions for an **authenticated user who has no `UserRole`**. Assigned users get
  only their roles; anonymous callers keep `public` as today.
- **Email-trust policy** anchored on the trusted issuer/tenant (see §6), with `email_verified` as an
  optional hardening for providers that emit it. OIDC scheme not registered when unconfigured (mirrors
  the Redis fallback), so existing flows are unaffected.
- TDD coverage + a live verification gate (real PG + Redis + a real OIDC provider).

**Out of scope (deferred)**
- Multiple simultaneous IdPs; a `UserExternalLogin` table; provider+`sub` binding. Introduced only when
  a second IdP is added or email changes become a real pain point (see §3).
- IdP claims/groups → local role mapping (future extension).
- Front-end login UI (Phase 7); SPA-driven PKCE/token flow.
- Single-logout back to the IdP; token refresh (the cookie session already governs the local session).
- Self-service registration, email-verification mail flows, password reset.

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Flow mechanism | ASP.NET built-in OIDC handler, **authorization code flow + PKCE**; `OnTokenValidated` provisions, then signs in with the existing **Cookie** scheme (`NameIdentifier = local userId`). Chosen over SPA-driven PKCE and hand-rolled OIDC: reuses the whole existing pipeline, uses Microsoft-maintained crypto (state/nonce/PKCE/JWKS), fits the same-origin cookie session. |
| Provider | Generic OIDC, single authority via config; primary target **Entra ID**. Scheme not registered when unconfigured. |
| Account linking | **Email-only** match; **no extra table or columns**. Later logins re-match by email. |
| JIT provisioning | Create `IsActive` user, **no role**, empty password (never password-loginable); effective permission = `public` floor. |
| Email trust | Anchored on **trusted issuer/tenant** (single-tenant authority + optional `tid` check), not on `email_verified` (Entra workforce tokens generally omit it). `RequireEmailVerified` is an **opt-in** hardening for providers like Google. |
| `public` resolution | **Option B**: no role → `public`; has role(s) → only those roles (change `LoadForUserAsync`). |
| Local password login | Coexists. |
| Service placement | `ExternalLoginService` + ports in **Application** (pure); OIDC wiring/callback in **Api** (web-coupled). Infrastructure stays web-free. |

## 3. Why email-only, no subject binding (recorded rationale)

The OIDC spec warns that `email` is not a stable identifier — `sub` is. Storing provider+`sub` guards
against (a) email change at the IdP creating a duplicate JIT account, (b) email reuse after an employee
leaves, (c) cross-provider takeover once a second IdP is added. In **this** context — a single trusted
enterprise IdP, org-controlled emails, and multi-provider explicitly out of scope — (c) cannot happen,
(b) is rare, and (a) is infrequent and admin-recoverable. Email-only matching is the YAGNI choice.

**Migration trigger (so it isn't lost):** when a second IdP is added, or email changes become a real
pain point, introduce a `UserExternalLogin` collection (`UserId`/`Provider`/`Subject`, unique on
`(Provider, Subject)`) and match by `sub`, updating email on each login. Not now.

## 4. Components

### Domain
- No new types (SSO is an Application/Infrastructure concern, consistent with 6a).

### Application (ports + pure orchestration; depends only on Domain)
- `record ExternalIdentity(string Issuer, string? TenantId, string? Email, bool? EmailVerified, string? Name)`
  — normalized IdP claims after extraction.
- `IExternalUserStore` (new port; deliberately bypasses the generic projection, like `IUserCredentialStore`):
  - `Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken)`
  - `Task<Guid> CreateExternalUserAsync(string email, string? name, CancellationToken)` — creates an
    `IsActive=true` user with `Password = ""` (empty; `Argon2idPasswordHasher.Verify` returns false for
    an empty encoded hash, so password login can never succeed), no `AccessToken`, no role.
- `IExternalLoginService` / `ExternalLoginService`:
  - `Task<ExternalLoginResult> ResolveOrProvisionAsync(ExternalIdentity id, ExternalLoginPolicy policy, CancellationToken)`
  - `record ExternalLoginResult(bool Succeeded, Guid? UserId, bool Provisioned, ExternalLoginFailure? Failure)`
    with `enum ExternalLoginFailure { NoEmail, EmailNotVerified, DomainNotAllowed, TenantNotAllowed, Inactive }`.
  - Logic (pure): apply the email-trust policy (§6) → resolve email → `FindUserIdByEmailAsync` (hit →
    `Ok(userId, provisioned:false)`; also reject if the found user is inactive) → miss →
    `CreateExternalUserAsync` → `Ok(userId, provisioned:true)`. No HttpContext.
- `OidcOptions` (config-bound): `Enabled`, `Authority`, `ClientId`, `ClientSecret`, `CallbackPath`,
  `Scopes[]` (default `openid`,`email`,`profile`), `ReturnUrlDefault`, `RequireEmailVerified` (default
  **false**), `AllowedTenantId` (optional), `AllowedEmailDomains[]` (optional). The trust-relevant
  subset is projected into an `ExternalLoginPolicy` passed to the service.

### Infrastructure (implementations; no web coupling)
- `SqlSugarExternalUserStore : IExternalUserStore` — email lookup case-insensitive via `ToLower()`
  (mirrors `SqlSugarUserCredentialStore`); create uses `Guid.CreateVersion7()`.
- `SqlSugarRolePermissionStore.LoadForUserAsync` **modified (Option B)**: for an authenticated user,
  load their `UserRole` roles; **if that set is empty, load the `public` role instead**. Anonymous
  (`userId is null`) still loads `public`. Net rule: *has role → roles; no role → public*.

### Api (composition root + endpoints; web-coupled auth lives here)
- `OidcWiring` (new; invoked from the Api composition root next to `AddStruoAuth`) — when `Oidc:Enabled`
  and `Authority` is set, `AddOpenIdConnect(AuthSchemes.Oidc, …)`: `Authority`, `ClientId`,
  `ClientSecret`, `CallbackPath`, `ResponseType = code`, `UsePkce = true`, `SaveTokens = false`, add
  scopes. `SignInScheme` is not the default external cookie — the cookie is issued explicitly in the
  event. When unconfigured, the whole block is skipped (handler not registered).
- `AuthSchemes.Oidc` constant added.
- `AuthController` additions:
  - `[AllowAnonymous] GET /api/auth/login/oidc?returnUrl=` → validate `returnUrl` is a **site-relative
    path** (else fall back to `ReturnUrlDefault`; prevents open redirect) → `Challenge(Oidc, redirectUri)`.
    If OIDC is disabled → 404.
  - The callback is handled inside the OIDC handler events (below), not as a separate action.
- OIDC event wiring (in `OidcWiring`):
  - `OnTokenValidated`: extract `email`, `email_verified`, `preferred_username`, `name`, `iss`, `tid`
    from `ctx.Principal` (see §6 for the extraction/normalization rules) → build `ExternalIdentity` →
    `IExternalLoginService.ResolveOrProvisionAsync`. On failure → `ctx.Fail(...)` (→ 401 JSON). On
    success → build a fresh `ClaimsIdentity(AuthSchemes.Cookie)` with `NameIdentifier = local userId`
    → `ctx.HttpContext.SignInAsync(Cookie, principal)`, and clear the handler's own external principal
    so there is no double sign-in.
  - `OnRemoteFailure` (user cancels / IdP-side error) → write **401/400 JSON**, not a 302 to HTML
    (consistent with the existing cookie `OnRedirectToLogin` override).

## 5. Data flow (SSO login)

1. Front end navigates to `GET /api/auth/login/oidc?returnUrl=/admin` → `Challenge` → 302 to the IdP
   authorize endpoint (handler-generated `state`, PKCE `code_challenge`, `nonce`).
2. User authenticates at the IdP → 302 back to `CallbackPath` (e.g. `/signin-oidc`) with an auth code.
3. The OIDC handler exchanges code+PKCE at the token endpoint, validates the id_token signature (JWKS),
   `nonce`, and `state`, and builds the external `ClaimsPrincipal`.
4. `OnTokenValidated`: extract `ExternalIdentity` → `ResolveOrProvisionAsync`:
   - Email-trust policy fails (see §6) → `ctx.Fail` → 401 JSON, **no provisioning**.
   - Email matches a local active user → take its `userId`.
   - No match → JIT-create a role-less, password-less user → take the new `userId`.
5. Build a Cookie identity with `NameIdentifier = userId` → `SignInAsync(Cookie)` → ticket written to
   Redis via the **existing** `DistributedCacheTicketStore` (6a).
6. Handler redirects to the validated `returnUrl`.
7. Every subsequent request: cookie → cookie middleware → Redis ticket → `HttpContext.User` →
   `HttpContextCurrentUserAccessor` → `PermissionResolutionMiddleware` resolves permissions (role-less
   users receive `public`) → audit AOP stamps the real user. **Identical downstream to password login.**

## 6. Email-trust policy, extraction & normalization

**The trust anchor is the issuer/tenant, not `email_verified`.** Microsoft Entra ID (workforce / M365)
tokens generally **do not emit `email_verified`**; requiring it would reject every legitimate M365 user.
Within a single trusted tenant, emails/UPNs are org-assigned and cannot be self-set to someone else's,
so a token proven to originate from that tenant makes its email trustworthy.

- **Issuer restriction (primary):** `Authority` points at the specific tenant
  (`https://login.microsoftonline.com/{tenantId}/v2.0`) with a **single-tenant** app registration, so
  the handler's `iss` validation already restricts to your organization.
- **Optional `tid` check:** if `AllowedTenantId` is set, the `tid` claim must equal it (guards against a
  multi-tenant misconfiguration). Mismatch → `TenantNotAllowed` → 401.
- **Optional domain allow-list:** if `AllowedEmailDomains` is set, the email's domain must be in it →
  else `DomainNotAllowed` → 401.
- **`email_verified` as opt-in hardening:** if `RequireEmailVerified = true`, the claim must be present
  and true (else `EmailNotVerified` → 401). Default is **false** for Entra; set true for providers that
  reliably emit it (e.g. Google).
- **Email extraction order:** `email` claim → fall back to `preferred_username` (UPN). Both absent →
  `NoEmail` → 401.
- **`email_verified` normalization:** tolerate both JSON boolean `true` and string `"true"` (providers
  differ), normalized to a `bool`, to avoid false negatives.

## 7. Error handling & edge cases

| Situation | Handling |
|---|---|
| `RequireEmailVerified=true` and claim false/absent | `EmailNotVerified` → **401 JSON**, never JIT. |
| No email and no `preferred_username` | `NoEmail` → 401 (generic message; no detail leak). |
| `tid` ≠ `AllowedTenantId` (when configured) | `TenantNotAllowed` → 401. |
| Email domain not in `AllowedEmailDomains` (when configured) | `DomainNotAllowed` → 401. |
| Matched local user is `IsActive=false` | `Inactive` → 401 (mirrors password-login inactive semantics). |
| User cancels at IdP / IdP-side error | `OnRemoteFailure` → 401/400 JSON, not 302 HTML. |
| `returnUrl` is an absolute/off-site URL | Accept only site-relative paths; else fall back to `ReturnUrlDefault` (open-redirect guard). |
| OIDC unconfigured (`Enabled=false` or no authority) | Scheme not registered; `/api/auth/login/oidc` → 404. Password login unaffected. |
| A JIT user attempts password login | `Password = ""` → `Verify` returns false → always 401 (no empty-password bypass). |
| state / nonce / PKCE / signature | Handled entirely by the ASP.NET OIDC handler (CSRF / replay / code-interception / forgery). |
| client secret | Read only from config/env; never hardcoded or committed. |

## 8. Testing (TDD — RED first)

**Unit**
1. `ExternalLoginService`: `RequireEmailVerified=true` + verified false/absent → `EmailNotVerified`;
   no email/UPN → `NoEmail`; `tid` mismatch → `TenantNotAllowed`; domain not allowed → `DomainNotAllowed`;
   email hit → `Ok(provisioned:false)` with the right userId; hit but inactive → `Inactive`; miss →
   `Ok(provisioned:true)` and `CreateExternalUserAsync` called. (fake `IExternalUserStore`.)
2. Email extraction/normalization: falls back to `preferred_username` when `email` absent;
   `email_verified` as boolean `true` and string `"true"` both normalize to true.
3. RBAC resolution (Option B): authenticated + no role → `public` permissions; authenticated + role →
   only that role (**no** `public`); anonymous → `public`.

**Integration** (`WebApplicationFactory` + SQLite + in-memory cache; OIDC exercised via a fake handler
or by injecting a stub `IExternalLoginService`/principal — no real IdP call)
4. JIT: first SSO (email absent locally) → creates a role-less, `IsActive=true`, non-password-loginable user.
5. Existing user SSO: email hit → same `userId`, no duplicate row.
6. SSO success → cookie session issued; `/api/auth/me` returns the correct id; audit `CreatedBy/UpdatedBy`
   is that user.
7. `RequireEmailVerified=true` + unverified → 401 and **no** user row created (DB scan confirms).
8. Role-less SSO user: read on a `public` collection → 200; write elsewhere → 403; after admin assigns a
   role → behavior follows the role.
9. OIDC disabled → `/api/auth/login/oidc` → 404; password login still works.
10. `returnUrl` off-site → falls back to default; no redirect to the external site.

**Live verification gate** (memory: *SQLite green ≠ Postgres correct*; real PG + Redis + a real OIDC
provider — Entra ID, or Keycloak/Google for local dev)
11. Perform one real OIDC login: PG `users` shows the JIT row; Redis holds the session ticket; `/me` is
    correct; audit stamps the real user; role-less → `public` read allowed, write denied; after admin
    assigns a role, behavior changes. Capture evidence.

## 9. Migration

- `dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect` (latest; centralized in
  `Directory.Packages.props`).
- **No schema change** — no new table or column. JIT is an ordinary insert into the existing `users` table.
- New `Oidc` config section (`Enabled`/`Authority`/`ClientId`/`ClientSecret`/`CallbackPath`/`Scopes`/
  `ReturnUrlDefault`/`RequireEmailVerified`/`AllowedTenantId`/`AllowedEmailDomains`); `ClientSecret` via
  env / user-secrets, never committed.
- The `LoadForUserAsync` Option B change is behavior-only (no schema); covered by test 3.

## 10. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Requiring `email_verified` would reject all M365 users | Trust anchored on issuer/tenant; `email_verified` is opt-in (`RequireEmailVerified=false` for Entra). §6, test 1/7. |
| Untrusted tenant/email in a multi-tenant misconfig | Single-tenant authority (issuer validation) + optional `tid` check + optional domain allow-list. |
| Email change at IdP → duplicate JIT account | Known, accepted trade-off (single trusted IdP, rare); admin merges; documented migration trigger (§3). |
| Open redirect via `returnUrl` | Site-relative paths only; else default. Test 10. |
| OIDC misconfig breaking existing login | Scheme not registered when unconfigured; password path independent. Test 9. |
| Option B changes 6b behavior | Explicit three-case test (test 3); it is the "authenticated ≥ anonymous" relaxation, and `public` only grants already-public reads → no extra exposure. |
| client secret leak | Config/env only; never committed; checked in acceptance. |
| JIT empty-password account being loginable by password | Empty PHC hash → `Verify` always false; test 4. |

## 11. Acceptance (verification gate, with evidence)

- `dotnet build` clean (warnings-as-errors); all unit/integration tests green (SQLite + in-memory cache).
- Live PG + Redis + a real OIDC login (test 11) passes with captured evidence: JIT row, Redis ticket,
  `/me`, audit user, public-floor behavior, and post-role-assignment change.
- §2 dependency rule intact: `ExternalLoginService` + ports in Application (pure); OIDC wiring/callback in
  Api; Infrastructure stays web-free. Domain untouched.
- Password login and SSO coexist and both work; JIT users cannot password-login.
- No secret committed; OIDC config from env/user-secrets.
