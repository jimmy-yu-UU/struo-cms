# Phase 8 — GraphQL (read-only delivery API) (design)

**Date:** 2026-07-08
**Status:** approved (brainstorm); pending implementation plan
**Scope:** the first GraphQL phase — a **read-only** GraphQL API that exposes every discovered
`[CmsCollection]` as a strongly-typed, introspectable schema, sitting on top of the existing
metadata + query DSL + `ItemService` projection + RBAC surface. **Mutations (create/update/delete)
are explicitly out of scope** and deferred to a later phase (8b); writes continue via the REST
controllers. Cross-relation (dotted-path) filtering and nested-relation filter/sort/pagination are
likewise deferred. This phase adds a new `Struo.Api/GraphQl/` layer only — Domain / Application /
Infrastructure are **untouched** and gain **no new packages** (dependency rule §2).

---

## 1. Goal & positioning

StruoCMS is a reusable base template: clone it, add `[CmsCollection]` classes, get CRUD APIs by
convention (Phase 6.9). Phase 8 extends that promise to GraphQL: **adding a `[CmsCollection]` should
also yield a typed GraphQL query surface for free**, with no per-collection GraphQL code.

The audience is **both** headless content consumers (external sites / apps / frontends querying
published content, à la Directus/Contentful delivery APIs) **and** internal tooling. Both want a
typed, introspectable schema — so the schema is generated per-collection with proper object types,
not a generic JSON passthrough.

The entire read pipeline already exists and is live-verified: `ItemService.QueryAsync/GetAsync`
returns camelCase dictionaries with i18n overlay, relation expansion, permission filtering, and the
JSON-column field types (multi-value / `Json` / `KeyValue` / `Files` / `Repeater`) projected out.
GraphQL is a **new presentation layer over that pipeline** — it does not re-implement reads.

### 1.1 Non-goals (this phase)

- **Mutations** — no create/update/delete; writes stay on REST. (Deferred to Phase 8b.)
- **Cross-relation (dotted-path) filtering** (`filter[category.name]`) — deferred; Phase 8 filters
  on own fields + M2O FK ids only.
- **Nested-relation filter/sort/pagination** — nested relation fields expand only (with an optional
  `limit` on to-many). Per-nested-level filtering/sorting is deferred.
- **Field-level read restriction beyond today** — `ReadableFields` currently returns all fields;
  GraphQL honours it but adds nothing new.
- **Dynamic GraphQL enums for `Select`/`Radio` options** — options are runtime data; mapped to
  `String` for now (typed enum is a possible future enhancement).

## 2. Approach (decided)

Three approaches were weighed in the brainstorm:

- **(A, chosen) Metadata-driven dynamic schema with HotChocolate.** At startup, read
  `IMetadataProvider` and build one typed object type per collection; resolvers delegate to the
  existing scoped `ItemService`; relations are nested fields resolved via the existing
  `IRelationExpander` logic wrapped in DataLoaders; filter/sort reuse `QueryModel` + `QueryValidator`.
- (B) Generic passthrough — a single `items(collection, filter: JSON, …): JSON` query. Rejected:
  throws away typing and introspection, contradicting the "both consumers want a typed schema"
  requirement.
- (C) Static code-first types per entity POCO with SqlSugar-backed resolvers. Rejected: entities are
  host-defined and convention-discovered, and this would duplicate `ItemService`'s
  projection/permission/i18n/relation logic and break the "add a `[CmsCollection]`, get the API free"
  convention.

**Approach A** is the only option that is both strongly-typed and convention-driven, and it uniquely
reuses the already-live read pipeline.

**Library:** HotChocolate (`HotChocolate.AspNetCore`), the natural GraphQL server for this .NET 10
stack. The dynamic schema is built via HotChocolate's runtime type registration (`ITypeModule` /
runtime `ObjectType`/`InputObjectType` descriptors added from metadata at schema-build time). The
package is installed at execution time via `dotnet add package` (latest) and pinned in
`Directory.Packages.props` — **no hand-authored version string** (§17.5). No GraphQL package exists
today.

## 3. Layer & wiring

GraphQL depends on ASP.NET/HotChocolate → it is a **web concern** → it lives in `Struo.Api`, keeping
Application/Infrastructure web-free (§2). New folder `src/Struo.Api/GraphQl/`:

```
src/Struo.Api/GraphQl/
  GraphQlServiceCollectionExtensions.cs   // AddStruoGraphQl(...) — registers the server + type module
  StruoTypeModule.cs                      // reads IMetadataProvider, emits object/input types + query fields
  SchemaTypeMapper.cs                     // FieldInterface (+ CLR type) -> GraphQL type; filter-input builder
  CollectionQueryResolver.cs              // list/single resolvers -> ItemService.QueryAsync/GetAsync
  RelationResolver.cs                     // nested relation fields + file resolution (DataLoader-backed)
  DataLoaders/                            // by-FK (M2O), by-reverse-FK (O2M), by-M2M-parent, by-id (File)
  FilterInputTranslator.cs                // typed filter input -> existing FilterNode tree
  StruoErrorFilter.cs                     // domain exceptions -> GraphQL error codes
```

**`Program.cs` changes (Api only):**

- Service registration: add `builder.Services.AddStruoGraphQl()` **after** `AddStruoData(...)` (so
  `IMetadataProvider`, `IEntityRegistry`, `IRelationshipGraph`, and scoped `ItemService` are all
  resolvable; the type module reads metadata at schema-build time).
- Endpoint: add `app.MapGraphQL("/graphql")` next to `app.MapControllers()`, positioned **after**
  `PermissionResolutionMiddleware` in the pipeline so `ICurrentPermissions` is populated per request
  (same auth / CSRF / permission pipeline as REST).
- The GraphQL IDE (Nitro / Banana Cake Pop, built into HotChocolate) is enabled **only in non-production**,
  mirroring how Scalar is gated today.

The HotChocolate request scope aligns with the ASP.NET request scope, so resolvers get the same
scoped `ItemService` / `ICurrentPermissions` as REST.

## 4. Schema generation & type mapping

### 4.1 Naming

- Object type = PascalCase of the collection name (`article` → `Article`).
- Field names reuse the projection's camelCase keys.
- Per collection, two root query fields:
  - **single**: `article(id: ID!, locale: String): Article`
  - **list**: `articles(filter: ArticleFilterInput, sort: [String!], limit: Int, offset: Int,
    search: String, locale: String): ArticleList`, where `ArticleList { items: [Article!]!, total: Int! }`.
- List field name uses a simple, predictable pluralizer: `…y` → `…ies`; `…s/x/z/ch/sh` → `…es`;
  else `+ s`. Documented so developers can rely on it.
- `locale` is **always optional** (nullable); when omitted, the existing
  `explicit ?? collection-default ?? global-default` resolution applies.

### 4.2 `FieldInterface` → GraphQL type

| FieldInterface | GraphQL type | Notes |
|---|---|---|
| Text / Textarea / RichText / Markdown / Code / Slug / Email / Url / Color / Phone | `String` | |
| Number / Slider / Rating | by CLR property type: `int`→`Int`, `long`→`Long`, `decimal`/`double`→`Float` | reflected from the entity property |
| Boolean / Checkbox | `Boolean` | |
| Date / DateTime | `Date` / `DateTime` scalar | HotChocolate built-in; `Time` → `String` |
| Select / Radio | `String` | options are runtime data; dynamic enum deferred |
| MultiSelect / CheckboxGroup | `[String!]` | |
| Tags | `[TagItem!]` where `TagItem { value: String!, label: String }` | |
| Json | `JSON` scalar | free-form by nature |
| KeyValue | `JSON` scalar | `Dictionary<string,string>`; GraphQL has no map type |
| **Repeater** | recursive nested object type `[ {Collection}{Field}Item! ]` | built from `FieldMetadata.Fields`; the headline typed feature |
| File / Image (scalar `xxxId`) | `xxxId: ID` **plus** resolved `xxx: File` | see §4.3 |
| Files (list, e.g. `gallery`) | `gallery: [ID!]` **plus** resolved `galleryFiles: [File!]` | see §4.3 |
| **id** (always) | `ID!` | independent of field selection |
| **version** (AuditableEntity) | `Long` | optimistic-concurrency token |
| Password | **excluded** | Hidden; never projected |
| Hidden / Divider | **excluded** | already stripped / presentational, no data |

- Nested value types (`TagItem`, each `{Collection}{Field}Item`) are registered once and de-duplicated
  globally.
- **Exhaustiveness:** every `FieldInterface` value must have a mapping; an unmapped interface is a
  startup fail-fast (mirroring the frontend field-type registry's compile-time exhaustiveness). This
  is asserted by a schema-generation test.
- **Empty-schema guard:** GraphQL requires the root `Query` to have ≥1 field. If zero readable
  collections are discovered, a minimal root field (e.g. `_serviceCollections: [String!]!`) keeps the
  schema valid.

### 4.3 File / Image / Files resolution

File references resolve to the actual related `File` content (the user's explicit requirement), while
also keeping the raw id (no forced DB round-trip), mirroring how M2O relations expose both the FK
scalar and the nested object:

- Scalar `File`/`Image` field `xxxId` → keep `xxxId: ID` + add `xxx: File` (strip trailing `Id`).
- `Files` list field `xxx` → keep `xxx: [ID!]` + add `xxxFiles: [File!]` (append `Files`).
- `File` is itself a generated collection type (there is a `file` `[CmsCollection]`), so consumers can
  select `{ url, title, alt, width, height, … }` on it.
- Resolution uses the shared by-id DataLoader (§6); a deleted file → that entry is `null` (aligns with
  the frontend raw-id fallback). Resolution is subject to the same read-permission check as any node.
- Note: `ItemService` already resolves **translatable** Image/File fields into nested `file` objects
  (Phase 5.6); GraphQL adds the equivalent for **non-translatable** File fields — semantically
  consistent.

## 5. Query arguments (filter / sort / pagination)

All GraphQL arguments are translated back into the existing `QueryModel` inside the resolver and run
through `QueryValidator` — **defense in depth**: the schema already restricts fields, and the
whitelist re-validates.

### 5.1 Filter — typed per-collection input (own fields + FK only)

```graphql
input ArticleFilterInput {
  and: [ArticleFilterInput!]
  or:  [ArticleFilterInput!]
  id:          IdFilter
  title:       StringFilter
  status:      StringFilter
  categoryId:  IdFilter        # M2O FK
  publishedAt: DateTimeFilter
  # …one entry per filterable own field
}
```

- Shared per-scalar operator inputs (registered once), operators mapping 1:1 to the existing
  `QueryOperator` enum:
  - `StringFilter { eq neq in nin contains startsWith endsWith isNull }`
  - `IntFilter` / `FloatFilter` / `DateTimeFilter` `{ eq neq in nin lt lte gt gte isNull }`
  - `BooleanFilter { eq neq isNull }`
  - `IdFilter { eq neq in nin isNull }`
- `and`/`or` → `LogicalFilter`; a field + operator → `ComparisonFilter`. `FilterInputTranslator`
  produces the existing `FilterNode` tree.
- Filterable field set = the existing `QueryValidator` allowlist: non-hidden own fields + M2O FKs +
  `id`. Multi-value / `Json` / `KeyValue` / `Repeater` / `Files` are **not** filterable (parity with
  REST, §9).
- **Cross-relation (dotted-path / nested) filtering is deferred** (§1.1).

### 5.2 Sort

`sort: [String!]` using `field` / `-field` tokens — identical to REST, minimal generated types,
validated against the existing `Sortable` allowlist. (A typed sort enum is a possible future upgrade.)

### 5.3 Pagination

Offset-based, matching the existing DSL exactly: `limit` / `offset` arguments; the list wrapper
returns `{ items, total }`. `limit` is clamped by the existing `StruoQueryOptions.MaxLimit`. (Relay
cursor connections were considered and rejected — the repository is offset/total-based.)

## 6. Relations & N+1 avoidance (DataLoader)

Relations become natural nested fields (the point of GraphQL). Per `RelationMetadata`:

- M2O → `category: Category` (nullable single)
- O2M → `articles: [Article!]` (accepts an optional `limit`, reusing `DeepRelationSpec.Limit`)
- M2M → `tags: [Tag!]` (accepts an optional `limit`)

**Why not reuse `ItemService.ExpandDeepAsync` directly:** REST expansion batches the whole parent set
in one pass, but GraphQL resolves per-field, per-node — naïvely querying each node's relation is
textbook N+1. So nested relation fields use **HotChocolate DataLoaders**: within one request, all
`Article.category` resolutions collect their `categoryId`s and coalesce into a **single**
`id IN (...)` query, dispatched back to each node. This is exactly the batched stitching logic already
inside `IRelationExpander` (M2O collects FKs → `id IN`; O2M queries by reverse-FK `IN`, groups; M2M
via junction then `id IN`, ordered by junction sort). The existing single-relation batch logic is
**wrapped** into DataLoaders — the query logic is not rewritten. File/Image/Files resolution (§4.3)
shares a by-id DataLoader.

- Nested nodes are projected through the same `ItemService` projection (camelCase dicts) and are
  subject to per-node read permission: an unreadable relation target → `null` / empty array (parity
  with REST deep expansion).
- **Depth control:** the existing `StruoQueryOptions.MaxRelationDepth` still bounds relation traversal;
  additionally, HotChocolate's max execution depth (and optionally a complexity limit) guards against
  malicious deep/broad queries.

## 7. i18n

No new concepts — reuse the existing per-locale read semantics:

- Every query has an **optional** `locale: String` argument mapping to the `ItemService` `locale`
  parameter; omission → `explicit ?? collection-default ?? global-default` (only when the collection
  has a translation sidecar).
- Translatable fields are overlaid to the requested locale by the existing `OverlayTranslationsAsync`;
  the type schema is **not** split per-locale.
- A `translations` field exposes the existing `row["translations"]` map so headless consumers can
  fetch all locales at once, typed as `[Translation!]` where `Translation { locale: String!,
  fields: JSON! }` (per-collection translatable field sets differ → `fields` uses the JSON scalar; a
  more typed shape is a possible future enhancement).
- Nested relation nodes inherit the same query locale (parity with REST deep expansion).

## 8. Auth / RBAC & error mapping

**Same pipeline as REST.** `/graphql` sits after `UseAuthentication → UseAuthorization →
CsrfProtectionMiddleware → PermissionResolutionMiddleware`, so Cookie or Bearer (`CookieOrBearer`)
identity flows in identically and the per-request `ICurrentPermissions` snapshot is populated before
resolvers run.

**Read permission is enforced per resolver** through `ItemService` (which calls
`IPermissionService.CanRead(collection)` and `ReadableFields`):

- Public-read collections (public role grant) → queryable anonymously.
- A collection with no read grant → the query returns a GraphQL error (§8.1); an unreadable relation
  target → that nested field is `null` / empty array.
- Field-level: unreadable fields are filtered by `ReadableFields` (returns all today; reserved).
- The schema is **global** (all collections present); permission is decided **at execution time** —
  identical to REST ("all routes exposed, permission enforced in the service"). No per-user schema.

**CSRF:** read-only GraphQL is unaffected (`CsrfProtectionMiddleware` targets cookie mutating
requests only). Revisit when mutations arrive.

### 8.1 Error mapping

GraphQL bypasses the REST exception→HTTP middleware, so a `StruoErrorFilter : IErrorFilter` maps
domain exceptions to GraphQL errors carrying a stable `code` extension (no internal detail leaked):

| Domain exception | GraphQL error `code` |
|---|---|
| `PermissionDeniedException` | `FORBIDDEN` (authenticated) / `UNAUTHENTICATED` (anonymous) |
| `CollectionNotFoundException` | `NOT_FOUND` |
| `QueryException` | `BAD_USER_INPUT` |
| `ConcurrencyConflictException` / `RelationConflictException` | `CONFLICT` |
| anything else | `INTERNAL_SERVER_ERROR` (message withheld, logged server-side) |

### 8.2 Query protection

Enable HotChocolate's max execution depth and (optionally) a complexity limit to bound malicious
deep/broad queries. Introspection stays **enabled** (headless consumers need it); production
introspection gating is a future ops decision.

## 9. Testing strategy (TDD — failing test first, §17.2)

Four layers, in the existing `tests/Struo.Tests` (SQLite for automated tests), gated finally on real
Postgres (project rule: SQLite-green ≠ Postgres-correct).

1. **Schema generation (unit).** Given metadata, assert: every collection yields an object type +
   `xxx(id)` / `xxxs(...)` queries; the full `FieldInterface → GraphQL type` table (incl. Number by
   CLR type, Repeater recursive nested type, `Tags` → `[TagItem]`, `Json`/`KeyValue` → JSON scalar,
   File → `ID` + resolved `File`); relations become nested fields; filter inputs contain only own +
   FK fields; Hidden/Password/Divider fields absent. **Exhaustiveness:** every collection generates a
   type and every `FieldInterface` maps, else startup fail-fast. SDL snapshot supported.
2. **Execution / integration (HotChocolate executor + SQLite).** Execute real queries and assert
   results: list filter (each operator) / sort / limit-offset / `total` / search; single by id;
   missing id → `NOT_FOUND`; i18n with/without `locale` + the `translations` field; nested M2O/O2M/M2M
   stitching; File/Image/Files resolving to `File` nodes; permission (unreadable collection →
   `FORBIDDEN`/`UNAUTHENTICATED`, unreadable relation → `null`/empty); every error `code`; depth-limit
   rejection.
3. **DataLoader batching (unit).** With a counting repository, assert that resolving a relation across
   N parents triggers exactly **one** batched query (the N+1 regression lock).
4. **Live gate (real Postgres).** Representative GraphQL queries against live PG: list + filter +
   pagination + `total`; single; i18n overlay; all three relation kinds nested; File resolution;
   public-read anonymous access vs. a restricted collection denied. Evidence recorded per the project
   live-gate convention.

**Verification baseline continuity:** backend `dotnet build -warnaserror` clean + `dotnet test` all
green (378 + new GraphQL tests). Frontend untouched (237).

## 10. Risks & open points

- **Dynamic HotChocolate schema** is the main new complexity (runtime type registration vs. the usual
  static code-first). Mitigation: the schema is built once at startup from cached metadata; the type
  module is unit-tested; SDL snapshot pins the generated shape.
- **`Number` scalar mapping** depends on reflecting the entity CLR property type (Int/Long/Float);
  handled by the `SchemaTypeMapper` reading the entity descriptor's property type. Unmapped numeric
  CLR types fail fast.
- **`version` token type** — mapped to `Long`; confirm the CLR type during implementation.
- **Deferred scope** (mutations, cross-relation filtering, nested filter/sort/pagination, typed
  Select/Radio enums, typed `translations`) is recorded here so it isn't lost; each is a clean
  follow-up.
- **Production introspection** left enabled; flag as an ops decision, not a Phase 8 blocker.
