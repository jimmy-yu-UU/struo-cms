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
