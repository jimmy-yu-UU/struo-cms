# 1. Introduction & Architecture

## What StruoCMS is

StruoCMS is a reusable, headless CMS **template** built on .NET 10, SqlSugar and PostgreSQL, with a
Vue 3 admin single-page application. "Template" is precise: the repository ships only core
framework/system capability, and a downstream team forks it, then defines its own content collections
and migrations for its actual project.

The core ships:

- A metadata-driven collections engine — you declare a plain C# entity with attributes, and StruoCMS
  derives its database table, REST endpoints, GraphQL schema, query-DSL surface and admin-UI form from
  that one declaration.
- Identity and role-based access control (RBAC), with per-collection read/write/delete permissions,
  Argon2id password hashing, cookie- and bearer-token authentication, and optional OpenID Connect SSO.
- Files and media, with a local-disk or S3-compatible storage backend and on-the-fly image transforms.
- Revisions and soft delete, both opt-in per collection.
- Internationalization (per-locale content), site settings/branding, a query DSL, a REST API, a
  GraphQL API, and the admin SPA shell that drives all of the above.

## What it is not

StruoCMS is not a finished product and contains **no business content models**. The collections you
would recognize from a typical CMS demo — articles, tags, categories — are not part of the shipped
core; they exist only as `samples/Struo.Sample.Blog`, a demo that shows *how* to define collections
using the same primitives your own fork would use. That sample is not referenced by the API host by
default and is meant to be deleted once you have used it to learn the patterns (chapter 16 covers the
opt-in and the deletion checklist).

Concretely, verified on this checkout: a default install has **zero content collections**. The
database contains only the ten framework tables (see below); the admin SPA's sidebar has no "Content"
navigation group at all, because there is nothing to show. That is the correct, intended shape of a
fresh template checkout — not a bug and not an incomplete install.

Also worth stating plainly: of the five database engines StruoCMS's `Database:DbType` setting accepts
(`PostgreSQL`, `MySql`, `SqlServer`, `Sqlite`, `Oracle`), only **PostgreSQL is the supported runtime
database**, and SQLite is used for the test suite only. MySQL, SqlServer and Oracle are type-mapped in
code but unverified/experimental — some ORDER-BY and literal-coercion code paths are written against
PostgreSQL/SQLite behavior specifically.

## Core vs. sample boundary

**Core** is everything under `src/Struo.*` (the four backend projects below) plus the framework's own
persisted entity types, gathered in one list — `FrameworkEntityTypes.All`. That list currently has
10 entries, each backing one database table:

| Entity type | Table |
|---|---|
| `Language` | `languages` |
| `File` | `files` |
| `FileTranslation` | `file_translations` |
| `MediaFolder` | `media_folders` |
| `User` | `users` |
| `Role` | `roles` |
| `Permission` | `permissions` |
| `UserRole` | `user_roles` |
| `Revision` | `revisions` |
| `SiteSettings` | `site_settings` |

Two directories are core **despite living outside `src/Struo.*`**, and a fork keeps both alongside it:
`frontend/` (the admin SPA) and `schema/` (the committed contract snapshots the schema gate checks both
stacks against — `schema/README.md`). Apart from those two, anything outside `src/Struo.*` that is not
one of the ten types above is not core. In
particular, `samples/Struo.Sample.Blog` is a demo, deleted on fork; `db/migrations/` ships **zero** SQL
scripts for the core — CodeFirst creates the core's own tables in every environment, on any of the five
supported backends, so core needs no bootstrap script of its own — and any scripts a fork adds under
`db/migrations/` belong to that fork, not to core; and framework code never references `samples/*` —
this is a checkable invariant, not just a convention (see the dependency rule below).

## Solution layout

```
struo-cms/
├── src/
│   ├── Struo.Domain/           # domain types; no project or package dependencies
│   ├── Struo.Application/      # application-layer abstractions, options, query/security contracts
│   ├── Struo.Infrastructure/   # SqlSugar wiring, identity, files, health checks, DI extensions
│   └── Struo.Api/               # ASP.NET Core host: controllers, GraphQL, Scalar, Serilog, Program.cs
├── samples/
│   └── Struo.Sample.Blog/      # demo content collections (Article/Tag/Category) — deletable
├── tests/
│   └── Struo.Tests/            # xUnit tests; references all four src projects and the sample
├── frontend/                    # Vue 3 admin SPA (separate pnpm workspace)
├── db/migrations/               # reviewed *.sql scripts that alter existing tables (all backends); ships empty
└── docs/                        # this manual
```

## Dependency rule

The four backend projects form a strict, one-directional dependency chain (read from each project's
own `.csproj`):

| Project | References |
|---|---|
| `Struo.Domain` | — (no project or package references at all) |
| `Struo.Application` | → `Struo.Domain` |
| `Struo.Infrastructure` | → `Struo.Application`, `Struo.Domain` |
| `Struo.Api` | → `Struo.Application`, `Struo.Infrastructure` |

```
Struo.Domain  <──  Struo.Application  <──  Struo.Infrastructure  <──  Struo.Api
   (nothing)         (→ Domain)           (→ Application, Domain)   (→ Application, Infrastructure)
```

Note that `Struo.Api` does not reference `Struo.Domain` directly — only transitively, through
`Struo.Application` and `Struo.Infrastructure`. `Struo.Domain` stays free of every external package;
persistence attributes live on entities in `Struo.Infrastructure`, not on domain types.

**Framework code never references `samples/*`.** None of the four `src/Struo.*` projects has a
`ProjectReference` into `samples/`; only `tests/Struo.Tests/Struo.Tests.csproj` (which intentionally
exercises the sample) does. This is what makes the sample truly optional and deletable at the
`src/Struo.*` level — removing `samples/Struo.Sample.Blog` cannot break the core. A naive `rm -rf
samples/` still breaks a solution-level `dotnet build` (`StruoCMS.slnx` and the test project reference
above), though — see chapter 16's removal checklist for the full, safe procedure.

## Technology stack

Backend:

| Component | Version | Source |
|---|---|---|
| .NET SDK | 10.0.0, `rollForward: latestMinor` | `global.json` |
| Target framework | `net10.0` | `Directory.Build.props` |
| C# language version | `latest` | `Directory.Build.props` |
| Nullable reference types | enabled | `Directory.Build.props` |
| ORM | SqlSugarCore 5.1.4.215 | `Directory.Packages.props` |
| Runtime database | PostgreSQL (`postgres:17-alpine` in `docker-compose.yml`) | `docker-compose.yml` |
| Test-only database | SQLite (`Microsoft.Data.Sqlite` 10.0.9) | `Directory.Packages.props` |
| GraphQL | HotChocolate.AspNetCore 16.4.0 | `Directory.Packages.props` |
| API explorer | Scalar.AspNetCore 2.16.5 (Mars theme, Axios client) | `Directory.Packages.props`, `Program.cs` |
| Logging | Serilog.AspNetCore 10.0.0 + Serilog.Sinks.File 7.0.0 | `Directory.Packages.props` |
| Password hashing | Isopoh.Cryptography.Argon2 2.0.0 (Argon2id) | `Directory.Packages.props` |
| Object storage (S3 backend) | AWSSDK.S3 4.0.25.3 | `Directory.Packages.props` |
| Image transforms | NetVips 3.2.0 / NetVips.Native 8.18.4 | `Directory.Packages.props` |
| Rich-text sanitization | HtmlSanitizer 9.1.974 | `Directory.Packages.props` |
| Session store | Redis, via Microsoft.Extensions.Caching.StackExchangeRedis 10.0.9 | `Directory.Packages.props` |
| SSO | Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.9 | `Directory.Packages.props` |
| Test runner | xUnit 2.9.3 | `Directory.Packages.props` |

Frontend (versions as declared in `frontend/package.json`; caret ranges resolve via the committed
lockfile):

| Component | Version | Source |
|---|---|---|
| Vue | ^3.5.39 | `frontend/package.json` |
| UI library | PrimeVue ^4.5.5 (+ `@primeuix/themes` ^2.0.3) | `frontend/package.json` |
| Rich text editor | TipTap ^3.27.1 (starter-kit + extensions) | `frontend/package.json` |
| State management | Pinia ^3.0.4 | `frontend/package.json` |
| Router | vue-router ^5.1.0 | `frontend/package.json` |
| Internationalization | vue-i18n ^11.4.6 | `frontend/package.json` |
| Build tool | Vite ^8.1.1 | `frontend/package.json` |
| Language | TypeScript ~6.0.2 | `frontend/package.json` |
| E2E testing | Playwright ^1.61.1 | `frontend/package.json` |

Toolchain versions pinned in CI (`.github/workflows/ci.yml`): .NET SDK `10.0.x`, Node.js `24`, pnpm
`10`.

## Capability summary

| Capability | Status | Notes |
|---|---|---|
| Metadata-driven collections | Core | `[CmsCollection]` attribute, scanned from assemblies listed in `Struo:ContentAssemblies` |
| Identity & RBAC | Core | Argon2id hashing; cookie + bearer auth; per-collection read/write/delete grants |
| SSO (OpenID Connect) | Core, off by default | `Oidc:Enabled = false` |
| Files & media | Core | local-disk or S3-compatible backend; on-the-fly image transforms |
| Revisions | Core, opt-in per collection | `[CmsCollection(Revisions = true)]` |
| Soft delete | Core, opt-in per collection | `ISoftDeletable` |
| Internationalization | Core | per-locale translation sidecar tables |
| Site settings / branding | Core | singleton `site_settings` row, editable in-app by a super-admin |
| Query DSL | Core | filter/sort/pagination with whitelist-validated field and relation paths |
| REST API | Core | ASP.NET Core Controllers, unified response envelope |
| GraphQL API | Core | HotChocolate, schema generated from the same collection metadata |
| Admin SPA | Core | Vue 3 + PrimeVue + TipTap |
| Blog sample | Demo, not shipped by default | `samples/Struo.Sample.Blog`; opt-in, deletable |

## Where to go next

- Chapter 2, [Getting Started](02-getting-started.md), to boot the stack and log in.
- Chapter 3, [Configuration Reference](03-configuration-reference.md), for every `appsettings.json`
  key.
- Chapter 4, [Defining a Collection](04-defining-a-collection.md), once you are ready to add your own
  content type.
- Chapter 16, [Sample Walkthrough](16-sample-walkthrough.md), to see a full collection built with
  these primitives before you build your own.
