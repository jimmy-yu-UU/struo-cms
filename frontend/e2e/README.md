# E2E tests (Playwright)

Two suites, configured as separate Playwright projects (`playwright.config.ts`):

- **`core`** (`pnpm e2e`) — framework-only specs under `e2e/` (excluding `e2e/sample/**`): auth, media,
  media folders, media soft-delete, settings. No content collections required; green on a default
  checkout.
- **`sample`** (`pnpm e2e:sample`) — specs under `e2e/sample/`: collections, items CRUD, relations,
  revisions and related flows. Requires the Blog sample opted into `Struo:ContentAssemblies` first —
  see [chapter 23](../../docs/guide/en/23-sample-walkthrough.md). The config adds this project only
  while `e2e/sample/` exists.
- **`pnpm e2e:all`** runs both projects.

## Prerequisites

Both suites need, at minimum:

- The API running and reachable at the base URL the specs call (default
  `http://localhost:5221`, the `http` launch profile's `applicationUrl`; override with `E2E_API` — this
  is separate from the Vite dev-proxy target in `vite.proxy.ts`, which is not environment-driven).
- A reachable, migrated database with the seeded bootstrap admin (`Auth:BootstrapAdmin:Email` /
  `Auth:BootstrapAdmin:Password`). The API refuses to start on an unmigrated database: run
  `dotnet run --project src/Struo.Api -- migrate` once, or set `Database:MigrateOnStartup=true` in local
  configuration. A local database built by v0.8.x CodeFirst needs `migrate:baseline` once instead.
  `E2E_PASSWORD` is required: the template ships no bootstrap password, so the specs fail at load
  without it. Set it to the seeded admin's password (`Auth__BootstrapAdmin__Password`).
  `E2E_EMAIL` defaults to the shipped `admin@admin.com`.
- No login-rate-limit override needed for a default checkout. `RateLimiting:Login` (the per-client-IP
  limiter, which counts every attempt — success or failure — against its window) ships **disabled** by
  default, and `RateLimiting:LoginAccount` (the per-account throttle, on by default) only counts
  *failed* attempts, so the suites' repeated *successful* logins across specs never trip either one. A
  fork that has turned `RateLimiting:Login:Enabled` back on for its own deployment needs
  `RateLimiting__Login__Enabled=false` for the test run instead, for the reason above.
- The Vite dev server — Playwright starts this for you (`playwright.config.ts`'s `webServer` block,
  `reuseExistingServer: true`), so `pnpm dev` does not need to be running separately, though it's fine
  if it already is.
- `pnpm e2e:sample` additionally needs the Blog sample opted in (chapter 23) and whatever seed data
  each sample spec documents inline. **`relations.spec.ts` needs one pre-existing `category` row and
  one `tag` row** — the sample ships no seed data for either, and a freshly opted-in / freshly
  migrated database has none, so that spec alone fails otherwise (its own header comment has the
  exact `POST` calls to create them). Specs that create data (e.g. `items.spec.ts`) stamp their
  records with a unique value (`E2E_STAMP`, default `e2e`) so a run is self-cleaning and safe to
  repeat or run in parallel against a shared database — pass a unique stamp explicitly when in doubt:

  ```bash
  E2E_STAMP=$(date +%s) pnpm e2e:sample
  ```

## Running

From `frontend/`, with the API and database already up (see
[chapter 3](../../docs/guide/en/03-getting-started.md)'s quick start for the standard way to bring
those up):

```bash
pnpm e2e
```

Pass the credentials:

```bash
E2E_EMAIL=someone@example.com E2E_PASSWORD=your-admin-password pnpm e2e
```

## Artifacts

Playwright writes `test-results/` and `playwright-report/` on failures/traces; these are gitignored
and should not be committed.

## Further reading

[Chapter 22](../../docs/guide/en/22-testing.md) covers all four test layers (backend, admin SPA
unit, E2E, and the schema contract) and what CI runs instead.
[Chapter 23](../../docs/guide/en/23-sample-walkthrough.md) covers opting the Blog sample in and
out, including the corresponding cleanup of this directory's `sample/` specs if the sample is
removed from a fork.
