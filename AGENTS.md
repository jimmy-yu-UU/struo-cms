# AGENTS.md

Guidance for any AI coding agent working in this repository. This file is meant to stand on its own —
read it first, every session. For depth, see `docs/ai/architecture.md` (layer map and extension
points), `docs/ai/conventions.md` (naming/errors/validation/config/tests/commits), and
`docs/ai/task-playbooks.md` (long-form playbooks); for conceptual background, the manual under
`docs/guide/en/`.

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
`src/Struo.*`.

`samples/Struo.Sample.Blog` (Article/Tag/Category/…) is an optional, detachable **demo** — it shows how
to define collections using the same primitives a fork would use. It is not shipped capability: the
host has no `ProjectReference` to it, `Struo:ContentAssemblies` ships as `[]`, and framework code never
references it. Only `tests/Struo.Tests` references it, which is what makes it truly deletable — doing
so also means deleting the many test files that use it as a fixture.

## Repo map

| Path | Contents |
|---|---|
| `src/Struo.Domain` | Domain types only — no project or package references at all. |
| `src/Struo.Application` | Application-layer abstractions, use-case contracts, options, query/security contracts. |
| `src/Struo.Infrastructure` | SqlSugar wiring, identity, files, health checks, all `AddStruoXxx` DI registration. |
| `src/Struo.Api` | The ASP.NET Core host: controllers, GraphQL, Scalar, Serilog, `Program.cs`. |
| `frontend/` | The Vue 3 + PrimeVue admin SPA, a separate pnpm workspace. |
| `samples/Struo.Sample.Blog` | Optional, detachable demo content project — not shipped capability. |
| `db/migrations/` | Core-only, reviewed SQL migration scripts (`001-core-baseline.sql` is the prod bootstrap). |
| `docs/` | The bilingual manual (`guide/en/`, `guide/zh-TW/`) and this `ai/` reference set. |
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

## Invariants

- **All database access is through SqlSugar; zero vendor SQL.** Migration scripts under
  `db/migrations/` are the one place raw SQL is written deliberately, and are PostgreSQL-only by design.
- **Outbound JSON is camelCase** everywhere (`JsonSerializerDefaults.Web`).
- **The unified response envelope** wraps every REST response: `{success, data, meta?}` or
  `{success:false, error:{code, message, details?}}` (`src/Struo.Api/Http/Envelope.cs`,
  `EnvelopeResultFilter.cs`).
- **Metadata is scanned once at startup and cached** in a singleton `IMetadataProvider` — never
  per-request reflection (`MetadataScanner.Scan`, called from `AddStruoMetadata`).
- **Query DSL paths are whitelist-validated** against scanned metadata before any SQL is built
  (`QueryValidator`) — an unknown filter/sort/relation path is rejected, never passed through.
- **RichText is sanitized server-side** before required-field validation (`ItemDeserializer` calling
  `IHtmlSanitizer`).
- **Immutable update patterns**: domain/query model types are `record`s with `init` properties, updated
  via non-destructive `with` expressions, not mutated in place.
- **`InitTables` is Development-only.** Production schema changes go through reviewed migrations under
  `db/migrations/` applied by `MigrationRunner` (PostgreSQL-only; a hard no-op on any other backend).
- **Hidden fields are never projected or accepted** — `[CmsField(Hidden = true)]` is excluded from
  schema, GraphQL, item projections, and query filtering/search/sort.

## Task playbooks (condensed — see `docs/ai/task-playbooks.md` for the full form)

1. **Add a collection** — new entity class (outside `src/Struo.*`) inheriting `AuditableEntity`, with
   `[CmsCollection]`/`[CmsField]`; wire its assembly into `Struo:ContentAssemblies` + a
   `ProjectReference` from `Struo.Api`; grant RBAC; write a production migration. Gate: `dotnet build &&
   dotnet test`.
2. **Add a field type** — swapping an editor for an existing `FieldInterface` is frontend-only
   (`frontend/src/lib/fieldTypes/registry.ts`). A genuinely new `FieldInterface` value touches the
   backend enum, `MetadataScanner`, possibly `SqlSugarClientFactory`'s column-widening hook, and the
   frontend registry + type union together. Gate: all four standing gates; live-PostgreSQL check if you
   touched column mapping.
3. **Add an endpoint** — new controller under `src/Struo.Api/Controllers/`, envelope-friendly return
   values, `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]` on any action that must not
   be anonymous, new domain exceptions mapped in `DomainErrorMap`. Gate: `dotnet build && dotnet test`.
4. **Add a migration** — next `NNN-short-kebab-description.sql` under `db/migrations/`, idempotent,
   forward-only, `timestamptz` for new temporal columns, never edit an already-applied filename. Gate:
   `dotnet build && dotnet test`, plus live-PostgreSQL verification (the runner is a SQLite no-op).
5. **Change the admin SPA** — only when metadata isn't enough (new field editor, theming, i18n, a
   bespoke view); the SPA never hardcodes a collection's fields/columns/labels. Gate: `pnpm test &&
   pnpm build`.

## Verification

The **four standing gates** — the same ones CI runs on every push/PR — are `dotnet build`,
`dotnet test`, `pnpm test`, `pnpm build` (the latter two from `frontend/`). Run whichever apply to your
change; run all four before anything touching both stacks.

**Live-PostgreSQL verification is required for any change to DB behavior** (a migration, a
`SqlSugarClientFactory` column-mapping change, a query-building change) — SQLite passing is not evidence
of PostgreSQL correctness. This codebase has a documented, specific divergence: `IsJson` without an
explicit `text` column type truncates at `varchar(1)` on PostgreSQL but appears to work on SQLite,
which ignores declared column length. Set `Testing:PostgresConnection` (via the
`STRUO_TEST_PG_CONNECTION` environment variable) to a disposable database whose name contains `test`,
or verify directly against a real PostgreSQL instance.

**E2E** (`pnpm e2e` for the `core` Playwright project; `pnpm e2e:sample` needs the sample opted in) is a
further check for changes to user-facing flows — it needs a live API and database, is not one of the
four standing gates, and is not run by CI.

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

## Where to read more

- `docs/guide/en/` — the sixteen-chapter manual (zh-TW parallel at `docs/guide/zh-TW/`).
- `docs/ai/architecture.md`, `docs/ai/conventions.md`, `docs/ai/task-playbooks.md` — this reference set.
