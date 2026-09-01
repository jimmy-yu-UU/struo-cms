# Architecture Reference (for AI agents)

This document maps the four backend layers plus the frontend workspace, and describes every
extension point an agent is likely to need when working in a fork of this template. It expands on the
condensed version in `AGENTS.md`. For conceptual background, each section links the manual chapter that
covers it in full.

## Layer map

```
Struo.Domain  <──  Struo.Application  <──  Struo.Infrastructure  <──  Struo.Api
   (nothing)         (→ Domain)           (→ Application, Domain)   (→ Application, Infrastructure)

frontend/            Vue 3 admin SPA (separate pnpm workspace), talks to Struo.Api over REST/GraphQL
```

- **`src/Struo.Domain`** — domain types only: metadata attributes/enums/models (`Metadata/`),
  auditing base types (`Auditing/`), the query AST (`Query/`), localization value types
  (`Localization/`), SEO translation types (`Seo/`). `Struo.Domain.csproj` declares **zero**
  `PackageReference` and **zero** `ProjectReference` entries — it is verified by directly reading the
  `.csproj` file, not by an automated test.
- **`src/Struo.Application`** — application-layer abstractions and use cases: the metadata contracts
  (`Metadata/IMetadataProvider.cs`, `IEntityRegistry.cs`, `IEntityTypeCollector.cs`,
  `IRelationshipGraph.cs`), the query/item contracts (`Query/IItemUseCases.cs`, `IItemRepository.cs`,
  `IRelationExpander.cs`, `IRelationFilterResolver.cs`), the write-path validators
  (`Query/Write/Validators/`), file storage and security abstractions (`Files/`, `Security/`),
  options records (`Configuration/`). References only `Struo.Domain`.
- **`src/Struo.Infrastructure`** — the concrete implementations: SqlSugar wiring
  (`Persistence/SqlSugarClientFactory.cs`, `Persistence/MigrationRunner.cs`), metadata scanning and
  caching (`Metadata/MetadataScanner.cs`, `CachedMetadataProvider.cs`, `EntityRegistry.cs`,
  `RelationshipGraph.cs`, `EntityTypeCollector.cs`, `FrameworkEntityTypes.cs`), the query
  implementations (`Query/SqlSugarItemRepository.cs`, `RelationExpander.cs`,
  `RelationFilterResolver.cs`), identity (`Identity/`), files (`Files/`), revisions (`Revisions/`),
  settings (`Settings/`), health checks (`Health/`), and every `AddStruoXxx` DI-registration extension
  method (`DependencyInjection/`). References `Struo.Application` and `Struo.Domain`.
- **`src/Struo.Api`** — the ASP.NET Core host: controllers (`Controllers/`), the GraphQL schema
  (`GraphQl/`), the envelope/error-handling plumbing (`Http/`), authentication/CSRF/CORS wiring
  (`Auth/`), and `Program.cs`. References `Struo.Application` and `Struo.Infrastructure`.
- **`frontend/`** — a separate pnpm workspace (Vue 3 + Pinia, Tailwind v4 + shadcn-vue), not part of
  the .NET solution dependency graph at all; it talks to `Struo.Api` only over REST/GraphQL HTTP calls.

The dependency direction above is a structural fact of the four `.csproj` files' `ProjectReference`
lists (checked directly: `Struo.Domain.csproj` has none at all; `Struo.Application.csproj` references
only `Struo.Domain`; `Struo.Infrastructure.csproj` references `Struo.Application` and `Struo.Domain`;
`Struo.Api.csproj` references `Struo.Application` and `Struo.Infrastructure`) — nothing scans or asserts
it automatically. The one edge that **is** guarded by an automated test is framework code never
referencing `samples/*`: `tests/Struo.Tests/Template/TemplateInvariantsTests.cs`'s
`Host_project_has_no_project_reference_into_samples` reads `Struo.Api.csproj` directly and fails if any
`<ProjectReference>` line mentions `samples`; its sibling
`Shipped_appsettings_declares_no_content_assemblies` asserts the shipped `appsettings.json`'s
`Struo:ContentAssemblies` is empty. Only `tests/Struo.Tests` references the sample project — that is
what makes `samples/Struo.Sample.Blog` truly optional and deletable.

See `docs/guide/en/01-introduction-and-architecture.md` for the conceptual introduction to this layering.

## Metadata: the single source every other layer derives from

A **collection** is a plain C# class carrying `[CmsCollection]` (`src/Struo.Domain/Metadata/Attributes/
CmsCollectionAttribute.cs`) plus `[CmsField]` on its properties. At DI-registration time,
`MetadataScanner.Scan` (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) reflects over every
scanned assembly and builds one `CollectionMetadata` record per attributed class
(`src/Struo.Domain/Metadata/Models/CollectionMetadata.cs`). That record is the one thing the database
table, REST endpoints, GraphQL schema, and admin SPA form/list all derive from — there is no second
place to declare a route, a GraphQL type, or an admin screen. The scan is eager, happens once, and is
cached in a singleton — nothing about it re-runs per request. See
`docs/guide/en/04-defining-a-collection.md`.

## Extension points

For each interface below: what it does, where it lives, its real implementation(s), and how it is
registered. `IMetadataProvider`, `IEntityRegistry`, `IEntityTypeCollector`, `IRelationshipGraph`,
`IItemUseCases`, `IItemRepository`, `IRelationExpander`, and `IRelationFilterResolver` all exist under
those exact names in `Struo.Application`.

### `IMetadataProvider`

`src/Struo.Application/Metadata/IMetadataProvider.cs`: `GetCollections()` / `GetCollection(name)` over
the scanned `CollectionMetadata` list. Implementation: `CachedMetadataProvider`
(`src/Struo.Infrastructure/Metadata/CachedMetadataProvider.cs`), a thin read-only wrapper around the
list `MetadataScanner.Scan` produced. Registered as a singleton, constructed directly (not via the
container) in `MetadataServiceCollectionExtensions.AddStruoMetadata`
(`src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs`):
`services.AddSingleton<IMetadataProvider>(new CachedMetadataProvider(collections))`. This is mostly a
consumed seam (GraphQL's `StruoTypeModule`, `ItemsController`, `SchemaController` all read from it) —
a fork extends what it returns by adding `[CmsCollection]` classes to a scanned assembly, not by
reimplementing the interface.

### `IEntityRegistry`

`src/Struo.Application/Metadata/IEntityRegistry.cs`, alongside the `EntityDescriptor` record (CLR
type, field→property map, id property name, eagerly-built property-accessor cache) it operates over.
`Get(collection)` resolves case-insensitively. Implementation:
`EntityRegistry` (`src/Struo.Infrastructure/Metadata/EntityRegistry.cs`), built from
`MetadataScanner.ScanDescriptors`. Registered as a singleton, same construction pattern:
`services.AddSingleton<IEntityRegistry>(new EntityRegistry(descriptors))`
(`MetadataServiceCollectionExtensions.cs`).

### `IEntityTypeCollector`

`src/Struo.Application/Metadata/IEntityTypeCollector.cs`: `CollectForInitTables()` returns every CLR
entity type that needs a database table — scanned collections, their translation sidecars, and M2M
junction types — unioned and de-duplicated against `FrameworkEntityTypes.All`
(`src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs`, the eleven framework entity types: Language,
File, FileTranslation, MediaFolder, User, Role, Permission, UserRole, Revision, SiteSettings,
UserSession).
Implementation: `EntityTypeCollector` (`src/Struo.Infrastructure/Metadata/EntityTypeCollector.cs`).
Registered as a singleton via the container:
`services.AddSingleton<IEntityTypeCollector, EntityTypeCollector>()`
(`MetadataServiceCollectionExtensions.cs`). Consumed by `DatabaseInitializer.CreateMissingTables`
(`Program.cs`), which runs in every environment, on every backend — table creation is no longer gated to
Development (see `docs/guide/en/15-deployment-operations-testing.md`, "Schema management").

### `IRelationshipGraph`

`src/Struo.Application/Metadata/IRelationshipGraph.cs`: `Relations(collection)`,
`Resolve(collection, relationName)`, `InboundRestrict(targetCollection)`, plus two default-interface
methods `InboundSetNull`/`InboundCascade` that default to an empty list (so an older implementer, e.g.
a test fake, still compiles). Implementation: `RelationshipGraph`
(`src/Struo.Infrastructure/Metadata/RelationshipGraph.cs`), which also implements a second interface,
`IM2MDescriptorSource`. Registered as a singleton, with the **same instance** bound to both interfaces
plus its own concrete type (`MetadataServiceCollectionExtensions.cs`):
```csharp
var graph = new RelationshipGraph(collections, collectionTypes);
services.AddSingleton<IRelationshipGraph>(graph);
services.AddSingleton<IM2MDescriptorSource>(graph);
services.AddSingleton(graph);
```

### `IItemUseCases`

`src/Struo.Application/Query/IItemUseCases.cs`: the item read/write surface the HTTP layer depends on —
`QueryAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `RestoreAsync`,
`ListRevisionsAsync`, `GetRevisionAsync`, `RevertAsync`. Its doc comment states it was "extracted
verbatim from `ItemService`" so controllers depend on this seam rather than the concrete class. Sole
implementation: `ItemService` (`src/Struo.Application/Query/ItemService.cs`) — note this is one of the
few Application-layer classes with real business logic rather than a pure contract; it enforces RBAC
internally by calling `ICurrentPermissions.CanRead`/`CanWrite`/`CanDelete` before each operation and
throwing `PermissionDeniedException` on denial. Registered scoped, with `IItemUseCases` resolved from
the same `ItemService` instance in `DataServiceCollectionExtensions.AddStruoData`
(`src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`):
```csharp
services.AddScoped<ItemService>();
services.AddScoped<IItemUseCases>(sp => sp.GetRequiredService<ItemService>());
```
GraphQL does **not** consume `IItemUseCases` — it keeps its own `IGraphQlDataSource` seam (see the
GraphQL section below) so the REST and GraphQL read/write paths can differ in shape while sharing the
same underlying repository/expander/resolver primitives.

### `IItemRepository`

`src/Struo.Application/Query/IItemRepository.cs`: the SqlSugar-facing data-access seam — query/get/
create/update/delete, soft-delete/restore, transaction scoping, batched `WHERE...IN` reads, M2M sync,
translation-sidecar load/sync, and a set of purge referential-integrity primitives
(`SetForeignKeyNullAsync`, `DeleteByPropertyAsync`, `QueryWhereInWithDeletedAsync`). Those three purge
primitives are declared with default bodies that `throw new NotSupportedException(...)` rather than a
silent no-op — the interface's own comment explains why: a second implementation that forgot to
override one of them would otherwise silently orphan referential rows on purge, exactly the defect
class the defaults exist to prevent. Sole implementation: `SqlSugarItemRepository`
(`src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs`). Registered scoped:
`services.AddScoped<IItemRepository, SqlSugarItemRepository>()`
(`src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`). This is the seam
a fork would implement to point at a different storage engine; any replacement must implement the three
purge primitives explicitly or purge will throw for every collection.

### `IRelationExpander`

`src/Struo.Application/Query/IRelationExpander.cs`: `ExpandAsync(...)` expands `deep` relations for a
page of parent entities via batched follow-up queries, returning a per-parent-id map of
`relationName -> (object?|list)`. Its doc comment states it is "Implemented in Infrastructure (where the
relationship-graph descriptors and junction CLR types live)" specifically so `Struo.Application` never
references SqlSugar internals. Implementation: `RelationExpander`
(`src/Struo.Infrastructure/Query/RelationExpander.cs`). Registered scoped:
`services.AddScoped<IRelationExpander, RelationExpander>()`
(`DataServiceCollectionExtensions.cs`).

### `IRelationFilterResolver`

`src/Struo.Application/Query/IRelationFilterResolver.cs`: `RewriteAsync(rootCollection, filter,
queryLocale, ct)` rewrites every dotted (cross-relation) filter node into an own-collection
`id IN (...)` (or `id IS NULL`) condition by resolving the relation path to a set of root ids; when
`queryLocale` is supplied it also rewrites translatable own-collection fields via the translation
sidecar at that locale. Implementation: `RelationFilterResolver`
(`src/Struo.Infrastructure/Query/RelationFilterResolver.cs`). Registered scoped:
`services.AddScoped<IRelationFilterResolver, RelationFilterResolver>()`
(`DataServiceCollectionExtensions.cs`).

See `docs/guide/en/07-relations.md` and `docs/guide/en/08-query-dsl.md` for the query DSL these four
query-layer seams jointly implement.

### Identity seams (`IUserCredentialStore`, `IUserAccountStore`, `IPermissionGrantStore`, `IRolePermissionStore`, `IExternalUserStore`)

All five live in `src/Struo.Application/Security/`, all five are implemented by a `SqlSugar*` class in
`src/Struo.Infrastructure/Identity/`, and all five are registered scoped in
`ServiceCollectionExtensions.AddStruoInfrastructure`. They are deliberately narrow rather than one
identity god-interface, split by *consumer* rather than by table:

- **`IUserCredentialStore`** → `SqlSugarUserCredentialStore`. The authentication read path
  (`AuthService`, `BearerTokenAuthenticationHandler`) plus the self-service password change's
  proof-of-knowledge check. Its reason for existing is that it hands back password hashes, so it
  bypasses the generic projection to keep them off the normal read path — which is also why the
  by-id lookup lives here rather than on `IUserAccountStore`.
- **`IUserAccountStore`** → `SqlSugarUserAccountStore`. User-account administration: create, exists,
  profile projection, and the three credential mutations (set password, issue/revoke access token).
  Every mutator stamps `UpdatedAt`/`UpdatedBy` and increments `Version` itself, because these are
  column-scoped `SetColumns` updates and therefore outside both `AuditAop` (which hooks only
  `InsertByObject`/`UpdateByObject`) and `SqlSugarItemRepository`'s version bump.
- **`IPermissionGrantStore`** → `SqlSugarPermissionGrantStore`. The RBAC *write* side — the admin
  permission matrix's read and atomic full-replace of a role's grants.
- **`IRolePermissionStore`** → `SqlSugarRolePermissionStore`. The RBAC *read* side, resolving a
  caller's effective permissions once per request. Kept separate from the write side above because it
  documents itself as bypassing permission gating on purpose: the query that resolves the gate must
  never itself be gated.
- **`IExternalUserStore`** → `SqlSugarExternalUserStore`. OIDC just-in-time provisioning by email.

`tests/Struo.Tests/Template/ControllerPersistenceBoundaryTests.cs` guards the boundary these exist to
create: no file under `src/Struo.Api/Controllers/` may mention `ISqlSugarClient`. Before these seams,
`UsersController`/`RolesController`/`AuthController.Me` injected the SqlSugar client and wrote
`Insertable`/`Updateable`/`Deleteable`/`Ado.BeginTranAsync` inline against `Struo.Infrastructure.Identity`
entity types — which quietly falsified `IItemRepository`'s billing as "the seam a fork implements to
point at a different storage engine", and put those writes outside the repository's audit/version
behavior. Note the guard bans raw ORM access only: three controllers still depend on the concrete
`Struo.Infrastructure.Files.FileService` (aliased, so its `File` type doesn't collide with
`System.IO.File`), which is a service rather than an ORM handle.

### File storage backends (`IFileStorage`)

The extension point for file storage backends is `IFileStorage`
(`src/Struo.Application/Files/IFileStorage.cs`): `SaveAsync`, `OpenReadAsync`, `DeleteAsync`,
`SupportsPresignedUrls`, `GetPresignedUrlAsync`. Its doc comment states "Exactly one
implementation is registered." Two implementations ship: `LocalFileStorage` and `S3FileStorage`
(both `src/Struo.Infrastructure/Files/`). The active one is chosen at registration time in
`FileStorageServiceCollectionExtensions.AddStruoFiles`
(`src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`) by a
factory lambda keyed on `FileStorageOptions.Backend` (`"s3"` selects `S3FileStorage`, anything else —
including the default — selects `LocalFileStorage`), registered singleton. Adding a third backend means
implementing `IFileStorage` and extending that lambda's branch (or switch) to select it. See
`docs/guide/en/11-files-and-media.md`.

### The GraphQL type module

The GraphQL schema's extension point is HotChocolate's own `ITypeModule`, implemented once by
`StruoTypeModule` (`src/Struo.Api/GraphQl/StruoTypeModule.cs`). It takes `IMetadataProvider` and
`IEntityRegistry` as constructor dependencies and, in `CreateTypesAsync`, emits one object type + list wrapper + filter
input + two root query fields (and, per-collection, mutation fields) for every scanned collection, plus
shared value types (`TagItem`, `Translation`, the `DeletedFilter` enum, revision types, shared filter
inputs). Registered in `GraphQlServiceCollectionExtensions.AddStruoGraphQl`
(`src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`):
```csharp
services.AddSingleton<StruoTypeModule>();
// ...
.AddGraphQLServer()
// ...
.AddTypeModule<StruoTypeModule>()
```
The comment above the singleton registration explains why it must be explicit: HotChocolate's
`AddTypeModule<T>()` resolves `T` via `GetRequiredService<T>()` against application services, not
schema-scoped activation. Because the schema is metadata-driven, a fork does not usually reimplement
`ITypeModule` — a new `[CmsCollection]` automatically gets a GraphQL type the next time the process
starts and rescans metadata. See `docs/guide/en/10-graphql-api.md`.

### The frontend field-type registry

`frontend/src/lib/fieldTypes/registry.ts` — a `Record<FieldInterface, FieldTypeDef>` mapping every
`FieldInterface` value (`frontend/src/lib/fieldTypes/types.ts`) to a Vue component plus
`defaultValue`/`parse`/`serialize`/`listColumn` (and an optional `validate`) function. `FieldInput.vue`,
the dispatcher every generated item form renders, looks a field's component up via
`getFieldType(field.interface)` and forwards `field`/`modelValue`/`disabled` — a new editor never needs
to know it is being rendered inside a generated form. `types.ts`'s own comment states the `FieldInterface`
union "mirrors backend `Struo.Domain.Metadata.Enums.FieldInterface`... MUST be kept in sync with that
enum." Two distinct extension motions:
- **Swap the editor for an existing interface** (e.g. give `Color` a real swatch picker) — edit only
  `registry.ts`'s entry for that interface; no backend change needed.
- **Add a genuinely new interface value** — requires the backend `FieldInterface` enum, `MetadataScanner`,
  and (if the value needs JSON/text column widening) `SqlSugarClientFactory`'s CodeFirst hook, in
  addition to `types.ts` and `registry.ts`. See `docs/ai/task-playbooks.md`, "Add a field type", and
  `docs/guide/en/05-field-types.md` / `docs/guide/en/14-admin-spa-customization.md`.

## Backend request flow (REST)

`ItemsController` (`src/Struo.Api/Controllers/ItemsController.cs`) is the one controller that serves
every collection generically — it depends on `IItemUseCases`, never on `ItemService` directly. A read
(`GetAsync`/`QueryAsync`) resolves the query through `QueryValidator` (whitelist/depth-cap validation,
`src/Struo.Application/Query/QueryValidator.cs`), `IRelationFilterResolver` (cross-relation filter
rewrite), `IItemRepository` (the actual SqlSugar query), and `IRelationExpander` (deep-relation
batching) before `ItemProjector` (`src/Struo.Application/Query/Projection/ItemProjector.cs`) turns the
result into the camelCase dictionary the envelope serializes. A write (`CreateAsync`/`UpdateAsync`)
goes through `ItemDeserializer` (`src/Struo.Application/Query/Write/ItemDeserializer.cs`, which also
sanitizes non-translatable `RichText` fields via `RichTextCleaner` — a wrapper around `IHtmlSanitizer` —
before required-field validation; translatable `RichText` fields are sanitized separately, per locale,
in `ItemWriteSideSync.SyncTranslationsAsync`) and the `FieldValidatorRegistry`-driven per-`FieldInterface` validators
(`src/Struo.Application/Query/Write/FieldValidatorRegistry.cs`) before `IItemRepository` commits inside
a transaction (`InTransactionAsync`). Every response — success or error — passes through
`EnvelopeResultFilter`/`StruoExceptionHandler` (`src/Struo.Api/Http/`) so the wire shape is uniform. See
`docs/guide/en/08-query-dsl.md` and `docs/guide/en/09-rest-api.md`.

## Next steps

- `docs/ai/conventions.md` for naming, file organization, error handling, validation, configuration,
  and test conventions that apply across all of the above.
- `docs/ai/task-playbooks.md` for the five step-by-step recipes that put these extension points to use.
- The manual chapters cited throughout this document for full conceptual background.
