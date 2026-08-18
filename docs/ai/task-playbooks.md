# Task Playbooks (for AI agents)

Long-form, step-by-step versions of the five recipes summarized in `AGENTS.md`. Each names the exact
files to touch, the shape of the code, the tests to add, and the gate to run. Background reading for
each is the linked manual chapter — read it before making the change if anything here is unclear.

Gate vocabulary used below: the **five standing gates** are `dotnet build`, `dotnet test`, `pnpm test`
(from `frontend/`), `pnpm build` (from `frontend/`), and `pnpm build` (from `docs/`) — the same five
commands `.github/workflows/ci.yml` runs on every push/PR. The `docs/` build resolves every cross-chapter
link in the manual and fails on a dead one, then checks that every chapter actually rendered — `vitepress
build` alone exits 0 on a page whose body came out empty; it is relevant to a change only when that
change touches `docs/guide/**` or the docs project itself, not automatically to every playbook below.
**Live-database verification** is a separate, additional step for any change to
DB behavior, performed against whichever backend the deployment is configured for: on PostgreSQL (the
verified target) it is the strongly-recommended live-PG check described below; on `MySql`/`SqlServer`/
`Oracle` that backend needs its own equivalent check, since a green PostgreSQL run does not transfer.
It is a robustness practice rather than a CI gate — CI runs the SQLite suite only, so that no single
engine is privileged over DB replaceability. For the PostgreSQL
form — set `Testing:PostgresConnection` to a disposable database whose name contains
`test`. It resolves in this order: the `STRUO_TEST_PG_CONNECTION` environment variable first, else the
`Testing:PostgresConnection` key in `src/Struo.Api/appsettings.json`/`appsettings.Development.json`;
either route works. Or run the application against a real PostgreSQL instance directly. SQLite passing
is not evidence of PostgreSQL
correctness: this codebase has a documented, specific SQLite/PostgreSQL divergence — `IsJson` without
an explicit `text` column type truncates on PostgreSQL at `varchar(1)`, but "works" on SQLite because
SQLite ignores declared column length. **E2E** (`pnpm e2e` for the `core` Playwright project,
`pnpm e2e:sample` for the sample) is a further, separate check for changes that touch user-facing flows
end-to-end; it needs a live API and database reachable at the dev proxy target and is not part of the
standing gates or of CI.

## Playbook 1: Add a collection

Background: `docs/guide/en/04-defining-a-collection.md` (full checklist and rationale),
`docs/guide/en/06-internationalization.md` (translatable fields), `docs/guide/en/07-relations.md`
(relations), `docs/guide/en/13-revisions-and-soft-delete.md` (soft delete / revisions),
`docs/guide/en/12-auth-and-rbac.md` (RBAC grants).

Adding a collection is purely additive to a fork's own content project — it never touches
`src/Struo.*`.

1. **Create or reuse a content class library** outside `src/Struo.*` (the shipped
   `samples/Struo.Sample.Blog` is the worked example — do not add new collections there or in
   `src/Struo.*`; those are core/demo, not your fork's content). Reference `Struo.Domain` for the
   metadata attributes/enums, plus enough of SqlSugar (directly, or transitively via
   `Struo.Infrastructure`) for `[SugarTable]`/`[SugarColumn]`.
2. **Write the entity**: inherit `Struo.Domain.Auditing.AuditableEntity`; override `Id` with
   `[SugarColumn(IsPrimaryKey = true)]` (the base class declares `Id` `abstract` specifically so a
   forgotten override is a compile error, not a runtime surprise); add
   `[SugarTable("your_table_name")]` (lower-case, plural, snake_case) and
   `[CmsCollection("Your Label", Icon = "...", Group = "...", DefaultDisplayField = nameof(SomeField))]`.
3. **Add `[CmsField]`** to every property the API/admin form should expose (`Interface =
   FieldInterface.Xxx`, plus `Required`/`Searchable`/`Sortable`/`Sort`/`ReadOnly`/`Hidden`/
   `Translatable`/`MaxLength` as needed — see `docs/guide/en/05-field-types.md` for the full
   `FieldInterface` reference), and `[CmsOptions(...)]` on any `Select`/`Radio`/`MultiSelect`/
   `CheckboxGroup` field. `Hidden` is a read-side exclusion only (see `AGENTS.md`'s invariants) — pair
   it with `ReadOnly` if the field must also be unwritable.
4. **Opt into soft delete and/or revisions** if needed: implement `ISoftDeletable` on the class, and/or
   set `Revisions = true` on `[CmsCollection]`.
5. **Add relations** with `[CmsRelation]` + SqlSugar's `[Navigate]` if the collection references
   another one.
6. **Wire the content project in**: add a `ProjectReference` from `src/Struo.Api/Struo.Api.csproj` to
   your content project, and add its assembly name to `Struo:ContentAssemblies`. Put that setting in
   `src/Struo.Api/appsettings.Development.json` (gitignored, dev-only) or supply it via the
   `Struo__ContentAssemblies__0` environment variable — **not** in the shipped
   `src/Struo.Api/appsettings.json`. That file is the one
   `tests/Struo.Tests/Template/TemplateInvariantsTests.cs`'s
   `Shipped_appsettings_declares_no_content_assemblies` test asserts has an empty
   `Struo:ContentAssemblies`; editing it to wire in permanent content fails that test immediately, and
   is a collision with the invariant `AGENTS.md`'s "Hard constraints" documents as enforced. If a fork
   deliberately wants its content assembly wired into the shipped `appsettings.json` itself (e.g. it
   is replacing the template's "ships with zero collections" posture permanently), update or remove
   that test deliberately as part of the same change — don't leave it contradicting the new
   configuration.
7. **Restart the API.** CodeFirst creates the table automatically from the entity class — in **every**
   environment, not just Development. Because the table doesn't exist yet, this step is inherently
   non-destructive: it only ever creates, never alters or drops anything on an existing table (that's a
   separate, opt-in mechanism, `Database:AutoSyncSchema`, Development-only — see Playbook 4) — confirm
   the admin SPA's sidebar shows the new collection under its configured `Group`.
8. **Grant RBAC** read/write/delete permissions for the collection to whichever roles need them — a
   brand-new collection has zero grants, so only a super-admin can use it until you add some.
9. **Nothing further is needed for the table itself before deploying** — CodeFirst creates it in
   Production the same way it does everywhere else. A migration under `db/migrations/` is only needed
   later, if you change the shape of a table that already holds data you need to keep (see Playbook 4).
10. **Tests to add**: if the collection has any non-trivial behavior worth locking down (a relation, a
    computed default, an interaction with soft delete/revisions), add a test in your content project's
    own test suite, or, for framework-level behavior you are exercising rather than declaring, follow
    the pattern of `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` /
    `tests/Struo.Tests/Api/ItemsEndpointTests.cs` for how the shipped suite verifies scanned metadata
    and generic CRUD behavior. Do not add your business collection's tests under `tests/Struo.Tests` —
    that project is the framework's own test suite.
11. **Gate**: `dotnet build && dotnet test` (the standing gates' backend pair; this playbook does not
    touch `docs/guide/**`, so the docs gate does not apply). Add
    live-database verification before a production deploy, against whichever backend you are actually
    configured for — confirm CodeFirst creates the new table with the columns/indexes/constraints you
    expect; a green SQLite run does not guarantee the same result on PostgreSQL or another backend.

## Playbook 2: Add a field type

Background: `docs/guide/en/05-field-types.md` (the full `FieldInterface` reference and the three things
one enum value drives), `docs/guide/en/14-admin-spa-customization.md`, "Adding a custom field editor"
(the frontend-only variant of this playbook).

There are two distinct versions of "add a field type." Pick the one that matches what's actually needed:

**2a. Swap or add an editor for an EXISTING `FieldInterface` value** (e.g. give `Color` a real swatch
picker instead of a plain text input) — frontend-only, no backend change:

1. Write the new Vue component under `frontend/src/components/fields/` following the standard
   three-prop, one-event contract every field editor uses:
   ```ts
   defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
   defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
   ```
2. In `frontend/src/lib/fieldTypes/registry.ts`, point that interface's entry at the new component
   (keep its existing `defaultValue`/`parse`/`serialize`/`listColumn` unless the value shape itself is
   changing too).
3. **Tests to add**: a `*.test.ts` next to the new component if it has non-trivial logic, and update
   any existing test that asserts the old component was rendered for that interface.
4. **Gate**: `pnpm test && pnpm build` (the standing gates' frontend pair; this is frontend-only and
   doesn't touch `docs/guide/**`, so the docs gate does not apply).

**2b. Add a genuinely new `FieldInterface` value** — touches all three layers `docs/guide/en/
05-field-types.md` describes:

1. Add the new member to `src/Struo.Domain/Metadata/Enums/FieldInterface.cs` (a fixed declaration
   order; add yours at the end unless you have a specific reason to group it near related interfaces —
   reordering existing members is a breaking change for any stored numeric-enum data, so append rather
   than insert unless you are certain nothing persists the numeric value).
2. Teach `MetadataScanner.BuildField` (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) about
   any interface-specific validation the new value needs (e.g. requiring `[CmsOptions]`, restricting
   allowed CLR property types, `MaxLength` defaulting rules).
3. Add the new member to `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`'s `ScalarSdl` (and
   `WritableInputSdl` if the interface should be writable) switch expressions, choosing the GraphQL SDL
   type it maps to. **This step is easy to miss and fails loudly but unhelpfully if skipped**: both
   switches compile fine with the new member unhandled (there's a discard arm), but `ScalarSdl`'s
   discard arm throws at GraphQL schema-build time, which happens during host startup — so the failure
   surfaces as a misleading `ObjectDisposedException` on `IServiceProvider` (from
   `WebApplicationFactory` tearing down a host that never finished starting), not as an error naming
   `SchemaTypeMapper` or the new enum member at all.
4. If the value's CLR-side value is a structured aggregate (a `List<>`/`Dictionary<>`) that needs a
   JSON column, add it to `SqlSugarClientFactory`'s `JsonColumnInterfaces` set
   (`src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`) so SqlSugar (de)serializes it and
   the column gets `IsJson = true` + `text` together (either one alone is a documented pitfall —
   `IsJson` without an explicit `text` type defaults to `varchar(1)` and truncates on PostgreSQL, a
   failure that does not reproduce on SQLite). If instead it's a plain long string that needs widening,
   add it to `ContentBearingInterfaces` in the same file.
5. If the value needs write-time structural validation beyond `Required` (like `MultiSelect`/`Tags`/
   `KeyValue`/`Files`/`Repeater` already have), add an `IFieldValidator` implementation under
   `src/Struo.Application/Query/Write/Validators/` and register it in `FieldValidatorRegistry.Phases`
   (`src/Struo.Application/Query/Write/FieldValidatorRegistry.cs`) — note the phase order is observable
   (it determines exception precedence when a body violates several fields at once), so add a new phase
   rather than silently reordering existing ones unless changing precedence is the actual intent.
6. Mirror the new value in `frontend/src/lib/fieldTypes/types.ts` (`FieldInterface` union AND
   `ALL_FIELD_INTERFACES`) and add its entry to `frontend/src/lib/fieldTypes/registry.ts` (component +
   `defaultValue`/`parse`/`serialize`/`listColumn`), following Playbook 2a for the component itself.
7. The committed `schema/interfaces.json` snapshot is now stale — **always**, because it pins every
   declared enum member regardless of use, which is what makes step 6 enforced rather than
   honour-system. If the new interface is additionally used by (or an existing field's `Interface`/other
   `[CmsField]`/`[CmsCollection]` attribute is changed to use) one of the seven core `[CmsCollection]`
   types, `schema/core-collections.json` is stale too. One command regenerates both; commit the result
   (bash shown; see `schema/README.md` for the PowerShell form, which needs an explicit unset
   afterward):
   ```bash
   UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot
   ```
   Skipping this fails the `backend` CI job's `CoreSchemaSnapshotTests` with a diff against the stale
   snapshot (`schema/README.md` has the full contract).
8. **Tests to add**: a backend test alongside `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`
   covering the new interface's scan-time validation, a persistence-level test if you touched
   `SqlSugarClientFactory` (follow the pattern of existing column-widening tests in
   `tests/Struo.Tests/Persistence/`), and a frontend `*.test.ts` for the new registry entry.
9. **Gate**: four of the five standing gates (`dotnet build && dotnet test`, `pnpm test && pnpm build`)
   — this change spans both stacks. The docs gate does not apply unless this change also updated
   `docs/guide/en/05-field-types.md`'s `FieldInterface` reference, in which case add `pnpm build` from
   `docs/` too. **Verify against the backend you are configured for** if you touched
   `SqlSugarClientFactory`'s column mapping — column mapping is precisely where backends diverge, and
   the `IsJson`/`text` truncation failure mode above does not reproduce on SQLite at all. On
   PostgreSQL that means the live-PG check (strongly recommended here); on another backend, its own
   equivalent.

## Playbook 3: Add an endpoint

Background: `docs/guide/en/09-rest-api.md` (envelope, error codes, CSRF, status conventions),
`docs/guide/en/12-auth-and-rbac.md` (authentication schemes and permission checks).

1. **Add a controller** under `src/Struo.Api/Controllers/`, following the shipped pattern (e.g.
   `src/Struo.Api/Controllers/PingController.cs` for the minimal shape,
   `src/Struo.Api/Controllers/ItemsController.cs` for one with authentication and permission checks):
   ```csharp
   [ApiController]
   [Route("api/[controller]")]
   public sealed class YourController(/* constructor-injected dependencies */) : ControllerBase
   {
       [HttpGet]
       public async Task<IActionResult> Get(CancellationToken ct) => Ok(/* plain object or DTO */);
   }
   ```
   Return values via plain `Ok(...)`/`NotFound()`/`StatusCode(...)` for the common path —
   `EnvelopeResultFilter` wraps them into the standard envelope automatically. Only reach for
   `Struo.Api.Http.ApiResults.Fail(status, code, message)` when you need a specific error `code` the
   filter's default per-status message wouldn't produce.
2. **Authentication**: add `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]`
   (`src/Struo.Api/Auth/AuthSchemes.cs`) to any action that must not be reachable anonymously. Note
   the established asymmetry in this codebase: `ItemsController`'s read actions (`List`/`Query`/`Get`)
   carry **no** `[Authorize]` attribute at all — permission is enforced inside `ItemService` via
   `IPermissionService`/`ICurrentPermissions` regardless of authentication scheme — while its write
   actions (`Create`/`Update`/`Delete`/`Restore`/`Revert`) do carry
   `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]`. Bearer tokens do not authenticate
   `ItemsController` reads or `/graphql` at all in this codebase; decide deliberately, per new endpoint,
   whether it needs the same treatment or a plain `[Authorize]` (cookie scheme only).
3. **CSRF**: cookie-authenticated mutations need the caller to send the `X-Struo-CSRF` header
   (presence-only check, enforced by `CsrfProtectionMiddleware`, `src/Struo.Api/Auth/
   CsrfProtectionMiddleware.cs`) — this applies automatically to any action reached over the cookie
   scheme; bearer-authenticated requests are exempt (CSRF is a cookie-specific attack). Add no code for
   this — it is pipeline middleware — but document the header requirement for the endpoint's callers.
4. **Error mapping**: if the endpoint throws a new domain exception type that should surface a specific
   client-safe message/status, add it to `DomainErrorMap.Map` (and `StatusFor` if it needs a REST status
   other than the 500 fallback) in `src/Struo.Api/Http/DomainErrorMap.cs` — this is shared verbatim with
   GraphQL's error filter, so both protocols pick up the new mapping together.
5. **Tests to add**: an integration test under `tests/Struo.Tests/Api/` using the `WebApplicationFactory`
   -based fixture in `tests/Struo.Tests/Support/ApiFactory.cs` (see `EndpointSmokeTests.cs` for the
   minimal shape of standing up the factory and asserting a 200 + envelope shape, or
   `RbacEnforcementTests.cs` for asserting a permission-gated action rejects the right way).
6. **Gate**: `dotnet build && dotnet test`.

## Playbook 4: Add a migration

Background: `docs/guide/en/15-deployment-operations-testing.md`, "Schema management"; `db/migrations/
README.md`.

1. Determine the next number: the current highest `NNN-*.sql` filename in `db/migrations/` **+ 1**,
   zero-padded. The template ships **zero** scripts, so a fresh fork's first migration is `001-...`.
2. Create `db/migrations/NNN-short-kebab-description.sql` with a header comment (date, author, one-line
   intent). One logical change per file.
3. Write plain, portable SQL: standard types (`varchar(n)`, `integer`, `bigint`, `boolean`, `timestamp`,
   `numeric(p,s)`), no PostgreSQL-specific syntax (`jsonb`/`uuid`/`timestamptz`/`serial`, `DO $$ … $$`,
   `::` casts, `RETURNING`). **Idempotency is not required, and `IF NOT EXISTS` should be avoided** —
   the CodeFirst-created `schema_migrations` tracking table already guarantees each filename runs at
   most once, and `IF NOT EXISTS` is not supported on SQL Server. Forward-only — no automatic
   down-migration; a rollback is a new compensating script, not an edit to this one. See
   `db/migrations/README.md` §5 for the full prefer/avoid tables.
4. Use a time-zone-aware type (not bare `timestamp`) for any new column or table storing an instant,
   and store UTC — this is the convention, not a claim about any single file: there is no baseline file
   to check anymore. If the column is also modeled as an entity property, mark it
   `[ColumnShape(ColumnShape.TimestampWithTimeZone)]`
   (`src/Struo.Infrastructure/Persistence/ColumnShape.cs`) rather than a PostgreSQL-only `timestamptz`
   literal, so CodeFirst resolves the matching type per backend and a freshly created table agrees with
   what this migration adds to an existing one. Hand-writing the DDL directly instead (a column no
   entity property backs)?
   `ColumnTypeMap.cs` centralizes the per-backend literal to copy in — see `db/migrations/README.md` §5
   for the full mapping. Either way, check the target table's actual current column type (the entity
   declaration in `src/`, or the live schema) rather than assuming one. Do not retroactively convert an
   existing bare-`timestamp` column while you're at it unless that specific column is the subject of
   this migration (re-anchoring already-stored values against a session time zone is a silent data
   shift).
5. **Never edit a filename that may already be recorded as applied anywhere** — `MigrationRunner`
   tracks applied migrations by filename only, in the CodeFirst-created `schema_migrations` table, with
   no checksum, so an edited file with an already-applied filename is silently never re-run. Ship a new
   file instead.
6. **Tests to add**: this is SQL, not C# — there is no unit test for a migration script itself. Verify
   it directly (see the live-database step below). If the migration backs a new collection, that
   collection's own tests (Playbook 1) are the regression coverage.
7. **Gate**: `dotnet build && dotnet test` first — `MigrationRunner` now runs on every backend,
   including the SQLite test suite, so this exercises the runner itself (though not your specific SQL,
   which SQLite may accept or reject differently than your actual target backend). **The migration
   itself can only be verified by applying it**: point `Database:MigrationsPath` at `db/migrations/` (an
   absolute path) against a disposable instance of the backend you are actually configured for, and
   confirm the script applies cleanly and is recorded in `schema_migrations`. PostgreSQL is this
   repository's only verified live target; on any other backend, that backend needs its own equivalent
   live check — a green SQLite (or PostgreSQL) run does not transfer.

## Playbook 5: Change the admin SPA

Background: `docs/guide/en/14-admin-spa-customization.md` (the full "when to customize vs. when
metadata is enough" decision and the directory map).

Before touching `frontend/src` at all: confirm the requirement is not already satisfiable by declaring
metadata (Playbooks 1/2). Adding a `[CmsCollection]`/`[CmsField]` alone produces a full sidebar entry,
schema-driven list, and generated create/edit form with zero Vue code — the admin SPA never hardcodes a
collection's fields, columns, or labels; everything comes from `GET /api/schema`. Reach into
`frontend/src` only for: a field editor experience none of the shipped interfaces provide (Playbook
2a), branding/theming beyond a name and logo, admin-UI language (i18n), or a workflow that doesn't fit
the generic list/form pattern at all (a dashboard widget, a bespoke wizard).

1. **Locate the right directory** using the map in chapter 14: `api/` (thin REST wrappers), `components/`
   (`ItemForm.vue`, `fields/`, `common/`, `shell/`, `media/`, `revisions/`, `rbac/`),
   `composables/`, `i18n/` + `locales/`, `layouts/`, `lib/` (framework-free helpers, including
   `fieldTypes/`), `router/`, `stores/` (Pinia), `theme/`, `types/`, `views/` (one component per route).
2. **Theming**: edit `frontend/src/assets/tokens.css` for the shadcn semantic custom properties
   (`--background`/`--primary`/`--radius`/…, on the `.app-dark` toggle class) that every vendored `ui/`
   component and Tailwind utility reads, and `frontend/src/assets/theme.css` for the unlayered global
   layer (page/surface/foreground and status/shadow/overlay/font custom properties, `html`/`body`
   resets, the theme-transition rule, `.app-breadcrumb`) first-party scoped CSS reads.
   **`frontend/src/components/ui/` is vendored, read-only output — never edit it and never `:deep()`
   into it**; a re-theme changes a token layer or a wrapper component outside `ui/`. See chapter 14's
   "Restyling a vendored `ui/` component" for how to override a component's style without losing a
   specificity fight. Avoid `!important`.
3. **i18n**: add the same key to both `frontend/src/locales/en.ts` and `frontend/src/locales/zh-TW.ts`
   under the right namespace (`common`, `nav`, `dashboard`, `collectionList`, `itemForm`, `media`,
   `revisions`, `rbac`, `settings`, `fields`, ...) — `en` is the fallback locale, so a key missing only
   from `zh-TW` degrades gracefully but a key missing from `en` does not.
4. **A new view/component**: follow the existing `views/` pattern (one component per route, registered
   in `frontend/src/router/index.ts`, gated by `frontend/src/router/guard.ts` if it needs
   authentication/permission checks).
5. **Tests to add**: a `*.test.ts` next to any new `lib/` helper or non-trivial component logic
   (Vitest). If the change affects a user-facing flow end-to-end, add or update a Playwright spec under
   `frontend/e2e/` (`core` project — never `e2e/sample/**` unless the change is specific to the Blog
   sample). A new field-type component also needs its own `frontend/src/lib/fieldTypes/registry.ts`
   entry (see Playbook 2a) — `frontend/tests/schemaContract.test.ts` enforces that every interface a
   core collection actually uses has one, so a component with no registry entry fails that test rather
   than silently rendering read-only.
6. **Gate**: `pnpm test && pnpm build` (the standing gates' frontend pair; `pnpm build` runs
   `vue-tsc -b`, which is CI's only enforcement of the SPA's TypeScript types) — this playbook's own
   steps change `frontend/src`, not `docs/guide/**`, so the docs gate does not apply; if a change under
   this playbook also edits a manual chapter (e.g. chapter 14 itself), add `pnpm build` from `docs/` too.
   Run `pnpm e2e` as a further check for any change touching a critical flow — it needs a live API and
   database and is not part of the standing gates or of CI.

## Next steps

- `AGENTS.md` for the condensed version of these five playbooks and the invariants they must respect.
- `docs/ai/architecture.md` for the extension points these playbooks put to use.
- `docs/ai/conventions.md` for the naming/error-handling/validation/test conventions each playbook
  should follow.
