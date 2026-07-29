# E2E tests (Playwright)

This suite proves the auth walking skeleton end-to-end: an unauthenticated visit
redirects to `/login`, a valid login lands on the dashboard, and logout returns
to `/login`.

## Suites

- `pnpm e2e` runs the **core** project (`e2e/*.spec.ts`, 5 specs) — framework
  behaviour only (auth, media, media folders, media soft-delete, settings). No
  content collections required; green on a default checkout.
- `pnpm e2e:sample` runs the **sample** project (`e2e/sample/*.spec.ts`, 8
  specs) — exercises collections/relations/revisions/etc. through the optional
  Blog sample (`Struo.Sample.Blog`), which must be opted into
  `Struo:ContentAssemblies` first (see `docs/guide/en/16-sample-walkthrough.md`).
- `pnpm e2e:all` runs both projects.

## Same-origin design

The SPA talks to the API through Vite's dev proxy (`/api` -> `http://localhost:5221`,
see `frontend/vite.config.ts`). Playwright drives the browser against
`http://localhost:5173` (Vite), so the browser sees the API as same-origin. This
matches the cookie's `SameSite=Lax` setting — no CORS configuration is needed for
this test. The true cross-origin/HTTPS deployment flow is exercised separately as
a manual smoke test, not by this spec.

## Prerequisites

A run needs, at minimum:

- The API running on `http://localhost:5221` (the `http` launch profile's
  `applicationUrl`; override the base URL the specs call directly with `E2E_API`
  — the Vite proxy target in `vite.config.ts` is separate and is not
  environment-driven).
- The Vite dev server (Playwright starts this for you; see below).
- A reachable database (SQLite is enough — see the example) with the seeded
  bootstrap admin from `Auth:BootstrapAdmin:Email` / `Auth:BootstrapAdmin:Password`.
  Override the credentials the specs log in with via `E2E_EMAIL` / `E2E_PASSWORD`
  if your seeded admin differs from the shipped default.
- **`RateLimiting__Login__Enabled=false`.** The shipped default
  (`RateLimiting:Login:Enabled=true`) is deliberately secure-by-default and is
  *not* changed for this — but the core suite alone performs 6 logins across its
  specs, the limiter counts successful logins too, and the default policy only
  permits 5 per 60 seconds, so the 6th login gets HTTP 429 unless the limiter is
  disabled for the test run.
- `pnpm e2e:sample` additionally requires the Blog sample opted into
  `Struo:ContentAssemblies` (see the Suites section above), plus the seed data
  each sample spec documents inline.

1. **Start the API** in Development mode with a seeded admin user. The API's
   Development startup runs `InitTables` and seeds a bootstrap admin from
   `Auth:BootstrapAdmin:Email` / `Auth:BootstrapAdmin:Password`. Example (from
   the repo root), using SQLite and no Redis (falls back to in-memory cache):

   ```bash
   ASPNETCORE_ENVIRONMENT=Development \
   ASPNETCORE_URLS=http://localhost:5221 \
   Database__DbType=Sqlite \
   "Database__ConnectionString=Data Source=/path/to/e2e-test.db" \
   Auth__BootstrapAdmin__Email=admin@admin.com \
   Auth__BootstrapAdmin__Password=admin \
   RateLimiting__Login__Enabled=false \
   dotnet run --project src/Struo.Api
   ```

   Wait until `http://localhost:5221/health/ready` returns 200 before running
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

- `E2E_EMAIL` (default `admin@admin.com`)
- `E2E_PASSWORD` (default `admin`)

Override them if your seeded admin uses different credentials:

```bash
E2E_EMAIL=someone@example.com E2E_PASSWORD=secret pnpm e2e
```

## Artifacts

Playwright writes `test-results/` and `playwright-report/` on failures/traces;
these are gitignored and should not be committed.

## Phase 7b browse E2E (sample/collections.spec.ts)

This spec now lives under `e2e/sample/` and runs via `pnpm e2e:sample`, not `pnpm e2e` — it
requires the `article` collection, which only exists once the Blog sample is opted into
`Struo:ContentAssemblies` (empty `[]` in the shipped configuration). Requires the same seeded admin
as the auth E2E, plus at least one `article` row so the table is non-empty (the header assertion
passes even when empty). Seed one via the authenticated API after login, e.g. from a REST client or
`curl` against the dev API:

    POST /api/items/article   { "status": "published" }

(super-admin session cookie required).

## Items CRUD E2E (sample/items.spec.ts)

Also lives under `e2e/sample/` and runs via `pnpm e2e:sample`. Exercises the full i18n-gated
create/edit/delete flow that `collections.spec.ts` (browse-only) doesn't cover: login → open the
`article` list → New → fill the shared `Status` field (a
`Select`, driven by clicking the combobox then the option, not `.fill()`) plus the default-locale
required translatable fields `Title`/`Body` (rendered under the first — default-locale — tab of
`ItemForm.vue`'s Tabs) → Save → new row visible in the list → open it → edit `Status` → Save →
open it again → Delete → confirm via the PrimeVue `ConfirmDialog` (`Yes`, PrimeVue's default
`acceptLabel`) → row gone.

The spec titles each run's article `E2E Title ${E2E_STAMP}` so the run is self-cleaning
(create-then-delete) and safe to run repeatedly or in parallel against a shared database:

    E2E_STAMP=$(date +%s) pnpm e2e:sample

If `E2E_STAMP` is omitted it defaults to `e2e`, which is fine for a single sequential run but can
collide with a leftover row from a previous failed run — pass a unique stamp when in doubt.

Requires the authenticated user to hold **write and delete** grants on `article` (create, update,
and delete all go through the same RBAC checks as the API). The bootstrap super-admin used by
`auth.spec.ts`/`collections.spec.ts` satisfies this with no extra setup.

Selector notes (see inline comments in `items.spec.ts` for the "why"):

- `ItemForm.vue` renders each field's `<label>` without a `for`/`id` pairing to the underlying
  PrimeVue control, so `page.getByLabel(...)` cannot resolve any form field here. The spec scopes
  by the field's `.field` wrapper (matched on its label text) instead.
- `Article.Status` is `[CmsField(Interface = FieldInterface.Select)]` with
  `CmsOptions("draft:Draft", "published:Published")`, so `FieldInput.vue` renders a PrimeVue
  `Select` (`role="combobox"` trigger + `role="listbox"`/`role="option"` overlay) — it is clicked
  and an option is chosen, never `.fill()`ed.
- `Title` (`FieldInterface.Text`) and `Body` (`FieldInterface.RichText`, rendered as a plain
  `Textarea` — Phase 7c has no rich text editor widget yet) live on `ArticleTranslation` and are
  therefore translatable fields under the Tabs' first panel, which is the default locale and
  active on load.

This run is part of the user-driven live gate (same prerequisites as above: API on `:5221` with a
seeded bootstrap admin, plus the Blog sample opted in, `pnpm e2e:sample` from `frontend/`) — it is
not executed automatically as part of authoring this spec.
