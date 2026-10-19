# 4. Configuration Reference

When you need to change a setting and don't know its exact name, its default value, or whether
getting it wrong will block startup, this chapter has the answer.

## Where settings come from

Settings layer in order, and each later source overrides the ones before it:
`src/Struo.Api/appsettings.json` → `appsettings.{Environment}.json` → environment variables →
command-line arguments. Three sections — `Query`, `Struo:Cors`, and `GraphQl` — don't appear in the
default `appsettings.json`. Not having a block to edit for these doesn't mean the keys don't exist;
you just have to add the section yourself.

Any key can be overridden with an environment variable by replacing `:` with `__` — for example,
`Database__ConnectionString`. The exceptions are the four test connection keys in the `Testing`
section, covered in the last section of this chapter.

Changing any key requires restarting the process to take effect. Where a paragraph below a table
says a key has "no validation rule", a correctly typed value is never checked further. A type
mismatch — a misspelled `Database:DbType`, or text in a numeric field — still fails: at startup for
a section bound with `ValidateOnStart`, and in most other sections only when the value is first
used.

An array-valued key has to be replaced as a whole: at a higher-precedence layer, indices you don't
override keep the value from the layer below, so filling in only the first few elements doesn't
shorten the array.

The default `appsettings.json` fully populates four arrays: `Oidc:Scopes`,
`Struo:Files:ImageTransform:AllowedFormats`, `Struo:Files:AllowedContentTypes`, and
`Serilog:WriteTo`; the first two fall back to their built-in default even if you delete them
entirely. Array elements use an index, e.g. `Oidc__Scopes__0=openid`; to shorten an array, edit the
settings file itself.

| Key | Type | Default |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | environment variable | `Production` when unset |

Both profiles in `launchSettings.json` set it to `Development`, so the `dotnet run` in chapter 3
runs as Development.

`ASPNETCORE_ENVIRONMENT` decides which `appsettings.{Environment}.json` is read, and also gates the
following environment-specific behavior:

- Development-only: `GraphQl:ExposeSchema`'s default value, the Nitro IDE, and the startup schema
  check (the same comparison as `migrate:check`; startup is refused when it finds an error).
- Development-only by default: `/scalar` and `/openapi/v1.json` (`OpenApi:Enabled` overrides it).
- Every environment except Development by default: the cookie `SecurePolicy` is `Always`
  (`Auth:Cookie:RequireSecure` overrides it).

These seven are host-facing settings in `docker-compose.yml`; they never reach the API's own
configuration:

- `STRUO_PG_PORT`
- `STRUO_REDIS_PORT`
- `STRUO_ADMIN_PORT`
- `STRUO_API_PORT`
- `STRUO_MINIO_PORT`
- `STRUO_MINIO_CONSOLE_PORT`
- `STRUO_BIND_ADDRESS` (the host address every port binds to, default `127.0.0.1`)

If you change `STRUO_MINIO_PORT`, update `Struo:Files:S3:Endpoint` yourself to match.

`STRUO_ADMIN_EMAIL` and `STRUO_ADMIN_PASSWORD` are different: Compose passes them to its `api`
service as `Auth__BootstrapAdmin__Email` / `Auth__BootstrapAdmin__Password`. They only affect the
API that Compose starts, not one you run yourself. `STRUO_ADMIN_PASSWORD` is empty by default, so
the seed generates a password and prints it once in the `api` log.

## Database

| Key | Type | Default |
|---|---|---|
| `Database:DbType` | enum | `PostgreSQL` |
| `Database:ConnectionString` | string | see below |
| `Database:TablePrefix` | string (may be empty) | `struo_` |
| `Database:MigrateOnStartup` | bool | `false` |
| `Database:MigrationLockTimeoutSeconds` | integer (seconds) | `60` |

This is the actual value, not the nested form used in `appsettings.json`:
```text
Database:ConnectionString = Host=localhost;Port=5432;Database=struo;Username=REPLACE_ME;Password=REPLACE_ME
```

Leaving `ConnectionString` empty or omitted fails startup, not the first query. The API never
creates tables itself: the `migrate` command creates them, or startup does when `MigrateOnStartup`
is `true` — see [Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md).

`TablePrefix` is prepended to the base name of the framework's 12 tables (the 11 entities in
`FrameworkEntityTypes.All` plus the version table, `schema_versions`). Lower-case letters, digits and
underscores only, first character a letter, at most 16 characters; the empty string means no
prefix. Sample and fork collections are unaffected.

Changing it against a database that already holds framework tables leaves no tables and no
migration history under the new names: `migrate` builds a fresh, empty set of framework tables under
them and seeds it, and the existing tables are not read; with `MigrateOnStartup` set to `false`,
startup refuses with "migrations pending" instead. To change the prefix, rename the existing tables
(the version table included) first.

With `MigrateOnStartup` set to `false` (the default in every environment), the API refuses to start
while the database has pending migrations, so the deployment runs `migrate` first. Set to `true`,
the API applies pending migrations before it starts serving.

`MigrationLockTimeoutSeconds` caps how many seconds `migrate`, `migrate:baseline` and
`MigrateOnStartup` wait for the migration lock before failing; the range is 1 to 3600, and a value
outside it fails startup.

The API does not use the keys `Database:MigrationsPath` and `Database:AutoSyncSchema`: left in the
configuration they have no effect, and startup logs a warning for each, for example:

```text
[17:42:58 WRN] The configuration key Database:MigrationsPath is not used: migrations are code in the scanned assemblies and are applied with the 'migrate' command.
```

The database part of startup runs in this order:

1. Log a warning for each of those two unused keys present in the configuration.
2. With `MigrateOnStartup` set to `true`, take the lock and apply the migrations; with `false`,
   check the database and refuse to start if the framework tables exist without a version table,
   or if migrations are pending.
3. In Development, run the schema check and refuse to start on errors.

## Struo:ContentAssemblies

| Key | Type | Default |
|---|---|---|
| `Struo:ContentAssemblies` | string array | `[]` |

Every assembly name in the list must resolve through `Assembly.Load`, which in practice means the
API host project references it directly; an unresolvable name fails startup rather than being
skipped.

This key is read directly from `builder.Configuration` before `builder.Build()` runs, bypassing
`IOptions<T>`. So it sees every normal configuration source installed before startup, but a custom
configuration provider you register after `CreateBuilder` won't be visible to it, with no error
reported.

## Struo:Files

| Key | Type | Default |
|---|---|---|
| `Struo:Files:Backend` | string | `"local"` |
| `Struo:Files:MaxUploadBytes` | long (bytes) | `26214400` |
| `Struo:Files:AllowedContentTypes` | string array | `[]` |
| `Struo:Files:PresignedRedirect` | bool | `false` |

`Backend` is the only top-level key in this section that's actually validated at startup: an
unrecognized value fails startup immediately, and the required fields of whichever backend you
picked (see Local/S3 below) are validated along with it. `MaxUploadBytes`, `AllowedContentTypes`,
`PresignedRedirect`, and the whole `ImageTransform` section have no validation rule — a correctly
typed value is never checked further.

- `MaxUploadBytes`: caps both the declared length and the actual byte count, so a client that lies
  about the length is still limited.
- `AllowedContentTypes`: leaving it empty means no format is restricted; the default
  `appsettings.json` already lists 18 MIME types.
- `PresignedRedirect`: when `true`, a download becomes a 302 redirect to a storage-presigned URL;
  the default `false` has the API stream the bytes itself, which suits setups where the browser
  can't reach storage directly.

### Local

| Key | Type | Default |
|---|---|---|
| `Struo:Files:Local:RootPath` | string | `"App_Data/uploads"` |

When `Backend="local"`, `RootPath` is the only field validated at startup.

### S3

| Key | Type | Default |
|---|---|---|
| `Struo:Files:S3:Endpoint` | string (may be empty) | `"REPLACE_ME"` |
| `Struo:Files:S3:Bucket` | string (may be empty) | `"REPLACE_ME"` |
| `Struo:Files:S3:AccessKey` | string (may be empty) | `"REPLACE_ME"` |
| `Struo:Files:S3:SecretKey` | string (may be empty) | `"REPLACE_ME"` |
| `Struo:Files:S3:Region` | string | `"us-east-1"` |
| `Struo:Files:S3:ForcePathStyle` | bool | `true` |
| `Struo:Files:S3:PresignTtlSeconds` | integer (seconds) | `300` |

When `Backend="s3"`, `Endpoint`, `Bucket`, `AccessKey`, and `SecretKey` must all have a value at
startup; the default value is the `REPLACE_ME` placeholder, waiting to be replaced. Getting
`Region`, `ForcePathStyle`, or `PresignTtlSeconds` wrong won't block startup.

### ImageTransform

| Key | Type | Default |
|---|---|---|
| `Struo:Files:ImageTransform:Enabled` | bool | `true` |
| `Struo:Files:ImageTransform:MaxWidth` | integer (px) | `4096` |
| `Struo:Files:ImageTransform:MaxHeight` | integer (px) | `4096` |
| `Struo:Files:ImageTransform:AllowedFormats` | string array | see below |
| `Struo:Files:ImageTransform:DefaultQuality` | integer | `82` |
| `Struo:Files:ImageTransform:CachePath` | string | `"App_Data/image-cache"` |

This is the actual value, not the nested form used in `appsettings.json`:
```text
Struo:Files:ImageTransform:AllowedFormats = ["webp", "jpeg", "png", "avif"]
```

Leaving `AllowedFormats` empty or deleting it restores the built-in `webp`/`jpeg`/`png`/`avif` at
startup, so there is no way to configure "no format allowed"; the list is also only checked when
the caller asks for an explicit `format`. To turn transforms off entirely, set `Enabled=false`.
A transform only happens when the caller requests a width, height, or format, the file itself is
an image, and `Enabled` is `true`; otherwise the original file is streamed as-is.

When `CachePath` is a relative path, it's relative to the application's content root, not the
process's current working directory. This matters for where the cache actually ends up when
something like systemd starts the process from a different directory.

## Struo:Cors

| Key | Type | Default |
|---|---|---|
| `Struo:Cors:AllowedOrigins` | string array | `[]` |

When `AllowedOrigins` is empty, `UseCors` isn't called at all, and no response carries a CORS
header; leave it empty for any same-origin setup (the Vite dev proxy, or deploying the admin SPA and
the API on the same domain). This key is read directly from `IConfiguration` and has no validation
rule of its own.

Configuring even one origin also switches the session cookie to `SameSite=None` plus
`SecurePolicy=Always`, and opens the CORS policy to those origins with `AllowCredentials`, any
header, and any method. Browsers only honor `SameSite=None` over HTTPS, so once this key is
non-empty, both the admin SPA and the API must run over HTTPS, or authentication silently stops
working.

## Query

| Key | Type | Default |
|---|---|---|
| `Query:MaxLimit` | integer | `100` |
| `Query:DefaultLimit` | integer | `25` |
| `Query:MaxFilterConditions` | integer | `50` |
| `Query:MaxRelationDepth` | integer | `6` |
| `Query:MaxSortFields` | integer | `5` |
| `Query:MaxDeepRelations` | integer | `20` |
| `Query:MaxExpandRows` | integer | `1000` |
| `Query:MaxFacets` | integer | `10` |
| `Query:MaxFacetValues` | integer | `50` |
| `Query:MaxAggregates` | integer | `10` |
| `Query:MaxSearchCandidates` | integer | `1000` |

All eleven keys carry `[Range(1, int.MaxValue)]` validation and are bound with `ValidateOnStart`;
setting one to zero or a negative number fails startup immediately. A key name outside this list (a
typo, say) is silently ignored, with no error.

A `limit` above `MaxLimit` is clamped, not rejected; omitting `limit`, or supplying a non-positive
value, falls back to `DefaultLimit`. Exceeding `MaxFilterConditions`, `MaxFacets`, or
`MaxAggregates` instead throws an exception naming the limit, and the query never executes. So does
exceeding `MaxSortFields`, which counts the fields of the top-level `sort` and of each nested list's
`sort` separately, and `MaxDeepRelations`, which counts every relation node in one request's `deep`
tree. `MaxFilterConditions` is one budget for the whole request: the root `filter` and every nested
`deep` filter draw from it together.

`MaxExpandRows` bounds the rows one relation fetch reads while `deep` expands a page. A fetch that
finds more than `MaxExpandRows` rows fails with a 400 naming the relation; the rows are never
truncated. There is no cap on `offset`.

`MaxRelationDepth` counts the number of relation hops in a path; the final field itself and the
`_junction` pseudo-segment don't count toward it. Exceeding `MaxSearchCandidates` doesn't truncate
the results. It's treated as an `ISearchProvider` returning more than it should, and throws an
exception (500) instead.

## Auth

| Key | Type | Default |
|---|---|---|
| `Auth:BootstrapAdmin:Email` | string | `"admin@admin.com"` |
| `Auth:BootstrapAdmin:Password` | string | `""` |
| `Auth:Password:MinLength` | integer | `8` |
| `Auth:Password:MaxLength` | integer | `128` |
| `Auth:Cookie:RequireSecure` | bool (may be empty) | none (environment-dependent) |

The core seed migration (`SeedCoreData`) applies the pair once, on an empty database, and it is
never backfilled onto an existing database. These two keys have no options class; the seed
migration reads them straight from configuration as strings, so the environment that runs `migrate`
needs them. An empty password means the seed generates a random password of at least 24 characters and logs
it once as a Warning, in the run that creates the admin. A password you set must satisfy `Auth:Password`
and may not be `admin` in any letter case. While it holds a weak value, `migrate`,
`migrate:preview` and a start with `Database:MigrateOnStartup` all refuse, with an error that names
`Auth__BootstrapAdmin__Password`. The value only seeds the account, so once the account exists it can
be removed.

The password policy is shared by the same validation on `POST /api/users` and
`PUT /api/users/{id}/password`; `MinLength`/`MaxLength` are just the bounds on a reasonable input.
These two keys carry their own startup validation, `MinLength >= 1 && MinLength <= MaxLength`.
Setting them backwards fails startup. Only `MinLength` is returned to the admin SPA, through
`GET /api/config`.

`Auth:Cookie:RequireSecure` decides whether the session cookie is always `Secure`. Unset, it is `Always`
in every environment except Development, which follows the request itself. `true` forces `Secure`
everywhere; `false` lets the cookie travel over plain HTTP, which a Staging-like host without TLS in
front of it needs. Configuring any `Struo:Cors` origin forces `Secure` whatever this key says.

## OpenApi

| Key | Type | Default |
|---|---|---|
| `OpenApi:Enabled` | bool (may be empty) | none (environment-dependent) |

Unset, `/openapi/v1.json` and `/scalar` exist in Development only; every other environment, Production
included, returns 404. `true` maps them in any environment and `false` in none. Both publish the
whole API spec with no authentication, so put your own authentication layer in front before enabling
them outside Development.

## Rbac

| Key | Type | Default |
|---|---|---|
| `Rbac:PublicReadCollections` | string array | `[]` |

This key is applied only once, when the core seed migration runs; changing it and restarting won't
retroactively grant public read access on an existing database. Either set it before the first
`migrate`, or grant the access afterward directly through the admin UI or the API.

## GraphQl

| Key | Type | Default |
|---|---|---|
| `GraphQl:ExposeSchema` | bool (may be empty) | none (environment-dependent) |

When unset, it defaults to open only in Development; every other environment, Production included,
defaults to closed. It governs only whether the
schema can be queried (introspection and `GET /graphql?sdl`); `POST /graphql` itself is unaffected.
The built-in Nitro browser IDE separately only recognizes Development, and isn't controlled by this
key.

## RateLimiting

| Key | Type | Default |
|---|---|---|
| `RateLimiting:Login:Enabled` | bool | `false` |
| `RateLimiting:Login:PermitLimit` | integer | `5` |
| `RateLimiting:Login:WindowSeconds` | integer (seconds) | `60` |
| `RateLimiting:LoginAccount:Enabled` | bool | `true` |
| `RateLimiting:LoginAccount:PermitLimit` | integer | `10` |
| `RateLimiting:LoginAccount:WindowSeconds` | integer (seconds) | `900` |
| `RateLimiting:LoginConcurrency:Enabled` | bool | `true` |
| `RateLimiting:LoginConcurrency:PermitLimit` | integer | `4` |
| `RateLimiting:LoginConcurrency:QueueLimit` | integer | `32` |
| `RateLimiting:Password:Enabled` | bool | `true` |
| `RateLimiting:Password:PermitLimit` | integer | `5` |
| `RateLimiting:Password:WindowSeconds` | integer (seconds) | `60` |

None of the twelve keys has a validation rule; a wrong value only surfaces once a request comes
in. When a rate-limiter policy (`Login:*`, `LoginConcurrency:*` or `Password:*`) blocks a request it
returns 429, with a message naming the policy and a `Retry-After` header; the `LoginAccount` 429 is
returned by the login endpoint itself, with a fixed message.

`Login:*` is a fixed-window limit bucketed by client IP, protecting only `POST /api/auth/login`;
it's off by default because admin users often share the same outbound IP, and enabling it can lock
out an entire office, even with `UseForwardedHeaders` configured correctly.

To enable it behind a reverse proxy, you have to add `UseForwardedHeaders` yourself and list
`KnownProxies`/`KnownNetworks`. This project doesn't register it by default. This limiter's state
lives in process memory and isn't shared across replicas; a cross-replica IP limit belongs in a
gateway or WAF in front of the API.

`LoginAccount:*` buckets by the email in the request and counts the attempt before the password is
verified, so a blocked request never spends Argon2id compute and parallel guesses cannot all slip
past the limit. A successful login clears that account's count. Whether the account doesn't exist,
the password is wrong, or the account is disabled, all of it counts as one attempt and returns the
same 429. That uniformity is what prevents account enumeration.

With `Redis:ConnectionString` configured, each account gets one atomic Redis counter shared across
replicas, and a login fails with a 5xx error while Redis is unreachable rather than skipping the
check. Left empty, the counts live in the in-process distributed cache, serialized by a per-account
lock inside each replica; replicas count on their own.

Because every attempt counts, whoever knows an account's email can use up its limit and keep that
account locked out of password sign-in for the rest of the window. This is a deliberate trade-off;
SSO sign-in is unaffected.

`LoginConcurrency:*` caps how many logins verify a password at the same time in one process:
`PermitLimit` run at once, up to `QueueLimit` more wait for a slot, and the rest get 429. It bounds
Argon2id CPU and memory whatever the source IPs or accounts. Each replica enforces its own cap.

The default is looser than the IP limit, with a long 900-second window. It's aimed at sustained
password spraying against a single account.

`Password:*` protects only `PUT /api/users/{id}/password`, bucketed by the caller's own user id,
falling back to IP only when no user id is available — since the endpoint requires login, that
fallback doesn't happen in practice. Because the bucket key is the caller, a super-admin resetting
many other users' passwords in bulk spends their own quota, not the reset users'.

## Branding

| Key | Type | Default |
|---|---|---|
| `Branding:Name` | string | `"StruoCMS"` |
| `Branding:LogoUrl` | string (may be empty) | none |

The brand name and logo are normally changed in the admin UI, not in a settings file. These two keys
are only the default used when the `struo_site_settings` table has no value yet: `GET /api/config` decides
for each field independently, and only falls back here when nothing has been saved (or the logo file
has been taken down).

## Localization

| Key | Type | Default |
|---|---|---|
| `Localization:Languages` | array of `Code`, `Name` | `en`, `zh-TW` |
| `Localization:DefaultLanguage` | string | `en` |

This section is only the **seed source** for the `struo_languages` table: when the core seed
migration runs (`migrate` or `MigrateOnStartup`), each entry becomes a row in list order (`sort`
ascending, `enabled` true, `isDefault` on the row whose code is `DefaultLanguage`). The seed
migration runs once, and a database joined with `migrate:baseline` is not seeded; languages are
edited in the admin UI under System › Language. Validated at startup: at least one
language, every `Code` matching `[A-Za-z0-9_-]{1,35}` and unique case-insensitively, `Name` not
blank, `DefaultLanguage` present in the list — any failure stops the host. Environment variables and
`appsettings.{Environment}.json` merge arrays by index: they can change an entry or append one
(`Localization__Languages__2__Code=ja`) but cannot remove one; to drop a language, edit
`appsettings.json` itself (the fork owns that file).

## AdminUi

| Key | Type | Default |
|---|---|---|
| `AdminUi:Locales` | string array | `["zh-TW", "en"]` |
| `AdminUi:DefaultLocale` | string | `zh-TW` |

Which languages the admin UI offers and which one it starts in, independent of content languages.
`GET /api/config` publishes them as `uiLocales` and `uiDefaultLocale`; the SPA keeps only the codes
for catalogs it ships (`zh-TW` and `en`), dropping the rest with an error in the browser console.
With a single enabled locale the language switcher is not rendered. Startup validates each code's
shape and uniqueness and that `DefaultLocale` is in the list. Adding a catalog is described in
[Chapter 19: Admin Customization](19-admin-customization.md).

## Redis

| Key | Type | Default |
|---|---|---|
| `Redis:ConnectionString` | string (may be empty) | `""` |

Left empty, it falls back to an in-memory distributed cache, and sessions disappear every time the
process restarts — fine only for brief local testing; set this key whenever you run more than one
replica, or need sessions to survive a restart. The default value is an empty string, and an absent
key yields `null`; both land on the same in-memory branch. This key also decides where
`RateLimiting:LoginAccount` counts: left empty, each process counts in its own memory; when set, the
login throttle counts in Redis directly.

## Oidc

| Key | Type | Default |
|---|---|---|
| `Oidc:Enabled` | bool | `false` |
| `Oidc:Authority` | string (may be empty) | see below |
| `Oidc:ClientId` | string (may be empty) | `"REPLACE_ME"` |
| `Oidc:ClientSecret` | string (may be empty) | none |
| `Oidc:CallbackPath` | string | `"/signin-oidc"` |
| `Oidc:Scopes` | string array | see below |
| `Oidc:ReturnUrlDefault` | string | `"/"` |
| `Oidc:RequireEmailVerified` | bool | `false` |
| `Oidc:AllowedTenantId` | string | `"REPLACE_TENANT_ID"` |
| `Oidc:AllowedEmailDomains` | string array | `[]` |

This is the actual value, not the nested form used in `appsettings.json`:
```text
Oidc:Authority = https://login.microsoftonline.com/REPLACE_TENANT_ID/v2.0
Oidc:Scopes = ["openid", "email", "profile"]
```

When `Enabled=true`, `Authority`, `ClientId`, and `ClientSecret` must all be non-empty, or startup
fails; with `Enabled=false` that rule always passes. But `Enabled=false` alone leaves the OIDC
authentication scheme unregistered, and `/api/auth/login/oidc` returns 404. That's not a
configuration error.

On the first login with an external identity, StruoCMS matches it to an existing local account by
email; that matching only happens once `AllowedTenantId`, `RequireEmailVerified`, and
`AllowedEmailDomains` all pass their checks. Loosen them, and anyone with a matching email at any
identity provider can take over a password account.

`RequireEmailVerified` and `AllowedEmailDomains` default to permissive; `AllowedTenantId` doesn't.
The default value is `REPLACE_TENANT_ID`, which matches no real tenant, so it blocks every external
login until you replace it. When you enable OIDC in Production, set all three explicitly rather than
relying on the defaults.

## Serilog

| Key | Type | Default |
|---|---|---|
| `Serilog:MinimumLevel:Default` | string | `"Information"` |
| `Serilog:MinimumLevel:Override` | map | `{"Microsoft.AspNetCore":"Warning"}` |
| `Serilog:WriteTo` | array | see below |

Two sinks are configured by default: the console, and a daily-rolling `logs/struo-.log`
(`shared: true`).

This section isn't bound to a custom options class; Serilog's own configuration reader parses it
directly. Serilog builds its two loggers once each, at startup, so changing a level requires
restarting the process; the first few lines — logged before `WriteTo` is read — go to the console
only.

## Testing

| Key | Overriding environment variable |
|---|---|
| `Testing:PostgresConnection` | `STRUO_TEST_PG_CONNECTION` |
| `Testing:SqlServerConnection` | `STRUO_TEST_SQLSERVER_CONNECTION` |
| `Testing:MySqlConnection` | `STRUO_TEST_MYSQL_CONNECTION` |
| `Testing:MariaDbConnection` | `STRUO_TEST_MARIADB_CONNECTION` |

All four keys default to `""`. The running API never reads this section; only the test project's
own `ConfigurationBuilder` does: it checks the environment variable in the table first, then the key
in `appsettings.json` and `appsettings.Development.json`. These are the only keys in the whole
configuration surface where the `__` override convention doesn't apply; a variable such as
`Testing__PostgresConnection` has no effect.

Each connection turns on that backend's live tests: the migration tests, `LiveRepositoryTests` and
`TestDatabaseLiveTests`, plus `PostgresOnlyTests` on PostgreSQL. The database it names must contain
the word `test` and must already exist, and the login needs rights to create and drop databases.

Left empty, that backend's live tests return early and pass, so judge from each test's duration
whether it really connected. The SQLite database tests are unaffected and still run.

The environment variable `STRUO_TEST_BACKEND` selects the backend the tests run on: `Sqlite` (the
default), `PostgreSQL`, `SqlServer`, `MySql` or `MariaDb`. A live backend also moves the API test
hosts' and the unit tests' databases onto it; if that backend's connection isn't configured, the
whole run fails instead of passing quietly. The test layers are in
[Chapter 22: Testing and CI](22-testing.md).

## What's next

With configuration in place, the next chapter defines your first content collection.
