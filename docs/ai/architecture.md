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
  `IRelationExpander.cs`), the write-path validators
  (`Query/Write/Validators/`), file storage and security abstractions (`Files/`, `Security/`),
  options records (`Configuration/`). References only `Struo.Domain`.
- **`src/Struo.Infrastructure`** — the concrete implementations: SqlSugar wiring
  (`Persistence/SqlSugarClientFactory.cs`, `Persistence/MigrationRunner.cs`), metadata scanning and
  caching (`Metadata/MetadataScanner.cs`, `CachedMetadataProvider.cs`, `EntityRegistry.cs`,
  `RelationshipGraph.cs`, `EntityTypeCollector.cs`, `FrameworkEntityTypes.cs`), the query
  implementations (`Query/SqlSugarItemRepository.cs`, `RelationExpander.cs`,
  `FilterTranslator.cs`/`FilterTranslator.Subquery.cs`), identity (`Identity/`), files (`Files/`), revisions (`Revisions/`),
  settings (`Settings/`), health checks (`Health/`), and the general-purpose `AddStruoXxx`
  DI-registration extension methods (`DependencyInjection/`: `AddStruoData`, `AddStruoFiles`,
  `AddStruoMetadata` (two overloads), `AddStruoInfrastructure`) — the four host-specific ones
  (`AddStruoAuth`, `AddStruoCors`, `AddStruoOidc`, `AddStruoGraphQl`) live in `src/Struo.Api` instead.
  References `Struo.Application` and `Struo.Domain`.
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

### ORM coupling

"Database-replaceable" in this template means the SqlSugar *provider*, not the ORM. `Database:DbType`
maps 1:1 onto `SqlSugar.DbType` in `DbTypeMapper.Map`
(`src/Struo.Infrastructure/Persistence/DbTypeMapper.cs`): PostgreSQL is the verified target, SQLite
backs the test suite, MySQL/SqlServer/Oracle are mapped but unverified. The ORM itself is not swappable
at the content-project level — every content entity (`using SqlSugar;`, e.g.
`samples/Struo.Sample.Blog/Article.cs`) carries SqlSugar's own
`[SugarTable]`/`[SugarColumn]`/`[SugarIndex]`/`[Navigate]` attributes directly alongside StruoCMS's
`[CmsCollection]`/`[CmsField]`, and CodeFirst's DDL rules (`IsPrimaryKey`, `IsJson` (hook-widened),
`[ColumnShape]` — see
`docs/guide/en/04-defining-a-collection.md`,
`docs/guide/en/05-field-types.md`) are SqlSugar semantics, not a StruoCMS abstraction over them. The
sidecar `(fk, locale)` unique is the exception, not SqlSugar semantics but a StruoCMS abstraction
derived from `[CmsTranslations]` metadata — see the next paragraph.
`docs/guide/en/13-revisions-and-soft-delete.md` is a different kind of SqlSugar coupling, not a DDL one:
revisions need no extra column and `ISoftDeletable` is a package-free marker interface, but the
soft-delete floor itself is `db.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null)`,
registered against the SqlSugar client in `SqlSugarClientFactory.Create` (see that chapter's "The global
query filter" section) — a SqlSugar API dependency, just not a DDL attribute one.
`IItemRepository` (`src/Struo.Application/Query/IItemRepository.cs`, sole implementation
`SqlSugarItemRepository`) is an internal seam inside core, not an ORM-abstraction layer forks are meant
to reimplement to swap ORMs — an entity's SqlSugar attributes stay bound to SqlSugar regardless of what
implements that interface. Consequence: a SqlSugar major-version upgrade, or a semantic change to an
attribute like `[SugarIndex]`, lands directly on every fork's entity classes; core does not absorb it.
Two decisions that used to fall in that category are the exception: bare-`IsJson` column widening and
the translation sidecar's `(fk, locale)` unique are both derived inside `SqlSugarClientFactory`'s
`EntityService` hook rather than declared per entity, so core absorbs a change to either one on a
fork's behalf. That absorption leans on SqlSugar-internal surface — the hook sets
`EntityColumnInfo.UIndexGroupNameList`, not a documented public API. A SqlSugar upgrade that renames the
property is a compile error, caught immediately; the risk is a *reshape* that leaves the property
itself in place but changes what CodeFirst does with it, which would silently stop deriving the unique
with no build-time signal. `TranslationSidecarIndexPolicyTests` (`tests/Struo.Tests/Persistence/`)
exercises the hook end-to-end against a real `InitTables` run and asserts the resulting index, so that
kind of reshape fails the test suite instead of failing silently. When bumping core, diff
`Directory.Packages.props`'s `SqlSugarCore` version line
against the fork's previous checkout and read that release's changelog before merging.

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
`IItemUseCases`, `IItemRepository`, and `IRelationExpander` all exist under those exact names in
`Struo.Application`.

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
internally by calling `IPermissionService.CanRead`/`CanWrite`/`CanDelete` before each operation and
throwing `PermissionDeniedException` on denial. The sole shippable `IPermissionService` implementation,
`RbacPermissionService`, answers those calls by reading the resolved snapshot off
`ICurrentPermissions.Current` (`src/Struo.Application/Security/ICurrentPermissions.cs`), which only
exposes that read-only `Current` getter; writing a new snapshot goes through the separate
`ICurrentPermissionsWriter.Set`, and the only caller of it is `PermissionResolutionMiddleware`
(`src/Struo.Api/Auth/PermissionResolutionMiddleware.cs`). `DataServiceCollectionExtensions.AddStruoData`
resolves both interfaces from the same factory-forwarded scoped `CurrentPermissions` instance, the same
pattern used for `IItemUseCases` below — otherwise the middleware's write and a controller's read would
land on two different objects. A fork that substitutes its own permissions holder must replace all
three registrations together (`CurrentPermissions`, `ICurrentPermissions`, `ICurrentPermissionsWriter`);
replacing only one or two leaves `PermissionResolutionMiddleware` writing into the framework's
`CurrentPermissions` while the fork's reader is resolved from a different instance and never sees the
write. Read enforcement is not confined to `ItemService`
itself: a query that reaches into a related collection is checked hop by hop, and the failure mode
differs by path. `QueryValidator.DenyUnreadableHops`
(`src/Struo.Application/Query/QueryValidator.cs`) throws `PermissionDeniedException` on the first
unreadable collection a dotted filter/sort path traverses; `DeepExpansionCoordinator.PruneUnreadable`
(`src/Struo.Application/Query/Read/DeepExpansionCoordinator.cs`) instead silently omits an unreadable
`deep=` relation from the response; and `TranslationOverlay`
(`src/Struo.Application/Query/Read/TranslationOverlay.cs`) gates translatable Image/File resolution on
`CanRead` for the file collection. Consequence for an anonymous-read deployment: every collection a
public filter or `deep=` path traverses needs its own `Rbac:PublicReadCollections` entry, not just the
root collection — that key is consulted only at first boot (`docs/guide/en/12-auth-and-rbac.md`
covers the caveat and the live-database workaround). Registered scoped, with `IItemUseCases` resolved
from
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
class the defaults exist to prevent. Two more members, `FacetAsync` and `AggregateAsync` (the facet-
bucket and aggregate-value computation behind `docs/guide/en/08-query-dsl.md`'s "Facets and
aggregates"), follow the identical default-throw pattern for the identical reason — a fork with its own
`IItemRepository` implementation that doesn't override them gets a clear `NotSupportedException` the
moment a caller requests `facets=`/`aggregate[<op>]=`, not a silently empty or wrong result. Sole
implementation: `SqlSugarItemRepository`
(`src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs`). Registered scoped:
`services.AddScoped<IItemRepository, SqlSugarItemRepository>()`
(`src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`). This is the seam
a fork would implement to point at a different storage engine, not a different ORM — see *ORM coupling*
above; the content entities' SqlSugar attributes stay regardless. Any replacement must implement the
three purge primitives explicitly or purge will throw for every collection.

**M2M sync strategy — diff-and-patch, not delete-and-recreate.** `SyncManyToManyAsync` (implemented by
`ManyToManySync`, below) diffs the incoming `IReadOnlyList<JunctionLink>` against the junction rows
already on file for `parentId`: rows for targets no longer present are deleted, rows for newly present
targets are inserted, and rows for targets that remain keep their own primary key and are updated
in-place rather than dropped and reinserted — a junction row's id is therefore stable across a write
that keeps its target linked. A `JunctionLink` (`src/Struo.Application/Query/Write/JunctionLink.cs`)
pairs a target id with an optional payload dictionary (CLR property names → values); a `null` payload
(`JunctionLink.Bare`) is membership-only and leaves an existing row's payload untouched. If more than
one existing row targets the same id (a duplicate left over from data written before this diff-and-patch
strategy existed), the lowest-primary-key row is kept and the rest are deleted, with one warning logged
naming the table and the count removed.

**Breaking change for a fork implementing `IItemRepository` itself**: `SyncManyToManyAsync`'s `links`
parameter changed from a plain target-id list to `IReadOnlyList<JunctionLink>` — a fork with
its own `IItemRepository` implementation (not `SqlSugarItemRepository`) must update that method's
signature and, if it wants junction-payload writes to actually take effect, apply each link's `Payload`
dictionary itself; a fork that only calls through the framework's `SqlSugarItemRepository` is
unaffected.

**Second breaking change, from the same relation-filter-pushdown work**: `IItemRepository` lost the
two single-condition id-lookup members (own-collection and translation-sidecar) that existed only to
feed the retired two-phase id-resolution filter resolver; `QueryWhereInFilteredAsync` gained a
`string? queryLocale` parameter (before its trailing `CancellationToken`) so it can resolve
translatable leaves in its `extraFilter` at that locale. A fork with its own `IItemRepository`
implementation must drop the two removed members and add the new parameter; a fork that only calls
through `SqlSugarItemRepository` is unaffected.

**Additive, but with the same catch as the purge primitives above**: `FacetAsync`/`AggregateAsync`
(previous paragraph) compile-check clean for any existing `IItemRepository` implementation, thanks to
their default-throw bodies — this is not a breaking change in the usual sense. A fork with its own
repository implementation only needs to actually override them once it wants `facets=`/
`aggregate[<op>]=` to work; until then, a request naming either gets `NotSupportedException` rather
than a silently wrong answer.

`SqlSugarItemRepository` is a facade: it keeps the `IItemRepository` members `QueryAsync`,
`GetByIdAsync`, `CreateAsync`, `UpdateAsync`, and `DeleteAsync` itself (plus the private helpers
`RunQueryAsync`, `GetByIdGenericAsync`, `CreateGenericAsync`, `UpdateGenericAsync`,
`DeleteGenericAsync`, and `CloneEntity` those methods use internally), and delegates every other
`IItemRepository` member to one of eight collaborators — `OrderByExpressionBuilder` and
`FilterTranslator` are also fields, but are used inline inside `QueryAsync`
(`orderByBuilder.BuildOrderBy(...)`, `filters.Translate(...)`) rather than delegated to — all ten
in the same `Query/` folder. Each is initialized in a field initializer from the facade's
primary-constructor parameters rather than injected as its own dependency — only
`OrderByExpressionBuilder` is also registered scoped in DI. `ManyToManySync`, `TranslationStore`, and
`FacetQueries` each get their own `new TransactionRunner(db)` instance, and `WhereInQueries`,
`FacetQueries`, and `AggregateQueries` each get their own `new FilterTranslator(...)` rather than the
facade's own `filters` field (a field initializer cannot reference another instance field — CS0236);
none of these types holds state beyond its constructor arguments, so the extra instances behave
identically to sharing one:

- `OrderByExpressionBuilder` (`OrderByExpressionBuilder.cs`) — builds `OrderBy` expressions for the
  query DSL; the only collaborator registered as a scoped DI service. Not a delegation target —
  used inline in `QueryAsync`.
- `FilterTranslator` (`FilterTranslator.cs` + `FilterTranslator.Subquery.cs`) — translates the filter
  DSL into SqlSugar conditionals, pushing relation paths and `_some`/`_none` predicates down as SQL
  subqueries. Not a delegation target — used inline in `QueryAsync`; `WhereInQueries`, `FacetQueries`,
  and `AggregateQueries` each hold their own separate instance (see above; the `FacetQueries`/
  `AggregateQueries` subsection below covers why).
- `TransactionRunner` (`TransactionRunner.cs`) — nesting-safe `InTransactionAsync` (both overloads).
- `WhereInQueries` (`WhereInQueries.cs`) — the batched `WHERE...IN` reads: `QueryWhereInAsync`,
  `QueryEntityWhereInAsync`, `QueryWhereInFilteredAsync`, and `QueryWhereInWithDeletedAsync`.
- `SoftDeleteOps` (`SoftDeleteOps.cs`) — `SoftDeleteAsync`/`RestoreAsync`, the atomic
  `UPDATE ... WHERE deletedat IS [NOT] NULL` with the version bump.
- `PurgeOps` (`PurgeOps.cs`) — the purge referential-integrity primitives, `SetForeignKeyNullAsync`
  and `DeleteByPropertyAsync`.
- `ManyToManySync` (`ManyToManySync.cs`) — `SyncManyToManyAsync`.
- `TranslationStore` (`TranslationStore.cs`) — the translation-sidecar seam: `LoadTranslationsAsync`,
  `SyncTranslationsAsync`.
- `FacetQueries` (`FacetQueries.cs` + `FacetQueries.Leaf.cs`) — `FacetAsync`; see the dedicated
  subsection below.
- `AggregateQueries` (`AggregateQueries.cs`) — `AggregateAsync`; see the dedicated subsection below.

`RepositoryHelpers` (`RepositoryHelpers.cs`) is a static helper class — `TypeNameOf`,
`TypeNameOfProperty`, `Descriptor`, `ConvertId` — used by the facade and by `WhereInQueries`,
`SoftDeleteOps`, `PurgeOps`, `ManyToManySync`, `TranslationStore`, `FacetQueries`, and
`AggregateQueries`.

`GenericDispatcher<TDelegate>`/`BiGenericDispatcher<TDelegate>` (`GenericDispatcher.cs`) are the single
implementation of the "resolve a private open generic method, cache the closed open-instance delegate
per entity type" pattern the facade and the collaborators that dispatch by entity type
(`WhereInQueries`, `SoftDeleteOps`, `PurgeOps`, `ManyToManySync`, `TranslationStore`, `FacetQueries`)
use — `FacetQueries` also needs `BiGenericDispatcher` alongside the ordinary single-type
`GenericDispatcher`, for the same reason `FilterTranslator.Subquery.cs`'s own `ProjectDispatcher`
does (dispatching on two runtime types at once — here, entity type and the facet value's CLR type —
not just one), rather than being the only collaborator that needs it. `AggregateQueries` uses
`GenericDispatcher` alone (dispatch on entity type only, since its projection carries every requested
op/field in one row regardless of their individual value types). Not `TransactionRunner` (plain C#
generics) or `OrderByExpressionBuilder` (a single reflection `GetProperty` lookup, no per-entity-type
dispatch).

### `IRelationExpander`

`src/Struo.Application/Query/IRelationExpander.cs`: `ExpandAsync(...)` expands `deep` relations for a
page of parent entities via batched follow-up queries, returning a per-parent-id map of
`relationName -> (object?|list)`. Its doc comment states it is "Implemented in Infrastructure (where the
relationship-graph descriptors and junction CLR types live)" specifically so `Struo.Application` never
references SqlSugar internals. Implementation: `RelationExpander`
(`src/Struo.Infrastructure/Query/RelationExpander.cs`). Registered scoped:
`services.AddScoped<IRelationExpander, RelationExpander>()`
(`DataServiceCollectionExtensions.cs`).

### `FilterTranslator` (not a DI seam)

`src/Struo.Infrastructure/Query/FilterTranslator.cs` (own-collection leaves, logical composition,
the SqlSugar-adjacency-defect workarounds) and `FilterTranslator.Subquery.cs` (the actual subquery
construction) together are the successor to the interface-backed cross-relation-filter resolver this
codebase used to have, restructured around SQL pushdown rather than in-memory id resolution:
`Translate(collection, filter, search, searchableFields,
queryLocale)` turns a validated `FilterNode` tree (chapter 8's grammar, including `_some`/`_none`
relation quantifiers and `_junction`) into a `List<IConditionalModel>` for **one** queryable over
`collection` — a dotted (cross-relation) condition, a relation quantifier, and a translatable-field
condition (own-collection or reached across a hop) all become a nested `IN (SELECT …)` subquery
(`RelationPredicateFilter`/`ComparisonFilter` cases in `FilterTranslator.Subquery.cs`), never an
intermediate id set. It is `internal`, not registered in DI at all — `SqlSugarItemRepository`
constructs it directly with `new FilterTranslator(db, graph, metadata, registry, options)` (four
times: its own `filters` field, plus one separate instance each for `WhereInQueries`, `FacetQueries`,
and `AggregateQueries`, since a field initializer cannot reference another instance field) exactly the
way it constructs its other Query-folder collaborators. It also needs the graph parameter to actually
be the **concrete** `RelationshipGraph`, not just an `IRelationshipGraph` — relation-subquery
construction reads junction/reverse-FK descriptor information off the concrete type that an
interface-level caller does not expose, so it casts and throws `InvalidOperationException` if handed
anything else (never actually reachable in this codebase's own DI wiring, where `IRelationshipGraph`
and `RelationshipGraph` are always bound to the same singleton instance — see `IRelationshipGraph`
above). `FacetQueries` needs the same concrete cast, for the same reason (junction/reverse-FK
descriptors for its relation-name facet form) — see the next subsection.

### `FacetQueries` / `AggregateQueries` (not a DI seam)

`src/Struo.Infrastructure/Query/FacetQueries.cs` + `FacetQueries.Leaf.cs` and
`src/Struo.Infrastructure/Query/AggregateQueries.cs` back `IItemRepository.FacetAsync`/`AggregateAsync`
— the value/count-bucket and sum/min/max/avg/count computation behind
`docs/guide/en/08-query-dsl.md`'s "Facets and aggregates". Both are `internal`, constructed directly
by `SqlSugarItemRepository` in a field initializer exactly like `FilterTranslator` above, not
registered in DI. `FacetQueries` additionally needs the **concrete** `RelationshipGraph` (its `Graph`
property casts `IRelationshipGraph` and throws `InvalidOperationException` otherwise) — a facet on a
relation name needs the same junction/reverse-FK descriptor information `FilterTranslator`'s subquery
construction does, to know whether to group by the junction's target FK (M2M) or the child's own
reverse FK (O2M).

Both hold to the same **typed-API-only** constraint the rest of the query layer does: no
`Select<T>(string)`/`GroupBy(string)`/`OrderBy(string)` or other string-accepting SqlSugar overload,
and no hand-assembled SQL text. A facet's SqlSugar call shape is `GroupBy(key)`
`.OrderBy(count, OrderByType.Desc).OrderBy(key)` `.Select(proj).Take(n)`, where `key`/`count`/`proj`
are runtime-built `Expression<Func<T, …>>` lambdas assembled by `ColumnSelectorFactory`'s facet-
specific factories (`BoxedSelector`, `CountSelector` — wrapping `SqlFunc.AggregateCount`/
`AggregateDistinctCount` — and `FacetProjection`, which projects into the generic `FacetRow<TValue>` a
facet's value type varies per field) from a runtime CLR `Type` plus a property name — never from a
string fed straight to GroupBy/OrderBy/Select themselves. `AggregateQueries`'s single-statement,
multi-field projection is the same idea one level up: `ColumnSelectorFactory.AggregateProjection`
builds one `Select(lambda)` whose members cover every requested op/field pair (chunked at
`AggregateRow.SlotCount`, 10, so a request within the default `Query:MaxAggregates` cap costs exactly
one statement), each member built from `SqlFunc.AggregateSum`/`Min`/`Max`/`Avg`/`Count` the same way.
The one place either collaborator's SQL is not entirely typed-API-composed is the same one every other
relation-crossing query in this layer uses: the "filtered root ids" a to-many facet's related/junction
side query needs are supplied through `SubQueryConditional.Wrap` over a plain `ToSql()` result — the
same wrapper `FilterTranslator.Subquery.cs`'s relation-quantifier pushdown uses, and for the identical
reason (`docs/ai/conventions.md`'s raw-SQL exception list covers this wrapper) — never SqlSugar's
typed `In(Expression, ISugarQueryable)` overload, which cannot rename the inner query's parameters and
so collides whenever two subqueries sit at the same level, as a to-many facet's own root-id subquery
can with another subquery already at that level from the request's own filter.

See `docs/guide/en/07-relations.md` and `docs/guide/en/08-query-dsl.md` for the query DSL these three
query-layer seams (`IItemRepository`, `IRelationshipGraph`, `IRelationExpander`) — plus the
non-DI-registered `FilterTranslator`/`FacetQueries`/`AggregateQueries` above — jointly implement.

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

Session revocation is a separate pair of seams, registered alongside the five above in the same
`AddStruoInfrastructure` (`src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`):
`IUserSessionStore` → `SqlSugarUserSessionStore` and `IUserSessionRevocationService` →
`UserSessionRevocationService` (both `src/Struo.Infrastructure/Identity/`). `IUserSessionStore` backs
onto the `user_sessions` table (`UserSession.cs`, one row per live cookie ticket, indexed by
`UserId`) — the index the ticket cache itself has no way to scan, needed to find "every live session
for user X". `IUserSessionRevocationService.RevokeAllForUserAsync` clears a user's cache entries and
`user_sessions` rows together, and is invoked on two triggers: password change and
`ItemService.RevokeSessionsIfUserAsync` on **both** DELETE branches (soft-delete and purge) of a
`user` row — placed in `ItemService` rather than a controller so the GraphQL `deleteUser` mutation is
covered too. Logout is a narrower, single-ticket operation: `AuthController.Logout` →
`DistributedCacheTicketStore.RemoveAsync` removes just the signed-out cookie's cache entry and index
row, without touching `IUserSessionRevocationService`. A revocation failure after a committed
delete/password-change throws
`SessionRevocationFailedException` rather than masking it as a normal success. Between triggers, a
still-live cookie is caught reactively: `AuthWiring`'s `OnValidatePrincipal` re-checks
`IUserCredentialStore`'s `IsActive` on every cookie request (mirroring what
`BearerTokenAuthenticationHandler` already does for bearer requests) and signs the session out the
moment a deactivated account's cookie is next presented.

`tests/Struo.Tests/Template/ControllerPersistenceBoundaryTests.cs` guards the boundary the identity
seams above exist to create: no file under `src/Struo.Api/Controllers/` may mention `ISqlSugarClient`.
Before these seams,
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
`src/Struo.Application/Query/QueryValidator.cs`), `IItemRepository` (the actual SqlSugar query — a
cross-relation filter is pushed down into a subquery by `FilterTranslator` inside this step, not
rewritten beforehand), and `IRelationExpander` (deep-relation batching) before `ItemProjector` (`src/Struo.Application/Query/Projection/ItemProjector.cs`) turns the
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
