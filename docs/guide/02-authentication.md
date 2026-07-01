# Authentication (Phase 6a)

StruoCMS authenticates callers two ways and stamps the real user on audit fields. Authorization
(per-collection roles / RBAC) is **Phase 6b** — in 6a every authenticated user has full access.

## Setup

Real secrets live in `appsettings.Development.json` (gitignored) / environment variables — never in
the committed `appsettings.json` (which holds empty placeholders).

```jsonc
// appsettings.Development.json
"Database": { "DbType": "PostgreSQL", "ConnectionString": "Host=...;Database=...;Username=...;Password=..." },
"Redis":    { "ConnectionString": "localhost:6379" },
"Auth":     { "BootstrapAdmin": { "Email": "admin@example.com", "Password": "<strong-password>" } }
```

- **Redis** backs the server-side session store. If `Redis:ConnectionString` is empty, the app falls
  back to an in-memory distributed cache (fine for a single dev process; sessions are not shared or
  durable). `/health/ready` reports the cache (and DB) health.
- **Bootstrap admin**: in Development, on startup, if the `users` table is empty, one admin is seeded
  from `Auth:BootstrapAdmin`. Leave the placeholder empty to skip seeding.

## Sessions (cookie)

`POST /api/auth/login` `{ "email", "password" }` → `200` + an HttpOnly session cookie. The session
ticket is stored server-side in Redis (revocable), so:

- `POST /api/auth/logout` removes the ticket immediately (the cookie can't be replayed).
- Sessions survive an app restart (the ticket lives in Redis, not process memory).
- `GET /api/auth/me` → the current user, or `401` when unauthenticated.

The cookie is `HttpOnly`, `SameSite=Lax`, and `Secure` in Production (relaxed to `SameAsRequest` in
dev/test so it works over HTTP).

## Programmatic access (bearer token)

For non-browser callers, each user can hold one permanent access token:

- `POST /api/users/{id}/access-token` → `{ "token": "<plaintext>" }` — **shown once**; only its
  SHA-256 hash is stored.
- Send it as `Authorization: Bearer <token>` on any request.
- `DELETE /api/users/{id}/access-token` revokes it (nulls the stored hash).

## User provisioning

- `POST /api/users` `{ "email", "password", "name?" }` → `201`. Rejects duplicate email (`409`) and
  passwords shorter than 8 chars (`400`). The password hash is never returned.
- `PUT /api/users/{id}/password` `{ "newPassword" }` → `204`.
- Read/list users via the generic engine (`GET /api/items/user`) — `password` and `accessToken` are
  hidden fields and never projected.

Passwords are hashed with **Argon2id** (PHC-encoded; salt + parameters embedded).

## Enforcement model (6a)

| Surface | 6a rule |
|---|---|
| Writes / deletes (any collection), user provisioning, token management | Require authentication (cookie **or** bearer) |
| Reads of the `user` collection | Require authentication |
| Reads of content collections (e.g. `article`) | **Anonymous allowed** |

Per-collection authorization — including marking some collections publicly readable and others
role-gated — arrives in **Phase 6b** (collection-based RBAC). Until then `IPermissionService` grants
all authenticated callers full access, and `PUT /api/users/{id}/password` does not yet verify
`currentPassword` or ownership (also 6b).

## Single Sign-On (OIDC)

StruoCMS can additionally authenticate against an external OIDC identity provider — primary target
**Microsoft Entra ID (Microsoft 365)** — and issues the **same** cookie session used by password
login. Both mechanisms coexist; SSO does not replace local email+password login.

### Setup

```jsonc
// appsettings.Development.json
"Oidc": {
  "Enabled": true,
  "Authority": "https://login.microsoftonline.com/{tenant-id}/v2.0",
  "ClientId": "<app-registration-client-id>",
  // ClientSecret comes from user-secrets / environment — never committed
  "CallbackPath": "/signin-oidc",
  "Scopes": [ "openid", "email", "profile" ],
  "ReturnUrlDefault": "/",
  "RequireEmailVerified": false,
  "AllowedTenantId": null,
  "AllowedEmailDomains": []
}
```

```bash
dotnet user-secrets set "Oidc:ClientSecret" "<client-secret>" --project src/Struo.Api
```

- `Enabled` defaults to `false`. The OIDC authentication scheme is only registered when
  `Oidc:Enabled=true` **and** `Authority` is set — otherwise the block is skipped entirely and
  `/api/auth/login/oidc` returns `404`, with password login unaffected.
- `ClientSecret` must come from user-secrets (dev) or an environment variable (Production) —
  never from a committed `appsettings*.json`. The tracked `appsettings.json` ships with `Oidc`
  disabled and no secret.
- `Scopes`, `ReturnUrlDefault`, `RequireEmailVerified`, `AllowedTenantId`, and
  `AllowedEmailDomains` all have safe defaults; only `Authority` and `ClientId` (+ the secret) are
  required to turn SSO on.

### Email-trust policy (why Entra needs no extra flag)

The trust anchor is the **issuer/tenant**, not the `email_verified` claim:

- Point `Authority` at a **single-tenant** authority
  (`https://login.microsoftonline.com/{tenant-id}/v2.0`). The OIDC handler's own issuer validation
  then already restricts logins to your organization — a token proven to come from that tenant
  makes its `email`/UPN trustworthy, because tenant emails are org-assigned, not self-set.
- Entra ID workforce (M365) tokens generally **omit** `email_verified`. `RequireEmailVerified`
  therefore defaults to **false** — requiring it would reject legitimate M365 users. Set
  `RequireEmailVerified=true` only for providers that reliably emit the claim (e.g. Google).
- Optional hardening, both off by default:
  - `AllowedTenantId` — if set, the token's `tid` claim must match, guarding against a
    multi-tenant app-registration misconfiguration.
  - `AllowedEmailDomains` — if set, the resolved email's domain must be in the list.
- Email is read from the `email` claim, falling back to `preferred_username` (UPN) when absent.
  Missing both → login rejected.

### Login flow

`GET /api/auth/login/oidc?returnUrl=<site-relative-path>` (`[AllowAnonymous]`) challenges the
configured IdP. `returnUrl` must be a site-relative path — an absolute/off-site URL is rejected in
favor of `ReturnUrlDefault` (open-redirect guard). After the user authenticates at the IdP and the
handler validates the callback (state/nonce/PKCE/JWKS — all handled by the standard ASP.NET OIDC
handler), StruoCMS resolves-or-provisions a local user and signs in with the **same Cookie scheme**
as `POST /api/auth/login`: the resulting session is indistinguishable downstream (Redis ticket,
`ICurrentUserAccessor`, audit stamping, RBAC) from a password login.

### JIT (just-in-time) provisioning

On first successful SSO login with an email that has no matching local user, StruoCMS
auto-creates one:

- `IsActive = true`.
- **No role** — the user has only the `public` role's permissions until an admin assigns one via
  the `user_roles` collection.
- **No usable password** — the stored password hash is empty, so password login for this user
  always fails (no accidental credential-based access to an SSO-only account).

An existing user is matched by email on every subsequent login (no new row is created); if that
user is `IsActive = false`, SSO login is rejected the same way password login would be.

### Account linking: email-only, and its trade-off

Matching is **by email only** — there is no separate provider/subject binding table. This is a
deliberate YAGNI choice for a single trusted enterprise IdP with org-controlled emails:

- **Trade-off:** if a user's email changes at the IdP, the next SSO login won't match the old
  local row and will JIT-provision a *new* local user — an admin can recover by re-pointing or
  deactivating the stale account.
- **Recorded migration trigger:** introduce a `UserExternalLogin` table (provider + subject,
  matched by `sub` instead of email) when a **second** identity provider is added, or if email
  changes become a recurring operational problem. Not needed for the current single-IdP scope.
