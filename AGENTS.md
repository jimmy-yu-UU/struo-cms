# AGENTS.md

Guidance for any AI coding agent working in this repository. This file is meant to stand on its own —
read it first, every session. See "Where to read more" below for the deeper references.

## What this repository is

StruoCMS is a reusable, forkable **headless-CMS template**, not a finished product. It ships only
core framework/system capability — metadata-driven collections, identity/RBAC, files/media, revisions,
i18n, site settings, a query DSL, REST + GraphQL, and the admin SPA shell. It ships **no business
content models**. Downstream teams fork it and add their own collections and migrations for their
actual project.

## Core vs. sample boundary

**Core** = `src/Struo.*` plus the eleven `FrameworkEntityTypes.All` types
(`src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs`): `Language`, `File`, `FileTranslation`,
`MediaFolder`, `User`, `Role`, `Permission`, `UserRole`, `Revision`, `SiteSettings`, `UserSession`. Of
these, seven carry `[CmsCollection]` (are themselves collections): `Language`, `File`, `MediaFolder`,
`User`, `Role`, `Permission`, `UserRole` — `FileTranslation`, `Revision`, `SiteSettings`, and
`UserSession` are framework tables but not collections. If you're unsure whether something is core, it's core only if it lives in
`src/Struo.*` — with a few named exceptions that are core despite living elsewhere, `frontend/` among
them. `schema/` is core too, despite living outside that path: it holds the
committed snapshots of these seven collections' wire metadata and of the declared interface enums
(`schema/README.md`), and a fork keeps
it alongside `src/Struo.*`.

`samples/Struo.Sample.Blog` (Article/Tag/Category/…) is an optional, detachable **demo** — it shows how
to define collections using the same primitives a fork would use. It is not shipped capability: the
host has no `ProjectReference` to it, `Struo:ContentAssemblies` ships as `[]`, and framework code never
references it. It is listed as a solution member in `StruoCMS.slnx`, and only `tests/Struo.Tests`
carries an actual code reference to it — which is what makes it truly deletable — doing so also means
removing it from `StruoCMS.slnx` and deleting the many test files that use it as a fixture.

## Repo map

| Path | Contents |
|---|---|
| `src/Struo.Domain` | Domain types only — no project or package references at all. |
| `src/Struo.Application` | Application-layer abstractions, use-case contracts, options, query/security contracts. |
| `src/Struo.Infrastructure` | SqlSugar wiring, identity, files, health checks, the general-purpose `AddStruoXxx` DI registration (the host-specific ones — auth, CORS, OIDC, GraphQL — live in `src/Struo.Api`). |
| `src/Struo.Api` | The ASP.NET Core host: controllers, GraphQL, Scalar, Serilog, `Program.cs`. |
| `frontend/` | The Vue 3 admin SPA, a separate pnpm workspace, on Tailwind v4 + shadcn-vue. |
| `samples/Struo.Sample.Blog` | Optional, detachable demo content project — not shipped capability. |
| `schema/` | Committed snapshots the schema contract gate checks both stacks against: core-collection wire shape (`core-collections.json`) and the declared interface enums (`interfaces.json`) — `schema/README.md`. |
| `docs/` | The bilingual manual (`guide/en/`, `guide/zh-TW/`), this `ai/` reference set, and the VitePress site that renders the manual (`package.json`, `.vitepress/` — its own npm project, separate from `frontend/`). |
| `tests/Struo.Tests` | The backend xUnit suite (unit, integration, and the template-invariant guard). |

## Hard constraints

- **Dependency direction** (structural, visible in each project's `.csproj`): `Struo.Domain` → nothing;
  `Struo.Application` → `Struo.Domain`; `Struo.Infrastructure` → `Struo.Application` + `Struo.Domain`;
  `Struo.Api` → `Struo.Application` + `Struo.Infrastructure`. Never weaken this with a reversed or
  skip-layer project reference: reversing an edge creates a circular project reference and fails the
  build outright, while a skip-layer reference (e.g. `Struo.Api` referencing `Struo.Domain` directly)
  compiles cleanly, so nothing but this rule and code review catches it. Framework code never
  references `samples/*` — this one edge **is** test-enforced:
  `tests/Struo.Tests/Template/TemplateInvariantsTests.cs`'s
  `Host_project_has_no_project_reference_into_samples` fails if `Struo.Api.csproj` ever gains a
  `ProjectReference` into `samples/`.
- `Struo.Domain` stays free of external packages — `Struo.Domain.csproj` declares zero
  `PackageReference`/`ProjectReference` entries; this is a convention checked by reading the file, not
  by an automated test. A package reference here puts an external type in the one assembly every other
  layer compiles against, and nothing fails until a fork tries to swap that package out.
- **Target framework**: .NET 10 (`net10.0`, `Directory.Build.props`). **Database support**: SqlSugar
  is configured for five backends (`Database:DbType`: `PostgreSQL`/`MySql`/`SqlServer`/`Sqlite`/
  `Oracle`), but only **PostgreSQL is the verified runtime target**; `Sqlite` is used for the test suite
  only; `MySql`/`SqlServer`/`Oracle` are type-mapped in code but unverified/experimental. Schema
  creation (the core migrations, and CodeFirst in unit harnesses) is designed and mapped to run on all
  five backends, via a dialect-neutral `[ColumnShape]`/`ColumnTypeMap` layer that keeps vendor type literals in `ColumnTypeMap.cs` —
  with one gated exception, `SqlSugarClientFactory`'s SQLite identity-column rewrite
  (`ApplySqliteIdentityColumnRewrite`), which hardcodes `INTEGER` for a SQLite identity primary
  key — but that mapping is exercised live only for the core schema (the migration live tests run on
  SQL Server, MySQL and MariaDB; nothing runs on Oracle), and the query layer is unverified there: some
  ORDER-BY and literal-coercion code paths are written against PostgreSQL/SQLite behavior specifically. The per-backend type decisions behind that layer,
  and what is and is not verified about each, are recorded in
  `docs/ai/decisions/column-type-map-per-backend-literals.md`.

## Invariants

- **All database access is through SqlSugar, with four deliberate exceptions for raw SQL.** (Schema
  changes and seed inserts are FluentMigrator migrations, see "Migrations are the only schema source"
  below.) The first two assemble portable SQL text; the third is deliberately per-backend lock SQL that runs wherever
  `migrate` runs; the fourth is per-backend read-only catalog SQL that runs wherever `migrate:check`
  or the Development startup schema check runs, and wherever a fork's tests call
  `SchemaChecker.Check`.
  The first is the relation-filter pushdown's subquery wrapper
  (`src/Struo.Infrastructure/Query/SubQueryConditional.cs`, `OrOfSubqueriesConditional.cs`): SqlSugar's
  `ConditionalModel`/`ConditionalCollections` have no subquery member, so exactly four string forms are
  assembled around SqlSugar-generated SQL — `<col> IN (<sql>)`, `<col> NOT IN (<sql>)`,
  `(<col> IS NULL OR <col> NOT IN (<sql>))` and `(<sql1> OR <sql2> …)` — where `<col>` always comes from
  `GetDbColumnName` and `<sql>` from `ToSql()`.
  The second is `OrderByExpressionBuilder`'s ORDER BY text
  (`src/Struo.Infrastructure/Query/OrderByExpressionBuilder.cs`), the sole source of the string passed
  to the one `queryable.OrderBy(string)` call, in `SqlSugarItemRepository.RunQueryAsync`: it assembles
  three per-field forms — a plain column (`<col> ASC|DESC`), a to-one relation-path sort as a correlated
  subquery with one JOIN per extra hop (`RelationOrderExpr`), and a translatable-field sort as a correlated
  subquery against the translation sidecar with the query locale embedded as an escaped string literal
  (`TranslatableOrderExpr`; the locale is either a request locale already validated by
  `ItemService.ValidateLocale`, or — when no `?locale=` was given — the configured default code
  (`ItemService.QueryAsync`'s `locale ?? languages.DefaultCode()`), whose format is guarded only on
  write, by `ValidateLanguageCodeIfNeeded`; the quote-doubling on the literal at this sink remains
  either way) — plus the no-client-sort default clause (`<created> DESC, <id> ASC`, or `<id> ASC`
  alone when the entity has no `CreatedAt`) and the `, <id> ASC` pagination tiebreak/comma-join that
  wrap every sort. Every column name across all of it comes from
  `db.EntityMaintenance.GetDbColumnName`/`GetTableName`, never a hardcoded string.
  The third is the migration lock (`src/Struo.Infrastructure/Migrations/MigrationLockSql.cs`): one
  advisory-lock acquire command and one release command per backend (PostgreSQL
  `pg_try_advisory_lock`, MySQL `GET_LOCK`, SQL Server `sp_getapplock`; SQLite and Oracle take no
  lock), parameterised, with no request input.
  The fourth is `IndexCatalog`'s read-only index queries
  (`src/Struo.Infrastructure/Migrations/Schema/IndexCatalog.cs`), used by `migrate:check` and
  `SchemaChecker.Check`: one catalog query per backend (PostgreSQL, SQL Server, MySQL/MariaDB, SQLite),
  parameterised with `@table`, with no request input and no writes. Oracle has no index query, so the
  index check is skipped with a warning.
  Nothing else may assemble SQL text, and SqlSugar's own string overloads
  (`Select<T>(string)`, `GroupBy(string)`, `OrderBy(string)`, `Where(string, …)`) count as hand-written
  SQL outside the four exceptions above: use the typed lambda overloads, and when the typed surface
  cannot express something, stop and get the maintainer's explicit approval instead of falling back to
  a string.
- **Schema changes are FluentMigrator migrations** (`src/Struo.Infrastructure/Migrations/`).
  `MigrationHost` runs them and records them in `{TablePrefix}schema_versions`. The commands are
  `migrate`, `migrate:status`, `migrate:preview`, `migrate:baseline`, `make:migration` and
  `migrate:check`, each the first argument to `Struo.Api`.
  `make:migration <Name> [--entity <Type>] --output <dir> [--namespace <Ns>]` writes a new migration
  class; with `--entity` it holds a `Create.Table` built from that entity's metadata. It never
  overwrites a file. `migrate:check` compares the live schema with entity metadata, writes nothing and
  exits 1 on any error. A fork calls `SchemaChecker.Check` from its own tests and asserts no errors;
  it needs the application's DI-configured `ISqlSugarClient`, which supplies the table prefix and the
  translation-sidecar policy.
  On Oracle, `migrate:check` skips the type and length checks with one warning. On SQL Server it warns
  about non-Unicode string columns. Application queries and writes stay on SqlSugar.
- **Outbound JSON is camelCase** everywhere (`JsonSerializerDefaults.Web`).
- **The unified response envelope** wraps every REST response: `{success, data, meta?}` or
  `{success:false, error:{code, message, details?}}` (`src/Struo.Api/Http/Envelope.cs`,
  `EnvelopeResultFilter.cs`).
- **Metadata is scanned once at startup and cached** in a singleton `IMetadataProvider` — the scan
  itself never re-runs per request (`MetadataScanner.Scan`, called from `AddStruoMetadata`).
- **Query DSL paths are whitelist-validated** against scanned metadata before any SQL is built
  (`QueryValidator`) — an unknown filter/sort/relation path is rejected, never passed through.
  `QueryValidator` also enforces RBAC on every collection a relation path traverses, not just the root
  — see `docs/ai/conventions.md`'s "Input validation at boundaries".
- **RichText is sanitized server-side** before required-field validation, via `RichTextCleaner`
  (`src/Struo.Application/Query/Write/RichTextCleaner.cs`), which wraps `IHtmlSanitizer` plus
  blank-document coercion. Non-translatable RichText fields are sanitized in `ItemDeserializer.cs`;
  translatable ones are sanitized separately, per locale, in `ItemWriteSideSync.SyncTranslationsAsync`
  (`ItemWriteSideSync.cs`). Skipping this means stored HTML reaches every read path — REST, GraphQL,
  and the admin SPA's renderer — unsanitized: `IHtmlSanitizer`/`RichTextCleaner` is this repository's
  only server-side XSS control for RichText fields.
- **Immutable update patterns**: domain/query model types are `record`s with `init` properties, updated
  via non-destructive `with` expressions, not mutated in place — anything that flows through the
  metadata cache or the query pipeline is shared, longer-lived state, and an in-place mutation there
  would be visible to every subsequent caller.
- **Migrations are the only schema source.** The core migrations
  (`src/Struo.Infrastructure/Migrations/Core/`) create the framework tables and seed the languages,
  RBAC and the bootstrap admin; a fork's own migrations create its collections' tables (the sample Blog
  ships its own in `samples/Struo.Sample.Blog/Migrations/`). The host never creates or alters a table
  itself. `StartupMigrationGate` runs before the host serves: with `Database:MigrateOnStartup` set it
  applies pending migrations under the migration lock; otherwise it refuses to start while migrations
  are pending (the message says to run `migrate`) or while the framework tables exist without a
  migration history (the message says to run `migrate:baseline`). `MigrateOnStartup` defaults to
  `false` in every environment, so a deployment runs `migrate` as a step of its own. In Development the
  gate also runs `SchemaChecker` and fails startup when the schema differs from the entity model.
  The seed runs only as part of `migrate` or `MigrateOnStartup`.
  `migrate:baseline` joins a database built by v0.8.x CodeFirst: it records every migration as applied
  without running it, and refuses when migration history already exists or the core tables are missing.
  Tests: app-level hosts (`ApiFactory`) run the migrations with `MigrateOnStartup=true`; unit harnesses
  build tables with `db.CodeFirst.InitTables(...)`. That is safe only because `CoreSchemaParityTests`
  (SQLite) and `CoreMigrationsLiveTests` (PostgreSQL, SQL Server, MySQL, MariaDB) prove CodeFirst and
  the core migrations build the same structure. SqlSugar's default `InitTables` also drops columns
  absent from the entity, which is why no running application uses it — see
  `docs/ai/decisions/migrations-are-the-only-schema-source.md`.
- **Hidden fields are never projected on read** — `[CmsField(Hidden = true)]` is excluded from schema,
  GraphQL, item projections, and query filtering/search/sort. This is a **read-side exclusion only**
  on REST: `Hidden` plays no part in either write-path allowlist. `ItemDeserializer.cs`'s create/update
  allowlist admits every declared field that isn't `IsSystem`/`ReadOnly`, `Hidden` or not, and
  `ItemService.UpdateCoreAsync`'s merge-overlay applies the same `IsSystem`/`ReadOnly` check alone — so
  a client that already knows a hidden field's name can still set it via a normal REST create/update.
  GraphQL is stricter here:
  `CollectionSchemaBuilder` (`src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`) excludes
  `Hidden`/`ReadOnly`/`IsSystem` fields from every create/update input type, so a hidden field never
  appears as a GraphQL mutation argument in the first place. Do not rely on `Hidden` alone as a write
  guard for a privileged column; pair it with `ReadOnly` (or keep the field off the write path some
  other way) if it must never be client-writable.

## Task playbooks (condensed — see `docs/ai/task-playbooks.md` for the full form)

1. **Add a collection** — new entity class (outside `src/Struo.*`) inheriting `AuditableEntity`,
   with `[CmsCollection]`/`[CmsField]`; wire its assembly into `Struo:ContentAssemblies` via
   `appsettings.Development.json`, an environment-variable override, or the fork's own
   configuration — not the shipped `appsettings.json`, which stays `[]`
   (`TemplateInvariantsTests.Shipped_appsettings_declares_no_content_assemblies` asserts it;
   changing that file on purpose means updating or removing that test as part of the same change)
   — plus a `ProjectReference` from `Struo.Api`; grant RBAC. Create the table with a migration: in the
   fork's content project run `make:migration <Name> --entity <Type> --output <dir> [--namespace <Ns>]`
   (`<dir>` is the fork's own migrations directory), review the generated file, then run `migrate`.
   Gate: `dotnet build && dotnet test`.
2. **Add a field type** — swapping an editor for an existing `FieldInterface` is frontend-only
   (`frontend/src/lib/fieldTypes/registry.ts`). A genuinely new `FieldInterface` value touches
   the backend enum, `MetadataScanner`, `src/Struo.Api/GraphQl/SchemaTypeMapper.cs` (an unmapped
   member fails GraphQL schema build, which surfaces as a misleading `ObjectDisposedException` on
   `IServiceProvider` during host startup rather than a clear error — see
   `docs/ai/task-playbooks.md` Playbook 2b), possibly `SqlSugarClientFactory`'s column-widening
   hook, and — both required — `frontend/src/lib/fieldTypes/types.ts`'s type
   union/`ALL_FIELD_INTERFACES` and its `registry.ts` component. Then regenerate
   `schema/interfaces.json` — required for *every* new member, whether or not a collection uses
   it yet — and `schema/core-collections.json` too if the value is used by a core collection
   (`schema/README.md`; one command does both). A new `RelationInterface` member likewise needs
   an entry in `frontend/src/lib/relationInputKind.ts`, or the relation renders read-only at
   runtime — `pnpm test`'s schema-contract test (`frontend/tests/schemaContract.test.ts`) is what
   catches the missing entry first. Gate: four of the five standing gates — see
   `docs/ai/task-playbooks.md` Playbook 2b for when the fifth applies; live-PostgreSQL check if
   you touched column mapping.
3. **Add an endpoint** — new controller under `src/Struo.Api/Controllers/`, envelope-friendly return
   values, `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]` on any action that must not
   be anonymous, new domain exceptions mapped in `DomainErrorMap`. Gate: `dotnet build && dotnet test`.
4. **Add a migration** — a new FluentMigrator class in the fork's migrations directory, versioned
   `yyyyMMddHHmm` and deriving from `StruoMigration`; `make:migration <Name> --output <dir>` writes the
   skeleton. Use portable calls (`Create`/`Alter` with `AsString`, `AsGuid`, …; `AsJson(Db)` and
   `AsLongText(Db)` for the types that differ), and branch on `StruoMigration.Db` only where a backend
   needs its own form. Forward-only: never edit a migration that may already be applied anywhere —
   add another. Apply it with `migrate`; `migrate:preview` prints the pending DDL first. Gate:
   `dotnet build && dotnet test`, plus `migrate` and `migrate:check` against a disposable database of
   the backend the deployment actually uses.
5. **Change the admin SPA** — only when metadata isn't enough (new field editor, theming, i18n, a
   bespoke view); the SPA never hardcodes a collection's fields/columns/labels. Gate: `pnpm test &&
   pnpm build`.

## Verification

The **five standing gates** — the same ones CI runs on every push/PR — are `dotnet build`,
`dotnet test`, `pnpm test` and `pnpm build` (the latter two from `frontend/`), and `pnpm build` from
`docs/`, which resolves every cross-chapter link in the manual, fails on a dead one, checks that every
chapter actually rendered — `vitepress build` alone exits 0 on a page whose body came out empty — and
fails any table wider than the site's content column (`docs/scripts/check-table-width.mjs`). CI's docs
job also runs `pnpm test` from `docs/` first — the guard scripts' own unit tests — as a CI step, not a
sixth gate. Run whichever apply to your change; run all five before anything touching more than one of
the three.

`dotnet test` includes `CoreSchemaSnapshotTests`, which fails when a core collection/field change has
not been mirrored into `schema/core-collections.json`, or an enum change into `schema/interfaces.json`;
`pnpm test` includes `frontend/tests/schemaContract.test.ts`, which fails when the admin SPA cannot
handle what those snapshots describe — including a `FieldInterface` or `RelationInterface` member the
frontend has no mirror for, whether or not any collection uses it.
Regenerate with `UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot` (bash) and commit the
result — see `schema/README.md` for the full contract, including the PowerShell form of that command.

**Any change to DB behavior** (a migration, a `SqlSugarClientFactory` column-mapping change, a
query-building change) **should be verified against a live instance of whichever database this
deployment is actually configured for** — SQLite passing is not evidence of correctness on any other
backend, and the SQLite suite is a development convenience, not the portability guarantee. A project
built on this template verifies against the database(s) it actually uses; a change to the original
template project itself is verified against every backend available locally (PostgreSQL, SQL Server,
MySQL, MariaDB, SQLite, and Oracle when an instance exists).

- **Configured for PostgreSQL** (the verified target): run the live-PostgreSQL check. It is strongly
  recommended for every DB-behavior change, since it is the one backend with an existing suite
  (`PostgresIntegrationTests`) and the one whose divergences are already catalogued. This is a
  robustness recommendation, not a CI gate — CI deliberately runs the SQLite suite only, because
  mandating a specific engine in CI would privilege one backend over the replaceability the ORM
  abstraction exists to preserve.
- **Configured for any other backend** (`MySql`/`SqlServer`/`Oracle`): that backend needs its own
  equivalent live check before you rely on the change. Do not treat a green PostgreSQL run as
  transferable evidence — the divergences below are type/column-semantics issues, exactly the class
  the ORM does *not* abstract away.

The portability rule that governs application code is
"all DB access through SqlSugar, zero vendor SQL" (see Invariants). That rule keeps *query and
command* code portable. It does not make *type mapping, column semantics, or DDL* portable, and this
codebase has concrete counterexamples — which is why the verification above is per-backend rather
than per-codebase. This codebase has documented, specific divergences: a `DateTime` property with no
`[ColumnShape(TimestampWithTimeZone)]` maps to `timestamp without time zone` on PostgreSQL — the
reason `UserSession.CreatedAt`/`ExpiresAt` carry that shape explicitly — while SQLite has no real
column types to show the difference; and a SqlSugar `ConditionalType.Equal` filter that binds a text
value against a `uuid`/`bigint` column throws `42883` on PostgreSQL ("operator does not exist") but
passes silently on SQLite, whose loose typing accepts the comparison without complaint. Configure
`Testing:PostgresConnection` to a disposable database whose name contains `test` — the test resolves
it from the `STRUO_TEST_PG_CONNECTION` environment variable first, falling back to the
`Testing:PostgresConnection` key in `src/Struo.Api/appsettings.json`/`appsettings.Development.json`
if the env var is unset — or verify directly against a real PostgreSQL instance. The migration
subsystem's live tests (`CoreMigrationsLiveTests` applies the core migrations, runs `SchemaChecker` over
the result and compares it with what CodeFirst builds) also run on SQL Server, MySQL and MariaDB
(MariaDB on the `MySql` dialect). They
resolve `STRUO_TEST_SQLSERVER_CONNECTION` / `Testing:SqlServerConnection`,
`STRUO_TEST_MYSQL_CONNECTION` / `Testing:MySqlConnection` and `STRUO_TEST_MARIADB_CONNECTION` /
`Testing:MariaDbConnection`, apply the same `test`-in-the-name guard, and return early (a pass in
about a millisecond) when a connection is unset, so judge them by per-test duration. The database
must exist beforehand: SqlSugar's `CreateDatabase` cannot create a SQL Server database whose name
contains a hyphen. Beyond the migration tests and a SqlSugar smoke check, only PostgreSQL has a live suite.

**`PostgresIntegrationTests` disables Npgsql pooling, deliberately.** Reuse of a pooled physical
connection across a connection-close boundary made this suite go red locally with a
`WSA_OPERATION_ABORTED` socket abort; `PgTestConnectionString.DisablePooling` gives each test its own
physical connection. That is test isolation, not tolerance — no retry, no swallowed exception, no
relaxed assertion, and every test still runs real DDL and DML against a real PostgreSQL. Two things
worth knowing before you touch it:

- **If this suite turns red, check whether pooling was re-enabled before assuming you caused it.** The
  abort is not branch-specific and which test goes red is not stable. Setting `Pooling=true` in
  `STRUO_TEST_PG_CONNECTION` restores the reproduction on purpose — `DisablePooling` honours an explicit
  caller choice — and the expected casualty is `Resolved_connection_disables_pooling`, which says so.
- **The mechanism is still unknown, and production was never observed to hit it — but that is a
  non-observation, not a proof of safety.** Don't upgrade it to one.

`PgTestConnectionString`'s class doc points to `docs/ai/decisions/pg-test-connection-pooling.md` for the
full write-up.

**E2E** (`pnpm e2e` for the `core` Playwright project; `pnpm e2e:sample` needs the sample opted in) is a
further check for changes to user-facing flows — it needs a live API and a migrated database (run
`dotnet run --project src/Struo.Api -- migrate` once, or set `Database:MigrateOnStartup=true` in local
configuration; a local database built by v0.8.x CodeFirst needs `migrate:baseline` once instead), is
not one of the five standing gates, and is not run by CI.

## Prohibitions

- Never hand-author a package version. Install via the package manager itself (`dotnet add package`,
  `pnpm add <pkg>`) and let it write the version; NuGet versions are centralized in
  `Directory.Packages.props`. A hand-written NuGet version that doesn't exist on nuget.org fails
  `dotnet restore`; a hand-written `package.json` version that `pnpm` didn't resolve itself won't match
  `pnpm-lock.yaml`, and CI's `pnpm install --frozen-lockfile` refuses to proceed. Exception:
  `frontend/pnpm-workspace.yaml` and `docs/pnpm-workspace.yaml` hand-write bounded
  transitive-dependency `overrides:` ranges for advisories a package hasn't picked up yet — that is the
  one place versions are legitimately hand-authored; see `frontend/pnpm-workspace.yaml` for why every
  entry there carries an upper bound.
- Never add a business collection to `src/Struo.*` — new content belongs in a fork's own project (or,
  for learning/demo purposes only, the existing sample). Nothing mechanically stops this; the cost
  lands on every fork that pulls an upstream update, since it inherits business content mixed into what
  is supposed to be reusable core.
- Never reintroduce a sample reference into `Struo.Api` (no `ProjectReference` from
  `Struo.Api.csproj` into `samples/`, no default `Struo:ContentAssemblies` entry for it) — the first is
  caught by `TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` (Hard
  constraints), the second by `Shipped_appsettings_declares_no_content_assemblies`.
- Never commit `src/Struo.Api/appsettings.Development.json` — it is gitignored and holds local secrets.
- **Never add documentation the reader does not need in order to act.** `docs/ai/conventions.md`,
  "Only the current state" names the section that bans change narratives, misplaced evidence, and
  oversized comments. This cuts *both* ways: do **not** delete a load-bearing caveat (an
  unverified claim, a backend divergence, a security consequence, an honest "the mechanism is
  unknown") to make prose read cleaner — that is a correctness regression. The test is "would a
  reader act differently without this?", never "is this long?". `docs/ai/conventions.md`, "How
  documentation is written", has the full rule for both audiences — the manual and this
  reference set are written to different standards — and the anti-patterns.

## Where to read more

- `docs/guide/en/` — the twenty-three-chapter manual (zh-TW parallel at `docs/guide/zh-TW/`).
- `docs/ai/architecture.md`, `docs/ai/conventions.md`, `docs/ai/task-playbooks.md` — this reference set.
