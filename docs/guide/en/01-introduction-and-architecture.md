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
database contains only the eleven framework tables (see below); the admin SPA's sidebar has no "Content"
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
11 entries, each backing one database table:

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
| `UserSession` | `user_sessions` |

Two directories are core **despite living outside `src/Struo.*`**, and a fork keeps both alongside it:
`frontend/` (the admin SPA) and `schema/` (the committed contract snapshots the schema gate checks both
stacks against — `schema/README.md`). Apart from those two, anything outside `src/Struo.*` that is not
one of the eleven types above is not core. In
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

## What is replaceable — and what is not

"Database replaceable," elsewhere in this manual, means the SqlSugar **provider** — not the ORM.
`Database:DbType` selects among five values, mapped 1:1 onto `SqlSugar.DbType` in `DbTypeMapper.Map`
(`src/Struo.Infrastructure/Persistence/DbTypeMapper.cs`): PostgreSQL is the verified runtime target,
SQLite backs the test suite, and MySQL/SqlServer/Oracle are mapped in code but unverified (see
"What it is not" above).

The ORM itself is not something a fork's content project swaps out. Every content entity — see
`samples/Struo.Sample.Blog/Article.cs` — opens with `using SqlSugar;` and carries SqlSugar's own
attributes directly: `[SugarTable]`, `[SugarColumn]`, `[SugarIndex]`, `[Navigate]`, alongside StruoCMS's
own `[CmsCollection]`/`[CmsField]`. The CodeFirst DDL rules chapters 4 and 5 document — the `Id`
override needing `[SugarColumn(IsPrimaryKey = true)]`, `[ColumnShape]` in general — are SqlSugar's
semantics, not a StruoCMS abstraction over them. One example this manual doesn't spell out elsewhere: a
`DateTime` property needs `[ColumnShape(TimestampWithTimeZone)]` to get a zone-aware column on
PostgreSQL instead of a bare `timestamp`, which is why `UserSession.CreatedAt`/`ExpiresAt`
(`src/Struo.Infrastructure/Identity/UserSession.cs`) carry that shape explicitly. Chapter 13's
soft-delete floor is the same direct dependency in a different form, expressed as a registered query
filter rather than a DDL attribute (see below). `IItemRepository`
(`src/Struo.Application/Query/IItemRepository.cs`) is an internal seam inside core —
its sole implementation is `SqlSugarItemRepository` — not an ORM-abstraction layer a fork is meant to
reimplement in order to swap ORMs; a content entity's SqlSugar attributes stay bound to SqlSugar
regardless of what implements that interface.

`ISearchProvider` (`src/Struo.Application/Search/ISearchProvider.cs`) is a different kind of seam —
one core deliberately hands to a fork, the same way `IFileStorage` is. Core ships only the default,
`NullSearchProvider`, which never handles a search, so the built-in `LIKE` scan runs unmodified until a
fork registers its own implementation pointing at whatever search engine it chooses (Meilisearch,
Elasticsearch, PostgreSQL full-text, …). Unlike `IItemRepository` above, a second `ISearchProvider`
implementation is exactly the intended way to extend this template — see chapter 8's
[Search providers](08-query-dsl.md#search-providers) for the contract and a worked example.

`IItemChangeListener` (`src/Struo.Application/Changes/IItemChangeListener.cs`) is the same kind of
deliberately-handed seam: core ships no implementation at all (only the always-registered
`ItemChangeNotifier`, which fans out to whatever a fork registers), so with nothing registered a write
simply notifies zero listeners. A fork registers any number of its own — one to keep a search index in
sync, another to fire a webhook — each reacting to the same post-commit batch of `ItemChange`s
independently. See chapter 13's
[Reacting to writes: `IItemChangeListener`](13-revisions-and-soft-delete.md#reacting-to-writes-iitemchangelistener)
for the full contract.

Practical consequence: a SqlSugar major-version upgrade, or a change in what an attribute like
`[SugarIndex]` means, lands directly on every fork's entity classes — core does not, and cannot, absorb
that change on a fork's behalf. Two DDL decisions that used to work this way no longer do: a bare
`[SugarColumn(IsJson = true)]`'s column width and a translation sidecar's `(fk, locale)` unique index
are both computed inside core's `SqlSugarClientFactory` rather than declared per entity, so core absorbs
a change to either one for you. When you upgrade core, diff `Directory.Packages.props`'s
`SqlSugarCore` version line against your fork's previous checkout and read that release's changelog
before merging.

For the concrete traps this coupling already produces, see chapter 4's
[Minimal collection](04-defining-a-collection.md#minimal-collection) (the `Id` override and
`[SugarColumn(IsPrimaryKey = true)]`) and chapter 5's [Pitfalls](05-field-types.md#pitfalls)
(`[ColumnShape]`, including where combining it with a JSON-column field is refused outright) for the
DDL-attribute side of this coupling.
Chapter 13's [The global query filter](13-revisions-and-soft-delete.md#the-global-query-filter) is a
different kind of SqlSugar coupling: revisions need no extra column on the entity, and `ISoftDeletable`
itself is a package-free marker interface — the SqlSugar dependency for soft delete is the query filter
`db.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null)`, registered against the
SqlSugar client in `SqlSugarClientFactory.Create`, not declared on the entity. This section states the
coupling; those chapters show its concrete shape.

## Technology stack

Backend:

| Component | Detail | Source |
|---|---|---|
| .NET SDK | `rollForward: latestMinor` | `global.json` |
| Target framework | `net10.0` | `Directory.Build.props` |
| C# language version | `latest` | `Directory.Build.props` |
| Nullable reference types | enabled | `Directory.Build.props` |
| ORM | SqlSugarCore | `Directory.Packages.props` |
| Runtime database | PostgreSQL (`postgres:17-alpine` in `docker-compose.yml`) | `docker-compose.yml` |
| Test-only database | SQLite (`Microsoft.Data.Sqlite`) | `Directory.Packages.props` |
| GraphQL | HotChocolate.AspNetCore | `Directory.Packages.props` |
| API explorer | Scalar.AspNetCore (Mars theme, Axios client) | `Directory.Packages.props`, `Program.cs` |
| Logging | Serilog.AspNetCore + Serilog.Sinks.File | `Directory.Packages.props` |
| Password hashing | Isopoh.Cryptography.Argon2 (Argon2id) | `Directory.Packages.props` |
| Object storage (S3 backend) | AWSSDK.S3 | `Directory.Packages.props` |
| Image transforms | NetVips / NetVips.Native | `Directory.Packages.props` |
| Rich-text sanitization | HtmlSanitizer | `Directory.Packages.props` |
| Session store | Redis, via Microsoft.Extensions.Caching.StackExchangeRedis | `Directory.Packages.props` |
| SSO | Microsoft.AspNetCore.Authentication.OpenIdConnect | `Directory.Packages.props` |
| Test runner | xUnit | `Directory.Packages.props` |

Frontend (see `frontend/package.json` for exact versions; caret ranges resolve via the committed
lockfile):

| Component | Detail | Source |
|---|---|---|
| Vue | — | `frontend/package.json` |
| UI library | Tailwind CSS + shadcn-vue's vendored components (reka-ui; vue-sonner for toasts) | `frontend/package.json` |
| Rich text editor | TipTap (starter-kit + extensions) | `frontend/package.json` |
| State management | Pinia | `frontend/package.json` |
| Router | vue-router | `frontend/package.json` |
| Internationalization | vue-i18n | `frontend/package.json` |
| Build tool | Vite | `frontend/package.json` |
| Language | TypeScript | `frontend/package.json` |
| E2E testing | Playwright | `frontend/package.json` |

The .NET SDK, Node.js and pnpm versions every gate runs against are pinned in
`.github/workflows/ci.yml`.

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
| Admin SPA | Core | Vue 3 + Tailwind v4 + shadcn-vue + TipTap |
| Blog sample | Demo, not shipped by default | `samples/Struo.Sample.Blog`; opt-in, deletable |

## Where to go next

- Chapter 2, [Getting Started](02-getting-started.md), to boot the stack and log in.
- Chapter 3, [Configuration Reference](03-configuration-reference.md), for every `appsettings.json`
  key.
- Chapter 4, [Defining a Collection](04-defining-a-collection.md), once you are ready to add your own
  content type.
- Chapter 16, [Sample Walkthrough](16-sample-walkthrough.md), to see a full collection built with
  these primitives before you build your own.
