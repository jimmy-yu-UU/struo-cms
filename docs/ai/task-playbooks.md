# Task Playbooks (for AI agents)

Long-form, step-by-step versions of the five recipes summarized in `AGENTS.md`, and a sixth playbook
for the manual's transcripts. Each names the exact
files to touch, the shape of the code, the tests to add, and the gate to run. Background reading for
each is the linked manual chapter — read it before making the change if anything here is unclear.

Gate vocabulary used below: the **five standing gates** are `dotnet build`, `dotnet test`, `pnpm test`
(from `frontend/`), `pnpm build` (from `frontend/`), and `pnpm build` (from `docs/`) — the same five
commands `.github/workflows/ci.yml` runs on every push/PR. The `docs/` build is the manual's gate in
three parts; `AGENTS.md`, "Verification", has what each part checks. The docs build is relevant to a
change only when that change touches `docs/guide/**` or the docs project itself, not automatically to
every playbook below.

**Live-database verification** is a separate, additional step for any change to DB behavior —
`AGENTS.md`, "Verification", has the per-backend rule, the verified backends, the documented
divergences, and how to configure each backend's test connection. SQLite passing is not evidence of
correctness on another backend.

## Playbook 1: Add a collection

Background: `docs/guide/en/05-collections.md` (full checklist and rationale),
`docs/guide/en/07-i18n.md` (translatable fields), `docs/guide/en/08-relations.md`
(relations), `docs/guide/en/09-revisions-and-trash.md` (soft delete / revisions),
`docs/guide/en/17-roles-and-permissions.md` (RBAC grants).

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
   `Translatable`/`MaxLength` as needed — see `docs/guide/en/06-field-types.md`, "Interface overview",
   for the full `FieldInterface` reference), and `[CmsOptions(...)]` on any `Select`/`Radio`/
   `MultiSelect`/`CheckboxGroup`/`Tags` field. `Hidden` is a read-side exclusion only (see `AGENTS.md`'s
   "Invariants") — pair it with `ReadOnly` if the field must also be unwritable.
4. **Opt into soft delete and/or revisions** if needed: implement `ISoftDeletable` on the class, and/or
   set `Revisions = true` on `[CmsCollection]`.
5. **Add relations** with `[CmsRelation]` + SqlSugar's `[Navigate]` if the collection references
   another one.
6. **Wire the content project in**: add a `ProjectReference` from `src/Struo.Api/Struo.Api.csproj` to
   your content project, and add its assembly name to `Struo:ContentAssemblies`. Put that setting in
   `src/Struo.Api/appsettings.Development.json` (gitignored, dev-only) or supply it via the
   `Struo__ContentAssemblies__0` environment variable — **not** in the shipped
   `src/Struo.Api/appsettings.json`. That file is the one
   `tests/Struo.Tests.Template/TemplateInvariantsTests.cs`'s
   `Shipped_appsettings_declares_no_content_assemblies` test asserts has an empty
   `Struo:ContentAssemblies`; editing it to wire in permanent content fails that test immediately, and
   contradicts the shipped posture `AGENTS.md`'s "Core vs. sample boundary" records
   (`Struo:ContentAssemblies` ships as `[]`). If a fork deliberately wants its content assembly wired
   into the shipped `appsettings.json` itself (e.g. it is replacing the template's "ships with zero
   collections" posture permanently), update or remove that test deliberately as part of the same
   change — don't leave it contradicting the new configuration.
7. **Create the table with a migration.** In your content project run
   `dotnet run --project src/Struo.Api -- make:migration <Name> --entity <Type> --output <dir>
   [--namespace <Ns>]`, where `<dir>` is the migrations directory your project uses. The command reads the entity's metadata,
   writes a `Create.Table` migration and never overwrites a file. Review the generated file (column
   types, indexes, the translation sidecar if the entity has translatable fields), then apply it with
   `dotnet run --project src/Struo.Api -- migrate`. Start the API and confirm the admin SPA's sidebar
   shows the new collection under its configured `Group`.
8. **Grant RBAC** read/write/delete permissions for the collection to whichever roles need them — a
   brand-new collection has zero grants, so only a super-admin can use it until you add some.
9. **Deploy the migration with the code.** Production does not create tables itself: run `migrate` as a
   deployment step, or set `Database:MigrateOnStartup=true` where you want the host to apply it. With
   neither, the host refuses to start while the migration is pending. Later changes to the table's
   shape are further migrations (Playbook 4).
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
    configured for — run `migrate` and `migrate:check` and confirm the new table has the
    columns/indexes/constraints you expect; a green SQLite run does not guarantee the same result on
    PostgreSQL or another backend.

## Playbook 2: Add a field type

Background: `docs/guide/en/06-field-types.md` (the full `FieldInterface` reference, the three things
one enum value drives, and its "Adding a custom field editor" section — the frontend-only variant
of this playbook).

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

**2b. Add a genuinely new `FieldInterface` value** — touches all three layers
`docs/guide/en/06-field-types.md` describes:

1. Add the new member to `src/Struo.Domain/Metadata/Enums/FieldInterface.cs` (a fixed declaration
   order; add yours at the end unless you have a specific reason to group it near related interfaces —
   reordering existing members is a breaking change for any stored numeric-enum data, so append rather
   than insert unless you are certain nothing persists the numeric value).
2. Teach `MetadataScanner.BuildField` (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) about
   any interface-specific validation the new value needs (e.g. requiring `[CmsOptions]`, restricting
   allowed CLR property types). The default `MaxLength` needs no rule of its own: a string field
   takes its column's width from `StringColumnWidth.Of`
   (`src/Struo.Infrastructure/Metadata/StringColumnWidth.cs`), which `SqlSugarClientFactory` also
   builds the column from.
3. Add the new member to `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`'s `ScalarSdl` (and
   `WritableInputSdl` if the interface should be writable) switch expressions, choosing the GraphQL SDL
   type it maps to. **This step is easy to miss and fails loudly but unhelpfully if skipped**: both
   switches compile fine with the new member unhandled (there's a discard arm), but `ScalarSdl`'s
   discard arm throws at GraphQL schema-build time, which happens during host startup — so the failure
   surfaces as a misleading `ObjectDisposedException` on `IServiceProvider` (from
   `WebApplicationFactory` tearing down a host that never finished starting), not as an error naming
   `SchemaTypeMapper` or the new enum member at all. Name collisions between collections are a
   different case: `GraphQlNameValidator` checks them at startup and the failure's cause chain
   carries a `MetadataException` naming the collections and the names, though under
   `WebApplicationFactory` the same `ObjectDisposedException` masking applies. Its fixed-name lists are
   kept in step with the built schema by `GraphQlNameCollisionTests`; a new shared GraphQL type or
   root field goes into those lists.
4. If the value's CLR-side value is a structured aggregate (a `List<>`/`Dictionary<>`) that needs a
   JSON column, add it to `FieldInterfaceSets.JsonColumnInterfaces`
   (`src/Struo.Infrastructure/Metadata/FieldInterfaceSets.cs`). That one set is what
   `SqlSugarClientFactory` reads so SqlSugar (de)serializes it and the column gets `IsJson = true` + a
   widened `text`-shaped type together, and what `MetadataScanner` reads to refuse a translatable
   field of that interface. The `EntityService` hook also maps a `[CmsField]` property of a structured type
   (`StructuredType`) as a JSON column, which the scanner allows only on `JsonColumnInterfaces` and
   `Hidden`, and widens a *bare* `[SugarColumn(IsJson = true)]` on its own, so
   neither needs a hand-added `ColumnDataType`; the scanner rejects a `ColumnDataType` on a JSON
   column (`ColumnDeclarationCheck`). If instead it's a plain long
   string that needs widening, add it to `FieldInterfaceSets.ContentBearingInterfaces` in the same file.
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
   types, `schema/core-collections.json` is stale too, and so is `schema/core-graphql.graphql` when the
   GraphQL type of a core field changes. One command regenerates all of them; commit the result
   (bash shown; see `schema/README.md` for the PowerShell form, which needs an explicit unset
   afterward):
   ```bash
   UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot
   ```
   Skipping this fails the `backend` CI job's `CoreSchemaSnapshotTests` with a diff against the stale
   snapshot (`schema/README.md` has the full contract).
8. **Tests to add**: the new member's row in `tests/Struo.Tests/Metadata/FieldInterfaceSetsTests.cs`
   (it fails until the row exists: membership in each set and the two GraphQL SDL results), a backend
   test alongside `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` covering the new interface's
   scan-time validation, a persistence-level test if you touched
   `SqlSugarClientFactory` (follow the pattern of existing column-widening tests in
   `tests/Struo.Tests/Persistence/`), and a frontend `*.test.ts` for the new registry entry.
9. **Gate**: four of the five standing gates (`dotnet build && dotnet test`, `pnpm test && pnpm build`)
   — this change spans both stacks. The docs gate does not apply unless this change also updated
   `docs/guide/en/06-field-types.md`'s "Interface overview" `FieldInterface` reference, in which case
   add `pnpm build` from `docs/` too. **Verify against the backend you are configured for** if you
   touched `SqlSugarClientFactory`'s column mapping — column mapping is precisely where backends
   diverge: a missing `[ColumnShape]` yields `timestamp without time zone` on PostgreSQL, and a
   text-bound `ConditionalType.Equal` against a `uuid`/`bigint` column throws `42883` there; neither
   reproduces on SQLite at all. That means a `STRUO_TEST_BACKEND` run on each backend you verify
   against (strongly recommended here).

**2c. Add a new `RelationInterface` value** — the same shape as 2b on a smaller surface: add the
member to `src/Struo.Domain/Metadata/Enums/RelationInterface.cs`, give it an entry in
`frontend/src/lib/relationInputKind.ts`'s map (absent members fall through to `'readonly'`, so the
relation renders read-only with no error), regenerate `schema/interfaces.json` with the same command as
step 7 above, and run `dotnet build && dotnet test` plus `pnpm test && pnpm build`.

## Playbook 3: Add an endpoint

Background: `docs/guide/en/12-rest-conventions.md` (envelope, error codes, CSRF, status
conventions), `docs/guide/en/16-authentication.md` (authentication schemes), `docs/guide/en/17-roles-and-permissions.md` (permission checks).

1. **Add a controller** under `src/Struo.Api/Controllers/`, following the shipped pattern (e.g.
   `src/Struo.Api/Controllers/PingController.cs` for the minimal shape,
   `src/Struo.Api/Controllers/ItemsController.cs` for one with authentication and permission checks):
   ```csharp
   [ApiController]
   [Route("api/[controller]")]
   public sealed class YourController(/* constructor-injected dependencies */) : ControllerBase
   {
       [HttpGet]
       public IActionResult Get() => Ok(/* plain object or DTO */);
   }
   ```
   Make the action `async Task<IActionResult>` and take a `CancellationToken` only once the body
   actually awaits something — this repository builds with `TreatWarningsAsErrors`
   (`Directory.Build.props`), so an `async` method with no `await` is CS1998 and fails `dotnet build`.
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
   `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]`. A bearer-only caller is still
   authenticated on these `[Authorize]`-free actions and on `/graphql`: `AuthSchemes.Adaptive` is
   registered as the DEFAULT scheme (`AuthWiring.cs`), and its `ForwardDefaultSelector` forwards to
   `AuthSchemes.Bearer` whenever the request carries an `Authorization: Bearer …` header
   (`AuthSchemes.HasBearerHeader`), so `ICurrentPermissions` sees the bearer identity even with no
   `[Authorize]` attribute in play. Decide deliberately, per new endpoint, whether it needs the same
   treatment or a plain `[Authorize]` (cookie scheme only).
3. **CSRF**: cookie-authenticated mutations need the caller to send the `X-Struo-CSRF` header
   (presence-only check, enforced by `CsrfProtectionMiddleware`,
   `src/Struo.Api/Auth/CsrfProtectionMiddleware.cs`) — this applies automatically to any action
   reached over the cookie scheme; bearer-authenticated requests are exempt (CSRF is a
   cookie-specific attack). Add no code for this — it is pipeline middleware — but document the
   header requirement for the endpoint's callers.
4. **Error mapping**: if the endpoint throws a new domain exception type that should surface a specific
   client-safe message/status, add it to `DomainErrorMap.Map` (and `StatusFor` if it needs a REST status
   other than the 500 fallback) in `src/Struo.Api/Http/DomainErrorMap.cs` — this is shared verbatim with
   GraphQL's error filter, so both protocols pick up the new mapping together. A fork's own error needs no
   arm: derive it from `StruoDomainException` (`src/Struo.Domain/StruoDomainException.cs`), whose code, 400 to
   499 status and message are returned as they are.
5. **Tests to add**: an integration test under `tests/Struo.Tests/Api/` using the `WebApplicationFactory`
   -based fixture in `tests/Struo.Tests/Support/ApiFactory.cs`, which derives from `StruoApiFactory` in `tests/Struo.Tests.Support/` (see `EndpointSmokeTests.cs` for the
   minimal shape of standing up the factory and asserting a 200 + envelope shape, or
   `RbacEnforcementTests.cs` for asserting a permission-gated action rejects the right way).
6. **Gate**: `dotnet build && dotnet test`.

## Playbook 4: Add a migration

Background: `docs/guide/en/21-schema-and-upgrades.md`, "Writing a migration"; `AGENTS.md`'s
"Migrations are the only schema source" invariant.

1. Write a new class in your content project's migrations directory:
   `dotnet run --project src/Struo.Api -- make:migration <Name> --output <dir> [--namespace <Ns>]`
   writes the skeleton with a version of the form `yyyyMMddHHmm` that does not collide with a file
   already in `<dir>`. The class derives from `StruoMigration`. One logical change per migration.
2. Express the change with FluentMigrator's portable calls (`Create.Table`, `Alter.Table`,
   `Create.Index`, `Insert.IntoTable`, …) and the type helpers: `AsString`, `AsGuid`, `AsInt64`, …,
   plus `AsShape(shape, Db)`, `AsJson(Db)`, `AsLongText(Db)` and `AsTimestamp(Db)` for the types that
   differ per backend. Branch on `StruoMigration.Db` only where a backend genuinely needs its own form.
   Do not write SQL strings, and do not reference entity classes from a migration: a migration is a
   frozen snapshot of the schema at the time it was written.
3. Resolve a framework table's physical name through `FrameworkTable("<logical>")` (it applies
   `Database:TablePrefix`); a fork's own tables carry no prefix. Use
   `AsShape(ColumnShape.TimestampWithTimeZone, Db)` only for a property declared
   `[ColumnShape(ColumnShape.TimestampWithTimeZone)]`; a plain `DateTime` property uses
   `AsTimestamp(Db)`. Store UTC.
4. **Never edit a migration that may already be applied anywhere.** Applied versions are recorded in
   `{TablePrefix}schema_versions` by version number, so an edited file is silently not re-run. Add a
   new migration instead. Migrations are forward-only; a rollback is a further migration.
5. Preview with `migrate:preview` (prints the pending DDL), apply with `migrate`, and inspect with
   `migrate:status`. `migrate:check` then compares the live schema with the entity metadata and exits 1
   on any error, so a migration that drifted from the entity is caught.
6. **Tests to add**: a migration is code, so apply it in a test against SQLite the way the shipped
   suite does (`tests/Struo.Tests/Migrations/`), and call `SchemaChecker.Check` from your own tests
   (from an `async` test method) and assert it reports no errors.
7. **Gate**: `dotnet build && dotnet test` first. The SQLite suite may accept or reject your DDL
   differently from the backend you actually run, so also apply the migration with `migrate` to a
   disposable instance of that backend and run `migrate:check` against it. A green SQLite or
   PostgreSQL run does not transfer to another backend.

## Playbook 5: Change the admin SPA

Background: `docs/guide/en/19-admin-customization.md`, "Ask whether metadata is enough" and
"The `frontend/src` map".

Before touching `frontend/src` at all: confirm the requirement is not already satisfiable by declaring
metadata (Playbooks 1/2). Adding a `[CmsCollection]`/`[CmsField]` alone produces a full sidebar entry,
schema-driven list, and generated create/edit form with zero Vue code — the admin SPA never hardcodes a
collection's fields, columns, or labels; everything comes from `GET /api/schema`. Reach into
`frontend/src` only for: a field editor experience none of the shipped interfaces provide (Playbook
2a), branding/theming beyond a name and logo, admin-UI language (i18n), or a workflow that doesn't fit
the generic list/form pattern at all (a dashboard widget, a bespoke wizard).

1. **Locate the right directory** using `docs/guide/en/19-admin-customization.md`'s
   "The `frontend/src` map": `api/` (thin REST wrappers), `assets/` (`tokens.css` and `theme.css`
   — the whole look), `components/` (`ItemForm.vue` at the top level, everything else a
   subdirectory by purpose, including the vendored `ui/`), `composables/`, `i18n/` + `locales/`,
   `layouts/`, `lib/` (framework-free helpers, including `fieldTypes/`), `router/`, `stores/`
   (Pinia), `theme/`, `types/`, `views/` (one component per route).
2. **Theming**: edit `frontend/src/assets/tokens.css` for the shadcn semantic custom properties
   (`--background`/`--primary`/`--radius`/…, on the `.app-dark` toggle class) that every vendored `ui/`
   component and Tailwind utility reads, and `frontend/src/assets/theme.css` for the unlayered global
   layer (page/surface/foreground and status/shadow/overlay/font custom properties, `html`/`body`
   resets, the theme-transition rule, `.app-breadcrumb`) first-party scoped CSS reads.
   **`frontend/src/components/ui/` is vendored, read-only output — never edit it and never `:deep()`
   into it**; a re-theme changes a token layer or a wrapper component outside `ui/`. See
   `docs/guide/en/19-admin-customization.md`'s "Restyling a vendored `ui/` component" section for
   how to override a component's style without losing a specificity fight. Avoid `!important`.
3. **i18n**: add the same key to every catalog in the `frontend/src/locales/index.ts` registry
   (`en.ts`, `zh-TW.ts`) under the right namespace (`common`, `nav`, `dashboard`, `collectionList`,
   `itemForm`, `media`, `revisions`, `rbac`, `settings`, `fields`, ...) — the i18n
   fallback locale is the build's default catalog (`en`, or `VITE_DEFAULT_UI_LOCALE`), so a key missing
   only from a non-default catalog degrades gracefully but one missing from the default catalog does not.
4. **A new view/component**: follow the existing `views/` pattern (one component per route, registered
   in `frontend/src/router/index.ts` as a `() => import()` lazy loader — routed views are code-split,
   and `frontend/tests/codeSplitting.test.ts` enforces it — gated by `frontend/src/router/guard.ts` if
   it needs authentication/permission checks).
5. **Tests to add**: a `*.test.ts` next to any new `lib/` helper or non-trivial component logic
   (Vitest). If the change affects a user-facing flow end-to-end, add or update a Playwright spec under
   `frontend/e2e/` (`core` project — never `e2e/sample/**` unless the change is specific to the Blog
   sample). A new field-type component also needs its own `frontend/src/lib/fieldTypes/registry.ts`
   entry (see Playbook 2a) — `frontend/tests/schemaContract.test.ts` checks the backend's declared
   interface enums directly, so *every* declared `FieldInterface` needs a registry entry whether or not
   a collection uses it yet, and one without an entry fails that test rather than silently rendering
   read-only.
6. **Gate**: `pnpm test && pnpm build` (the standing gates' frontend pair; `pnpm build` runs
   `vue-tsc -b`, which is CI's only enforcement of the SPA's TypeScript types) — this playbook's own
   steps change `frontend/src`, not `docs/guide/**`, so the docs gate does not apply; if a change under
   this playbook also edits a manual chapter (e.g. `docs/guide/en/19-admin-customization.md` itself),
   add `pnpm build` from `docs/` too. Run `pnpm e2e` (the `core` Playwright project) as a further check
   for any change touching a critical flow; `pnpm e2e:sample` runs the sample specs and needs the Blog
   sample opted in first. Locally, E2E needs a live API and database reachable at the dev proxy
   target; it is not one of the standing gates. CI's `e2e` job runs the `core` project against the
   Compose stack, and the `sample` project is not run by CI.

## Playbook 6: Re-capture the manual's transcripts

Background: `docs/ai/conventions.md`, "Transcripts". The tool is `docs/scripts/capture/` (Node,
standard library only; `docs/ai/decisions/capture-client-reuses-one-connection.md` records why
its client reuses one connection). It replays one ordered run against a host and writes the
responses into the marked blocks of both languages from that run. Chapters 7 to 17 are covered:
every transcript block in those chapters is marked and produced by the tool. The run is one list
of groups, `scenarios/index.mjs`: the Guides data set and the editor account, chapters 10 and 11,
the first block of chapter 14, chapters 12 and 13, the rest of chapter 14, chapters 15 to 17, the
Tutorials data set, then chapters 7, 8 and 9. The order is the order the shared articles change in.

**What the tool needs**

- A host with the Blog sample opted in (`docs/guide/en/23-sample-walkthrough.md`, "Opting the
  sample in") on a freshly migrated database that holds only the bootstrap admin. Keep every
  other shipped default: no public read grants, login rate limit off. Over plain HTTP the session
  cookie needs `Auth__Cookie__RequireSecure=false`.
- **Isolation is the operator's job.** Start the host with explicit `Database__*` settings that
  point at a throwaway database, in an environment that loads no developer-local configuration. A
  Development run from a working checkout reads the developer's local settings and can reach a real
  database. A container built from a clean source copy and started with
  `ASPNETCORE_ENVIRONMENT=Production` has none. Read the host's first log lines and confirm the
  database it connected to before the run.
- **The guard is the last line of defence, not the isolation.** Before the first step the tool logs
  in as the admin and requires exactly one user, no files, and no article, category or tag rows,
  trashed rows included. It stops and says what it found. The real safety is the throwaway database
  plus this guard. On top of that a DELETE step is refused unless one whole path segment equals an
  id a 201 response of the same run returned (a row the run created), or the step declares a
  `deletes` reason, for example revoking a token under the admin's id. Values the run read, fields
  other than an id, and empty values never qualify.
- A new database for every run.
- Redis for the host (`Redis__ConnectionString`): the login-lock scenario of chapter 16 counts
  attempts there, and the first `auth:unlock` block needs it. Local file storage is enough for
  chapter 15; the image block needs the native image library the published output carries.
- Settings: `STRUO_CAPTURE_ADMIN_PASSWORD` (required), `STRUO_CAPTURE_BASE_URL` and
  `STRUO_CAPTURE_ADMIN_EMAIL` (default `admin@admin.com`). The password has no command-line flag, so
  it stays out of the process list. The editor account's password is
  `STRUO_CAPTURE_EDITOR_PASSWORD` or, when unset, a random value made for the run; it is a
  registered secret and no block prints it.
- Two fixtures for the upload scenarios live in `docs/scripts/capture/fixtures/`: `swatch.png` (a
  64x48 PNG that `swatch.mjs` generates with `zlib`; a test proves the committed bytes are what the
  generator produces) and `note.txt`. Sizes the chapter quotes follow the capture.

**Run it** from `docs/`:

```text
node scripts/capture/capture.mjs --base-url http://localhost:5221 --chapters 10,11 --write
node scripts/capture/capture.mjs --check
```

The run always executes every group in its fixed order, so a partial run builds the same data as a
full one. `--chapters` (comma-separated, duplicates ignored) chooses only which chapter files are
written; the other files are not read and their blocks are discarded. Without `--write` the
captured blocks are printed and no file changes. `--base-url` and `--email` override the settings
above, and `--guide-dir` points at another manual root inside the working directory.

**Command blocks.** The tool does not run commands. A scenario step with `cli: { program, args,
env, exit }` declares a command line (`$ <program> <args>`, the program one word), the environment
it needs on top of the host's, and its exit code. `node scripts/capture/capture.mjs --list-cli`
prints those steps as JSON (`id`, `file`, `program`, `args`, `env`, `exit`) without a network. The
operator runs each one inside the container or environment the host runs in, with the listed
environment added, and saves the merged stdout and stderr as `<id>.out` and the exit code as
`<id>.exit` in one directory, as UTF-8 without a byte-order mark (a result with a byte-order mark
or a NUL byte is refused). The run takes that directory through `--cli-results <dir>`, which lies
inside the working directory. Before the first request it refuses to run when a command of a
selected file has no result file or ended with another exit code, and it prints the saved output as
the block. The environment is never printed: the two `auth:unlock` blocks have the same `$` line
although the second runs without the Redis connection string, and the chapter's prose says when
that happens. They set `Struo__Logging__ConsoleEnabled=false` so host log lines stay out of the
output, and the second one sets `Redis__ConnectionString` to an empty value.

**The login lock** (chapter 16) sends ten failed logins, the first refused one and the printed
second refused one, one after another for an email made for the run, so it takes a few seconds
(each failed attempt verifies a password hash). `Retry-After` is the time left in the lock window,
so it differs on every run; the block prints the value received. A second run against the same
Redis within the window needs a new email, and the tool makes a new one for every run.

The run produces and checks every block of every selected file before it replaces any file. Each
file is written to a temporary `*.capture-tmp` file next to it and the temporaries are renamed one
after the other, a file's two languages adjacent. A failure before the renames changes no chapter
file. A failure during the renames removes the remaining temporaries and the error lists the files
already replaced; `git checkout` restores them.

`--check` needs no network. It verifies that every marker sits above a closed fence, that near-miss
spellings of a marker are rejected, that both languages hold the same markers and byte-identical
blocks, that every marker belongs to a scenario capture and every capture has a marker, that each
chapter file exists in both languages, and that a block ends with the status its step declares.
The docs build runs it.

**Rules**

- A marked block is never edited by hand. The tool replaces the lines between the fences.
- Every difference between a printed body and the server's body is a transform named on the step
  that uses it (`stripTraceId` and `stripGraphQlTraceId` for the trace id, `elideData` for the
  `data` array of a list, `prettyJson` for a body the manual shows indented). A transform that
  changes nothing fails the step. An undeclared difference does not exist.
- A printed request body differs from the sent one only through `print.requestBody` on the step
  (a string, an object or a function of the run variables; for example a placeholder where the
  sent body holds the admin password). The tool refuses to write a block that holds the admin
  password, the session cookie of an identity, a run variable listed as secret, or any value a
  step registered with `secret`. A step that prints one declares `print.mask: { name: '<text>' }`,
  naming the secret (`adminPassword`, `editorPassword`, `session_<identity>`, or the `secret`
  variable); every occurrence in that exchange, the `$` line included, is replaced by the text
  before the check runs.
- How an exchange is shown is set by `print`: `headers: true` prints the status line and every
  response header, `headers: ['Name', ...]` only the named ones (a name the response lacks fails
  the step); a step that transforms the body cannot show `Content-Length`, because the length is
  the server's and the body is not, so it names the headers it shows; `note` and `path` take
  `{{variables}}` and add to the `$` line after one space; `responseOnly` leaves the request out;
  `pretty: true`
  keeps the raw line and appends `pretty:` with an indented copy; `binary: 'png'` prints the
  decoded PNG size and the byte count in place of a binary body (`binary: true` the byte count
  only) and cannot be combined with a transform. Steps that share a capture id make one block.
- A step can also upload a fixture as one multipart file part (`upload: { field, fixture,
  contentType }`), send a bearer token (`bearer`), omit the CSRF header (`csrf: false`), or run as
  an identity with its own cookie jar (`as`). An upload's body is never printed; the `$` line gets
  the note `(multipart: file=@<fixture>; type=<contentType>)` from the declaration, before any
  `print.note`. A step with no `as` sends no cookie at all. A login runs under an identity of its
  own whose jar holds no cookie yet (chapters 13, 16 and 17): the session cookie it receives is kept
  in that jar and registered as the secret `session_<identity>`. A cookie set on a step with no
  `as` is registered as `session_anonymous`. `verify(json, vars)` and `verifyResponse(response)`
  check what the prose says about a request the manual does not show; a throw stops the run, after
  the step's secrets are registered, so a message that quotes the response is redacted.
- Ids and timestamps change on every capture, so a re-capture rewrites every block of the files
  it selects. Ids quoted outside a marked block (a JSON sample, a sentence) are updated by hand from
  the same run; scan the chapter files for UUID-shaped text outside the marked blocks.
- After a capture, read the prose around each block. A sentence that states a number, a row or an
  order is true for the new output or is fixed in both languages.

**Add a capture**

1. Put `<!-- capture: <id> -->` on the line directly above the opening fence of the block, in both
   languages. The id is unique and stable.
2. Add a step with `capture: '<id>'` to a group in `docs/scripts/capture/scenarios/`. A group is
   `{ name, file, steps }`; a capture step takes the file of its group or names its own. A step
   names its identity, method, path, body, the status the manual shows (`expect`), the variables it
   saves (`save`, referenced as `{{name}}`) and its transforms. `lib/runner.mjs` documents every
   field. Steps that share an id make one block with several exchanges.
3. Place the group in `GROUPS` (`scenarios/index.mjs`) where the run needs it: after every group
   whose data it depends on, and before every group that changes data it reads. One file may be fed
   by several groups.
4. Run the tool tests (`node --test "scripts/**/*.test.mjs"`), then `--check`, then a capture.

## Next steps

- `AGENTS.md` for the condensed version of these five playbooks and the invariants they must respect.
- `docs/ai/architecture.md` for the extension points these playbooks put to use.
- `docs/ai/conventions.md` for the naming/error-handling/validation/test conventions each playbook
  should follow.
