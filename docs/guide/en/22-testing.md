# 22. Testing and CI

This project's tests run in several layers, each with its own command. This chapter covers what
each layer tests, which jobs CI runs, and what it deliberately doesn't run.

## Backend tests

Backend tests live in projects under `tests/` and `samples/`, use xUnit, and run with `dotnet test`
from the repository root:

- `tests/Struo.Tests` holds the core's unit and integration tests.
- `samples/Struo.Sample.Blog.Tests` holds the sample's own tests. It is deleted together with the
  sample.
- `tests/Struo.Tests.Template` holds the template's own repository guards: the documentation
  conventions, the template invariants, and the CI, Docker and health conventions. A fork can delete
  it (see [Chapter 25](25-after-forking.md)).
- `tests/Struo.Tests.Fixtures` is a test-only content project, described below.
- `tests/Struo.Tests.Support` is a class library, not a test project. It holds the helpers the test
  projects share.

The default backend is SQLite: unit tests get their database from `TestBackend.CreateUnitDatabase()`,
which, with no backend switch set, is a temp SQLite file deleted when the test ends, so no test shares
state with another. A test that uses `SqliteTestDatabase` directly fails
`SqliteTestDatabaseUsageTests`, so every test follows the backend switch. Only the Support library
and a project's own top-level `Support/` folder may use it.

### The Support library

`Struo.Tests.Support` (namespace `Struo.Tests.Support`) carries `StruoApiFactory`, the base for a
test host; `TestBackend` and the live-database helpers behind the backend switch below;
`TestHostEnvironment`; and `StruoTestFramework`, which drops the live database pools after the last
test.

Each test project that hosts the API or leases pooled live databases sets up two things of its own.
An assembly attribute names the test framework, because without it the pools are dropped on process
exit and leave databases behind:
`[assembly: TestFramework("Struo.Tests.Support.StruoTestFramework", "Struo.Tests.Support")]`.
A module initializer calls `TestHostEnvironment.Pin` with the content assemblies the project's hosts
load. `Pin` also turns off the Redis connection and the two shipped OIDC providers, `microsoft` and
`google`, so a developer's gitignored Development settings cannot reach the suite. A provider your
fork adds is not touched. It sets environment variables, because they are the only override that
reaches `AddStruoCms` in time. In `tests/Struo.Tests` that initializer is `ContentAssemblyEnvBootstrap`.

`ApiFactory` in `tests/Struo.Tests` derives from `StruoApiFactory` and names the collections the host
makes publicly readable. A fork's test project does the same; [Chapter 25](25-after-forking.md) shows
the setup.

These unit tests build their tables from entities with CodeFirst. When your fork's own test
entities declare unique group indexes, keep the names within 63 characters; see
[Chapter 21, "Backend differences"](21-schema-and-upgrades.md#backend-differences).

### Tests against live backends

Four test connections each turn on one backend's live tests: `Testing:PostgresConnection`,
`Testing:SqlServerConnection`, `Testing:MySqlConnection` and `Testing:MariaDbConnection`. Each key
checks its own environment variable first, such as `STRUO_TEST_PG_CONNECTION`, then falls back to
the configuration files. These four keys don't honor the `Section__Key` override rule; the keys and
their variables are listed in the `Testing` section of
[Chapter 4: Configuration Reference](04-configuration.md). The tests' own `ConfigurationBuilder`
reads them directly, never through the application's option binding.

A backend with a connection configured runs these suites:

- the migration live tests, such as `CoreMigrationsLiveTests`, which apply the core migrations and
  check the result with `SchemaChecker`;
- `LiveRepositoryTests`, on every backend with a connection, independent of `STRUO_TEST_BACKEND`;
- `PostgresOnlyTests`, the tests that hold only on PostgreSQL;
- `TestDatabaseLiveTests`, which cover creating and cleaning up the test databases themselves.

When a connection is empty, every live test for that backend passes outright rather than being
marked skipped — a green line in the test report doesn't mean it ever connected to that database.
Judge by how long each test took.

The tests only accept a connection string whose database name contains `test` (case-insensitive);
a name that doesn't match is refused, so the tests can't connect to a database that's actually in
use. The configured database must exist beforehand, and the login needs rights to create and drop
databases: suites such as `LiveRepositoryTests` create their own databases on the same server and
drop them at the end.

Add `GSS Encryption Mode=Disable` to the PostgreSQL test connection. Otherwise every new connection
tries GSS first, and with many connections, tests that wait under a timeout, such as the migration
lock tests, can time out.

`PgTestConnectionString.DisablePooling` separately turns off Npgsql's connection pooling,
isolating a socket interruption observed under the test host — a phenomenon measured there, not
evidence that production carries the same risk.

### Moving the whole suite to a live backend

The `STRUO_TEST_BACKEND` environment variable selects the backend the whole suite runs on: `Sqlite`
(the default), `PostgreSQL`, `SqlServer`, `MySql` or `MariaDb`, case-insensitive. An invalid value,
or a live backend without its connection, fails the whole run with a message naming what to set.

```bash
STRUO_TEST_BACKEND=MySql dotnet test
```

The switch moves both kinds of test database:

- **API test hosts** (`ApiFactory` and the CORS test host): each host creates its own database and
  drops it when done.
- **Unit tests**: lease a database from a per-backend pool, created on the first read of its
  connection string or name. Every lease starts empty; returning it drops all its tables. Pool
  databases are named `<name>_unit_<seq>_<runId>`.

MySQL and MariaDB test databases are created as utf8mb4. Run MySQL with the server's default strict
`sql_mode`: non-strict mode silently truncates or replaces values, which hides length and charset
bugs.

A test that only means something on SQLite carries `[SqliteOnlyFact]` or `[SqliteOnlyTheory]` and
is skipped on a live backend, with a reason starting `SQLite-only:`.

Pool databases are dropped when the test run finishes. A crashed or killed run can leave
databases behind; on MySQL and MariaDB, a discarded database also survives until the connection
holding it closes. After a live run, this drops the databases other runs left on the selected
backend:

```bash
STRUO_TEST_BACKEND=<backend> STRUO_TEST_DROP_LEFTOVERS=1 dotnet test --filter "FullyQualifiedName~Drop_leftover_databases_on_request"
```

It force-drops every other run's databases, so don't run it while another suite uses the same
server.

### The core tests run on test-only fixtures

`tests/Struo.Tests.Fixtures` is a small content project that exists only for the tests. It declares
one collection of each shape the core has to handle: an article with a translation sidecar,
revisions and soft delete, tags joined through a junction that carries its own payload, and a
self-referencing category tree, plus a plain class used as a `Repeater` child type. It has its own
migration, and `tests/Struo.Tests` pins it as its content assembly through
`ContentAssemblyEnvBootstrap`.

No shipped host registers it. Its collections carry an `fx` prefix (`fxArticle`, `fxTag`) and its
tables are named `fx_*`, so they can never collide with a collection of your own, whatever you call
it. The sample is not part of this: the core tests don't reference `samples/`, and a test in
`tests/Struo.Tests.Template` fails if one ever does. Removing the sample leaves `tests/` untouched;
the steps are in [Chapter 23: Sample Project Walkthrough](23-sample-walkthrough.md).

### Tests that pin a rule

A handful of tests don't verify a feature — they pin down a rule; break the rule itself and the
test goes red.

- `CodeCitationConventionTests` (in `tests/Struo.Tests.Template`) — every citation in the manual and in code must point to a
  construct, never a line number.
- `TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` (in
  `tests/Struo.Tests.Template`) — the API project (`Struo.Api`) has no direct reference to any
  sample assembly.
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
[Chapter 18: Extension Points](18-extension-points.md).

## Admin SPA tests

Admin SPA tests use Vitest, configured in the `test` block of `frontend/vite.config.ts`: the
environment is `jsdom`, `globals`/`restoreMocks`/`clearMocks` are all on, and `setupFiles` points
at `vitest.setup.ts`. Every command in this section runs from `frontend/`.

Component- and `lib/`-level `*.test.ts` files sit next to the code they test rather than being
collected into a separate directory; the cross-file guard tests mostly live under `frontend/tests/`,
except the locale one, which sits with `frontend/src/locales/`.

Three guard tests each pin one thing down:

- `frontend/tests/iconCoverage.test.ts` — scans `frontend/src/` and confirms every `pi-` icon token
  that appears in source has a matching key in `ICON_MAP`.
- `frontend/src/locales/locales.test.ts` — the `zh-TW` and `en` UI-language directories have exactly
  symmetric key sets.
- `frontend/tests/schemaContract.test.ts` — feeds the two JSON files under `schema/` into the real
  field-interface registry, verifying the field types the admin SPA recognizes match what the
  backend declares; covered below under "The schema contract".

`pnpm test` only runs Vitest; type checking depends on `pnpm build` (`vue-tsc -b && vite build`) —
unit tests passing alone doesn't mean the types are right too.

## End-to-end tests

End-to-end tests use Playwright, configured in `frontend/playwright.config.ts`, split into two
projects: `core` (`pnpm e2e`) runs the specs under `e2e/`, excluding `e2e/sample/**`, against a
setup with no content collections opted in; `sample` (`pnpm e2e:sample`) runs `e2e/sample/**`,
which needs the sample collections opted in first. The config adds the `sample` project only while
`e2e/sample/` exists. `pnpm e2e:all` runs both.

The `webServer` block only starts the admin SPA's dev server (`pnpm dev`,
`reuseExistingServer: true`) — it doesn't start the API or the database for you. Both projects
assume an API and its backing database are already running, reachable at the configured proxy
target.

The config sets `workers: 1`, and `use.baseURL` is `http://localhost:5173`; `core`'s `testDir` is
`./e2e`, whose scope already covers `e2e/sample/`, so it relies on
`testIgnore: '**/e2e/sample/**'` to keep the sample specs out; `sample`'s `testDir` points
straight at `./e2e/sample` and has nothing left to exclude. With `e2e/sample/` deleted, only
`core` remains.

The database has to be migrated first (`migrate`, or `Database:MigrateOnStartup=true`), which seeds
the bootstrap administrator account used for login; the rest of the prerequisites and how to
override the login credentials are in `frontend/e2e/README.md`.

For the `sample` project to run, opt the sample in first following the steps in
[Chapter 23](23-sample-walkthrough.md); running the `sample` project with `--list` lists 8 spec
files, 15 tests.

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
`dotnet test --no-build --configuration Release --verbosity normal`, running every backend test
project. CI sets no `STRUO_TEST_*` connection and no `STRUO_TEST_BACKEND`, so it runs on SQLite
only and every live suite takes its pass-outright path here.

`frontend`: inside the `frontend` directory, `pnpm install --frozen-lockfile --ignore-scripts`,
then `pnpm test`, then `pnpm build` (`vue-tsc -b && vite build`). Beyond the unit tests, this
build also does a full production build: `pnpm test` doesn't touch types — the admin SPA's
TypeScript types are checked at this step.

`docs`: inside the `docs` directory, `pnpm install --frozen-lockfile --ignore-scripts`, then
`pnpm test` (the guard scripts' own `node --test` suite), then `pnpm build` (the three parts
`vitepress build`, `check-rendered-chapters.mjs`, `check-table-width.mjs`).

`docker`: builds both container images and runs one smoke test against each, tagging the images
`struo-api:ci`/`struo-admin:ci` (not the same tag as the `:local` you'd use building them
yourself in [Chapter 20: Deployment](20-deployment.md)), then starts the whole Compose stack and
smoke-tests that too.

The API image first runs `migrate` with `Database__DbType=Sqlite` and
`Database__ConnectionString=Data Source=/app/App_Data/struo-ci.db`, against a SQLite file on the
named volume `struo-ci-data`; the API then starts on the same volume and is polled at
`/health/ready` until healthy. The admin SPA image, once started, is polled at its home page for an
`assets/index-` fragment in the response, proving the built assets are actually reachable through
nginx.

Right after the builds, Trivy scans both images and fails the job on any HIGH or CRITICAL
vulnerability that has an upstream fix. Findings with no fix yet are ignored, so a CVE you cannot
act on never blocks a merge. The same results are uploaded as SARIF to the repository's Security
tab (code scanning), except where that upload is skipped (see below);
the blocking scan still runs everywhere.

The SARIF upload runs only on the public repository's pushes and same-repository pull requests.
It is skipped on private repositories, because code scanning there needs GitHub Advanced Security
and the upload would fail without it. A private repository with code scanning enabled can drop the
`github.event.repository.private` condition from the `sarif-gate` step.

The Compose step runs `docker compose up --wait`, which builds its own images from the same two
Dockerfiles. It then checks the API's `/health/ready` on port 8081, the admin home page on port
8080 for the same `assets/index-` fragment, and an anonymous `/api/config` call through nginx. It
always ends with `docker compose down --volumes`, and prints `docker compose logs` first if any
check failed.

This job needs no secret, so a pull request from a fork or a Dependabot update can run it through
to completion just the same.

`sonar-backend` and `sonar-frontend` send analysis results to SonarQube Cloud; both jobs need
the `SONAR_TOKEN` secret and read the organization and project keys from the repository variables
`SONAR_ORGANIZATION`, `SONAR_BACKEND_PROJECT_KEY` and `SONAR_FRONTEND_PROJECT_KEY`. A repository
without `SONAR_ORGANIZATION` skips both jobs. A Dependabot update always skips them, and a pull
request only runs them when its source branch and the target are in the same repository.

`sonar-backend` first restores the two `dotnet-sonarscanner` and `dotnet-coverage` CLI tools at
the versions pinned in `.config/dotnet-tools.json`, runs `dotnet dotnet-sonarscanner begin`,
builds normally, wraps `dotnet test` with `dotnet dotnet-coverage collect` to produce
`coverage.xml`, then sends the analysis with `dotnet dotnet-sonarscanner end`.

`sonar-frontend` starts with the same `pnpm install --frozen-lockfile --ignore-scripts`, then
`pnpm vitest run --coverage --coverage.reporter=lcov` to produce the coverage report first, then
sends it with SonarSource's `sonarqube-scan-action`, pinned by commit SHA and updated by
Dependabot.

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

CI also never sets a `STRUO_TEST_*` connection, so the live suites always take their pass-outright
path here, unlike locally with a real database connection wired up.

The schema contract needs no job of its own: each side rides along inside `backend`'s
`dotnet test` and `frontend`'s `pnpm test`.

## What's next

That's testing and CI covered; the next chapter,
[Chapter 23: Sample Project Walkthrough](23-sample-walkthrough.md), walks through the sample file
by file to see which mechanisms it demonstrates.
