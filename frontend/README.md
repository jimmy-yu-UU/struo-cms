# StruoCMS Frontend (Vue 3 SPA)

A standalone Vue 3 single-page app that authenticates against the StruoCMS
.NET API (`Struo.Api`). Built with Vite, Pinia, and Vue Router. This is Phase
7a (frontend foundation & auth) — login/dashboard shell only; content
tables/forms, TipTap, i18n, and RBAC-aware UI land in later Phase 7 work.

See the design/plan docs for the full picture:
[spec](../docs/superpowers/specs/2026-07-01-phase7a-frontend-foundation-auth-design.md) ·
[plan](../docs/superpowers/plans/2026-07-01-phase7a-frontend-foundation-auth.md).

## Prerequisites

- Node.js (v20+; developed against v24)
- pnpm 10.x (developed against 10.33.2)
- The StruoCMS API (`Struo.Api`) running somewhere reachable — see below.

## Install

```bash
pnpm install
```

## Dev server

```bash
pnpm dev
```

Starts Vite on `http://localhost:5173`. By default the SPA talks to the API
through Vite's dev proxy (`/api` → `http://localhost:5221`, configured in
`vite.config.ts`), so the browser sees the API as same-origin and no CORS
setup is required. Start the API separately on port 5221 (see "Running the
API" below).

## Unit / component tests

```bash
pnpm test
```

Runs the Vitest suite (component + store + client tests, jsdom environment).
Note: `pnpm test` (`vitest run`) and `pnpm e2e` (`playwright test`) are
separate suites — only run `pnpm e2e` for the Playwright spec under `e2e/`.

## Build

```bash
pnpm build
```

Type-checks (`vue-tsc -b`) and produces a production bundle in `dist/`.

## E2E tests (Playwright)

```bash
pnpm e2e
```

Exercises the full login → dashboard → logout flow in a real browser against
a running API. Playwright starts the Vite dev server for you
(`playwright.config.ts`'s `webServer` block); you still need to start the API
yourself first. See `e2e/README.md` for full detail — summary:

1. Start the API in Development mode with a seeded bootstrap admin (SQLite,
   no Redis needed — falls back to in-memory cache):

   ```bash
   ASPNETCORE_ENVIRONMENT=Development \
   ASPNETCORE_URLS=http://localhost:5221 \
   Database__DbType=Sqlite \
   "Database__ConnectionString=Data Source=/path/to/e2e-test.db" \
   Auth__BootstrapAdmin__Email=admin@admin.com \
   Auth__BootstrapAdmin__Password=admin \
   dotnet run --project src/Struo.Api --no-launch-profile
   ```

   Wait for `http://localhost:5221/health/ready` to return 200.

2. From `frontend/`, run `pnpm e2e`. Override credentials with `E2E_EMAIL` /
   `E2E_PASSWORD` env vars if your seeded admin differs from the defaults
   above.

## Configuration: `VITE_API_BASE_URL`

The API client (`src/api/apiClient.ts`) reads `VITE_API_BASE_URL` at build
time and falls back to `/api` when unset. Copy `.env.example` to `.env` to
override it. There are two supported modes:

### 1. Same-origin dev via Vite proxy (default)

Leave `VITE_API_BASE_URL` unset. The browser always talks to the Vite origin
(`http://localhost:5173`); Vite proxies `/api/*` to the API on
`http://localhost:5221`. The session cookie is `SameSite=Lax` and works
without any backend CORS configuration. This is what `pnpm dev` and `pnpm e2e`
use out of the box.

### 2. True cross-origin (SPA and API on different origins)

Set `VITE_API_BASE_URL` to the API's full origin (e.g.
`https://api.example.com`) and configure the backend to allow the SPA's
origin via `Struo:Cors:AllowedOrigins` (e.g.
`Struo__Cors__AllowedOrigins__0=https://app.example.com`). Configuring any
allowed origin flips the session cookie from `SameSite=Lax` to
`SameSite=None; Secure` (see `src/Struo.Api/Auth/AuthWiring.cs` and
`CorsWiring.cs`), which browsers only honor over HTTPS — so **both** the SPA
and the API must be served over HTTPS in this mode. This mode is required
before a real cross-origin deployment (or the manual cross-origin smoke
described in the spec, §9) will work; it will not work over plain HTTP.

## Project layout

- `src/api/apiClient.ts` — thin fetch wrapper (credentials included, 401
  hook, response envelope unwrapping).
- `src/stores/` — Pinia stores (`authStore`: session state, login/logout,
  `fetchCurrentUser`).
- `src/router/` — Vue Router routes + auth guard (redirects unauthenticated
  users to `/login`).
- `src/views/` — `LoginView`, `DashboardView`.
- `e2e/` — Playwright spec + its own `README.md` with full E2E setup detail.
