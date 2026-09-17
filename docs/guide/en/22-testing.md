# 22. Testing and CI

This project's tests run in several layers, each with its own command. This chapter covers what
each layer tests, which jobs CI runs, and what it deliberately doesn't run.

## Backend tests

Backend tests live in `tests/Struo.Tests`, use xUnit, and run with `dotnet test`. The default
backend is SQLite: most tests each create their own temp-file database (`SqliteTestDatabase`),
deleted when the test ends, so no test shares state with another.

The opt-in PostgreSQL integration suite (`PostgresIntegrationTests`) only actually runs when it
can resolve a connection string: it checks the `STRUO_TEST_PG_CONNECTION` environment variable
first, then falls back to the key in configuration.

That key's name, and the fact that it's the one place in the whole configuration surface that
doesn't honor the `Section__Key` override rule, are in the `Testing` section of
[Chapter 4: Configuration Reference](04-configuration.md); the tests' own `ConfigurationBuilder`
reads it directly, never through the application's option binding.

When no connection string resolves, every test in this suite passes outright rather than being
marked skipped — a green line in the test report doesn't mean it ever actually connected to
PostgreSQL.

The suite only accepts a connection string whose database name contains `test`
(case-insensitive); a name that doesn't match is refused, so the tests can't connect to a
database that's actually in use. `PgTestConnectionString.DisablePooling` separately turns off
Npgsql's connection pooling, isolating a socket interruption observed under the test host — a
phenomenon measured there, not evidence that production carries the same risk.

### The test suite uses the sample as its fixture

Across the backend test suite, 36 files have `using Struo.Sample.Blog;` — most under `Query`, the
rest scattered across `Metadata`, `Persistence`, `Revisions`, `Search`, `Changes`, and `Health` —
loaded through the `[ModuleInitializer]` `ContentAssemblyEnvBootstrap`.

The sample project isn't only a demonstration; it's the fixture these tests actually query and
assert against. Remove the sample, and these tests either get rewritten or switch to a different
fixture. The full steps for removing the sample are in
[Chapter 23: Sample Project Walkthrough](23-sample-walkthrough.md).

### Tests that pin a rule

A handful of tests don't verify a feature — they pin down a rule; break the rule itself and the
test goes red.

- `CodeCitationConventionTests` — every citation in the manual and in code must point to a
  construct, never a line number.
- `TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` — the API project
  (`Struo.Api`) has no direct reference to any sample assembly.
- `OptionsValidationTests` — drives the real startup pipeline to confirm a configuration error
  fails at startup itself rather than waiting for the first request: a missing database
  connection string, `Query:MaxLimit` set to 0, and OIDC enabled without a client ID must each
  fail startup.
- `CoreSchemaSnapshotTests` — a snapshot test that pins the core schema to a snapshot committed to
  version control; covered below under "The schema contract".

### Testing your own search provider and change listener

The test suite carries one test double for each of the two interfaces the framework leaves for a
fork to implement: `ScriptedSearchProvider` wires a scripted `SearchOutcome` into
`ISearchProvider` while recording every `SearchRequest` it receives; `RecordingItemChangeListener`
records every `OnChangedAsync` call and can optionally attach a scripted callback.

How the two interfaces themselves work is covered in
[Chapter 18: Extension Points: Search Providers and Change Listeners](18-extension-points.md).

## Admin SPA tests

Admin SPA tests use Vitest, configured in the `test` block of `frontend/vite.config.ts`: the
environment is `jsdom`, `globals`/`restoreMocks`/`clearMocks` are all on, and `setupFiles` points
at `vitest.setup.ts`. Every command in this section runs from `frontend/`.

Component- and `lib/`-level `*.test.ts` files sit next to the code they test rather than being
collected into a separate directory; the cross-file guard tests mostly live under `tests/`,
except the locale one, which sits with `src/locales/`.

Three guard tests each pin one thing down:

- `tests/iconCoverage.test.ts` — scans `src/` and confirms every `pi-` icon token that appears in
  source has a matching key in `ICON_MAP`.
- `src/locales/locales.test.ts` — the `zh-TW` and `en` UI-language directories have exactly
  symmetric key sets.
- `tests/schemaContract.test.ts` — feeds the two JSON files under `schema/` into the real
  field-interface registry, verifying the field types the admin SPA recognizes match what the
  backend declares; covered below under "The schema contract".

`pnpm test` only runs Vitest; type checking depends on `pnpm build` (`vue-tsc -b && vite build`) —
unit tests passing alone doesn't mean the types are right too.

## End-to-end tests

End-to-end tests use Playwright, configured in `frontend/playwright.config.ts`, split into two
projects: `core` (`pnpm e2e`) runs the specs under `e2e/`, excluding `e2e/sample/**`, against a
setup with no content collections opted in; `sample` (`pnpm e2e:sample`) runs `e2e/sample/**`,
which needs the sample collections opted in first. `pnpm e2e:all` runs both.

The `webServer` block only starts the admin SPA's dev server (`pnpm dev`,
`reuseExistingServer: true`) — it doesn't start the API or the database for you. Both projects
assume an API and its backing database are already running, reachable at the configured proxy
target.

The config sets `workers: 1`, and `use.baseURL` is `http://localhost:5173`; `core`'s `testDir` is
`./e2e`, whose scope already covers `e2e/sample/`, so it relies on
`testIgnore: '**/e2e/sample/**'` to keep the sample specs out; `sample`'s `testDir` points
straight at `./e2e/sample` and has nothing left to exclude.

The bootstrap administrator used to log in has to be seeded first; the rest of the prerequisites
and how to override the login credentials are in `frontend/e2e/README.md`.

For the `sample` project to run, opt the sample in first following the steps in
[Chapter 23: Sample Project Walkthrough](23-sample-walkthrough.md); running the `sample` project
with `--list` lists 8 spec files, 15 tests.

With the API and the database both already running, run the admin SPA's unit tests, build, and
the `core` e2e project locally in one line:

```
cd frontend && pnpm test && pnpm build && pnpm e2e
```

## The schema contract

The schema contract is two JSON files committed to version control: `schema/core-collections.json`
records the `GET /api/schema` wire format of the seven framework collections, and
`schema/interfaces.json` records every declared field-interface and relation-interface member.

Each side has one test pinning them down: the backend's `CoreSchemaSnapshotTests` (two test
methods, `CoreSchema_MatchesCommittedSnapshot` and `InterfaceEnums_MatchCommittedSnapshot`), and
the admin SPA's `schemaContract.test.ts` — the latter feeds the two files into the real registry
and field-type resolution logic rather than re-declaring its own expected values.

This is a fourth kind of test, not a fourth command: both sides ride along with
`dotnet test`/`pnpm test` and need no separate way to run them.

After changing the core schema, regenerate the snapshot — one set of commands for Bash, one for
PowerShell:

```bash
UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot
```

```powershell
$env:UPDATE_SCHEMA_SNAPSHOT = 1
dotnet test --filter CoreSchemaSnapshot
Remove-Item Env:UPDATE_SCHEMA_SNAPSHOT
```

Clear that environment variable in PowerShell afterward: it stays for the rest of the same
terminal session, quietly turning this gate off, so every subsequent `dotnet test` regenerates
the snapshot instead of verifying it. The full rules are in `schema/README.md`.

## The manual's own gate

This gate is `pnpm build` from `docs/`, in three parts: first `vitepress build`, which resolves
every chapter link; then `check-rendered-chapters.mjs`, confirming every chapter actually
rendered content rather than an empty page; last `check-table-width.mjs`, which blocks tables
over four columns and cells over 60 display width. If any of the three parts fails, `pnpm build`
fails.

`pnpm test` from the same directory runs the guard scripts' own unit tests, with the command
`node --test "scripts/**/*.test.mjs"`; CI runs this step, but it's a step, not a gate.

## What CI runs

`.github/workflows/ci.yml` defines six jobs. `backend`, `frontend`, `docs`, and `docker` all run
on a push to `main`, on every pull request, and on manual dispatch; `sonar-backend` and
`sonar-frontend` trigger on the same events but add one more condition. `backend` and `docker`
run at the repository root; `frontend` and `docs` each run inside their own subdirectory.

`backend`: `dotnet restore`, `dotnet build --no-restore --configuration Release`,
`dotnet test --no-build --configuration Release --verbosity normal`, running the whole backend
test suite. CI never sets `STRUO_TEST_PG_CONNECTION`, so the opt-in PostgreSQL integration suite
always takes its pass-outright path here.

`frontend`: inside the `frontend` directory, `pnpm install --frozen-lockfile --ignore-scripts`,
then `pnpm test`, then `pnpm build` (`vue-tsc -b && vite build`). Beyond the unit tests, this
build also does a full production build: `pnpm test` doesn't touch types — the admin SPA's
TypeScript types are checked at this step.

`docs`: inside the `docs` directory, `pnpm install --frozen-lockfile --ignore-scripts`, then
`pnpm test` (the guard scripts' own `node --test` suite), then `pnpm build` (the three parts
`vitepress build`, `check-rendered-chapters.mjs`, `check-table-width.mjs`).

`docker`: builds both container images and runs one smoke test against each, tagging the images
`struo-api:ci`/`struo-admin:ci` (not the same tag as the `:local` you'd use building them
yourself in [Chapter 20: Deployment](20-deployment.md)).

The API image starts with `Database__DbType=Sqlite`,
`Database__ConnectionString=Data Source=/tmp/struo-ci.db`, and is polled at `/health/ready` until
healthy; the admin SPA image, once started, is polled at its home page for an `assets/index-`
fragment in the response, proving the built assets are actually reachable through nginx.

This job needs no secret, so a pull request from a fork or a Dependabot update can run it through
to completion just the same.

`sonar-backend` and `sonar-frontend` send analysis results to SonarQube Cloud; both jobs need
`SONAR_TOKEN`. A Dependabot update always skips them, and a pull request only runs them when its
source branch and the target are in the same repository.

`sonar-backend` first installs the two `dotnet-sonarscanner` and `dotnet-coverage` CLI tools,
runs `dotnet-sonarscanner begin`, builds normally, wraps `dotnet test` with
`dotnet-coverage collect` to produce `coverage.xml`, then sends the analysis with
`dotnet-sonarscanner end`.

`sonar-frontend` starts with the same `pnpm install --frozen-lockfile --ignore-scripts`, then
`pnpm vitest run --coverage --coverage.reporter=lcov` to produce the coverage report first, then
sends it with `pnpm dlx sonarqube-scanner`.

## The five standing gates

This project's five standing gates are:

- `dotnet build`
- `dotnet test`
- `pnpm test` from `frontend/`
- `pnpm build` from `frontend/`
- `pnpm build` from `docs/`

Run whichever side a change touches; when a change touches more than one of the three — the
backend, the admin SPA, or the manual — run all five before pushing. `docs/`'s `pnpm test`,
`docker`, `sonar-backend`, and `sonar-frontend` also run in CI, but none of them count as a
standing gate.

## What CI deliberately does not run

CI never runs `pnpm e2e`, `pnpm e2e:sample`, or `pnpm e2e:all`: no job starts a database, starts
the API, or calls `playwright test`. End-to-end tests need a running API and database alongside
the admin SPA's dev server — a heavier environment than any job here provisions — so they're a
local, pre-merge discipline, not an automated gate.

CI also never sets `STRUO_TEST_PG_CONNECTION`, so the opt-in PostgreSQL integration suite always
takes its pass-outright path here, unlike locally with a real PostgreSQL connection string wired
up.

The schema contract needs no job of its own: each side rides along inside `backend`'s
`dotnet test` and `frontend`'s `pnpm test`.

## What's next

That's testing and CI covered; the next chapter,
[Chapter 23: Sample Project Walkthrough](23-sample-walkthrough.md), walks through the sample file
by file to see which mechanisms it demonstrates.
