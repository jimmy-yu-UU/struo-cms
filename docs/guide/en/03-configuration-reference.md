# 3. Configuration Reference

Every setting in this chapter is read from `src/Struo.Api/appsettings.json` (the committed defaults),
optionally overridden by `appsettings.{Environment}.json`, then by environment variables, then by the
command line — standard ASP.NET Core configuration layering.

**Environment-variable override convention:** any key can be supplied as an environment variable using
a double-underscore (`__`) in place of each `:` in its path, e.g. `Database:ConnectionString` becomes
`Database__ConnectionString`. This works for every key in this chapter with exactly **one exception**:
`Testing:PostgresConnection` (documented at the end of this chapter) is read by the test suite through
its own `ConfigurationBuilder`, which has no environment-variable provider registered — so
`Testing__PostgresConnection` has no effect on it; only `STRUO_TEST_PG_CONNECTION` does.

**Restart behavior:** every option in this chapter is bound once, via the ASP.NET Core `IOptions<T>`
pattern, either at application startup or the first time it is resolved during startup. None of it is
re-read from a live-reloaded configuration file while the process is running — changing any key
requires **restarting the API process** to take effect, even though the underlying `appsettings.json`
file watcher is otherwise active. Where a key has an additional wrinkle beyond "just restart" — for
example, a value that is only ever consulted the first time a table is created — that is called out
explicitly below.

## `Database`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Database:DbType` | enum: `PostgreSQL`\|`MySql`\|`SqlServer`\|`Sqlite`\|`Oracle` | `PostgreSQL` | Selects the SqlSugar backend. Only `PostgreSQL` is the verified runtime target; `Sqlite` is test-only; `MySql`/`SqlServer`/`Oracle` are type-mapped but experimental. |
| `Database:ConnectionString` | string, required | none — ships as a `REPLACE_ME` placeholder | ADO.NET connection string for the selected engine. A missing or empty value fails startup (`[Required]` + `ValidateOnStart`), rather than surfacing as a confusing failure on first query. |
| `Database:MigrationsPath` | string?, optional | empty/absent (disabled) | Directory of reviewed `*.sql` migration scripts applied at startup by the migration runner. Only honored when `DbType` is `PostgreSQL` — a hard no-op on every other backend. Development normally leaves this empty and lets `InitTables` build the schema from the entity classes instead; Production points it at the deployed migrations directory so `001-core-baseline.sql` bootstraps an empty database. |

All three require a restart to take effect.

## `Struo:ContentAssemblies`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:ContentAssemblies` | string[] | `[]` (empty) | Assembly names scanned at startup for `[CmsCollection]` content types. |

This is the one setting in the whole configuration surface with a genuinely unusual read timing:
**it is read from `builder.Configuration` *before* `builder.Build()` is called**, inside
`Program.cs`, not through the normal `IOptions<T>` pipeline used everywhere else. Practically, this
means:

- It still works correctly from any of the *normal* configuration sources — `appsettings.json`,
  `appsettings.{Environment}.json`, user secrets, environment variables, command-line arguments —
  because `WebApplication.CreateBuilder` installs all of those inside its own constructor, before this
  read happens.
- A **custom** configuration provider a fork adds after `CreateBuilder` returns (for example, a secret
  manager or key-vault provider) must be registered on `builder.Configuration` *before* this read, or
  its `Struo:ContentAssemblies` entries will silently never be seen by the metadata scan.
- Each named assembly must actually be resolvable — referenced by the host project (a `ProjectReference`
  in `Struo.Api.csproj`) or otherwise present as a loadable DLL. An unresolvable entry fails startup
  rather than being silently skipped.

Restart required (this is a startup-time scan by construction). Chapter 16 walks through the concrete
two-step opt-in for the Blog sample.

## `Struo:Files`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:Backend` | string: `"local"`\|`"s3"` | `"local"` | Selects the storage backend. Validated at startup — an unrecognized value fails immediately. |
| `Struo:Files:MaxUploadBytes` | long | `26214400` (25 MB) | Maximum accepted upload size. |
| `Struo:Files:AllowedContentTypes` | string[] | the MIME-type list shown in `appsettings.json` (images, PDF, plain text, MP4, MP3, the Office formats, ZIP) | Allow-list of accepted content types for uploads. An empty array allows all content types. |
| `Struo:Files:PresignedRedirect` | bool | `false` | When `true`, `GET /api/files/{id}/content` responds with a 302 redirect to a storage-presigned URL instead of the API streaming the bytes itself. |

All of the above are validated at startup (`ValidateOnStart`) and require a restart.

### `Struo:Files:Local`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:Local:RootPath` | string | `"App_Data/uploads"` | Filesystem root for uploaded files when `Backend` is `local`. Required (validated) when that backend is selected. |

### `Struo:Files:S3`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:S3:Endpoint` | string? | `REPLACE_ME` placeholder | S3-compatible endpoint URL. Required (validated) when `Backend` is `s3`. |
| `Struo:Files:S3:Bucket` | string? | `REPLACE_ME` placeholder | Target bucket name. Required when `Backend` is `s3`. |
| `Struo:Files:S3:AccessKey` | string? | `REPLACE_ME` placeholder | Access key. Required when `Backend` is `s3`. |
| `Struo:Files:S3:SecretKey` | string? | `REPLACE_ME` placeholder | Secret key. Required when `Backend` is `s3`. |
| `Struo:Files:S3:Region` | string | `"us-east-1"` | Region passed to the AWS S3 SDK client. |
| `Struo:Files:S3:ForcePathStyle` | bool | `true` | Path-style addressing — needed by MinIO and most self-hosted S3-compatible servers. |
| `Struo:Files:S3:PresignTtlSeconds` | int | `300` | Lifetime of generated presigned URLs, in seconds. |

Using the bundled MinIO container for this backend needs `docker compose --profile s3 up -d` (which
also runs the one-shot bucket-creation step) — see chapter 2.

### `Struo:Files:ImageTransform`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:ImageTransform:Enabled` | bool | `true` | Turns the on-the-fly image-transform endpoint on or off. |
| `Struo:Files:ImageTransform:MaxWidth` | int | `4096` | Upper bound on requested transform width. |
| `Struo:Files:ImageTransform:MaxHeight` | int | `4096` | Upper bound on requested transform height. |
| `Struo:Files:ImageTransform:AllowedFormats` | string[] | `["webp", "jpeg", "png", "avif"]` | Output formats the transform endpoint will produce. |
| `Struo:Files:ImageTransform:DefaultQuality` | int | `82` | Default encode quality when a request does not specify one. |
| `Struo:Files:ImageTransform:CachePath` | string | `"App_Data/image-cache"` | Root directory for cached transformed-image variants. A relative path is resolved against the application's content root, **not** the process's current working directory — this matters if you ever launch the process from a different working directory than the project folder (e.g. a systemd unit). |

## `Auth:BootstrapAdmin`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Auth:BootstrapAdmin:Email` | string | `"admin@admin.com"` | Bootstrap administrator email. |
| `Auth:BootstrapAdmin:Password` | string | `"admin"` | Bootstrap administrator password. |

**This pair is consulted only once: the first time the `users` table is created.** Once that table
exists, no later boot re-reads or re-applies these values, even against an emptied `users` table — the
account (and its password) is whatever was seeded that first time, or whatever it has since been
changed to through normal use. To ship a different bootstrap identity, set these keys *before* the
first boot against a fresh database (typically via `Auth__BootstrapAdmin__Email` /
`Auth__BootstrapAdmin__Password` environment variables in a deployment pipeline).

If a `Production`-environment start is still seeded with (or still configured to seed) the literal
default password `admin`, the API logs a **warning** naming the exact setting to change — it does not
refuse to start on that condition.

## `Rbac:PublicReadCollections`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Rbac:PublicReadCollections` | string[] | `[]` (empty) | Names of collections granted anonymous ("public" role) read access. |

Like `Auth:BootstrapAdmin`, this list has a first-boot-only effect, but the trigger is different: it is
consulted only the first time the `roles` table is created — at that moment the seeder also creates the
`admin` (super-admin) and `public` roles and assigns the bootstrap admin to `admin`. On every later
boot, the entire RBAC-seeding step (including this grant loop) is skipped because `roles` already
exists. **Editing this key and restarting the app does not retroactively grant public read on an
existing database** — either grant the permission directly (RBAC admin UI or API) against a live
database, or set the value before the very first boot against a fresh one.

## `GraphQl`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `GraphQl:ExposeSchema` | bool (nullable) | *unset* → Development only | Whether this instance may **disclose its GraphQL schema**. Governs both routes that can read it — introspection queries (`__schema`/`__type`) and HotChocolate's built-in `GET /graphql?sdl` — through one resolved flag, so the two cannot drift apart. Unset preserves the shipped behavior (open in Development, closed everywhere else). |

Set it explicitly to override per environment without a rebuild — `GraphQl__ExposeSchema=true` is the
intended switch for a fork that deliberately publishes a public GraphQL API and wants its schema
readable in production.

This is schema **disclosure** only. Query execution through `POST /graphql` is unaffected either way: a
client that already knows its queries never reads the schema at runtime, so closing this breaks no
GraphQL consumer. What it does affect is schema-dependent **tooling** — codegen, Postman/Insomnia schema
import, Apollo Sandbox — which should point at a Development or staging instance. The Nitro browser IDE
is gated separately and stays Development-only regardless of this setting. Chapter 10 has the measured
per-environment behavior.

## `RateLimiting:Login`

| Key | Type | Default | Effect |
|---|---|---|---|
| `RateLimiting:Login:Enabled` | bool | `true` | Turns the in-app login rate limiter on or off. |
| `RateLimiting:Login:PermitLimit` | int | `5` | Attempts allowed per client IP within the window. |
| `RateLimiting:Login:WindowSeconds` | int | `60` | Fixed-window length, in seconds. |

This limiter applies only to `POST /api/auth/login` (fixed-window, partitioned by client IP); it is not
a general API rate limiter. `Enabled = true` is secure-by-default for a direct or single-instance
deployment. Set it to `false` only in multi-pod deployments (e.g. Kubernetes) where per-IP rate
limiting is instead enforced at the ingress/edge/WAF — that layer sees the real client IP and sits in
front of every pod, whereas this limiter's state is in-memory and per-pod, so it cannot enforce a true
global limit across replicas in that topology. Restart required.

## `Branding`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Branding:Name` | string | `"StruoCMS"` | Product name shown on the login page, the admin topbar and the browser tab title. |
| `Branding:LogoUrl` | string? | `null` | Logo URL shown in the same places. |

These are **deploy-time defaults**, not the only source of truth: a super-admin can edit the brand name
and logo in-app (Settings → Site Settings), which is stored in the singleton `site_settings` database
row. At request time (`GET /api/config`), the effective brand name and logo prefer the saved
`site_settings` values field-by-field, falling back to these `appsettings.json` values only where
nothing has been saved (or, for the logo, where the saved file is no longer published). A restart is
only needed to change the *deploy-time default* — changing the live value is an in-app action, not a
configuration change.

## `Redis`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Redis:ConnectionString` | string | `""` (empty) | StackExchange.Redis connection string backing the cookie-authentication session ticket store. |

Empty (the default) falls back to an in-memory distributed cache — sessions are then lost on every
process restart, which is fine for a single quick local run but not for anything longer-lived or
multi-instance. Set this to a real Redis instance (`docker compose up -d` already starts one at
`localhost:6379` by convention) for persistent, shared sessions. This value is read directly from
configuration during service registration, not through `IOptions<T>` — restart required either way.

## `Oidc`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Oidc:Enabled` | bool | `false` | Turns external OpenID Connect login on or off. |
| `Oidc:Authority` | string? | placeholder URL | OIDC authority/issuer. Required when `Enabled` is `true`. |
| `Oidc:ClientId` | string? | `REPLACE_ME` placeholder | OAuth client ID. Required when `Enabled` is `true`. |
| `Oidc:ClientSecret` | string? | none — never in `appsettings.json` | OAuth client secret. Required when `Enabled` is `true`; supply via user secrets, environment variables, or a secret manager — never commit it. |
| `Oidc:CallbackPath` | string | `"/signin-oidc"` | Local callback path registered with the identity provider. |
| `Oidc:Scopes` | string[] | `["openid", "email", "profile"]` | OIDC scopes requested. |
| `Oidc:ReturnUrlDefault` | string | `"/"` | Default post-login redirect. |
| `Oidc:RequireEmailVerified` | bool | `false` | Whether the identity provider's `email_verified` claim is required for JIT account linking. |
| `Oidc:AllowedTenantId` | string? | placeholder | Restricts JIT linking to a single tenant, where the provider supports one. |
| `Oidc:AllowedEmailDomains` | string[] | `[]` (empty — unrestricted) | Restricts JIT linking to specific email domains. |

If `Enabled` is `true`, startup validation requires `Authority`, `ClientId` and `ClientSecret` to all
be non-empty. JIT provisioning links an external identity to an existing local account **by email
equality**, once the tenant/verification/domain checks pass. `RequireEmailVerified` and
`AllowedEmailDomains` default to permissive; `AllowedTenantId` does not — it ships as the non-matching
placeholder `REPLACE_TENANT_ID`, which fails closed and rejects every external tenant until it is
replaced with the real one. A production deployment enabling OIDC should still pin all three explicitly
(a real single-tenant `AllowedTenantId` and/or `AllowedEmailDomains`, and `RequireEmailVerified = true`)
rather than rely on the zero-config defaults. Restart required for all keys in this section.

## `Serilog`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Serilog:MinimumLevel:Default` | string | `"Information"` | Default minimum log level. |
| `Serilog:MinimumLevel:Override` | object (namespace → level) | `{ "Microsoft.AspNetCore": "Warning" }` | Per-namespace level overrides. |
| `Serilog:WriteTo` | array | Console sink, plus a File sink writing `logs/struo-.log` with daily rolling and shared-file access | Configured log sinks. |

Unlike every other section in this chapter, `Serilog` is not bound to a custom C# options class — it is
consumed directly by Serilog's own configuration reader (`ReadFrom.Configuration`) during host startup,
so its shape follows Serilog's own configuration conventions rather than a fixed schema. Restart
required (both the bootstrap logger and the full logger are built once, at startup).

## `Testing`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Testing:PostgresConnection` | string | `""` (empty — suite skips) | Opt-in connection string for the live-PostgreSQL integration test suite. Point it at a **disposable** database whose name contains `test`. |

This section is not read by the running API at all — only by the test project, and by its own
`ConfigurationBuilder`, which has no environment-variable provider registered. That is why this is the
one key in the whole configuration surface where the `Section__Key` convention does **not** apply:
setting `Testing__PostgresConnection` has no effect. The environment variable that does work is
**`STRUO_TEST_PG_CONNECTION`**, checked before the `appsettings.Development.json` key.
