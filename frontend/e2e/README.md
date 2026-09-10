# E2E tests (Playwright)

Two suites, configured as separate Playwright projects (`playwright.config.ts`):

- **`core`** (`pnpm e2e`) — framework-only specs under `e2e/` (excluding `e2e/sample/**`): auth, media,
  media folders, media soft-delete, settings. No content collections required; green on a default
  checkout.
- **`sample`** (`pnpm e2e:sample`) — specs under `e2e/sample/`: collections, items CRUD, relations,
  revisions and related flows. Requires the Blog sample opted into `Struo:ContentAssemblies` first —
  see [chapter 16](../../docs/guide/en/16-sample-walkthrough.md).
- **`pnpm e2e:all`** runs both projects.

## Prerequisites

Both suites need, at minimum:

- The API running and reachable at the base URL the specs call (default
  `http://localhost:5221`, the `http` launch profile's `applicationUrl`; override with `E2E_API` — this
  is separate from the Vite dev-proxy target in `vite.config.ts`, which is not environment-driven).
- A reachable database with the seeded bootstrap admin (`Auth:BootstrapAdmin:Email` /
  `Auth:BootstrapAdmin:Password`). Override the credentials the specs log in with via `E2E_EMAIL` /
  `E2E_PASSWORD` if your seeded admin differs from the shipped default.
- No login-rate-limit override needed for a default checkout. `RateLimiting:Login` (the per-client-IP
  limiter, which counts every attempt — success or failure — against its window) ships **disabled** by
  default, and `RateLimiting:LoginAccount` (the per-account throttle, on by default) only counts
  *failed* attempts, so the suites' repeated *successful* logins across specs never trip either one. A
  fork that has turned `RateLimiting:Login:Enabled` back on for its own deployment needs
  `RateLimiting__Login__Enabled=false` for the test run instead, for the reason above.
- The Vite dev server — Playwright starts this for you (`playwright.config.ts`'s `webServer` block,
  `reuseExistingServer: true`), so `pnpm dev` does not need to be running separately, though it's fine
  if it already is.
- `pnpm e2e:sample` additionally needs the Blog sample opted in (chapter 16) and whatever seed data
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

Override credentials if needed:

```bash
E2E_EMAIL=someone@example.com E2E_PASSWORD=secret pnpm e2e
```

## Artifacts

Playwright writes `test-results/` and `playwright-report/` on failures/traces; these are gitignored
and should not be committed.

## Further reading

[Chapter 15](../../docs/guide/en/15-deployment-operations-testing.md) covers all three test layers
(backend, frontend unit, E2E) and what CI runs instead. [Chapter 16](../../docs/guide/en/16-sample-walkthrough.md)
covers opting the Blog sample in and out, including the corresponding cleanup of this directory's
`sample/` specs if the sample is removed from a fork.
