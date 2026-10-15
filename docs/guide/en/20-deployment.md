# 20. Deployment

Deploying the API project (`Struo.Api`) and the admin SPA to production means knowing which
misconfigured settings are silently ignored and which ones make startup fail outright; the second
half of this chapter covers what each of the two container images packages, and what each leaves
for the operator to do.

## Production checklist

Each item below is a setting that's easy to get wrong, with a consequence that isn't obvious. The
full meaning of each key is in [Chapter 4: Configuration Reference](04-configuration.md); this
section covers only the deployment consequence.

### Run `migrate` before the new API goes live

`Database:MigrateOnStartup` defaults to `false`: while the database has pending migrations, the API
refuses to start (see "What fails fast at startup" below). The deployment therefore runs `migrate`
once before the new API starts — as a one-off job, an init container, or
`docker run --rm … <image> migrate`. Alternatively, set `Database__MigrateOnStartup=true` and the
API applies them itself before it starts serving.

When several replicas all have `MigrateOnStartup` set, they queue on the migration lock and only one
applies; a replica that waits longer than `Database:MigrationLockTimeoutSeconds` fails to start.
SQLite and Oracle take no lock, so make sure `migrate` runs in one place only.

Migrations only go forward and there is no rollback command, so back up the database first.
Production does not run the schema check at startup; run `migrate:check` yourself after `migrate`.
The commands and the lock are covered in
[Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md).

### Choose or capture the bootstrap password at the first `migrate`

`Auth:BootstrapAdmin:Password` is read exactly once, the first time the core seed migration runs;
changing this value afterward has no effect on the account that already exists. When `migrate` runs
as a separate job, `Auth__BootstrapAdmin__*` belongs in that job's environment; the API's own
environment plays no part in seeding.

The template ships no bootstrap password. Leave it empty and the seed generates a random
24-character one, logged once as a Warning in the `migrate` output (or the API log, when the API
runs the migrations); copy it from there, because nothing prints it again. A password you set must
satisfy `Auth:Password` and may not be `admin`, in any environment; otherwise `migrate` stops with an
error naming `Auth__BootstrapAdmin__Password`.

### Login rate limiting: account layer on, IP layer depends on topology

Account-layer throttling (`RateLimiting:LoginAccount`) is on by default and blocks brute-force
attempts against a single account on its own; IP-layer throttling (`RateLimiting:Login`) is off by
default, because it buckets by `Connection.RemoteIpAddress`: behind a reverse proxy, every client
appears to come from the proxy's own address, and an office sharing one outbound NAT ends up in
the same bucket even when the proxy is configured correctly.

How each layer behaves is covered in the "Login rate limiting" section of
[Chapter 16: Authentication and SSO](16-authentication.md).

### A reverse proxy has to add `UseForwardedHeaders` itself

`Program.cs` deliberately doesn't call `UseForwardedHeaders(...)`. Deploying behind a reverse proxy
means adding this middleware and listing `KnownProxies`/`KnownNetworks` yourself; without it,
`Connection.RemoteIpAddress` always sees the proxy's own address — and it's not only the login IP
rate limit above that breaks, but anything that relies on the client's address.

### `Redis:ConnectionString`: required for more than one replica or to survive a restart

Left empty, session state — the ticket a cookie maps to, and the `RateLimiting:LoginAccount`
counters — all falls back to an in-process cache. With a single replica, and no concern about
losing every session on restart, leaving it empty is fine; with more than one replica, or a need
for sessions to survive a process restart, it has to be set.

Without it, every restart forces every user to log in again, and multiple replicas each keep their
own copy — the same user hitting a different replica looks like they were never logged in. With
Redis set, this counter is shared across every replica, but it isn't a guaranteed ceiling —
`IDistributedCache` has no atomic read-modify-write, so concurrent failed attempts can occasionally
undercount.

### Cookies come back only over HTTPS

In a Production environment, the authentication cookie is only sent back on requests over HTTPS;
sending traffic in without terminating TLS first makes the login response look normal (it comes
back with `Set-Cookie`), but the browser never attaches the cookie again afterward, and the caller
ends up stuck in a "logged in, yet asked to log in again" loop. How the security policy decides
this, and what extra rules apply once CORS is enabled, are in [Chapter 16](16-authentication.md).

The one exception is `http://localhost`: browsers treat it as a secure context and send the cookie
back, which is how the local Compose stack at the end of this chapter logs in without TLS.

### Scalar/OpenAPI are off in Production

`GET /scalar` and `/openapi/v1.json` are only registered outside Production; at Production
startup, these two routes don't exist at all.

The neighboring GraphQL schema-exposure route is controlled separately by
`GraphQl:ExposeSchema`, whose default value of `null` likewise only opens it in Development — the
mechanism is covered in [Chapter 14: GraphQL API](14-graphql.md).

### The three OIDC checks need pinning down

`Oidc:AllowedTenantId` defaults to a placeholder string that matches no tenant; until it's
replaced with a real tenant, external login fails for everyone.

Once a real tenant is in place, `Oidc:RequireEmailVerified` (not required by default) and
`Oidc:AllowedEmailDomains` (an empty list by default, meaning unrestricted) are both checks that
permit by default; all three should be pinned down in production, or the only remaining gate is
email equality. The order these checks run in is covered in [Chapter 16](16-authentication.md).

### Security response headers: the API only sends nosniff

The API project itself sends exactly one security response header,
`X-Content-Type-Options: nosniff`, covering error responses, CORS preflights, and bare 404s alike.

`Strict-Transport-Security`, `X-Frame-Options`/CSP's `frame-ancestors`, and `Referrer-Policy`
aren't its responsibility — they're left to the reverse proxy.
`frontend/nginx/default.conf.template` is a usable reference implementation, not a rule that
nginx is mandatory.

## What fails fast at startup

`Program.cs` wraps the entire startup sequence at the top level in a single `try`/`catch`/`finally`:
any exception thrown anywhere in it is logged with `Log.Fatal`, and `Environment.ExitCode` is set
to `1` before the process ends. Without this step, the process would exit with code `0`, leading an
orchestrator to mistake it for a successful start — it would neither restart nor alert.

The five settings groups `Database`, `Struo:Files`, `Oidc`, `Query`, and `Auth:Password` are all
bound with `ValidateOnStart`: a missing `Database:ConnectionString`, `Oidc:Enabled=true` without a
`ClientId`, or a `Query:MaxLimit` outside its range all throw an `OptionsValidationException` and
fail startup before the application ever starts accepting requests.

A database with pending migrations while `MigrateOnStartup` is `false` is another example of this
same fail-fast mechanism. Here is the API image starting against an empty SQLite database:

```
[10:35:35 FTL] StruoCMS host terminated unexpectedly
System.InvalidOperationException: 2 migration(s) pending, the lowest is 202610080000; run the 'migrate' command, or set Database:MigrateOnStartup to true.
…
```

The process then exits with code `1`; the stack trace that follows is omitted here. The message
itself names the command to run and the setting key.

One more thing that has nothing to do with configuration, but is decided just as early at the top
of startup: the process pins its whole default culture to invariant from the start, so the
operator doesn't need to separately pin `LANG` or `DOTNET_SYSTEM_GLOBALIZATION_*`. SqlSugar
re-parses the literal values rendered by query filters using that same culture, and the two have to
agree.

## Logging

Serilog configuration lives entirely under `Serilog:*`: one Console sink, plus a File sink writing
to `logs/struo-.log`, rolling by date and allowing shared read/write access from multiple processes
at once, with filenames shaped like `struo-YYYYMMDD.log`, one file per day.

The configuration is built once at startup; changing `Serilog:*` needs a process restart to take
effect, and `appsettings.json`'s file-watching doesn't make them live. `UseSerilog` reads both
`IConfiguration` and the DI container, so an enricher registered through DI is picked up and takes
effect too.

## The two container images

### Build

Two Dockerfiles each produce an independent image: the root `Dockerfile` builds the API,
`frontend/Dockerfile` builds the admin SPA. The root `docker-compose.yml` uses both Dockerfiles to
start a full development and testing stack, not a production deployment recipe — see "The local
Compose stack" at the end of this chapter. A local build and CI's `docker` job run the same two
commands, except a local build conventionally tags `:local` while CI tags `:ci`:

```
docker build --tag struo-api:local .
docker build --tag struo-admin:local frontend
```

The API image needs to read the entire repository, not just `src/Struo.Api/`: a fork's content
project is pulled in through a `ProjectReference`, so the build needs every project that reference
can reach, plus the root `Directory.Build.props`/`Directory.Packages.props`. The root
`.dockerignore` keeps `frontend/`, `docs/`, and ordinary build artifacts out.

### The API image

The image listens on `8080` and runs as the non-root user `app`. It applies no migrations when it
starts: the entry point is `dotnet Struo.Api.dll`, so `docker run --rm … struo-api:local migrate`
runs `migrate` against the database. Otherwise, set `Database__MigrateOnStartup=true`.

`app` owns three directories: `/app/App_Data` (uploaded content and the image-transform cache for
the local files backend), `/app/logs` (Serilog's File sink) and
`/home/app/.aspnet/DataProtection-Keys` (the key ring, see "DataProtection keys" below). A named
volume mounted on any of them starts writable.

Only `/app/App_Data` is also declared as a `VOLUME`, so even a `docker run` with no
`--mount`/`-v` at all still gets an anonymous volume here.

`HEALTHCHECK` hits `http://localhost:8080/health/live` every 30 seconds (5-second timeout, a
30-second start period, 3 retries) — that's liveness, not `/health/ready`. The health status
`docker ps`/`docker inspect` shows therefore means only "the process is still responding," not that
the database or cache is reachable.

### What it takes to start this image

The five environment variables below decide whether this image can start, and how it behaves once
it has; the full meaning of every other setting key is in [Chapter 4](04-configuration.md).

- `Database__DbType` — which backend to use (`PostgreSQL`, `Sqlite`, `MySql`, `SqlServer`,
  `Oracle`).
- `Database__ConnectionString` — required; the image's default value is a PostgreSQL connection
  string with a `REPLACE_ME` placeholder, and it can't be used as-is.
- `Database__MigrateOnStartup` (optional) — unset means `false`, and the deployment runs
  `migrate` separately; set to `true`, the API applies migrations itself when it starts.
- `Redis__ConnectionString` — leaving it empty means a single replica with an in-process cache
  that doesn't survive a restart.
- `Struo__Files__Backend` — defaults to `local`, writing into `/app/App_Data` (inside the
  declared volume); using `s3` also needs the matching four `Struo__Files__S3__*` keys.

This image doesn't set `ASPNETCORE_ENVIRONMENT`; left unset, it falls back to the ASP.NET Core
framework default of `Production`. That decides the authentication cookie's security policy — see
the production checklist above.

### DataProtection keys

The DataProtection key ring (it signs and encrypts the authentication cookie and antiforgery
tokens) is written inside the container at `/home/app/.aspnet/DataProtection-Keys`; this image
neither declares it as a volume nor mounts it.

Not persisting this directory has two consequences: replacing the container invalidates every
existing authentication cookie and antiforgery token, and every user has to log in again; running
more than one replica means each replica keeps its own key ring that the others don't share, and a
request that lands on a different replica from the one that issued it fails cookie validation or
antiforgery outright.

With a single replica, mounting this path as a volume is enough for the keys to survive rebuilding
the container; with more than one replica, a shared key store has to be configured — a shared
filesystem, Redis, or a cloud provider's key-ring service. This project doesn't come configured for
any of them.

Wherever it's mounted, these keys are stored in plain text — this project hasn't configured an XML
encryptor, so the key-ring directory holds directly readable key material that needs the same
protection as any other secret.

### The admin SPA image

The image listens on `80`, serving Vite's production build through nginx. Unlike the API image,
there's no `HEALTHCHECK` here: there's no downstream dependency to probe, so probing means hitting
`/` directly.

The SPA's API address is baked into the image at build time: `vite build` in production mode reads
`frontend/.env.production` if one is present, and `frontend/.dockerignore` deliberately doesn't
exclude such a file from the build context — only the developer's own
`.env`/`.env.local`/`.env.*.local`. The repository itself carries no `.env.production`, only the
tracked `frontend/.env.example`. Changing this value only takes effect after rebuilding the image.

`API_UPSTREAM` (defaulting to `http://api:8080`) has to be written as `scheme://host:port`, with no
path or trailing slash: `proxy_pass` takes an nginx variable rather than a literal value, which lets
the container start even before the API is up, at the cost that any path segment in the variable
replaces the request's URI instead of being prepended to it.

`HSTS_VALUE` defaults to an empty string, and nginx sends no header at all for an `add_header`
whose value is empty; it should only be set once every reachable request has TLS terminated here or
by an upstream proxy.

Regardless of how `HSTS_VALUE` is set, the three headers `X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`, and `Referrer-Policy: strict-origin-when-cross-origin` are always sent;
the API's own `nosniff` is stripped from responses proxied to `/api/*`, so this header appears only
once.

Caching rules: `index.html` sends `Cache-Control: no-cache`; `/assets/` sends
`public, max-age=31536000, immutable` (Vite's filenames carry a content hash).
`client_max_body_size` is `32m`, higher than the backend's `Struo:Files:MaxUploadBytes` of 25 MiB,
so an oversized upload gets the backend's own JSON error envelope instead of nginx's HTML error
page.

`NGINX_RESOLVER` (defaulting to `127.0.0.11`, Docker's built-in DNS, with a 5-second timeout) only
means anything on a user-defined Docker network; on the default bridge network, every `/api/*`
request gets a 502 after the resolution times out.

For deploying the SPA on a different origin from the API, how to set `VITE_API_BASE_URL` and
when the setting takes effect are in [Chapter 19: Admin Customization](19-admin-customization.md);
building this image means putting the value in a `frontend/.env.production` you add and commit
yourself — a developer's own `frontend/.env` never makes it into the image.

### What's left to do outside the image

The image is only responsible for getting the application running. Terminating TLS, configuring
`UseForwardedHeaders`, restart policy, horizontal scaling, secret injection, and wiring
`/health/live` and `/health/ready` into the orchestrator's own probes are all yours to set up.

### Verifying both images together

The five commands below create a user-defined network and a volume, run `migrate` against a SQLite
database in that volume, and then start two containers that can see each other; this is a
verification step, not a recommended production topology. The local Compose stack at the end of
this chapter starts both images together with PostgreSQL and Redis:

```
docker network create struo-verify
docker volume create struo-verify-data
docker run --rm --volume struo-verify-data:/app/App_Data --env Database__DbType=Sqlite --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' struo-api:local migrate
docker run --detach --name api --network struo-verify --volume struo-verify-data:/app/App_Data --env Database__DbType=Sqlite --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' struo-api:local
docker run --detach --name admin --network struo-verify --publish 8081:80 struo-admin:local
```

The third command ends by printing `Applied 202610080000  CreateCoreSchema` and
`Applied 202610080001  SeedCoreData`. Without it, the `api` container exits with the message shown
in "What fails fast at startup" above.

The admin container's default `API_UPSTREAM=http://api:8080` resolves because both containers sit
on this same user-defined network, and the API container is named `api`. If the API container
needs to reach a database on the Docker host, use `host.docker.internal`. That name works out of
the box only on Docker Desktop; on Linux, add `--add-host=host.docker.internal:host-gateway` to
`docker run`.

## Health probes

`/health/live` runs no checks at all (`Predicate = _ => false`); it only confirms the process is
still accepting requests. A liveness probe should restart the container or pod when this route stops
responding.

`/health/ready` runs two checks tagged `ready`: `DbReadinessCheck` makes an actual round trip to
the database, and `CacheReadinessCheck` writes to and reads back from the configured
`IDistributedCache` (Redis or the in-process cache); a readiness probe should wait until both are
reachable before routing traffic in.

How to hit both routes is already covered in [Chapter 3: Getting Started](03-getting-started.md);
below is the full response to an anonymous request:

```
$ curl -i http://localhost:5221/health/live
HTTP/1.1 200 OK
Content-Type: text/plain
Date: Thu, 17 Sep 2026 03:24:31 GMT
Server: Kestrel
Cache-Control: no-store, no-cache
Expires: Thu, 01 Jan 1970 00:00:00 GMT
Pragma: no-cache
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

Healthy
```

`/health/ready` returns exactly the same thing when it passes: `200`, body `Healthy`, the same set
of headers; the difference is that it only responds this way once both checks succeed.

## Redeploying with tabs still open

After the admin SPA redeploys, a tab still open from before is running an `index.html` that still
requests chunk filenames missing from the new version;
`frontend/src/router/chunkLoadRecovery.ts` recovers from this chunk-404 by reloading the page,
guarding against an infinite loop with a `sessionStorage` flag that's used only once. A chunk
that's genuinely missing turns into an error, rather than an endless reload.

## Backups

- **Database** — apart from uploaded files, the database is the authoritative source for every
  other piece of data; back it up with your backend's own tools, such as PostgreSQL's `pg_dump`, or
  a managed service's snapshots and PITR. StruoCMS provides no backup mechanism of its own.
  Migrations have no rollback command, so back up before every `migrate`.
- **Uploaded files** — stored outside the database; where to back them up depends on
  `Struo:Files:Backend`: `local` mode means backing up the `Local:RootPath` directory, `s3` mode
  relies on the S3-compatible bucket's own versioning or replication; backing up only the database
  silently misses every uploaded file.
- **Redis needs none** — Redis (or its in-process substitute) holds only short-lived state: the
  ticket behind a cookie session, account login-failure counters, and session revocation records.
  Losing it only forces every logged-in user to log in again and resets the login counters; it
  isn't an authoritative source and needs no separate backup.
- **Migrations and configuration** — the fork's own migration classes, `appsettings.*`, and the
  values in environment variables or a secret manager are ordinary source or deployment-pipeline
  artifacts; back them up along with the rest of the deployment.

## The local Compose stack

`docker compose up -d --build` uses the two Dockerfiles above to start four services: `postgres`,
`redis`, `api` and `admin`; [Chapter 3: Getting Started](03-getting-started.md) covers starting it
and logging in. It's a development and testing stack: `api` runs as Production and applies
migrations itself on every start (`Database__MigrateOnStartup`). For production, assemble your own
deployment along the checklist earlier in this chapter.

The PostgreSQL password is a fixed dev value and Redis has no password. Every port is published on
`127.0.0.1` by default, so only this machine reaches the database, Redis, the API and the admin.
`STRUO_BIND_ADDRESS` sets the address they bind to; `0.0.0.0` publishes the ports on all network
interfaces, so other machines on the network can reach them, and the fixed dev passwords and the
password-less Redis go with them. Do that only on a network you trust.

The health endpoints are on the API port: `/health/live` and `/health/ready`, by default
`http://localhost:8081/health/live` (`STRUO_API_PORT` changes it). The admin's nginx forwards only
`/api/*`, so `/health/...` on the admin port returns the SPA page. Compose gives `api` its own
health check every 5 seconds, so `docker compose up --wait` doesn't wait out the image's default
30-second interval.

The DataProtection keys live in the `struo-dpkeys` volume and survive rebuilding the container, but
in plain text: the first start logs a `No XML encryptor configured` warning. That's fine for
development and testing; in production, protect the keys yourself as the "DataProtection keys"
section above describes.

### An S3-compatible store

The `minio` and `createbuckets` services in `docker-compose.yml` both sit under `profiles: ["s3"]`;
a plain `docker compose up -d` neither pulls these two images nor waits on their health checks.

The `api` service Compose starts keeps the `local` files backend, so MinIO is for an API you run
yourself with `Struo:Files:Backend` set to `s3`. Start it next to the dependencies with:

```
docker compose --profile s3 up -d postgres redis minio createbuckets
```

`createbuckets` is one-shot: it creates the bucket `struo-media` at startup and then exits, with
exit code `0`. `docker compose ps` filters it out by default; `docker compose ps -a` is what shows
it as `Exited (0)`.

The ports are decided by `STRUO_MINIO_PORT` (defaulting to 9000, the API) and
`STRUO_MINIO_CONSOLE_PORT` (defaulting to 9001, the console); changing `STRUO_MINIO_PORT` means
updating `Struo:Files:S3:Endpoint` in `appsettings.Development.json` to match.

## What's next

That's deployment covered; how migrations create and change the database schema, and how the core's
schema changes reach your fork, is in
[Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md).
