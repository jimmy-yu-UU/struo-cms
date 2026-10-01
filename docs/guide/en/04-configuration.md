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
`Database__ConnectionString`. The one exception is `Testing:PostgresConnection`, covered in the last
section of this chapter.

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

- Development-only: `GraphQl:ExposeSchema`'s default value, the Nitro IDE,
  `Database:AutoSyncSchema`, the schema guard.
- Non-Production only: `/scalar` and `/openapi/v1.json`.
- Production-only: the cookie `SecurePolicy` is fixed to `Always`, and the warning about an
  unchanged default admin password.

These four are host-facing ports in `docker-compose.yml`; they never reach the API's own
configuration:

- `STRUO_PG_PORT`
- `STRUO_REDIS_PORT`
- `STRUO_MINIO_PORT`
- `STRUO_MINIO_CONSOLE_PORT`

If you change `STRUO_MINIO_PORT`, update `Struo:Files:S3:Endpoint` yourself to match.

## Database

| Key | Type | Default |
|---|---|---|
| `Database:DbType` | enum | `PostgreSQL` |
| `Database:ConnectionString` | string | see below |
| `Database:TablePrefix` | string (may be empty) | `struo_` |
| `Database:MigrationsPath` | string (may be empty) | `""` |
| `Database:AutoSyncSchema` | bool | `false` |

This is the actual value, not the nested form used in `appsettings.json`:
```text
Database:ConnectionString = Host=localhost;Port=5432;Database=struo;Username=REPLACE_ME;Password=REPLACE_ME
```

Leaving `ConnectionString` empty or omitted fails startup, not the first query. The framework's own
tables are always created automatically and need no configuration.

`TablePrefix` is prepended to the base name of the framework's 12 tables (the 11 entities in
`FrameworkEntityTypes.All` plus `schema_migrations`). Lower-case letters, digits and underscores only,
first character a letter, at most 16 characters; the empty string means no prefix. Sample and fork
collections are unaffected. Changing it against a database that already holds framework tables makes
startup create a fresh, empty set under the new names and seed them; the existing tables are not
read. To change the prefix, rename the existing tables first, then restart. With `MigrationsPath`
set, the tracking table `struo_schema_migrations` is likewise a fresh, empty table and every
migration runs again; rename it together with the other tables before changing the prefix.

`MigrationsPath` is only for applying migration scripts you prepare yourself; leaving it empty
disables it. It runs on every backend, without checking which backend a script was written for; one
written for the wrong backend fails outright when it's applied. The migration runner takes no
mutual-exclusion lock, so multiple replicas starting together can try to apply the same file at
once.

The startup order is: snapshot the existing tables → create any missing tables → (Development)
structural sync → (when `MigrationsPath` is set) apply SQL scripts → (Development) schema guard →
seed data; seeding only touches tables created during this startup. CodeFirst creates tables first
and scripts run after, so your own migrations should only `ALTER` existing tables.

`AutoSyncSchema=true` syncs existing tables to match the entity classes' structure. It adds,
modifies, and even drops columns. It only takes effect in Development; in any other environment it's
ignored and logs a warning. Against a table that already holds data, that column-drop behavior can
make data disappear outright, and whether columns are dropped depends on the backend: PostgreSQL
really drops them, SQLite doesn't.

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
| `Query:MaxFacets` | integer | `10` |
| `Query:MaxFacetValues` | integer | `50` |
| `Query:MaxAggregates` | integer | `10` |
| `Query:MaxSearchCandidates` | integer | `1000` |

All eight keys carry `[Range(1, int.MaxValue)]` validation and are bound with `ValidateOnStart`;
setting one to zero or a negative number fails startup immediately. A key name outside this list (a
typo, say) is silently ignored, with no error.

A `limit` above `MaxLimit` is clamped, not rejected; omitting `limit`, or supplying a non-positive
value, falls back to `DefaultLimit`. Exceeding `MaxFilterConditions`, `MaxFacets`, or
`MaxAggregates` instead throws an exception naming the limit, and the query never executes.

`MaxRelationDepth` counts the number of relation hops in a path; the final field itself and the
`_junction` pseudo-segment don't count toward it. Exceeding `MaxSearchCandidates` doesn't truncate
the results. It's treated as an `ISearchProvider` returning more than it should, and throws an
exception (500) instead.

## Auth

| Key | Type | Default |
|---|---|---|
| `Auth:BootstrapAdmin:Email` | string | `"admin@admin.com"` |
| `Auth:BootstrapAdmin:Password` | string | `"admin"` |
| `Auth:Password:MinLength` | integer | `8` |
| `Auth:Password:MaxLength` | integer | `128` |

The pair is applied only when the `struo_users` table is first created and is never backfilled onto an
existing database, but it is read again on every start — the Production default-password warning
compares against it. These two keys have no options class; `Program.cs` reads them straight from
configuration as strings. The seeder doesn't enforce the password policy, so whatever you put here
is accepted as-is. The default `admin` is only five characters, shorter than `MinLength`'s 8.

The password policy is shared by the same validation on `POST /api/users` and
`PUT /api/users/{id}/password`; `MinLength`/`MaxLength` are just the bounds on a reasonable input.
These two keys carry their own startup validation, `MinLength >= 1 && MinLength <= MaxLength`.
Setting them backwards fails startup. Only `MinLength` is returned to the admin SPA, through
`GET /api/config`.

## Rbac

| Key | Type | Default |
|---|---|---|
| `Rbac:PublicReadCollections` | string array | `[]` |

This key is applied only when the `struo_roles` table is first created; changing it and restarting won't
retroactively grant public read access on an existing database. Either set it before the first
startup, or grant the access afterward directly through the admin UI or the API.

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
| `RateLimiting:Password:Enabled` | bool | `true` |
| `RateLimiting:Password:PermitLimit` | integer | `5` |
| `RateLimiting:Password:WindowSeconds` | integer (seconds) | `60` |

None of the nine keys has a validation rule; a wrong value only surfaces once a request comes
in. When either of the two rate-limiter policies (`Login:*` and `Password:*`) blocks a request it
returns 429, with a message naming the policy and, where one applies, a `Retry-After` header; the
`LoginAccount` 429 is returned by the login endpoint itself, with a fixed message.

`Login:*` is a fixed-window limit bucketed by client IP, protecting only `POST /api/auth/login`;
it's off by default because admin users often share the same outbound IP, and enabling it can lock
out an entire office, even with `UseForwardedHeaders` configured correctly.

To enable it behind a reverse proxy, you have to add `UseForwardedHeaders` yourself and list
`KnownProxies`/`KnownNetworks`. This project doesn't register it by default. This limiter's state
lives in process memory and isn't shared across replicas; a cross-replica IP limit belongs in a
gateway or WAF in front of the API.

`LoginAccount:*` buckets by the email in the request and checks before password verification, so a
blocked request never spends Argon2id compute; only failures accumulate, and a successful login
clears that account's count. Whether the account doesn't exist, the password is wrong, or the
account is disabled, all of it counts as one failure and returns the same 429. That uniformity is
what prevents account enumeration.

This limiter's counts live in `IDistributedCache`, the same underlying store as session tickets:
with `Redis:ConnectionString` configured, each account gets one counter shared across replicas; left
empty, each replica counts on its own.

The count isn't an atomic read-modify-write, so in rare cases two simultaneous failures are recorded
as one. The default is looser than the IP limit, with a long 900-second window. It's aimed at
sustained password spraying against a single account.

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

This section is only the **seed source** for the `struo_languages` table: when that table is created
during a startup, each entry becomes a row in list order (`sort` ascending, `enabled` true,
`isDefault` on the row whose code is `DefaultLanguage`). An existing table is never touched; from then
on languages are edited in the admin UI under System › Language. Validated at startup: at least one
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
key yields `null`; both land on the same in-memory branch. This key also backs the store shared by
`RateLimiting:LoginAccount`.

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

| Key | Type | Default |
|---|---|---|
| `Testing:PostgresConnection` | string | `""` |

The running API never reads this section at all; only the test project's own `ConfigurationBuilder`
does. It's also the one key in the entire configuration surface where the environment-variable
override convention doesn't apply. Only `STRUO_TEST_PG_CONNECTION` is honored (checked first), with
this key itself as the fallback. Leaving it empty means the opt-in PostgreSQL integration tests
never connect — each one passes outright rather than being skipped. The SQLite database tests are
unaffected and still run. The connection string's database name must include the word `test`.

## What's next

With configuration in place, the next chapter defines your first content collection.
