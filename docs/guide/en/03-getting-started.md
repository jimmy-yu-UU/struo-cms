# 3. Getting Started

Follow these five steps and within ten minutes you'll have the API and admin SPA running, with one
login using the default account.

## Prerequisites

You'll need these tools ready:

- .NET SDK 10.0.x.
- Node.js 24 (the version CI uses).
- pnpm 10.x, applied automatically by corepack from the `packageManager` field — no need to pin a
  version yourself.
- Docker plus Compose, or set up PostgreSQL and Redis yourself; if self-hosted, swap the connection
  string in the next step for your own values.

## 1. Start PostgreSQL and Redis

```bash
docker compose up -d
```

This starts two containers, `struo-postgres` and `struo-redis`. Confirm both pass their health
check:

```bash
docker compose ps
```

The status must read `healthy` — `running` alone isn't enough.

The containers use dev-only credentials (`struo`/`struo`/`struo`); the next step's config file uses
them as-is.

The exposed ports default to PostgreSQL 5432 and Redis 6379; if something on your machine already
uses those ports, copy `.env.example` to `.env` and override them with `STRUO_PG_PORT` and
`STRUO_REDIS_PORT`. If you change `STRUO_PG_PORT`, update the `Port=` in the next step's connection
string too.

## 2. Configure the API

```bash
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json
```

Copied as-is it works immediately: both the connection string and `Redis:ConnectionString` match the
previous step's container defaults. `appsettings.Development.json` is listed in `.gitignore`, so it
is never committed.

Any config key can be overridden by an environment variable by replacing `:` with `__` — for
example, `Database__ConnectionString`. The one exception is `Testing:PostgresConnection`, which only
honors `STRUO_TEST_PG_CONNECTION`.

## 3. Run the API

```bash
dotnet run --project src/Struo.Api
```

By default this uses the `http` profile: it listens on `http://localhost:5221`, with
`ASPNETCORE_ENVIRONMENT` set to `Development`.

A successful start shows:

```text
[11:38:30 INF] Now listening on: http://localhost:5221
[11:38:30 INF] Application started. Press Ctrl+C to shut down.
[11:38:30 INF] Hosting environment: Development
```

These three lines are the tail of the output. When the tables already exist, a `DatabaseInitializer`
line above them reports the schema is up to date, followed by three `DataSeeder: skip …` lines. On
the very first start against an empty database, the lines above instead record table creation and
seeding.

If startup fails, the log prints one Fatal entry and exits with code 1.

In another terminal, confirm the API responds:

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/api/ping
```

```text
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-09-10T03:39:30.3332812Z"}}
HTTP_STATUS:200
```

## 4. Run the admin SPA

Back in the terminal where you ran `curl`:

```bash
cd frontend
pnpm install
pnpm dev
```

A successful start shows:

```text
  VITE v8.2.0  ready in 276 ms
  ➜  Local:   http://127.0.0.1:5173/
```

The banner prints `127.0.0.1` (the dev server binds to IPv4); `http://localhost:5173` reaches the
same server.

The dev server forwards `/api` to `http://localhost:5221`, so the browser sees the admin SPA and the
API as the same origin — the development environment needs no separate CORS configuration.

## 5. First login

Open `http://localhost:5173` and log in with the default account: `admin@admin.com` / `admin`.

Change the password right after logging in.

If you start with the Production environment while the password is still the default, the log
records only a warning naming `Auth__BootstrapAdmin__Password` — it does not block startup.

This account is seeded only the first time the `users` table is created. To start with a different
email and password, set `Auth__BootstrapAdmin__Email`/`Auth__BootstrapAdmin__Password` before the
first start; after the first start, they can only be changed by logging in to the admin SPA.

## What you'll see

After logging in, the sidebar shows only the System group, with no content collections — this is the
expected state, for the reason covered in
[Chapter 1: What StruoCMS Is](01-what-is-struocms.md).

You can also hit `/health/live` (returns 200 as long as the process is alive, running no checks) and
`/health/ready` (returns 200 only when both the database and the cache pass). `/health/ready` has one
caveat worth noting: when `Redis:ConnectionString` isn't configured, the cache check falls back to an
in-memory cache and still passes — so this green result is not proof that Redis itself is actually
reachable.

The API docs live at `/scalar`, and the raw OpenAPI spec at `/openapi/v1.json`; both routes are only
available outside the Production environment — Production returns 404. Both routes expose the entire
API spec with no authentication at all; if you open them in Production, add your own layer of
authentication in front.

## What's next

Now that it's running, the next step is defining your own first content collection — that's covered
in a later chapter dedicated to defining collections. The full configuration reference is in
[Chapter 4: Configuration Reference](04-configuration.md).
