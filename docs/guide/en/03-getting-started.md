# 3. Getting Started

This chapter gets the API and admin SPA running and logs in once with the seeded admin account — either
with a single Docker Compose command that starts the whole stack, or by starting only the services
you need and running the rest yourself.

## Prerequisites

Starting the whole stack with Docker Compose needs only Docker plus Compose; the other tools below
are for running the API and admin SPA yourself.

You'll need:

- .NET SDK 10.0.x.
- Node.js 24 (the version CI uses).
- pnpm 10.x, applied automatically by corepack from the `packageManager` field — no need to pin a
  version yourself.
- Docker plus Compose — or your own PostgreSQL and Redis, in which case swap step 2's connection
  string for your own values.

## Start the whole stack with Docker Compose

This is the simplest way to start the services for development and testing:

```bash
docker compose up -d --build
```

It builds the API and admin images and starts four services: `postgres`, `redis`, `api` and
`admin`. On the first start the API runs the database migrations and seeds the admin account
with a generated password.

Wait for them to come up:

```bash
docker compose ps
```

`postgres`, `redis` and `api` must read `healthy`; `admin` has no health check, so `Up` is enough.
You can also run `docker compose up -d --build --wait`, which returns only once everything is ready.

Open `http://localhost:8080` and log in as `admin@admin.com`. The password is generated on the first
start and printed once in the `api` log:

```bash
docker compose logs api
```

Look for the Warning that begins `Bootstrap admin created`. The API itself is at
`http://localhost:8081`.

The host ports default to 8080 for the admin, 8081 for the API, 5432 for PostgreSQL and 6379 for
Redis. If any is taken, copy `.env.example` to `.env` and override `STRUO_ADMIN_PORT`,
`STRUO_API_PORT`, `STRUO_PG_PORT` or `STRUO_REDIS_PORT`. Only the host side changes; the services
still reach each other by service name.

The ports bind to `127.0.0.1` by default, so only this machine reaches them. To let other machines
on the network in, set `STRUO_BIND_ADDRESS` to `0.0.0.0`; the fixed dev passwords and the
password-less Redis are then exposed with them.

The `api` service runs as Production, so the session cookie is `Secure`. Browsers treat
`http://localhost` as a secure context, so logging in locally works; reaching the admin under any
other host name needs HTTPS in front.

The account is seeded only on the first start, while the database is still empty. To choose the
email and password yourself, set `STRUO_ADMIN_EMAIL`/`STRUO_ADMIN_PASSWORD` in `.env` before that
first start; a password you set must pass the password policy and may not be `admin`. This account
and the database credentials are for local development only.

`docker compose down` removes the containers but keeps the database and uploaded files in their
volumes; `docker compose down -v` deletes the volumes too, so the next start begins with an empty
database. Compose gives Redis no named volume and sessions go with it, so after `down` and `up` you
log in again; restarting only the API (`docker compose restart api`) keeps you logged in.

## Start the services you need

For development work or special needs, start only the services you need and run the rest yourself.
The six steps below use Compose only for PostgreSQL and Redis, and run the API and admin SPA on
your machine.

### 1. Start PostgreSQL and Redis

```bash
docker compose up -d postgres redis
```

This starts two services, `postgres` and `redis`. Confirm both pass their health check:

```bash
docker compose ps
```

The status must read `healthy` — `running` alone isn't enough.

The containers use dev-only credentials (`struo`/`struo`/`struo`), and the next step's config file
takes them as-is.

The host ports default to 5432 for PostgreSQL and 6379 for Redis; if something on your machine
already uses those ports, copy `.env.example` to `.env` and override them with `STRUO_PG_PORT` and
`STRUO_REDIS_PORT`. If you change `STRUO_PG_PORT`, update `Port=` in the next step's connection
string too.

### 2. Configure the API

```bash
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json
```

The copy works as-is: both the connection string and `Redis:ConnectionString` already match the
previous step's container defaults. `appsettings.Development.json` is listed in `.gitignore`, so it
is never committed.

You can override any config key with an environment variable: replace `:` with `__` — for example,
`Database__ConnectionString`. The exceptions are the four test-only `Testing:*Connection` keys, each
of which honors only its own `STRUO_TEST_*_CONNECTION` environment variable; see the last section of
[Chapter 4](04-configuration.md).

### 3. Create the tables and seed data

```bash
dotnet run --project src/Struo.Api -- migrate
```

`migrate` applies the migrations in order: first the framework tables, then the seed of languages,
the `admin` and `public` roles, and the admin account. The output starts with
FluentMigrator's log of each step and ends with one `Applied` line per migration; the exit code is
`0`:

```text
$ dotnet run --project src/Struo.Api -- migrate
[16:47:28 INF] Beginning Transaction
[16:47:28 INF] Rolling back transaction
[16:47:28 INF] VersionMigration migrating
[16:47:28 INF] Beginning Transaction
…
[16:47:28 INF] 202610080001: SeedCoreData migrated
[16:47:28 INF] => 0.0019894s
[16:47:28 WRN] Bootstrap admin created: admin@admin.com with the generated password Yo43Ui4K7N3WJB92vKVIxFL8. It is shown only once; change it after the first login.
Applied 202610080000  CreateCoreSchema
Applied 202610080001  SeedCoreData
```

This step is required: the copied settings file sets `Database:MigrateOnStartup` to `false`, and
the API refuses to start while the database has pending migrations. You can set it to `true`
instead, and the API applies them itself on every start; the six commands and the startup check are
in [Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md).

If the Compose stack above already built the database (both use the same PostgreSQL volume),
`migrate` prints only `Nothing to migrate.`.

### 4. Run the API

```bash
dotnet run --project src/Struo.Api
```

By default this uses the `http` profile: it listens on `http://localhost:5221`, with
`ASPNETCORE_ENVIRONMENT` set to `Development`.

A successful start shows:

```text
[18:30:38 INF] Now listening on: http://localhost:5221
[18:30:38 INF] Application started. Press Ctrl+C to shut down.
[18:30:38 INF] Hosting environment: Development
…
```

With `MigrateOnStartup` set to `false`, nothing about the database is logged before these lines;
with `true`, they come after FluentMigrator's log and one `Applied migration …` line per migration
applied.

If startup fails, the log prints a single Fatal entry and the process exits with code 1. Skipping
the previous step looks like this (stack trace omitted):

```text
[18:30:49 FTL] StruoCMS host terminated unexpectedly
System.InvalidOperationException: 2 migration(s) pending, the lowest is 202610080000; run the 'migrate' command, or set Database:MigrateOnStartup to true.
…
```

In another terminal, confirm the API responds:

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/api/ping
```

```text
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-10-12T10:30:38.9562717Z"}}
HTTP_STATUS:200
```

### 5. Run the admin SPA

Back in the terminal where you ran `curl`:

```bash
cd frontend
pnpm install
pnpm dev
```

A successful start shows:

```text
…
  VITE v8.3.2  ready in 299 ms

  ➜  Local:   http://127.0.0.1:5173/
```

The banner prints `127.0.0.1` (the dev server binds to IPv4); `http://localhost:5173` reaches the
same server.

The dev server forwards `/api` to `http://localhost:5221`, so the browser sees the admin SPA and the
API as the same origin — the development environment needs no separate CORS configuration. It
also forwards the OIDC callback and sign-out paths of every provider; to try external login,
register `http://localhost:5173/signin-oidc-{id}` with your provider, for example
`http://localhost:5173/signin-oidc-google` ([Chapter 16](16-authentication.md)).

### 6. First login

Open `http://localhost:5173` and log in as `admin@admin.com` with the password that step 3's `migrate`
printed. It is a Warning in the `migrate` output, shown once and never again.

Change the password right after logging in.

Step 3's `migrate` seeds this account, once, on an empty database. To choose the email
and password yourself, set `Auth__BootstrapAdmin__Email`/`Auth__BootstrapAdmin__Password` before running
`migrate`; once it is seeded, the only way to change them is to log in to the admin UI.

## What you'll see

After logging in, the sidebar shows only the System group, with no content collections — this is
the expected state; [Chapter 1: What StruoCMS Is](01-what-is-struocms.md) explains why.

You can also call the API's `/health/live` (returns 200 as long as the process is alive, running
no checks) and `/health/ready` (returns 200 only when both the database and the cache pass).
Both sit on the API's own port: `http://localhost:5221` when you run it yourself,
`http://localhost:8081` under Compose. On the admin port (8080) these paths return the SPA page
instead.

`/health/ready` has one caveat: when `Redis:ConnectionString` isn't configured, the cache check
falls back to an in-memory cache and still passes — so a green result here is not proof that Redis
is reachable.

The API docs live at `/scalar`, and the raw OpenAPI spec at `/openapi/v1.json`; both routes are
mapped in the Development environment only; everywhere else, Production included, they return 404
unless `OpenApi:Enabled` is `true`. Either one exposes the entire API spec with no authentication,
so if you deliberately enable them outside Development, put your own authentication layer in front.

## What's next

With the server running, the next step is defining your own first content collection — a later
chapter is dedicated to that. The full configuration reference is in
[Chapter 4: Configuration Reference](04-configuration.md).
