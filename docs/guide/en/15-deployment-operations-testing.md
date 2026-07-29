# 15. Deployment, Operations & Testing

Chapter 3 documents every configuration key's semantics. This chapter is about what happens if a
production deployment gets one of them wrong, plus the operational surface around configuration:
schema management, startup and failure behavior, logging, health probes, backups, and the three
layers of tests this repository ships.

## Production checklist

Every row below was read directly against the cited source and, where noted, verified live against a
disposable scratch database and a second, Production-mode instance of this build — never against the
shared development database or the running development API.

| Setting | Required value | Consequence if wrong |
|---|---|---|
| `Database:MigrationsPath` | An **absolute** path | `MigrationRunner.ApplyAsync` (`src/Struo.Infrastructure/Persistence/MigrationRunner.cs`) passes the configured value straight to `Directory.Exists(...)` with no content-root resolution of its own — unlike `Struo:Files:ImageTransform:CachePath`, which is explicitly resolved against `IHostEnvironment.ContentRootPath` (`FileStorageServiceCollectionExtensions.cs:51`). A relative value therefore resolves against the **process's current working directory** at launch, which is not guaranteed to be the application's own folder (a systemd unit's `WorkingDirectory`, a container's `WORKDIR`, or any launcher that `cd`s elsewhere before starting the process can all differ from it). Verified live: run from `src/Struo.Api` with `MigrationsPath=db/migrations` (present only relative to the repository root, not that directory) throws `DirectoryNotFoundException: MigrationRunner: migrations directory not found: 'db/migrations'` and the process exits with code 1; run instead with the absolute path `D:/dotnet/struo-cms/db/migrations` against an empty scratch database, it correctly created all 10 core tables and recorded `001-core-baseline.sql` in `schema_migrations`. |
| `Auth:BootstrapAdmin:Password` | Overridden before the **first** boot against a fresh database | Consulted only once, the first time the `users` table is created (`DataSeeder.cs`); a later boot never re-reads it. A `Production` start still seeded with the literal default `admin` logs a `WARNING` naming the exact setting to change (`DataSeeder.WarnIfDefaultAdminPasswordInProduction`) but does **not** refuse to start — verified live: `[17:30:16 WRN] Bootstrap admin is using the default password 'admin'. Change it immediately via Auth__BootstrapAdmin__Password.` The warning compares the **currently configured** value against the literal string `"admin"` (`DataSeeder.cs:63`), not the password hash actually stored for the account — so an operator who let the database seed with the default and only later sets a strong value in configuration silences the warning on every subsequent boot while the stored account still has the original default-password hash. |
| `RateLimiting:Login:Enabled` | `false` only when per-IP limiting is enforced at the ingress/edge | The in-app login limiter partitions by `Connection.RemoteIpAddress` and its counters live in per-process memory (`Program.cs`, the `AddRateLimiter`/`AddPolicy("login", …)` block). Left `true` behind a load balancer fanning out to N replicas, the effective limit becomes ≈N× the configured value, inconsistently, and never a true global cap — the code's own comment states this is "delegated to the ingress/edge/WAF" for exactly that reason. |
| Reverse-proxy forwarded headers | The deployment must add `UseForwardedHeaders` (with `KnownProxies`/`KnownNetworks`) itself | `Program.cs` never calls `app.UseForwardedHeaders(...)` anywhere in the pipeline — the comment beside the login limiter's partition key says so explicitly ("deliberately out of scope here"). Behind any reverse proxy, `Connection.RemoteIpAddress` is the **proxy's** address, not the real client's, so every login attempt through that proxy collapses into a single rate-limit partition — either every user behind the proxy shares one 5-per-60s bucket (an accidental self-inflicted denial of service), or, combined with `RateLimiting:Login:Enabled=false`, no per-IP protection exists at all if the edge layer isn't actually providing it either. |
| Scalar / OpenAPI | Not reachable in Production — do not rely on network-level blocking alone | `app.MapOpenApi()` and `app.MapScalarApiReference(...)` are both guarded by `if (!app.Environment.IsProduction())` (`Program.cs`). Verified live against a Production-mode instance: `GET /scalar` → 404, `GET /openapi/v1.json` → 404. (Chapter 10 documents the one adjacent route that is *not* gated the same way: `GET /graphql?sdl`.) |
| `Redis:ConnectionString` | Set to a real Redis instance for any deployment with more than one API replica, or any deployment where sessions must survive a restart | Empty falls back to `AddDistributedMemoryCache()` (`AuthWiring.cs`) — an in-process, per-instance cache backing the cookie-authentication ticket store. A restart loses every session (forced re-login); with more than one replica behind a load balancer, each replica has its own session store, so a user's session is only valid on whichever replica issued it. |
| Cookie `Secure` policy | The reverse proxy/load balancer must terminate HTTPS in front of a Production deployment | `AuthWiring.cs` sets `CookieSecurePolicy.Always` whenever `env.IsProduction()` (`CookieSecurePolicy.SameAsRequest` otherwise, so the dev/test HTTP host still works). Serving Production over plain HTTP means the browser never sends the auth cookie back on any subsequent request — login appears to succeed once and then silently never persists. |
| `Oidc:RequireEmailVerified` / `AllowedTenantId` / `AllowedEmailDomains` | Pinned explicitly whenever `Oidc:Enabled=true` | `AllowedTenantId` ships as the non-matching placeholder `"REPLACE_TENANT_ID"` (`appsettings.json`), and `ExternalLoginService.ResolveOrProvisionAsync` (`src/Struo.Application/Security/ExternalLoginService.cs`) rejects any external identity whose tenant doesn't equal it (`TenantNotAllowed`) — so with the **shipped** defaults, external login fails closed for every real tenant until this is replaced with the real one. `RequireEmailVerified` (`false`) and `AllowedEmailDomains` (`[]`) do default permissive, though: once `AllowedTenantId` is set to a real, matching tenant, the only remaining checks are opt-in, and linking then proceeds by **email equality alone** — any external account whose claimed email matches an existing local user's is treated as that user, verification status notwithstanding. |

## Schema management

- **Development:** `InitTables` (SqlSugar CodeFirst) creates missing tables and additively adds missing
  columns, driven straight from the entity classes; it runs only when `app.Environment.IsDevelopment()`
  (`Program.cs`) and never in Production.
- **Reviewed migrations:** setting `Database:MigrationsPath` (chapter 3) points `MigrationRunner` at a
  directory of `NNN-short-kebab-description.sql` files. It runs in **every** environment once
  configured — that is the point, reviewed scripts reaching Production is the whole mechanism — and is
  a hard no-op on any non-PostgreSQL backend (`db.CurrentConnectionConfig.DbType != DbType.PostgreSQL`
  short-circuits with no reads or writes at all).
- **Tracking:** a `schema_migrations (filename text PRIMARY KEY, appliedat timestamptz)` table the
  runner creates on first use. Applied filenames are recorded by **filename only** — no checksum or
  content hash — so a file already recorded as applied is never re-run, even if its on-disk content is
  later edited (`001-core-baseline.sql`'s own "FILENAME-KEYED TRACKING" comment spells out the
  consequence: never edit a filename that may already be recorded as applied anywhere; ship a new file
  instead).
- **Writing the next script:** `NNN-short-kebab-description.sql`, a single contiguous zero-padded
  series (the next number is always the current highest **+ 1**), one logical change per file,
  idempotent (`IF NOT EXISTS` / guarded `ALTER` / `DO $$ … $$` existence checks), forward-only (no
  automatic down-migration — a rollback is a new compensating script). The shipped baseline is
  `001-core-baseline.sql`; a fork's first schema change is `002-…` (`db/migrations/README.md`).
- **Timestamp convention:** any new temporal column uses `timestamptz`, never bare `timestamp`, storing
  UTC — the convention the tracking table's own `appliedat` column follows. The baseline itself is not
  uniform: most `AuditableEntity` `createdat`/`updatedat` columns are bare `timestamp`, but
  `media_folders.createdat`/`media_folders.updatedat` and `site_settings.updatedat` (`site_settings` has
  no `createdat` column at all) are already `timestamptz` (`db/migrations/001-core-baseline.sql`,
  `src/Struo.Infrastructure/Files/MediaFolder.cs`) — check the baseline directly for the table being
  altered rather than assuming either type. None of the baseline's existing bare-`timestamp` columns are
  deliberately retro-migrated to close that gap, since re-anchoring already-stored values against a
  session time zone is a silent data shift (`db/migrations/README.md`).

Verified live: applying `001-core-baseline.sql` via `MigrationRunner` to an empty scratch database
(`Database:MigrationsPath` set to its absolute path, `ASPNETCORE_ENVIRONMENT=Production`) produced
exactly the ten core tables plus the tracking table, with the single file recorded as applied:

```
$ docker exec struo-postgres psql -U struo -d struo_probe -c "\dt"
             List of relations
 Schema |       Name        | Type  | Owner
--------+-------------------+-------+-------
 public | file_translations | table | struo
 public | files             | table | struo
 public | languages         | table | struo
 public | media_folders     | table | struo
 public | permissions       | table | struo
 public | revisions         | table | struo
 public | roles             | table | struo
 public | schema_migrations | table | struo
 public | site_settings     | table | struo
 public | user_roles        | table | struo
 public | users             | table | struo
(11 rows)

$ docker exec struo-postgres psql -U struo -d struo_probe -c "SELECT * FROM schema_migrations;"
       filename        |           appliedat
-----------------------+-------------------------------
 001-core-baseline.sql | 2026-07-29 09:32:09.208427+00
(1 row)
```

## Startup behavior and failure modes

- **Fail-fast options validation:** `Database`, `Struo:Files`, `Oidc` and `Query` are all bound with
  `ValidateOnStart`; a missing `Database:ConnectionString`, an `Oidc:Enabled=true` configuration missing
  `ClientId`, or an out-of-range `Query:MaxLimit` all throw `OptionsValidationException` before the app
  starts listening rather than surfacing as a confusing first-request failure
  (`tests/Struo.Tests/DependencyInjection/OptionsValidationTests.cs` exercises exactly these cases
  against a real generic host).
- **Non-zero exit code:** `Program.cs` wraps startup in a top-level `try`/`catch`/`finally`. Any startup
  exception is logged via `Log.Fatal` and `Environment.ExitCode` is set to `1` in the `catch` block —
  the code's own comment explains why: without it, the exception is still logged but the process exits
  `0`, so an orchestrator or process supervisor sees an apparently successful exit and never restarts or
  alerts. Verified live: a `Database:MigrationsPath` pointed at a directory that does not exist relative
  to the process's working directory produced

  ```
  [17:34:20 FTL] StruoCMS host terminated unexpectedly
  System.IO.DirectoryNotFoundException: MigrationRunner: migrations directory not found: 'db/migrations'. Check the Database:MigrationsPath configuration value.
     at Struo.Infrastructure.Persistence.MigrationRunner.ApplyAsync(ISqlSugarClient db, String migrationsDirectory, ILogger logger, CancellationToken ct) in D:\dotnet\struo-cms\src\Struo.Infrastructure\Persistence\MigrationRunner.cs:line 73
     at Program.<Main>$(String[] args) in D:\dotnet\struo-cms\src\Struo.Api\Program.cs:line 196
  ```

  and the process's own exit code was `1`.
- **Dev schema guard:** `SchemaGuard.AssertCriticalConstraintsAsync` runs in Development only, after
  `InitTables` and the migration runner, and asserts that the `revisions` composite UNIQUE index and
  each configured translation sidecar's UNIQUE `(fk, locale)` index physically exist — throwing
  `InvalidOperationException` with an actionable message if either is missing, rather than letting the
  app run with a silent correctness gap. It is a fail-fast dev convenience, not a Production safety net;
  Production schemas are expected to already carry these indexes from `001-core-baseline.sql` or a
  fork's own migrations.

## Logging and log files

Serilog is configured entirely under `Serilog:*` (chapter 3): a Console sink, and a File sink writing
`logs/struo-.log` with daily rolling and shared file access. Verified against this checkout's own log
directory — one file per calendar day, named `struo-YYYYMMDD.log`:

```
$ ls -la src/Struo.Api/logs
...
-rw-r--r-- 1 AzureAD+YuJimmy 4096   1572 Jun 25 17:51 struo-20260625.log
-rw-r--r-- 1 AzureAD+YuJimmy 4096  72757 Jun 26 16:26 struo-20260626.log
...
-rw-r--r-- 1 AzureAD+YuJimmy 4096  46961 Jul 28 20:28 struo-20260728.log
-rw-r--r-- 1 AzureAD+YuJimmy 4096 185558 Jul 29 17:52 struo-20260729.log
```

(one file per calendar day; oldest and newest shown above, the files in between elided — the directory
grows by one entry per day on any running install, so an exact count would go stale immediately.)

Like every other option in chapter 3, the Serilog configuration is built once at startup (both the
bootstrap logger and the full logger); changing `Serilog:*` needs a process restart, the file watcher
on `appsettings.json` notwithstanding. `builder.Host.UseSerilog(...)` reads from both `IConfiguration`
and the DI container (`ReadFrom.Services`), so any enrichers registered through DI are picked up as
well.

## Health probes for orchestrators

Chapter 2 introduces the two health routes; here is what each one actually checks, for wiring into an
orchestrator's liveness/readiness probes:

| Route | Checks run | Use |
|---|---|---|
| `/health/live` | None (`Predicate = _ => false`) — only confirms the process is accepting requests | Liveness probe: restart the container/pod if this stops responding at all. |
| `/health/ready` | Every check tagged `"ready"`: `DbReadinessCheck` (round-trips `IsValidConnection()` against the configured database) and `CacheReadinessCheck` (round-trips a `SetString`/`GetString` through the configured `IDistributedCache` — Redis, or the in-memory fallback when `Redis:ConnectionString` is empty) | Readiness probe: do not route traffic to an instance until both the database and the cache backend are reachable. |

## Backups

- **PostgreSQL** is the system of record for everything except uploaded file bytes — back it up with
  ordinary PostgreSQL tooling (`pg_dump`, `pg_basebackup`, or a managed service's own snapshot/PITR
  feature). Nothing in StruoCMS replaces or supersedes normal PostgreSQL backup practice.
- **Uploaded files** live outside the database: back up whichever `Struo:Files:Backend` is configured —
  the `Struo:Files:Local:RootPath` directory in `local` mode, or rely on the S3-compatible bucket's own
  versioning/replication in `s3` mode (chapter 3, chapter 11). A database-only backup silently misses
  every uploaded file.
- **Redis** holds only cookie-authentication session tickets (`DistributedCacheTicketStore` is the sole
  consumer of `IDistributedCache` besides the readiness check) — it is a session cache, not a system of
  record. Losing it simply forces every currently-authenticated user to log in again; it needs no backup
  of its own.
- **Migration scripts and configuration** (`db/migrations/`, `appsettings.*`, environment/secret-manager
  values) are ordinary source-controlled or deployment-pipeline artifacts — back them up the same way as
  the rest of the deployment, not as a database concern.

## The three test layers

- **Backend unit/integration** — `tests/Struo.Tests` (xUnit), run with `dotnet test`. SQLite by default:
  most tests build a unique temp-file database per test (`Support/SqliteTestDatabase.cs`, deleted on
  dispose). An opt-in live-PostgreSQL suite (`PostgresIntegrationTests`) exists specifically to catch
  the "SQLite-green ≠ Postgres-correct" class of bug; it activates only when `Testing:PostgresConnection`
  is configured (chapter 3's one exception to the `Section__Key` environment-variable convention — only
  `STRUO_TEST_PG_CONNECTION` actually works for this key) and otherwise every test in it is a no-op
  pass. It refuses to run against any database whose name does not contain `test`, so point it at a
  disposable one.
- **Frontend unit** — `pnpm test` (Vitest, `jsdom` environment, configured inside `frontend/vite.config.ts`'s
  `test` block). Component- and lib-level `*.test.ts` files sit next to the source they cover.
- **E2E** — Playwright (`frontend/playwright.config.ts`), two projects: `core` (`pnpm e2e`) runs the
  framework-only specs under `e2e/` (excluding `e2e/sample/**`) against the shipped template with zero
  content collections; `sample` (`pnpm e2e:sample`) runs `e2e/sample/**` and needs the Blog sample opted
  in first (chapter 16). `pnpm e2e:all` runs both projects. Playwright's own `webServer` block starts
  only the **frontend** dev server (`pnpm dev`, reused if one is already running) — both projects still
  need a running API and database reachable at the configured proxy target; nothing in the Playwright
  config starts either of those.

## What CI runs — and what it deliberately does not

`.github/workflows/ci.yml` defines exactly two jobs, both triggered on push to `main`, on every pull
request, and on manual dispatch:

- **`backend`** — `dotnet restore`, `dotnet build --no-restore --configuration Release`, then
  `dotnet test --no-build --configuration Release --verbosity normal` — the full backend
  unit/integration suite. No
  `STRUO_TEST_PG_CONNECTION` is set anywhere in the workflow, so the live-PostgreSQL suite's tests all
  take their no-op-pass path in CI; only the SQLite-backed tests actually exercise anything there.
- **`frontend`** — `pnpm install --frozen-lockfile`, then `pnpm test`, then `pnpm build` — the frontend
  unit suite plus a full production build (`vue-tsc -b && vite build`), which doubles as CI's only
  enforcement of the SPA's TypeScript types.

CI deliberately runs **neither** `pnpm e2e` nor `pnpm e2e:sample`/`pnpm e2e:all`: no step in `ci.yml`
starts a database, starts the API, or invokes `playwright test`. End-to-end coverage needs a live API
and a live database running alongside the frontend dev server — a heavier environment than either job
here sets up — so running it is a local, pre-merge discipline in this repository, not an automated gate.

## Next steps

- Chapter 3, [Configuration Reference](03-configuration-reference.md), for the full per-key semantics
  behind every row in the checklist above.
- Chapter 2, [Getting Started](02-getting-started.md), for the health-endpoint table and the dependency
  services this chapter assumes are already running.
- Chapter 16, [Sample Walkthrough](16-sample-walkthrough.md), for opting the Blog sample in — the
  prerequisite for `pnpm e2e:sample`.
