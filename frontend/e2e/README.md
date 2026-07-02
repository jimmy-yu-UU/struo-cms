# E2E tests (Playwright)

This suite proves the auth walking skeleton end-to-end: an unauthenticated visit
redirects to `/login`, a valid login lands on the dashboard, and logout returns
to `/login`.

## Same-origin design

The SPA talks to the API through Vite's dev proxy (`/api` -> `http://localhost:5080`,
see `frontend/vite.config.ts`). Playwright drives the browser against
`http://localhost:5173` (Vite), so the browser sees the API as same-origin. This
matches the cookie's `SameSite=Lax` setting — no CORS configuration is needed for
this test. The true cross-origin/HTTPS deployment flow is exercised separately as
a manual smoke test, not by this spec.

## Prerequisites

1. **Start the API** in Development mode with a seeded admin user. The API's
   Development startup runs `InitTables` and seeds a bootstrap admin from
   `Auth:BootstrapAdmin:Email` / `Auth:BootstrapAdmin:Password`. Example (from
   the repo root), using SQLite and no Redis (falls back to in-memory cache):

   ```bash
   ASPNETCORE_ENVIRONMENT=Development \
   ASPNETCORE_URLS=http://localhost:5080 \
   Database__DbType=Sqlite \
   "Database__ConnectionString=Data Source=/path/to/e2e-test.db" \
   Auth__BootstrapAdmin__Email=admin@struo.local \
   Auth__BootstrapAdmin__Password=change-me-please \
   dotnet run --project src/Struo.Api
   ```

   Wait until `http://localhost:5080/health/ready` returns 200 before running
   the tests.

2. Playwright itself starts the Vite dev server for you (see
   `frontend/playwright.config.ts`'s `webServer` block, `reuseExistingServer: true`),
   so you don't need to run `pnpm dev` separately — though it's fine if it's
   already running.

## Running

From `frontend/`:

```bash
pnpm e2e
```

## Credentials

The spec reads credentials from environment variables, falling back to the
bootstrap admin defaults used above:

- `E2E_EMAIL` (default `admin@struo.local`)
- `E2E_PASSWORD` (default `change-me-please`)

Override them if your seeded admin uses different credentials:

```bash
E2E_EMAIL=someone@example.com E2E_PASSWORD=secret pnpm e2e
```

## Artifacts

Playwright writes `test-results/` and `playwright-report/` on failures/traces;
these are gitignored and should not be committed.

## Phase 7b browse E2E (collections.spec.ts)

Requires the same seeded admin as the auth E2E, plus at least one `article` row so the table is
non-empty (the header assertion passes even when empty). Seed one via the authenticated API after
login, e.g. from a REST client or `curl` against the dev API:

    POST /api/items/article   { "status": "published" }

(super-admin session cookie required). The sample blog collection `article` is served because
`Struo:ContentAssemblies` includes `Struo.Sample.Blog` in the dev configuration.
