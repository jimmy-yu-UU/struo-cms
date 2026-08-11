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

**Core** = `src/Struo.*` plus the ten `FrameworkEntityTypes.All` types
(`src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs`): `Language`, `File`, `FileTranslation`,
`MediaFolder`, `User`, `Role`, `Permission`, `UserRole`, `Revision`, `SiteSettings`. Of these, seven
carry `[CmsCollection]` (are themselves collections): `Language`, `File`, `MediaFolder`, `User`,
`Role`, `Permission`, `UserRole` — `FileTranslation`, `Revision`, and `SiteSettings` are framework
tables but not collections. If you're unsure whether something is core, it's core only if it lives in
`src/Struo.*` — with a few named exceptions that are core despite living elsewhere, `frontend/` among
them. `db/migrations/` is **not** one of them: only its `README.md` — documenting the mechanism — is
core; the template ships no scripts, and any script a fork adds there is that fork's own content, not
core capability. `schema/` is core too, despite living outside that path: it holds the
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
| `src/Struo.Infrastructure` | SqlSugar wiring, identity, files, health checks, all `AddStruoXxx` DI registration. |
| `src/Struo.Api` | The ASP.NET Core host: controllers, GraphQL, Scalar, Serilog, `Program.cs`. |
| `frontend/` | The Vue 3 admin SPA, a separate pnpm workspace — mid-migration off PrimeVue onto Tailwind v4 + shadcn-vue, so both UI stacks currently coexist. |
| `samples/Struo.Sample.Blog` | Optional, detachable demo content project — not shipped capability. |
| `db/migrations/` | Reviewed, forward-only ALTER scripts for evolving an already-created schema; the template ships none — anything here belongs to the fork that put it there. |
| `schema/` | Committed snapshots the schema contract gate checks both stacks against: core-collection wire shape (`core-collections.json`) and the declared interface enums (`interfaces.json`) — `schema/README.md`. |
| `docs/` | The bilingual manual (`guide/en/`, `guide/zh-TW/`), this `ai/` reference set, and the VitePress site that renders the manual (`package.json`, `.vitepress/` — its own npm project, separate from `frontend/`). |
| `tests/Struo.Tests` | The backend xUnit suite (unit, integration, and the template-invariant guard). |

## Hard constraints

- **Dependency direction** (structural, visible in each project's `.csproj`): `Struo.Domain` → nothing;
  `Struo.Application` → `Struo.Domain`; `Struo.Infrastructure` → `Struo.Application` + `Struo.Domain`;
  `Struo.Api` → `Struo.Application` + `Struo.Infrastructure`. Framework code never references
  `samples/*` — this one edge **is** test-enforced:
  `tests/Struo.Tests/Template/TemplateInvariantsTests.cs`'s
  `Host_project_has_no_project_reference_into_samples` fails if `Struo.Api.csproj` ever gains a
  `ProjectReference` into `samples/`.
- `Struo.Domain` stays free of external packages — `Struo.Domain.csproj` declares zero
  `PackageReference`/`ProjectReference` entries; this is a convention checked by reading the file, not
  by an automated test.
- **Target framework**: .NET 10 (`net10.0`, `Directory.Build.props`). **Database support**: SqlSugar
  is configured for five backends (`Database:DbType`: `PostgreSQL`/`MySql`/`SqlServer`/`Sqlite`/
  `Oracle`), but only **PostgreSQL is the verified runtime target**; `Sqlite` is used for the test suite
  only; `MySql`/`SqlServer`/`Oracle` are type-mapped in code but unverified/experimental. Schema
  creation (CodeFirst) is designed and mapped to run on all five backends, via a dialect-neutral
  `[ColumnShape]`/`ColumnTypeMap` layer that confines vendor type literals to a single mapping file — but
  that mapping itself is unverified against a live MySQL/SqlServer/Oracle instance, and so is the query
  layer: some ORDER-BY and literal-coercion code paths are written against PostgreSQL/SQLite behavior
  specifically. The per-backend type decisions behind that layer, and the evidence for each, are recorded
  in `ColumnTypeMap`'s class doc (`src/Struo.Infrastructure/Persistence/ColumnTypeMap.cs`).

## Invariants

- **All database access is through SqlSugar; zero vendor SQL.** Migration scripts under
  `db/migrations/` are the one place raw SQL is written deliberately — and the template ships **none**:
  it holds only its `README.md`, and any script there belongs to the fork that put it there. Replaceability is
  a choice made once, at fork time, not a property every deployment must preserve forever: the core
  contains no vendor SQL; a fork writing PostgreSQL-specific ALTERs for the database it actually runs
  is not a violation.
- **Outbound JSON is camelCase** everywhere (`JsonSerializerDefaults.Web`).
- **The unified response envelope** wraps every REST response: `{success, data, meta?}` or
  `{success:false, error:{code, message, details?}}` (`src/Struo.Api/Http/Envelope.cs`,
  `EnvelopeResultFilter.cs`).
- **Metadata is scanned once at startup and cached** in a singleton `IMetadataProvider` — never
  per-request reflection (`MetadataScanner.Scan`, called from `AddStruoMetadata`).
- **Query DSL paths are whitelist-validated** against scanned metadata before any SQL is built
  (`QueryValidator`) — an unknown filter/sort/relation path is rejected, never passed through.
- **RichText is sanitized server-side** before required-field validation, via `RichTextCleaner`
  (`src/Struo.Application/Query/Write/RichTextCleaner.cs`), which wraps `IHtmlSanitizer` plus
  blank-document coercion. Non-translatable RichText fields are sanitized in `ItemDeserializer.cs`;
  translatable ones are sanitized separately, per locale, in `ItemWriteSideSync.SyncTranslationsAsync`
  (`ItemWriteSideSync.cs`).
- **Immutable update patterns**: domain/query model types are `record`s with `init` properties, updated
  via non-destructive `with` expressions, not mutated in place.
- **CodeFirst creates; migrations evolve.** Tables that do not yet exist are created by
  `DatabaseInitializer.CreateMissingTables` in **every environment and on every backend**, so an empty
  database bootstraps itself and `DataSeeder` seeds what was just created. **Existing** tables are never
  touched automatically: full CodeFirst structural sync is opt-in via `Database:AutoSyncSchema` and
  honoured in Development only (SqlSugar's default `InitTables` modifies *and* **drops** columns —
  measured on real PostgreSQL by
  `PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres`; on SQLite the
  same unfiltered call leaves the removed column in place
  (`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite`) — not because
  SQLite or SqlSugar's SQLite dialect lacks the capability, but because this repo's
  `SqlSugarClientFactory` never sets `ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn`,
  the flag that gates it (manual ch.15 row 7 has the detail, including what flipping that flag actually
  does) — so this repo's SQLite-only CI suite cannot demonstrate the claim by itself), while reviewed
  `db/migrations/` scripts applied by `MigrationRunner` are the all-environments path. The runner works
  on any backend; `Database:MigrationsPath` empty (the default) disables it.
- **Hidden fields are never projected on read** — `[CmsField(Hidden = true)]` is excluded from schema,
  GraphQL, item projections, and query filtering/search/sort. This is a **read-side exclusion only**
  on REST: the REST write path does not filter on `Hidden` at all (`ItemDeserializer.cs`/
  `ItemService.UpdateCoreAsync` strip only `IsSystem`/`ReadOnly` fields) — a client that already knows a
  hidden field's name can still set it via a normal REST create/update. GraphQL is stricter here:
  `CollectionSchemaBuilder` (`src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`) excludes
  `Hidden`/`ReadOnly`/`IsSystem` fields from every create/update input type, so a hidden field never
  appears as a GraphQL mutation argument in the first place. Do not rely on `Hidden` alone as a write
  guard for a privileged column; pair it with `ReadOnly` (or keep the field off the write path some
  other way) if it must never be client-writable.

## Task playbooks (condensed — see `docs/ai/task-playbooks.md` for the full form)

1. **Add a collection** — new entity class (outside `src/Struo.*`) inheriting `AuditableEntity`, with
   `[CmsCollection]`/`[CmsField]`; wire its assembly into `Struo:ContentAssemblies` + a
   `ProjectReference` from `Struo.Api`; grant RBAC. No migration needed to introduce the table —
   `DatabaseInitializer.CreateMissingTables` creates it automatically on next startup, in every
   environment and on every backend; a migration is only for altering a table that already exists
   (Playbook 4). Gate: `dotnet build && dotnet test`.
2. **Add a field type** — swapping an editor for an existing `FieldInterface` is frontend-only
   (`frontend/src/lib/fieldTypes/registry.ts`). A genuinely new `FieldInterface` value touches the
   backend enum, `MetadataScanner`, `src/Struo.Api/GraphQl/SchemaTypeMapper.cs` (an unmapped member
   fails GraphQL schema build, which surfaces as a misleading `ObjectDisposedException` on
   `IServiceProvider` during host startup rather than a clear error — see `docs/ai/task-playbooks.md`
   Playbook 2b), possibly `SqlSugarClientFactory`'s column-widening hook, and — both required —
   `frontend/src/lib/fieldTypes/types.ts`'s type union/`ALL_FIELD_INTERFACES` and its `registry.ts`
   component. Then regenerate `schema/interfaces.json` — required for *every* new member, whether or not
   a collection uses it yet — and `schema/core-collections.json` too if the value is used by a core
   collection (`schema/README.md`; one command does both). A new `RelationInterface` member likewise
   needs an entry in `frontend/src/lib/relationInputKind.ts`, or the relation silently renders
   read-only. Gate: four of the five standing gates — see `docs/ai/task-playbooks.md` Playbook 2b for
   when the fifth applies; live-PostgreSQL check if you touched column mapping.
3. **Add an endpoint** — new controller under `src/Struo.Api/Controllers/`, envelope-friendly return
   values, `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]` on any action that must not
   be anonymous, new domain exceptions mapped in `DomainErrorMap`. Gate: `dotnet build && dotnet test`.
4. **Add a migration** — next `NNN-short-kebab-description.sql` under `db/migrations/`; the template
   ships **zero** scripts, so a fresh fork's first is `001-...`, and anything already there belongs to
   that fork. Forward-only, plain portable SQL (no PostgreSQL-only syntax); idempotency is not required —
   `MigrationRunner` tracks applied filenames in `schema_migrations`, so each file runs at most once;
   never edit an already-applied filename. The runner applies pending scripts on **any** configured
   backend (no PostgreSQL gate) and is off by default — `Database:MigrationsPath` empty disables it
   entirely. Creating tables isn't its job: `DatabaseInitializer.CreateMissingTables` does that, in
   every environment and on every backend, so scripts here are ALTER-only by convention. See
   `db/migrations/README.md` for the full portability guidance and known limits. Gate: `dotnet build &&
   dotnet test` (exercises the runner, including on SQLite), plus live verification on the configured
   backend — PostgreSQL is this repo's only verified live target; any other backend needs its own
   equivalent check.
5. **Change the admin SPA** — only when metadata isn't enough (new field editor, theming, i18n, a
   bespoke view); the SPA never hardcodes a collection's fields/columns/labels. Gate: `pnpm test &&
   pnpm build`.

## Verification

The **five standing gates** — the same ones CI runs on every push/PR — are `dotnet build`,
`dotnet test`, `pnpm test` and `pnpm build` (the latter two from `frontend/`), and `pnpm build` from
`docs/`, which resolves every cross-chapter link in the manual, fails on a dead one, and then checks that
every chapter actually rendered — `vitepress build` alone exits 0 on a page whose body came out empty.
Run whichever apply to your change; run all five before anything touching more than one of the three.

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
backend, and the SQLite suite is a development convenience, not the portability guarantee.

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

The portability rule that governs application code is "all DB access through SqlSugar, zero vendor
SQL" (see Invariants). That rule keeps *query and command* code portable. It does not make
*type mapping, column semantics, or DDL* portable, and this codebase has concrete counterexamples —
which is why the verification above is per-backend rather than per-codebase. This codebase has a
documented, specific divergence: `IsJson` without an
explicit `text` column type truncates at `varchar(1)` on PostgreSQL but appears to work on SQLite,
which ignores declared column length. Configure `Testing:PostgresConnection` to a disposable database
whose name contains `test` — the test resolves it from the `STRUO_TEST_PG_CONNECTION` environment
variable first, falling back to the `Testing:PostgresConnection` key in
`src/Struo.Api/appsettings.json`/`appsettings.Development.json` if the env var is unset — or verify
directly against a real PostgreSQL instance.

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

`PgTestConnectionString`'s class doc is the full write-up: the exception chain, what the diagnosis ruled
out, the 2026-08-05 measurements bounding production exposure, the second-pool amplifier, and the one
residual unknown.

**E2E** (`pnpm e2e` for the `core` Playwright project; `pnpm e2e:sample` needs the sample opted in) is a
further check for changes to user-facing flows — it needs a live API and database, is not one of the
five standing gates, and is not run by CI.

## Prohibitions

- Never hand-author a package version. Install via the package manager itself (`dotnet add package`,
  `pnpm add <pkg>`) and let it write the version; NuGet versions are centralized in
  `Directory.Packages.props`.
- Never add a business collection to `src/Struo.*` — new content belongs in a fork's own project (or,
  for learning/demo purposes only, the existing sample).
- Never reintroduce a sample reference into `Struo.Api` (no `ProjectReference` from
  `Struo.Api.csproj` into `samples/`, no default `Struo:ContentAssemblies` entry for it).
- Never commit `src/Struo.Api/appsettings.Development.json` — it is gitignored and holds local secrets.
- Never weaken the dependency rule (no reversed or skip-layer project references).
- **Never add documentation the reader does not need in order to act.** No change narratives ("this used
  to be X"), no investigation journals (they belong in the doc comment of the class they explain), no
  restating a rule that already has a home elsewhere — link to it instead, and no preference notes (a
  comment or test that only justifies something never built protects no code). This cuts *both* ways: do
  **not** delete a load-bearing caveat (an unverified claim, a backend divergence, a security
  consequence, an honest "the mechanism is unknown") to make prose read cleaner — that is a correctness
  regression. The test is "would a reader act differently without this?", never "is this long?".
  `docs/ai/conventions.md`, "What documentation may contain", has the full rule and the five
  anti-patterns.

## Where to read more

- `docs/guide/en/` — the sixteen-chapter manual (zh-TW parallel at `docs/guide/zh-TW/`).
- `docs/ai/architecture.md`, `docs/ai/conventions.md`, `docs/ai/task-playbooks.md` — this reference set.
