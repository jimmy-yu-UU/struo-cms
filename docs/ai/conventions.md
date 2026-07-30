# Coding Conventions (for AI agents)

Conventions that apply across the backend and frontend. Where a convention is enforced by a test or by
the compiler, this document says so and names the mechanism; where it is a convention only, it says
that plainly instead of implying enforcement that does not exist.

## Naming

- **C# types and members**: PascalCase, standard .NET convention throughout `src/` and `tests/`.
  Interfaces are prefixed `I` (`IItemRepository`, `IMetadataProvider`, ...).
- **Database tables and columns**: lower-case, plural, snake_case (`languages`, `file_translations`,
  `media_folders`, `user_roles`) — every framework and sample entity follows this via `[SugarTable]`
  (`docs/guide/en/04-defining-a-collection.md`). Columns follow SqlSugar's default lower-casing of the
  CLR property name.
- **Collection/field JSON names**: camelCase on the wire (`fileName`, `createdAt`) — the outbound
  `JsonSerializerOptions` are `JsonSerializerDefaults.Web` throughout (see `EnvelopeJsonOptionsHolder`,
  `src/Struo.Api/Http/EnvelopeJsonOptionsHolder.cs`, and the MVC `JsonOptions` configured in
  `Program.cs`), and `MetadataScanner` derives each field's camelCase name from the CLR property name.
- **Migration files**: `NNN-short-kebab-description.sql`, zero-padded, one contiguous series
  (`db/migrations/README.md`) — the numeric prefix is the apply order via ordinal filename sort, so it
  must be monotonic and gap-free; the next number is always the current highest **+ 1**.
- **Frontend field interfaces**: camelCase string literals (`richText`, `multiSelect`, `checkboxGroup`)
  mirroring the backend `FieldInterface` enum member names — `frontend/src/lib/fieldTypes/types.ts`'s
  own comment states this must be kept in sync by hand; nothing generates one from the other.
- **Test class/file names**: one test class per source concept, named `<Subject>Tests.cs`
  (`ItemsEndpointTests.cs`, `MetadataScannerTests.cs`, `TemplateInvariantsTests.cs`) or
  `<subject>.test.ts` for frontend unit tests, co-located next to the source file it covers
  (`frontend/src/lib/buildItemPayload.ts` / `buildItemPayload.test.ts`).

## File organization

Both `Struo.Application` and `Struo.Infrastructure` are organized **by feature area**, not by technical
role, though the two projects don't carry identical folder sets — each only has the folders its own
concerns need. `Struo.Application`: `Abstractions/`, `Configuration/`, `Files/`, `Localization/`,
`Metadata/`, `Query/` (with `Query/Read/` and `Query/Write/` sub-folders for the read/write split, plus
`Query/Write/Validators/`), `Revisions/`, `Security/`, `Settings/`. `Struo.Infrastructure`:
`DependencyInjection/`, `Files/`, `Health/`, `Identity/`, `Localization/`, `Metadata/`, `Persistence/`,
`Query/`, `Revisions/`, `Security/`, `Settings/` — it additionally owns `Identity/` (the concrete user/
role/permission entities and SqlSugar wiring), `DependencyInjection/` (every `AddStruoXxx` extension
method), `Health/`, and `Persistence/` (SqlSugar client/migration plumbing), none of which
`Struo.Application` has any need for. `Struo.Api` mirrors this: `Controllers/`, `GraphQl/`,
`Http/`, `Auth/`. `tests/Struo.Tests` mirrors the same feature folders (`Metadata/`, `Query/`, `Api/`,
`Files/`, `Identity/`, ...) so a change to one feature's production code has an obvious, adjacent home
for its test. New code should follow the same pattern: add to (or create) a feature-named folder rather
than a generic `Services/`/`Helpers/`/`Utils/` bucket, and keep files small and single-purpose — the
codebase's existing files split by responsibility rather than by layer role (e.g. `ItemDeserializer`,
`ItemProjector`, `QueryValidator`, `FieldValidatorRegistry` are separate files even though they all
serve `ItemService`).

## Error handling and the response envelope

Every REST response — success or error — is wrapped in one of two shapes by `EnvelopeResultFilter`
(`src/Struo.Api/Http/EnvelopeResultFilter.cs`), an `IAlwaysRunResultFilter`:
- Success: `{ "success": true, "data": ..., "meta"?: { "total", "limit", "offset" } }` — `meta` is
  omitted entirely (never emitted as `null`) unless the action returned a paginated `PagedResult`.
- Error: `{ "success": false, "error": { "code", "message", "details"?: [{ "field", "message" }] } }` —
  `details` is present only for the `VALIDATION` code.

`Envelope.Success`/`Envelope.Error` (`src/Struo.Api/Http/Envelope.cs`) are the only helpers that should
construct these shapes; a controller action that needs a bare error response uses
`ApiResults.Fail(status, code, message)` (`src/Struo.Api/Http/ApiResults.cs`) rather than hand-building
an `ObjectResult`, so `EnvelopeResultFilter`'s idempotency check (already-an-envelope → left untouched)
keeps working. Domain exceptions are mapped to the ten stable `ErrorCodes`
(`src/Struo.Api/Http/ErrorCodes.cs`) by `DomainErrorMap`
(`src/Struo.Api/Http/DomainErrorMap.cs`) — the single source of the exception→code mapping, shared
verbatim by REST's `StruoExceptionHandler` and GraphQL's `StruoErrorFilter` so the two protocols cannot
drift apart. A new domain exception type that should surface a client-safe message needs a new arm in
`DomainErrorMap.Map` (and, if it needs a REST status other than the fallback 500, in
`DomainErrorMap.StatusFor`); anything left unmapped collapses to `INTERNAL_SERVER_ERROR` with a masked
generic message — the real exception is logged server-side, never leaked to the response. See
`docs/guide/en/09-rest-api.md`.

## Input validation at boundaries

- **Query DSL** (filter/sort/search/fields/deep paths): validated and whitelisted by `QueryValidator`
  (`src/Struo.Application/Query/QueryValidator.cs`) against the scanned `CollectionMetadata` before any
  SQL is built — an unknown field or relation path is rejected with `QueryException` /
  `BAD_USER_INPUT`, never passed through to the ORM. See
  `docs/guide/en/08-query-dsl.md`, "Validation: whitelisting, unknown paths, and the depth cap".
- **Write bodies**: `ItemDeserializer` (`src/Struo.Application/Query/Write/ItemDeserializer.cs`) parses
  the request JSON against the collection's metadata (unknown/`ReadOnly`/system fields are stripped,
  not silently trusted) and sanitizes non-translatable `RichText` values via `RichTextCleaner` (a
  wrapper around `IHtmlSanitizer`, `src/Struo.Application/Query/Write/RichTextCleaner.cs`) before
  required-field checks run; translatable `RichText` values go through the same `RichTextCleaner`
  separately, per locale, in `ItemWriteSideSync.SyncTranslationsAsync`
  (`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`). `FieldValidatorRegistry`'s
  per-`FieldInterface` validators
  (`src/Struo.Application/Query/Write/Validators/`) enforce structural constraints for `MultiSelect`/
  `CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater` fields, in that fixed phase order.
  `Required` is enforced for non-translatable fields at this layer.
- **Configuration**: bound with the `IOptions` pattern and validated with `ValidateOnStart` —
  `Database`, `Struo:Files`, `Oidc`, and `Query` options all fail fast at startup (before the app
  accepts a single request) rather than surfacing as a confusing first-request failure
  (`tests/Struo.Tests/DependencyInjection/OptionsValidationTests.cs` exercises this against a real
  generic host). `FileStorageOptionsValidator`
  (`src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`) is an
  example of wrapping a plain `Validate()` method into that pipeline.
- **RBAC**: enforced inside `ItemService` (`src/Struo.Application/Query/ItemService.cs`) via
  `ICurrentPermissions.CanRead`/`CanWrite`/`CanDelete`, throwing `PermissionDeniedException` on denial
  — not by a per-controller-action attribute for generic collection CRUD. `[CmsCollection(AdminOnly =
  true)]` collections additionally require a super-admin for any write, checked the same way. See
  `docs/guide/en/12-auth-and-rbac.md`.

## Configuration over hardcoding

Every deployment-tunable value is bound through the `IOptions<T>` pattern from `appsettings.json` /
environment-variable overrides (`Section__Key`), never hardcoded in source — `DatabaseOptions`,
`FileStorageOptions`, `OidcOptions`, `StruoQueryOptions`, `RateLimiting:Login`, `Serilog:*`, and so on
(`docs/guide/en/03-configuration-reference.md` is the full reference). A relative filesystem path in
configuration must be resolved against the correct root explicitly — `Struo:Files:ImageTransform:
CachePath` is resolved against `IHostEnvironment.ContentRootPath`
(`FileStorageServiceCollectionExtensions.cs`), which is the pattern to copy; `Database:MigrationsPath`
by contrast is passed straight to `Directory.Exists` with no content-root resolution of its own, so it
**must** be given as an absolute path in Production (`src/Struo.Infrastructure/Persistence/
MigrationRunner.cs`) — see `docs/guide/en/15-deployment-operations-testing.md`. New tunables should
follow the `ImageTransform:CachePath` pattern (explicit content-root resolution), not the
`MigrationsPath` one.

## Immutability

Domain and query model types are C# `record`s with `init`-only properties
(`src/Struo.Domain/Metadata/Models/CollectionMetadata.cs` and siblings; `QueryModel`,
`src/Struo.Domain/Query/`). Updating a value produces a new instance via `with` rather than mutating in
place — for example `QueryValidator.cs`'s `return q with { Limit = limit, Offset = offset };`. New code
in `Struo.Domain`/`Struo.Application` should follow the same pattern: prefer `record`/`sealed record`
with `init` properties and non-destructive `with` updates over mutable classes with setters, especially
for anything that flows through the metadata cache or the query pipeline (both are shared, longer-lived
state where an accidental in-place mutation would be visible to every subsequent caller).

## Tests per layer

- **Backend** (`tests/Struo.Tests`, xUnit, run with `dotnet test`): most tests build a fresh SQLite
  temp-file database per test (`Support/SqliteTestDatabase.cs`, deleted on dispose). An opt-in
  live-PostgreSQL suite (`PostgresIntegrationTests`) exists specifically to catch "SQLite-green ≠
  Postgres-correct" bugs; it activates only when `Testing:PostgresConnection` is configured — resolved
  from the `STRUO_TEST_PG_CONNECTION` environment variable first, falling back to the
  `Testing:PostgresConnection` key in `src/Struo.Api/appsettings.json`/`appsettings.Development.json` if
  the env var is unset — and is otherwise a no-op pass; it also refuses to run against any database
  whose name doesn't contain `test`. Integration-style tests for the HTTP
  surface live under `tests/Struo.Tests/Api/` using a `WebApplicationFactory`-based fixture
  (`Support/ApiFactory.cs`). `tests/Struo.Tests/Template/TemplateInvariantsTests.cs` is the one suite
  that guards template-shape invariants (no `samples/*` reference from `Struo.Api`, empty shipped
  `ContentAssemblies`) rather than exercising `AddStruoMetadata` at runtime.
- **Frontend unit** (`pnpm test`, Vitest, `jsdom` environment): `*.test.ts` files sit next to the
  source file they cover (e.g. `frontend/src/lib/buildItemPayload.ts` /
  `frontend/src/lib/buildItemPayload.test.ts`).
- **Contract** (`schema/core-collections.json` plus `tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs`
  and `frontend/tests/schemaContract.test.ts`): a committed snapshot of the core collections' `GET
  /api/schema` wire shape, checked from both ends. It exists because the admin SPA mirrors the backend
  DTOs by hand, and drift between them is silent to every other gate — an unknown `FieldInterface`
  falls back to a read-only renderer instead of erroring, and a field whose interface has no
  list-column formatter just disappears from the list view. Unlike E2E, this layer **is** run by CI:
  nothing about it is live or external — the backend half exercises `GET /api/schema` through the same
  in-process `WebApplicationFactory`/SQLite fixture other API tests use, not a running server or a real
  database — so both halves ride inside the existing `dotnet test`/`pnpm test` commands. See
  `schema/README.md` for the full contract and the regeneration command.
- **E2E** (Playwright, `frontend/playwright.config.ts`): two projects — `core` (`pnpm e2e`) runs
  framework-only specs under `frontend/e2e/` (excluding `e2e/sample/**`) against the shipped template
  with zero content collections; `sample` (`pnpm e2e:sample`) runs `e2e/sample/**` and needs the Blog
  sample opted in first. Neither is run by CI (`.github/workflows/ci.yml` runs only `dotnet build` +
  `dotnet test` and `pnpm test` + `pnpm build`) — both need a live API and database, not just a build.

See `docs/guide/en/15-deployment-operations-testing.md`, "The three test layers", for the full picture.

## Commit message format

Conventional commits, observed consistently in this repository's own history: `<type>(<scope>):
<description>`, scope optional. Types actually used: `feat`, `fix`, `docs`, `chore`, `test`, `refactor`,
`ci`, `style`, `perf`, `build`, `revert`. No attribution trailer is used in this repository's commits.

## Next steps

- `docs/ai/architecture.md` for the layer map and extension points these conventions apply to.
- `docs/ai/task-playbooks.md` for these conventions applied end-to-end in five concrete recipes.
