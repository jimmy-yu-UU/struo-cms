# 15. Deployment, Operations & Testing

Chapter 3 documents every configuration key's semantics. This chapter is about what happens if a
production deployment gets one of them wrong, plus the operational surface around configuration:
schema management, startup and failure behavior, logging, health probes, backups, and the four
layers of tests this repository ships.

## Production checklist

Every row below was read directly against the cited source and, where noted, verified live against a
disposable scratch database and a second, Production-mode instance of this build — never against the
shared development database or the running development API.

| Setting | Required value | Consequence if wrong |
|---|---|---|
| `Database:MigrationsPath` | An **absolute** path | `MigrationRunner.ApplyAsync` (`src/Struo.Infrastructure/Persistence/MigrationRunner.cs`) passes the configured value straight to `Directory.Exists(...)` with no content-root resolution of its own — unlike `Struo:Files:ImageTransform:CachePath`, which is explicitly resolved against `IHostEnvironment.ContentRootPath` (`AddStruoFiles`'s `IImageVariantCache` factory, `FileStorageServiceCollectionExtensions.cs`). A relative value therefore resolves against the **process's current working directory** at launch, which is not guaranteed to be the application's own folder (a systemd unit's `WorkingDirectory`, a container's `WORKDIR`, or any launcher that `cd`s elsewhere before starting the process can all differ from it). Confirmed live (`db/migrations/README.md` §6): a relative path that exists only relative to the repository root, not the process's actual working directory, throws `DirectoryNotFoundException: MigrationRunner: migrations directory not found: '<path>'` and the process exits with code `1`. The runner itself now applies on **every** backend, not just PostgreSQL; leaving this key empty (the default) disables it entirely, on any backend. |
| `Auth:BootstrapAdmin:Password` | Overridden before the **first** boot against a fresh database | Consulted only once, the first time the `users` table is created (`DataSeeder.cs`); a later boot never re-reads it. A `Production` start still seeded with the literal default `admin` logs a `WARNING` naming the exact setting to change (`DataSeeder.WarnIfDefaultAdminPasswordInProduction`) but does **not** refuse to start — verified live: `[17:30:16 WRN] Bootstrap admin is using the default password 'admin'. Change it immediately via Auth__BootstrapAdmin__Password.` The warning compares the **currently configured** value against the literal string `"admin"` inside `WarnIfDefaultAdminPasswordInProduction`, not the password hash actually stored for the account — so an operator who let the database seed with the default and only later sets a strong value in configuration silences the warning on every subsequent boot while the stored account still has the original default-password hash. |
| `RateLimiting:LoginAccount:Enabled` | `true` (the shipped default) for essentially every deployment | `AuthController.Login` checks `ILoginAttemptThrottle` before spending any Argon2id CPU (`src/Struo.Infrastructure/Identity/DistributedCacheLoginAttemptThrottle.cs`), throttling by the ACCOUNT named in the request body rather than by client IP. Turning it off leaves a distributed, slow password-spray against one account (many source IPs, one target account) completely unthrottled, since `RateLimiting:Login` below cannot see the request body and has no way to key on the account being attempted. Unlike the other two rate limiters in this table, whether this one is per-pod or global depends on `Redis:ConnectionString`: configured, it shares the same `IDistributedCache` as the session-ticket store, so every replica shares one counter per account instead of each pod keeping its own — though even then it's a shared counter, not a guaranteed cap, since `IDistributedCache` has no atomic read-modify-write and two concurrent failures against the same account can lose an increment (`DistributedCacheLoginAttemptThrottle`'s class doc has the detail); left empty in a multi-replica deployment, it silently degrades to the same in-process, per-pod cache the ticket store falls back to, and the effective limit becomes ≈N× the configured value like the other two. |
| `RateLimiting:Login:Enabled` | `false` (the shipped default) unless the deployment is single-instance, directly reachable, and its users don't share an egress IP | The in-app, per-client-IP login limiter partitions by `Connection.RemoteIpAddress` and its counters live in per-process memory (`Program.cs`, the `AddRateLimiter`/`AddPolicy("login", …)` block). Turning it `true` behind an admin-backend deployment whose staff share one NAT egress IP collapses that whole office into a single shared bucket — at the shipped `5`/`60` a handful of staff logging in around the same time trips the limit for everyone, a self-inflicted denial of service, and this needs no misconfigured proxy to happen. On more than one replica it additionally becomes ≈N× the configured value, inconsistently, and never a true global cap — the code's own comment states this is "delegated to the ingress/edge/WAF" for exactly that reason. `RateLimiting:LoginAccount` above is the layer that stays on by default instead. |
| Reverse-proxy forwarded headers | The deployment must add `UseForwardedHeaders` (with `KnownProxies`/`KnownNetworks`) itself | `Program.cs` never calls `app.UseForwardedHeaders(...)` anywhere in the pipeline — the comment beside the login limiter's partition key says so explicitly ("deliberately out of scope here"). Behind any reverse proxy, `Connection.RemoteIpAddress` is the **proxy's** address, not the real client's, so if `RateLimiting:Login:Enabled` is turned on behind one anyway, every login attempt through that proxy collapses into a single rate-limit partition — every user behind the proxy shares one 5-per-60s bucket, an even worse case of the shared-egress collapse described in the row above. |
| `RateLimiting:Password:Enabled` | `false` only when a global per-user limit doesn't matter for a multi-pod deployment | The change-password limiter partitions by the **authenticated caller's user id**, not `Connection.RemoteIpAddress`, so it does **not** share the reverse-proxy collapse-to-one-bucket failure mode in the row above. It still shares the login limiter's other failure mode, though: its counters are in-process, per-pod memory (`Program.cs`, `AddPolicy("password", …)`), so behind N replicas a given user's requests can land on different pods and the effective per-user limit becomes ≈N× the configured value rather than a true global cap. |
| Scalar / OpenAPI | Not reachable in Production — do not rely on network-level blocking alone | `app.MapOpenApi()` and `app.MapScalarApiReference(...)` are both guarded by `if (!app.Environment.IsProduction())` (`Program.cs`). Verified live against a Production-mode instance: `GET /scalar` → 404, `GET /openapi/v1.json` → 404. (Chapter 10 documents the adjacent GraphQL schema-disclosure routes — introspection and `GET /graphql?sdl` — which are gated by `GraphQl:ExposeSchema`, also Production-closed by default.) |
| `Redis:ConnectionString` | Set to a real Redis instance for any deployment with more than one API replica, or any deployment where sessions must survive a restart | Empty falls back to `AddDistributedMemoryCache()` (`AuthWiring.cs`) — an in-process, per-instance cache backing the cookie-authentication ticket store. A restart loses every session (forced re-login); with more than one replica behind a load balancer, each replica has its own session store, so a user's session is only valid on whichever replica issued it. |
| Cookie `Secure` policy | The reverse proxy/load balancer must terminate HTTPS in front of a Production deployment | `AuthWiring.cs` sets `CookieSecurePolicy.Always` whenever `env.IsProduction()` (`CookieSecurePolicy.SameAsRequest` otherwise, so the dev/test HTTP host still works). Serving Production over plain HTTP means the browser never sends the auth cookie back on any subsequent request — login appears to succeed once and then silently never persists. |
| `Oidc:RequireEmailVerified` / `AllowedTenantId` / `AllowedEmailDomains` | Pinned explicitly whenever `Oidc:Enabled=true` | `AllowedTenantId` ships as the non-matching placeholder `"REPLACE_TENANT_ID"` (`appsettings.json`), and `ExternalLoginService.ResolveOrProvisionAsync` (`src/Struo.Application/Security/ExternalLoginService.cs`) rejects any external identity whose tenant doesn't equal it (`TenantNotAllowed`) — so with the **shipped** defaults, external login fails closed for every real tenant until this is replaced with the real one. `RequireEmailVerified` (`false`) and `AllowedEmailDomains` (`[]`) do default permissive, though: once `AllowedTenantId` is set to a real, matching tenant, the only remaining checks are opt-in, and linking then proceeds by **email equality alone** — any external account whose claimed email matches an existing local user's is treated as that user, verification status notwithstanding. |
| Security response headers | Not configurable — the application always sends `X-Content-Type-Options: nosniff`; the reverse proxy is responsible for `Strict-Transport-Security`, `X-Frame-Options`/CSP `frame-ancestors`, and `Referrer-Policy`; `frontend/nginx/default.conf.template` is a working reference implementation | An inline middleware registered first in `Program.cs`'s pipeline sets `X-Content-Type-Options: nosniff` on every response — a plain 200, an error envelope, a CORS preflight, a bare 404 — unless a downstream handler has already set it. `Struo.Api` serves no HTML of its own (no `wwwroot`, `UseStaticFiles`, or `MapFallbackToFile`; the admin SPA is deployed separately), so this is defence in depth behind the file-download endpoint's `Content-Disposition: attachment` (`FilesController.Download`) and the image-transform path's fixed raster content types, not a patch for an existing hole. The application does **not** send the other usual headers itself: skipping them at the reverse proxy leaves whatever HTML surface sits behind it (e.g. the separately deployed admin SPA) without HSTS's downgrade protection, without clickjacking protection from `X-Frame-Options`/CSP `frame-ancestors`, and without a `Referrer-Policy` restricting what leaks to a link target — this API's own JSON responses carry none of that exposure to begin with. |

## Schema management

Schema management is split into three layers by **responsibility**, not by environment: CodeFirst
creates tables that do not exist (always, everywhere), `Database:AutoSyncSchema` alters existing ones
from an automatic diff (Development only, off by default), and `MigrationRunner` applies reviewed
`.sql` scripts (all environments, off by default). `db/migrations/README.md` §2 has the full
responsibility table; this section covers what a **deployment** has to get right about it, and the
auto-sync hazards below.

**Startup order** (`Program.cs`): a table-name snapshot → `CreateMissingTables` (all environments, all
backends, unconditional) → optional `SyncSchema` (only runs its full sync if `Database:AutoSyncSchema=true`
*and* the host is in Development — set anywhere else, it is ignored with a logged warning rather than
honored) → `MigrationRunner.ApplyAsync` (only if `Database:MigrationsPath` is configured) → dev-only
`SchemaGuard` → `DataSeeder` seeding. Table creation runs first because the migration runner's `ALTER`
scripts target tables that must already exist by the time it runs; `SchemaGuard` and seeding both run
last because they depend on the schema already being in its final shape.

**The one initialization guarantee that holds everywhere:** in any environment, on any of the five
configured backends, whenever an entity type's table does not yet exist, it gets created before anything
else runs, and `DataSeeder` then seeds initial data into whichever tables were newly created during that
startup. This is deliberately uniform across every environment and backend — it closes a real gap this
repository used to have, where a `Production` deployment configured for `MySql`, `SqlServer` or `Oracle`
started up cleanly against a completely empty database (no schema, no seed data, `DbReadinessCheck`
reporting healthy) and only failed on the first query, because table creation used to run in Development
only and the migration runner used to be gated to PostgreSQL alone. That said, "uniform" describes the
*code path*, not the evidence behind it: PostgreSQL is the only backend this repository verifies against
a live instance (chapter 1). `MySql`, `SqlServer` and `Oracle` are type-mapped by design so this same
code path is expected to produce valid DDL on them too, but that expectation is backed by no live run
against any of the three — treat table creation on those three as untested until you have run it
yourself.

**What never happens automatically: an existing table.** The only two ways an already-existing table gets
altered are an explicit `Database:AutoSyncSchema=true` in Development, or a reviewed script applied by
`MigrationRunner`. Both are opt-in and off by default; the default configuration only ever creates tables
that do not yet exist, on any backend, in any environment.

### The nine auto-sync hazards

`Database:AutoSyncSchema` (bool, default `false`, Development-only — chapter 3) turns on a full CodeFirst
structural sync against tables that already exist, allowing SqlSugar to add, modify and drop columns to
match the entity classes exactly. It is powerful, and on a table holding data you care about, dangerous.
Measured directly on real PostgreSQL, a removed column really is dropped
(`PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres`). On SQLite the
identical unfiltered call leaves the column in place instead
(`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite`) — not because SQLite
or SqlSugar's SQLite dialect lacks the capability (SqlSugar's `SqliteCodeFirst.ExistLogic` does
implement `DROP COLUMN`), but because that code path is gated behind
`ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn`, which this repository's
`SqlSugarClientFactory` never sets (row 7 below has the detail). So this repository's SQLite-only CI
suite cannot demonstrate that a removed column is actually dropped; only the live PostgreSQL run does.
The governing rule:

> Any structural change that touches existing data must go through a migration. `AutoSyncSchema` is
> intended only for fast schema iteration in Development, on a schema that does not yet hold any real
> data.

| # | Scenario | What auto-sync actually does | Correct handling |
|---|---|---|---|
| 1 | **Column rename** | Read as "old property gone + new property appeared" → `DROP COLUMN` + `ADD COLUMN`, **that column's data is permanently lost** | Write an `ALTER TABLE … RENAME COLUMN` migration first, then change the entity |
| 2 | Remove a property | `DROP COLUMN`, data lost | Confirm the data is genuinely no longer needed; in Production do this via an explicit migration |
| 3 | Type narrowing (e.g. `varchar(255)` → `varchar(50)`) | Depending on the engine: fails, or **silently truncates** | Three-step migration: add the new column → backfill and validate → cut over and drop the old one |
| 4 | Add a `NOT NULL` column to an existing populated table | `ALTER` fails, startup aborts | Three-step migration: add it nullable first → backfill → then add the `NOT NULL` constraint |
| 5 | Add `UNIQUE` to a column that already has duplicate values | `ALTER` fails, startup aborts | Migration deduplicates first (a data operation), then adds the constraint |
| 6 | Split/merge columns, or extract a new table | A structural diff cannot express this intent; the result is always either data loss or empty columns | Always a migration |
| 7 | Dropping a column on the SQLite backend | Measured (SqlSugarCore 5.1.4.215): a removed column survives on SQLite in this repository (`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite`) because `SqliteCodeFirst.ExistLogic` gates the drop of a column the entity no longer declares behind `ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn`, which this repository's `SqlSugarClientFactory` never sets. That flag does **not** gate every CodeFirst-issued `DROP COLUMN` on SQLite: `ExistLogic`'s key-change path calls `SqliteCodeFirst.ChangeKey`, which calls `DbMaintenance.UpdateColumn`, and `SqliteDbMaintenance.UpdateColumn` rewrites a column by adding a temp column, copying the data, and dropping the original (two `DropColumn` calls, neither flag-checked). What the flag governs is the removed-property scenario measured here. Verified against upstream (tag `5.1.4.197`): the drop set itself is unfiltered — `Realization/Sqlite/CodeFirst/SqliteCodeFirst.cs:24-27` puts *every* removed column into `dropColumns`, `PRIMARY KEY`/`UNIQUE`/indexed included, with no exclusion — and the actual drop, `DbMaintenance.DropColumn` (`Abstract/DbMaintenanceProvider/Methods.cs:532-538`), just formats `SqliteDbMaintenance`'s `DropColumnToTableSql` (`Realization/Sqlite/DbMaintenance/SqliteDbMaintenance.cs:118-124`, plain `ALTER TABLE {0} DROP {1}`) and executes it — no table-rebuild fallback, and `SqliteDbMaintenance` does not override `DropColumn` itself. So a fork that turns the flag on gets more than one outcome, not a uniform unlock: an ordinary column drops silently; a column SQLite itself refuses to drop natively (e.g. `PRIMARY KEY`, `UNIQUE`, indexed, referenced by a `CHECK` constraint or a `FOREIGN KEY`, or referenced by a generated column, a partial index, a trigger, or a view; native support since SQLite 3.35.0, 2021) makes the raw `ALTER TABLE` fail and the exception propagate — the sync throws and startup aborts; it is **not** dropped | This repository's configuration is why the column survives here — but flipping the flag does not uniformly "unlock" SQLite drops: it trades silent data loss for ordinary columns against a hard startup failure for restricted ones. Never assume a backend's own native DDL capability is what SqlSugar's CodeFirst sync actually does on it — verify per backend, as this repository does for PostgreSQL |
| 8 | Multiple replicas (`replicas > 1`) starting concurrently | Every replica computes and runs its own DDL diff at the same time — a race | See "Known limits" below |
| 9 | Wanting to preview a deployment | The DDL that will run **cannot be previewed** — it is computed from the live code diff at startup | This is the core reason `AutoSyncSchema` is never meant to be turned on in Production |

The reason so many of these are real hazards rather than edge cases is structural, not incidental:
CodeFirst's automatic sync computes a **structural diff** between the entity classes and the live
table — it knows the target shape, but it has no idea what you *intended*. A rename and a "drop one
column, add another" are indistinguishable to a diff.

### Writing a migration

Authoring rules — file naming, the portable-SQL prefer/avoid tables, why idempotency is *not* required,
filename-only tracking, the time-zone-aware timestamp convention, and the runner's known limits (no DDL
rollback on MySQL/Oracle, no advisory lock, no checksum/down-migration/dry-run) — all live in
**`db/migrations/README.md`**, §§4–6. That file is the single home for them; this chapter does not
restate it.

Two of those limits have direct deployment consequences worth repeating here:

- **No advisory lock.** Concurrently-starting replicas pointed at the same `Database:MigrationsPath` may
  each attempt the same pending file. Deploy schema changes with a single replica first, or as a separate
  one-off job. The same caveat applies to CodeFirst table creation and to `AutoSyncSchema` (hazard #8).
- **A deploy pipeline that copies `db/migrations/*.sql` may no longer create the directory itself.** The
  template ships **zero** `.sql` files, where it previously always shipped `001-core-baseline.sql`, so a
  pipeline step that copies whatever exists there no longer guarantees the directory exists in the
  deployed image. If `Database:MigrationsPath` is configured and the directory is absent, startup throws
  `DirectoryNotFoundException` and the process exits `1` (the `Database:MigrationsPath` row in the
  checklist above). Confirm the pipeline creates the directory — even empty — wherever this key is set.

### Upgrading core across a fork

Because the template ships zero SQL scripts and table creation is create-only, **a change to StruoCMS
core's own schema cannot automatically reach an existing fork's deployment.** Core schema changes are
announced in release notes; each fork writes its own `ALTER` script for the backend it actually runs.
`db/migrations/README.md` §7 covers the procedure, including using `AutoSyncSchema=true` against a
*copy* of the production schema as a diagnostic hint — never as the execution plan, since the hazard
table above applies to that diff exactly as it does anywhere else.

## Startup behavior and failure modes

- **Fail-fast options validation:** `Database`, `Struo:Files`, `Oidc`, `Query` and `Auth:Password` are
  all bound with `ValidateOnStart`; a missing `Database:ConnectionString`, an `Oidc:Enabled=true`
  configuration missing `ClientId`, or an out-of-range `Query:MaxLimit` all throw
  `OptionsValidationException` before the app
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
     at Struo.Infrastructure.Persistence.MigrationRunner.ApplyAsync(ISqlSugarClient db, String migrationsDirectory, ILogger logger, CancellationToken ct)
     at Program.<Main>$(String[] args)
  ```

  Stack-frame line numbers are omitted deliberately: this transcript predates later edits that shifted
  both `MigrationRunner.cs` and `Program.cs`, so the original numbers no longer point at the right
  lines — the behavior itself, and the process's own exit code of `1`, are unchanged.
- **Dev schema guard:** `SchemaGuard.AssertCriticalConstraintsAsync` runs in Development only, after
  table creation, the optional `SyncSchema`, and the migration runner, and asserts that the `revisions`
  composite UNIQUE index and each configured translation sidecar's UNIQUE `(fk, locale)` index
  physically exist — throwing `InvalidOperationException` with an actionable message if either is
  missing, rather than letting the app run with a silent correctness gap. It is a fail-fast dev
  convenience, not a Production safety net — Production schemas are expected to already carry these
  indexes from CodeFirst table creation or a fork's own migrations, and Production gets no automatic
  check that they actually do.

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

## Container images

Two Dockerfiles ship in this repository, each producing one independent image: the root `Dockerfile`
builds the API, `frontend/Dockerfile` builds the admin SPA. There is no production `docker compose`
file here — see "What deployment is still on you" below.

**Building** — the same two commands the CI `docker` job runs, with a local tag:

```bash
docker build --tag struo-api:local .
docker build --tag struo-admin:local frontend
```

The API image's build context is the whole repository, not just `src/Struo.Api/`: a fork's content
project is referenced from `Struo.Api.csproj` as a `ProjectReference`, so the build needs every project
that reference can point at, plus the root `Directory.Build.props`/`Directory.Packages.props`
(`Dockerfile`'s own comment 1). The root `.dockerignore` keeps `frontend/`, `docs/`, and the usual
build/test artifacts out of that context.

### The API image

- Listens on port `8080` (`EXPOSE 8080`) and runs as the non-root user `app`, not root.
- `db/migrations` is copied into the image at `/app/db/migrations`. `Database:MigrationsPath` is left
  unset by default, matching `appsettings.json`'s own default of not running migrations automatically
  on boot — set it explicitly to `/app/db/migrations` to turn the runner on inside this image (it must
  be an absolute path; see the checklist row above).
- Two directories are writable by `app`: `/app/App_Data` (local-backend uploads and the image-transform
  cache) and `/app/logs` (Serilog's File sink). `/app/App_Data` is additionally declared as a
  `VOLUME`, so even a plain `docker run` with no `--mount`/`-v` gets an anonymous volume there instead
  of writes silently landing in the container's writable layer.
- `HEALTHCHECK` polls `http://localhost:8080/health/live` every 30 s (5 s timeout, 30 s start period, 3
  retries) — the liveness route, not `/health/ready`. `docker ps`/`docker inspect`'s health status
  therefore reflects only "the process is accepting requests," not database or cache reachability; see
  the health-probe table below for what each route actually checks.

Verified live against PostgreSQL: with `Database:ConnectionString` pointed at a PostgreSQL instance
reachable from inside the container (`host.docker.internal` when the database runs on the host — this
resolves out of the box on Docker Desktop only; on Linux, add
`--add-host=host.docker.internal:host-gateway` to the `docker run` invocation) and
`Database:MigrationsPath=/app/db/migrations`, startup created the eleven core tables and the migration
runner ran against that directory with zero pending scripts — `db/migrations/` ships only its
`README.md`. `/health/ready` reported `Healthy` in roughly 2 seconds. A real upload made through the
SPA image landed at `/app/App_Data/uploads/<yyyy>/<MM>/<id>.<ext>`, confirming
`Struo:Files:Local:RootPath`'s default resolves inside the declared volume as expected.

#### Environment variables

Every key below exists in `src/Struo.Api/appsettings.json` (chapter 3 has the full reference); only the
ones most relevant to running the image are listed here.

| Variable | Purpose | Notes |
|---|---|---|
| `Database__DbType` | Which backend to connect to | `PostgreSQL` (the shipped default), `Sqlite`, `MySql`, `SqlServer`, or `Oracle` (`StruoDbType.cs`); the CI smoke test uses `Sqlite`. |
| `Database__ConnectionString` | Connection string for that backend | Ships as a full PostgreSQL connection string with `REPLACE_ME` credentials (`Host=localhost;Port=5432;Database=struo;Username=REPLACE_ME;Password=REPLACE_ME`) — required for the image to start at all. |
| `Database__MigrationsPath` | Directory of reviewed `.sql` migration scripts applied at startup | Must be an **absolute** path (see the checklist above). Empty/unset — the default — disables the runner entirely. Inside this image, the migrations directory is `/app/db/migrations`. |
| `Redis__ConnectionString` | Distributed cache backing the session-ticket store | Empty (the default) falls back to an in-process cache — fine for one replica, not for more than one. |
| `Struo__Files__Backend` | `local` or `s3` | Default `local`, writing under `Struo:Files:Local:RootPath` (`App_Data/uploads`, inside the declared volume). |
| `Struo__Files__S3__Endpoint` / `__Bucket` / `__AccessKey` / `__SecretKey` | S3-compatible storage credentials | Only consulted when `Struo__Files__Backend=s3`; all four ship as `REPLACE_ME` placeholders. |
| `Struo__Files__S3__Region` | S3 region | Ships with a real default, `us-east-1`, not a placeholder — override it if the bucket is not there. |
| `Auth__BootstrapAdmin__Email` / `__Password` | Overrides the seeded default admin account | Only read the **first** time the `users` table is created — see the checklist row above. |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core hosting environment | Defaults to `Production` in this image — the base image's own default, not something this `Dockerfile` sets — see the paragraph below on why that matters. |

Because `ASPNETCORE_ENVIRONMENT` defaults to `Production`, `CookieSecurePolicy.Always` applies (the
Cookie `Secure` policy row in the checklist above): a login over plain HTTP appears to succeed but the
browser never sends the cookie back on a later request. Either terminate TLS in front of the container,
or set `ASPNETCORE_ENVIRONMENT=Development` for a local, HTTP-only smoke test only — never for a real
deployment.

#### DataProtection keys

ASP.NET Core's DataProtection key ring — what signs and encrypts the auth cookie and antiforgery tokens
— is written inside the container, at `/home/app/.aspnet/DataProtection-Keys`. Nothing in this image
persists or shares that directory: it is not mounted as a volume, and the application logs a warning
about it at startup. Two consequences follow: replacing the container (a redeploy, a restart after an
image update) invalidates every existing auth cookie and antiforgery token, forcing every user to log
back in; and running more than one replica gives each one its own, unshared key ring, which breaks
cookie validation and antiforgery for any request a load balancer routes to a different replica than
the one that issued it. For a single instance, mount `/home/app/.aspnet/DataProtection-Keys` as a
volume so keys survive a container replacement. For more than one replica, configure a shared
DataProtection key store instead (a shared filesystem, Redis, or a cloud provider's key-ring service) —
this repository does not configure one itself; treat this as an honest limitation of the image, not a
feature.

### The admin SPA image

- Listens on port `80` (`EXPOSE 80`) and serves the Vite production build through nginx.
- `API_UPSTREAM` (default `http://api:8080`) is the backend to reverse-proxy `/api/*` to. It must be
  `scheme://host:port` with **no path and no trailing slash**: `proxy_pass` is given as an nginx
  variable rather than a literal (so the container can start even before the API is resolvable), and
  with a variable target any path segment on `API_UPSTREAM` *replaces* the request URI instead of being
  prefixed to it (`frontend/nginx/default.conf.template`, comment (d)).
- `HSTS_VALUE` defaults to empty, which means the image does **not** send `Strict-Transport-Security`
  at all — nginx omits an `add_header` whose value is the empty string. Set it (e.g.
  `max-age=31536000; includeSubDomains`) only once TLS is actually terminated here or by a fronting
  proxy for every request that can reach the container.
- Three security headers are always sent, independent of `HSTS_VALUE`: `X-Content-Type-Options:
  nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: strict-origin-when-cross-origin`. This is
  what the Security response headers row in the checklist above means by "the reverse proxy is
  responsible" — `frontend/nginx/default.conf.template` is a working reference implementation of that
  responsibility, not a requirement to use nginx specifically. The API's own `X-Content-Type-Options:
  nosniff` is hidden on proxied `/api/*` responses (`proxy_hide_header`), so the header appears exactly
  once on those responses, not twice.
- Caching: `index.html` is served `Cache-Control: no-cache` (it references the content-hashed bundle,
  so a stale cached copy would keep pointing at files a newer deploy replaced); everything under
  `/assets/` is served `Cache-Control: public, max-age=31536000, immutable` (Vite's filenames are
  content-hashed, so a new build is a new URL). gzip is on for the SPA's text payloads,
  `server_tokens off` stops nginx advertising its version, and `client_max_body_size 32m` gives some
  headroom above the backend's `Struo:Files:MaxUploadBytes` (25 MiB / 26214400 bytes,
  `src/Struo.Api/appsettings.json`) so a genuinely oversized upload gets the backend's own JSON error
  envelope rather than nginx's HTML error page.
- `NGINX_RESOLVER` (default `127.0.0.11`, Docker's embedded DNS, with a 5 s `resolver_timeout`) only
  exists on a **user-defined** Docker network — the kind `docker compose` or `docker network create`
  produces. On the default bridge network (a bare `docker run` with no `--network`), every `/api/*`
  request 502s after the resolver timeout, because nothing listens at `127.0.0.11` there. Put both
  containers on a user-defined network, or point `NGINX_RESOLVER` at a DNS server that actually is
  reachable from wherever the image runs.

### What deployment is still on you

Neither image is a deployment on its own — beyond the DataProtection key ring above:

- **HTTPS termination.** Neither image terminates TLS itself; both expect a reverse proxy or load
  balancer in front of them (the Cookie `Secure` policy row in the checklist above).
- **`UseForwardedHeaders`.** If the API sits behind any reverse proxy — including the admin SPA image's
  own nginx — configure `UseForwardedHeaders` with an explicit `KnownProxies`/`KnownNetworks` allowlist,
  as the checklist row above already covers.
- **Orchestration.** Restart policies, scaling, secret injection, and health-check wiring into whatever
  platform runs these images (Kubernetes, ECS, a plain `docker run` with `--restart`, ...) are outside
  what either Dockerfile provides. This repository does not ship a production `docker compose` file.

### Verifying the two images together

The minimal way to confirm both images can actually talk to each other — a **verification recipe**, not
a deployment topology:

```bash
docker network create struo-verify
docker run --detach --name api --network struo-verify \
  --env Database__DbType=Sqlite \
  --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' \
  struo-api:local
docker run --detach --name admin --network struo-verify --publish 8081:80 \
  struo-admin:local
```

The admin container's default `API_UPSTREAM=http://api:8080` resolves because both containers sit on
the same user-defined network `struo-verify` and the API container is named `api` — the same reasoning
as the `NGINX_RESOLVER` note above. Open `http://localhost:8081` and confirm the SPA loads and its
`/api/*` calls reach the backend.

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

## The four test layers

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
- **Schema contract** — a committed pair of files, `schema/core-collections.json` (the seven core
  collections in `GET /api/schema`'s wire shape) and `schema/interfaces.json` (every declared
  `FieldInterface` and `RelationInterface` member), asserted from both sides: the backend snapshot test
  `tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs` and the frontend contract test
  `frontend/tests/schemaContract.test.ts`, which feeds the same files through the real
  `selectListColumns` and field-type `registry`. This is a fourth *kind* of test, not a fourth command:
  both halves ride inside `dotnet test` and `pnpm test` above. `schema/README.md` is authoritative,
  including the `UPDATE_SCHEMA_SNAPSHOT=1` regeneration step.

## What CI runs — and what it deliberately does not

`.github/workflows/ci.yml` defines six jobs. `backend`, `frontend`, `docs` and `docker` all trigger on
push to `main`, on every pull request, and on manual dispatch; `sonar-backend` and `sonar-frontend` add
a condition on top of that (see their own bullet below):

- **`backend`** — `dotnet restore`, `dotnet build --no-restore --configuration Release`, then
  `dotnet test --no-build --configuration Release --verbosity normal` — the full backend
  unit/integration suite. No
  `STRUO_TEST_PG_CONNECTION` is set anywhere in the workflow, so the live-PostgreSQL suite's tests all
  take their no-op-pass path in CI; only the SQLite-backed tests actually exercise anything there.
- **`frontend`** — `pnpm install --frozen-lockfile --ignore-scripts`, then `pnpm test`, then `pnpm build` — the frontend
  unit suite plus a full production build (`vue-tsc -b && vite build`), which doubles as CI's only
  enforcement of the SPA's TypeScript types.
- **`docs`** — `pnpm install --frozen-lockfile --ignore-scripts`, then `pnpm build` from `docs/` — the
  manual's own gate: `vitepress build` resolves every cross-chapter link and fails on a dead one, then
  a `check-rendered-chapters.mjs` step asserts every chapter actually rendered non-empty content, since
  `vitepress build` alone exits `0` even when a page comes out empty.
- **`docker`** — builds both container images (the root `Dockerfile` for the API, `frontend/Dockerfile`
  for the admin SPA; the *Container images* section above has the detail) and smoke-tests each one: the
  API image boots against SQLite (`Database__DbType=Sqlite`,
  `Database__ConnectionString=Data Source=/tmp/struo-ci.db`) and the job polls `/health/ready` until it
  reports healthy; the SPA image boots and the job greps the served `/` for `assets/index-`, confirming
  the built bundle is actually reachable through nginx. It is deliberately **not** a sixth standing
  gate — contributors do not need Docker installed locally to work on this repository — and, unlike the
  two `sonar-*` jobs below, it needs no secret, so it runs the same way for a pull request from a fork
  and for Dependabot as it does for a normal push.
- **`sonar-backend`** / **`sonar-frontend`** — report to SonarQube Cloud. Neither is a standing gate,
  and both are skipped for pull requests from forks and for Dependabot, since both need `SONAR_TOKEN`, a
  secret those contexts cannot read — the opposite of the `docker` job above.

The five standing gates remain `dotnet build`, `dotnet test`, `pnpm test` and `pnpm build` (the latter
two from `frontend/`), plus `pnpm build` from `docs/` — `docker`, `sonar-backend` and `sonar-frontend`
are additional jobs, not additional gates.

CI deliberately runs **neither** `pnpm e2e` nor `pnpm e2e:sample`/`pnpm e2e:all`: no step in `ci.yml`
starts a database, starts the API, or invokes `playwright test`. End-to-end coverage needs a live API
and a live database running alongside the frontend dev server — a heavier environment than either job
here sets up — so running it is a local, pre-merge discipline in this repository, not an automated gate.

The schema contract gate needed no `ci.yml` change at all: `CoreSchemaSnapshotTests` is just another
test in the `backend` job's existing `dotnet test` step, and `schemaContract.test.ts` is just another
test in the `frontend` job's existing `pnpm test` step — both halves ride inside the same two commands
already covered above.

## Next steps

- Chapter 3, [Configuration Reference](03-configuration-reference.md), for the full per-key semantics
  behind every row in the checklist above.
- Chapter 2, [Getting Started](02-getting-started.md), for the health-endpoint table and the dependency
  services this chapter assumes are already running.
- Chapter 16, [Sample Walkthrough](16-sample-walkthrough.md), for opting the Blog sample in — the
  prerequisite for `pnpm e2e:sample`.
