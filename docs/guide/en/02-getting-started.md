# 2. Getting Started

This chapter boots the template exactly as shipped — zero content collections — end to end: dependency
services, configuration, the API, the admin SPA, and first login. Every command below was executed
successfully against this checkout while writing this chapter.

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.x (see `global.json`, `rollForward: latestMinor`) | `dotnet --version` |
| Node.js | 24.x | `node --version`; matches the version CI pins |
| pnpm | 10.x | `pnpm --version`; matches the version CI pins |
| Docker (with Compose) | any recent version | runs PostgreSQL and Redis for local development |

## 1. Start the dependency services

```bash
docker compose up -d
```

This starts **only PostgreSQL and Redis** — the two services the shipped default configuration needs.
Confirmed with `docker compose ps`: on a clean machine it lists exactly `struo-postgres` and
`struo-redis`, both healthy.

The default file storage backend is local disk, so the S3-compatible MinIO service is not started by
plain `up -d` — it sits behind a Compose profile:

```bash
docker compose --profile s3 up -d
```

This additionally starts `struo-minio` and a one-shot `struo-createbuckets` container that creates the
`struo-media` bucket and then exits 0. Plain `docker compose ps` does not list a one-shot container
that has already exited — use `docker compose ps -a` to see it as `Exited (0)`. You only need the `s3`
profile if you intend to set `Struo:Files:Backend` to `s3` (chapter 3 covers this key in full); the
walkthrough below uses the local-disk default and does not need it.

Host ports default to the conventional values — PostgreSQL `5432`, Redis `6379`, MinIO `9000`/`9001` —
and are overridable per-service via `.env` (copy the tracked `.env.example`) using
`STRUO_PG_PORT`/`STRUO_REDIS_PORT`/`STRUO_MINIO_PORT`/`STRUO_MINIO_CONSOLE_PORT`, if one of those ports
is already taken on your machine.

## 2. Configure the API

```bash
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json
```

`appsettings.Development.json` is **gitignored** — it is where local, non-committed settings (database
credentials, Redis address) live. The `.example` file is JSON-with-comments; it loads correctly
through the real ASP.NET Core configuration provider, and its default connection string
(`Host=localhost;Port=5432;...`) matches `docker-compose.yml`'s conventional ports as copied.

If you remapped any port in step 1 (via `.env`), update the corresponding connection string in
`appsettings.Development.json` to match, or override it for one run without editing the file:

```bash
Database__ConnectionString="Host=localhost;Port=<your-port>;Database=struo;Username=struo;Password=struo" \
Redis__ConnectionString="localhost:<your-port>" \
dotnet run --project src/Struo.Api
```

Every key in `appsettings.json` can be overridden this way, with a double-underscore path separator —
chapter 3 documents the one exception (`Testing:PostgresConnection`) and every other key in full.

## 3. Run the API

```bash
dotnet run --project src/Struo.Api
```

`dotnet run` applies the `http` launch profile automatically, so the host listens on
`http://localhost:5221` in the `Development` environment — confirmed by running it against this
checkout:

```
[INF] Now listening on: http://localhost:5221
[INF] Application started. Press Ctrl+C to shut down.
[INF] Hosting environment: Development
```

On first boot against an empty database, the `users`, `roles` and other framework tables are created
and seeded (see step 5). On every later boot, pre-existing tables are left untouched.

## 4. Run the admin SPA

In a second terminal:

```bash
cd frontend
pnpm install
pnpm dev
```

Confirmed output:

```
VITE v8.1.2  ready in 180 ms
➜  Local:   http://127.0.0.1:5173/
```

Vite's dev server binds the IPv4 loopback explicitly (`frontend/vite.config.ts`'s `server.host`) so the
startup banner prints `127.0.0.1` rather than `localhost` — this sidesteps a Windows-specific pitfall
where "localhost" can resolve to the IPv6 loopback first and leave the IPv4 address unreachable for a
Chromium-based client. The SPA is equally reachable at `http://localhost:5173` in a browser; only the
printed banner differs. Vite's dev server proxies `/api` requests to
`http://localhost:5221` (`frontend/vite.config.ts`), so the SPA and API can be used together without
any cross-origin configuration. Open `http://localhost:5173` in a browser.

## 5. First login

Log in with the bootstrap admin account:

- **Email:** `admin@admin.com`
- **Password:** `admin`

This account is seeded **only the first time the `users` table is created** — it is not re-created or
reset on later boots, even against an emptied table. Override the seeded credentials for a fresh
database via `Auth:BootstrapAdmin:Email` / `Auth:BootstrapAdmin:Password` (or the equivalent
`Auth__BootstrapAdmin__*` environment variables) before that first boot.

If a production start (`ASPNETCORE_ENVIRONMENT=Production`) is still using the default password
`admin`, the API logs a **warning** at startup telling you to change it — it does not refuse to start,
so do not rely on the log going unnoticed as a safety net.

## What you see with no collections yet

This is the important expectation to set: with the shipped default — no entry in
`Struo:ContentAssemblies` — the admin SPA's sidebar has **no "Content" navigation group at all**.
Verified directly against this checkout: the database contains exactly the eleven framework tables
(`languages`, `files`, `file_translations`, `media_folders`, `users`, `roles`, `permissions`,
`user_roles`, `revisions`, `site_settings`, `user_sessions`) and nothing else, and the sidebar shows
only Dashboard, Media Library, Settings and System (Language/Role/User) — because there is genuinely no
content collection to list yet.

**This is correct, not a bug.** A blank Content section is exactly what a template with zero business
collections looks like. Chapter 4 shows how to add your first collection and make that group appear;
chapter 16 shows the same thing using the pre-built Blog sample if you want to see it working before
you design your own.

## Health endpoints and Scalar

| URL | Purpose |
|---|---|
| `GET /health/live` | Liveness — always 200 once the process is up |
| `GET /health/ready` | Readiness — 200 only once the database and cache checks pass |
| `GET /api/ping` | Lightweight envelope response, useful for smoke-testing the REST pipeline |
| `/scalar` | Interactive API explorer (Mars theme) — **non-Production only** |
| `/openapi/v1.json` | Generated OpenAPI document — **non-Production only** |
| `/graphql` | GraphQL endpoint — reachable in every environment; the in-browser Nitro IDE is **Development-only**, and both schema-disclosure routes (introspection and `?sdl`) are gated together by `GraphQl:ExposeSchema`, which defaults to Development-only — stricter than the non-Production gate above. Query *execution* is unaffected by that gate; chapter 10 covers the distinction. |

Confirmed against this checkout:

```
$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/health/ready
Healthy
HTTP_STATUS:200

$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/api/ping
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-07-29T03:42:17.4968695Z"}}
HTTP_STATUS:200
```

The Scalar explorer and the raw OpenAPI document are mapped only when the environment is not
`Production` — in Production both routes return 404. Set up your own authentication in front of them
if you need them there.

## Next steps

- Chapter 3, [Configuration Reference](03-configuration-reference.md), documents every setting used
  above (and every one that was not).
- Chapter 4, [Defining a Collection](04-defining-a-collection.md), to make the Content navigation
  group appear with your first real collection.
- Chapter 16, [Sample Walkthrough](16-sample-walkthrough.md), to opt the Blog demo in (and cleanly back
  out) instead.
- Chapter 15, [Container images](15-deployment-operations-testing.md#container-images), to deploy the
  API and admin SPA as Docker images instead of running them with `dotnet run`/`pnpm dev`.
