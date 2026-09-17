# 2. Architecture

After you fork StruoCMS, you will add content and swap out components. First you need to know how
the system is layered, and which parts are the core you should not touch.

## Four projects and the dependency direction

The backend solution under `src/` has four projects, each responsible for one layer:

- `Struo.Domain`: pure domain types.
- `Struo.Application`: application-layer interfaces and options, plus the query and security
  contracts.
- `Struo.Infrastructure`: SqlSugar wiring, authentication, file storage, health checks, and various
  DI extensions.
- `Struo.Api`: the ASP.NET Core host itself — controllers, GraphQL, Scalar, Serilog, `Program.cs`.

Dependencies flow one way only. `Struo.Domain` depends on nothing; `Struo.Application` depends on
`Struo.Domain`; `Struo.Infrastructure` depends on `Struo.Application` and `Struo.Domain`;
`Struo.Api` depends on `Struo.Application` and `Struo.Infrastructure`. `Struo.Api` never references
`Struo.Domain` directly — it reaches it only indirectly through the two middle layers.

`Struo.Domain` itself carries no `PackageReference` or `ProjectReference` at all — not even
SqlSugar's persistence attributes appear on domain types.

The test project `tests/Struo.Tests` is the only place that references all four projects plus the
sample project. `Struo.Application` and `Struo.Infrastructure` both grant it `InternalsVisibleTo`;
if a fork renames the test project, both `InternalsVisibleTo` declarations need to change together.

## The core/sample boundary

The core is every file under `src/Struo.*`, plus the entities the framework itself persists. Those
entities are collected in `FrameworkEntityTypes.All`, eleven in total:

| Entity | Table |
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

Seven of the eleven are themselves collections — they declare `[CmsCollection]`: `Language`, `File`,
`MediaFolder`, `User`, `Role`, `Permission`, `UserRole`. The other four — `FileTranslation`,
`Revision`, `SiteSettings`, `UserSession` — are framework tables, but not collections.

The core also has two folders outside `src/Struo.*`, plus `db/migrations/README.md`, that a fork
keeps:

- `frontend/`: the admin SPA.
- `schema/`: the snapshots the schema gate uses to compare the admin SPA and backend contracts.

The only core-owned file under `db/migrations/` is a `README.md` documenting the mechanism:
CodeFirst creates the framework tables itself on any environment, on any configured database, so the
core needs no startup SQL script; scripts a fork adds of its own belong to that fork.

Framework code never references anything under `samples/`; a test enforces that boundary.

## One declaration drives everything

A single entity class declaration determines the shape of five things: the database table schema,
the REST endpoints, the GraphQL schema, the query DSL, and the admin SPA form.

A content entity class carries both sets of attributes together: SqlSugar's own `[SugarTable]`,
`[SugarColumn]`, `[SugarIndex]`, `[Navigate]`, and StruoCMS's `[CmsCollection]`/`[CmsField]`. Two
separate layers do not each manage their own.

These attributes are scanned only once at startup; the result is cached in a singleton metadata
provider and never rescanned per request.

## What can be swapped, what can't

Swappable parts:

- **Database engine**: `Database:DbType`'s five values map one-to-one to SqlSugar's own `DbType`;
  what changes is the provider underneath SqlSugar, not SqlSugar itself. CodeFirst relies on
  vendor-neutral `[ColumnShape]`/`ColumnTypeMap` mapping, so all five engines share one
  table-creation path; chapter 1 lists which engines are verified.
- **File backend**: `IFileStorage` is an interface the core hands to a fork; local disk and
  S3-compatible storage are both implementations of it.
- **Search provider**: `ISearchProvider` is another interface the core hands to a fork. The core's
  only built-in implementation is `NullSearchProvider`, registered with `TryAddScoped`, and it never
  handles a search. Until a fork registers its own provider, search runs the built-in LIKE scan.
- **Change notifications**: `IItemChangeListener` is another interface the core hands to a fork; the
  core provides only a default-registered `ItemChangeNotifier` to dispatch the notifications. When a
  fork registers no listener, a write notifies zero listeners; a fork can register any number.
- **Identity stores**: identity data access sits behind several interfaces too —
  `IUserCredentialStore`, `IUserAccountStore`, `IPermissionGrantStore`, `IRolePermissionStore`,
  `IExternalUserStore`, `IUserSessionStore` — all under `Struo.Application/Security/`. Each has a
  corresponding `SqlSugar*` implementation class, registered with `AddScoped` inside
  `AddStruoInfrastructure`. To replace an identity store, a fork registers its own implementation
  after calling `AddStruoInfrastructure` — the later registration wins, since these are not
  `TryAdd`.

Not swappable: SqlSugar itself, and the framework tables it drives. Every content entity carries
SqlSugar's attributes directly, so every entity a fork writes inherits that layer's semantics.
`IItemRepository` likewise has only one implementation, `SqlSugarItemRepository` — not a removable
ORM abstraction layer.

A SqlSugar major-version upgrade, or a semantic change to an attribute like `[SugarIndex]`, hits
every entity class a fork has written. Before upgrading, check the `SqlSugarCore` version in
`Directory.Packages.props` and read that version's changelog.

The bare JSON column width, tied to framework tables, is computed inside `SqlSugarClientFactory`.
The translation sidecar's unique index is derived by `TranslationSidecarIndexPolicy` and applied
by that same factory. The soft-delete filter condition is registered by the same factory, as a
query filter (`db.QueryFilter.AddTableFilter<ISoftDeletable>`); a fork does not need to recompute
or re-register any of them.

Every database access goes through SqlSugar. Only four deliberate exceptions assemble string SQL:
the scripts under `db/migrations/`, `SchemaGuard`'s read-only queries used only in development, the
four string forms assembled for relation-filter subqueries, and the strings used for ORDER BY.

## Technology stack

| Layer | Technology | Version or source |
|---|---|---|
| .NET SDK | 10.0.0, `rollForward: latestMinor` | `global.json` |
| Backend build settings | `net10.0`, nullable enabled, warnings as errors | `Directory.Build.props` |
| Dev database container | PostgreSQL `17-alpine` | `docker-compose.yml` |
| Dev cache container | Redis `7-alpine` | `docker-compose.yml` |
| Frontend framework | Vue 3.5.x, Vite 8.2.0, TypeScript 6.0.x | `frontend/package.json`/lockfile |
| Frontend styling and components | Tailwind CSS 4.3.x, shadcn-vue (reka-ui 2.10.x) | `frontend/package.json`/lockfile |
| Frontend editor and state | TipTap 3.30.x, Pinia 4.x, vue-router 5.x, vue-i18n 11.x | `frontend/package.json`/lockfile |
| Frontend testing | Playwright 1.62.x, Vitest 4.1.x | `frontend/package.json`/lockfile |
| CI version pins | Node 24, pnpm 10, .NET SDK `10.0.x` | `.github/workflows/ci.yml` |

`Directory.Build.props` settings apply to the whole solution; a project a fork adds inherits them
without setting them up again. `global.json`'s pinned `10.0.0` plus `latestMinor` and CI's installed
`10.0.x` are two separate pins, not the same thing.

Read the two outer projects' package lists straight from their own `.csproj`: `Struo.Api` carries
GraphQL, OpenAPI, and Serilog; `Struo.Infrastructure` carries `SqlSugarCore`, S3, password hashing,
and image processing.

API documentation uses Scalar, with the Mars theme, and JavaScript/Axios as the default client.

When `Redis:ConnectionString` is configured, Redis stores cookie-authentication session tickets in
addition to caching.

## Where a request passes through

Before reaching a controller, a request passes through, in order: the nosniff header, Serilog
request logging, CORS, exception handling, authentication, authorization, CSRF protection,
permission resolution, the rate limiter, and finally the controller or GraphQL. Exception handling
is deliberately placed ahead of authentication, so a failure during the authentication stage itself
still returns an enveloped 500.

Query parameters are validated against metadata by `QueryValidator` before they become SQL. Once
inside a controller, data access has exactly one path: `IItemRepository` has a single
implementation, `SqlSugarItemRepository`; no controller touches `ISqlSugarClient` directly.

## What's next

You have seen the layers and the boundaries; next, get the API and admin SPA actually running: read
[Chapter 3: Getting Started](03-getting-started.md).
