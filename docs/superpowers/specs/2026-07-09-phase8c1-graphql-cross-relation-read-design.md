# Phase 8c.1 — GraphQL cross-relation read (dotted-path filter + cross-relation sort)

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 8c (GraphQL advanced read querying). This is the **first** 8c slice.
> **Deferred to 8c.2:** to-many (O2M/M2M) cross-relation filtering, nested-list `filter/sort/limit/offset`
> arguments, and multi-level (depth > 1) relation **nesting** (expansion).

## 0. Summary

Expose, on the GraphQL read API, **cross-relation (dotted-path) filtering** over **many-to-one (M2O)
relations, multi-hop**, and **lock in cross-relation sorting** — both by reusing engine and validation
machinery that already exists for the REST side. Concretely:

- `articles(filter: { category: { name: { eq: "Tech" } } })` — filter a collection by a field on its
  M2O target.
- `articles(filter: { category: { parent: { name: { eq: "Root" } } } })` — multi-hop, through an M2O
  chain (`category.parent.name`).
- `articles(sort: ["category.name"])` — sort by an M2O target field (already routed through the engine;
  this slice verifies, tests, and documents it).

The change is confined to the `Struo.Api/GraphQl` layer plus making one translator metadata-aware.
**Domain / Application / Infrastructure are untouched and no new packages are added.**

## 1. Why this is mostly wiring, not new engine

An exploration of the read pipeline established that the *engine* for cross-relation querying already
exists and is multi-hop; the gap is purely the GraphQL **input surface**:

| Capability | Engine / validation status | GraphQL surface status (pre-8c.1) |
|---|---|---|
| Cross-relation (dotted) **filter** | ✅ `RelationFilterResolver.RewriteAsync` rewrites every dotted `ComparisonFilter` into an own-collection `id IN (…)` (or `id IS NULL` for empty match); multi-hop, all relation kinds | ❌ `BuildFilterInput` emits only own-field operator inputs + M2O **FK** columns (`categoryId: IdFilter`); the translator never synthesizes a dotted `FieldPath` |
| Cross-relation **sort** (to-one) | ✅ `SqlSugarItemRepository.RelationOrderExpr` builds a correlated subquery with one JOIN per hop (`category.name`, `category.parent.name`) | ⚠️ `sort:[String!]` tokens already pass through untyped; behavior is unverified/untested/undocumented |
| Dotted-path **validation** | ✅ `QueryValidator.CheckField` → `RelationPath.Parse` (multi-hop, depth-capped by `MaxRelationDepth`, leaf-field check, sort requires all-M2O `IsSortable`) | — reused unchanged |

Both engines run inside the **shared** `ItemService.QueryAsync` path that GraphQL already uses
(`QueryValidator.Validate` → `relationFilter.RewriteAsync` → `repository.QueryAsync` → `Project` →
`ExpandDeepAsync` → `OverlayTranslationsAsync`). So the only thing missing for filter is a GraphQL input
that produces a dotted `FieldPath`; for sort, nothing is missing but verification.

## 2. Scope

### In scope
1. **M2O cross-relation filter input**, multi-hop, via nested typed relation filter inputs.
2. **Cross-relation sort** (to-one, multi-hop) — verified, tested, documented; no schema change.

### Out of scope (fenced to 8c.2 unless noted)
- To-many (O2M / M2M) cross-relation **filter** (`category.articles.title` → ANY/EXISTS semantics).
- Nested-list arguments (`filter/sort/limit/offset` on a related list field).
- Multi-level relation **nesting/expansion** (depth > 1) — a relation-of-a-relation still expands empty
  (unchanged Phase 8 behavior).
- Typed `Select`/`Radio` enums, typed read-side `translations` (unrelated deferrals).

## 3. Architecture & dependency boundary

Pure `Struo.Api/GraphQl` change plus a metadata-aware translator. Continues the Phase 8 pattern
(Api-owned adapter; core untouched). No changes to `Struo.Domain`, `Struo.Application`,
`Struo.Infrastructure`. No new NuGet packages. `Directory.Packages.props` unchanged.

Reused unchanged:
- `RelationFilterResolver` / `IRelationFilterResolver` — dotted → `id IN (…)` rewrite.
- `QueryValidator` + `RelationPath.Parse` — multi-hop whitelist validation + depth cap.
- `SqlSugarItemRepository.RelationOrderExpr` — to-one cross-relation ORDER BY.
- `StruoErrorFilter` — domain exception → GraphQL `code`.

## 4. Schema generation — `CollectionSchemaBuilder.BuildFilterInput`

Today (`CollectionSchemaBuilder.cs:210-212`) each M2O relation with a foreign key already contributes a
FK operator field:

```csharp
foreach (var rel in meta.Relations)
    if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
        config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")));
```

**Add**, for each such M2O relation, a nested relation filter field typed as the **target collection's**
`{Target}FilterInput`, referenced **by name** (exactly like the existing self-referential `and`/`or`
fields at `:198-199`, so HotChocolate resolves recursive/self-referential input types without a build
loop):

```csharp
foreach (var rel in meta.Relations)
    if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
    {
        config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")));       // kept
        var targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";              // new
        config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse(targetFilter)));
    }
```

> `RelationMetadata.TargetCollection` (a `string`) is the target collection name; the
> `SchemaTypeMapper.TypeName(rel.TargetCollection)` pattern is already used verbatim at
> `CollectionSchemaBuilder.cs:109` to type the relation's **object** field, so the filter-input field
> reuses the exact same naming and is guaranteed consistent with the object type.

Result on the sample schema:
- `ArticleFilterInput` gains `category: CategoryFilterInput` (the FK field `categoryId: IdFilter` stays).
- `CategoryFilterInput` gains `parent: CategoryFilterInput` (**self-reference**) → `category.parent.name`
  multi-hop composes for free.
- No name collision: relation field name (`category`) ≠ FK field name (`categoryId`).
- O2M / M2M relations contribute **no** filter field in this slice.

> O2M / M2M relations contribute no filter field in this slice (M2O only).

## 5. Translator — `FilterInputTranslator` becomes metadata-aware

`FilterInputTranslator.Translate` currently flattens a GraphQL filter dictionary into a `FilterNode`
tree with **flat** field keys. It must now recurse into relation-named keys, accumulating a dotted path
prefix, so a nested relation filter becomes a dotted `ComparisonFilter`.

The recursion carries two extra pieces of state: `pathPrefix` (string, default empty) and
`currentCollection` (the collection whose relations the current dictionary's keys are resolved against).
Per key:

- `and` / `or` → recurse the group with the **same** `currentCollection` and `pathPrefix` (existing
  behavior, unchanged semantics).
- key is an **M2O relation** of `currentCollection` → recurse into the nested dictionary with
  `pathPrefix' = pathPrefix + key + "."` and `currentCollection' = <relation target collection>`.
- otherwise (own field or FK) → emit `ComparisonFilter(pathPrefix + field, op, value)` — identical to
  today except for the prefix.

Relation-name resolution comes from collection metadata threaded in from `GraphQlQueryBuilder.BuildQuery`
(which already holds the collection metadata). The existing **null-operator skip** guard
(`FilterInputTranslator.cs:81`) is preserved verbatim — HotChocolate materializes every declared
operator field as null, and unset ones must be skipped or a single `{ eq: x }` explodes into one
`ComparisonFilter` per operator.

The output remains a normal `FilterNode` tree: own-field comparisons and dotted (cross-relation)
comparisons mixed under the same `and`/`or` level. It flows into the **unchanged** `QueryValidator`
(which validates the dotted paths via `RelationPath.Parse`) → `RelationFilterResolver.RewriteAsync`
(dotted → `id IN (…)`).

### 5.1 Nested `and`/`or` inside a relation filter

Because the nested relation field reuses the target's full `{Target}FilterInput`, it also exposes
`and`/`or`. If a client uses them inside a relation filter and the result flattens to a logical group
nested ≥ 2 deep, the **existing** Phase-2 single-logical-level guard in `QueryValidator`
(`logicalDepth >= 2` → `QueryException`) rejects it → `BAD_USER_INPUT`, exactly as on REST. No new
validation is added; this is defense reused.

## 6. Sort

No schema change. GraphQL exposes `sort: [String!]` tokens that already pass through
`CollectionResolvers` → `GraphQlQueryBuilder` → `ItemService` → `QueryValidator`
(`RelationPath.Parse`, requiring `IsSortable` = all segments M2O) → `RelationOrderExpr`. This slice:

- Adds regression tests that `sort: ["category.name"]` (and a multi-hop `category.parent.name`) validate
  and execute.
- Confirms the same behavior on the live-gate (real Postgres).
- Documents in the guide the **D9 caveat**: `RelationOrderExpr` builds hand-assembled raw ORDER-BY SQL
  whose identifier quoting/casing is **PostgreSQL/SQLite-shaped** (injection-defended but not portable to
  MySQL/SqlServer/Oracle), and that **sort across to-many relations is not supported**
  (`QueryValidator` → "Sort across to-many relations is not supported").

## 7. Error handling

All mapped through the existing `StruoErrorFilter`:

| Condition | Origin | GraphQL `code` |
|---|---|---|
| Unknown relation name in a dotted path | `RelationPath.Parse` → `QueryException` | `BAD_USER_INPUT` |
| Path exceeds `MaxRelationDepth` (default 5) | `RelationPath.Parse` | `BAD_USER_INPUT` |
| Sort across a to-many relation | `QueryValidator` | `BAD_USER_INPUT` |
| Nested logical groups (`and/or` depth ≥ 2) | `QueryValidator` | `BAD_USER_INPUT` |
| Leaf field not on the terminal collection | `RelationPath.Parse` | `BAD_USER_INPUT` |
| Empty cross-relation match | `RelationFilterResolver` → `id IS NULL` leaf | returns empty list (not an error) |

## 8. Security

Cross-relation filtering resolves ids by **querying the target collection** (`RelationFilterResolver`
walks leaf → root). This is **pre-existing REST behavior** inherited unchanged; 8c.1 adds no new exposure
and RBAC behavior is identical to the REST path. One pre-existing property is recorded (not addressed in
this slice): filtering by a field on a collection the caller cannot read could act as a blind-extraction
oracle. It predates 8c.1, applies equally to REST, and any hardening is a separate cross-cutting concern
tracked outside this slice.

Introspection/Nitro remain dev-only; HotChocolate max-execution-depth still guards malicious queries;
the input recursion is bounded at runtime by `MaxRelationDepth`.

## 9. Testing strategy

- **Unit — `FilterInputTranslator`** (SQLite-free, dictionary in → `FilterNode` out):
  - nested relation dict → single dotted `ComparisonFilter` (`category.name`);
  - multi-hop → `category.parent.name`;
  - own-field + nested mixed under one implicit AND;
  - `and`/`or` flattening preserved with a dotted child;
  - null-operator skip still holds inside a nested relation input.
- **Schema** (introspect the built schema):
  - `ArticleFilterInput` has `category` of type `CategoryFilterInput` and still has `categoryId: IdFilter`;
  - `CategoryFilterInput` has `parent: CategoryFilterInput` (self-reference resolves).
- **Integration (SQLite)** through the real resolver + `ItemService`:
  - `articles(filter: { category: { name: { eq } } })` returns only matching rows;
  - multi-hop `category.parent.name`;
  - `sort: ["category.name"]` orders correctly; `sort: ["category.parent.name"]` too;
  - unknown relation / over-depth / to-many sort → `BAD_USER_INPUT`.
- **Live gate (real Postgres `web-struo-cms-db`)** — the SQLite-green ≠ Postgres-correct discipline:
  1. seed categories (with a parent chain) + articles;
  2. `articles(filter:{category:{name:{eq:"…"}}})` returns the right set;
  3. multi-hop `category.parent.name` filter;
  4. `sort:["category.name"]` (asc/desc) ordering verified;
  5. **CJK** category name filter round-trips code-point-exact;
  6. unknown relation path → `BAD_USER_INPUT`;
  7. empty match → empty list (not error).

## 10. Acceptance criteria (verification gate)

- `dotnet build -warnaserror` clean (0 warnings); `dotnet test` all green with the new unit/schema/
  integration tests added (backend count rises from the 513 baseline).
- Frontend untouched (237).
- Live gate on real Postgres passes all checks in §9 with evidence (queries + responses recorded),
  including at least one CJK round-trip verified by code point and at least one `BAD_USER_INPUT` negative.
- Guide updated: cross-relation filter examples + the D9 sort caveat + the to-many-sort limitation.

## 11. Files expected to change

- `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` — `BuildFilterInput`: add M2O relation filter field.
- `src/Struo.Api/GraphQl/FilterInputTranslator.cs` — metadata-aware recursive flattening with dotted prefix.
- `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs` — thread collection metadata into the translator call.
- `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs` — extended.
- `tests/Struo.Tests/GraphQl/**` — new schema + integration tests (cross-relation filter/sort).
- `docs/ROADMAP.md`, `docs/guide/*` — status + usage/caveat docs.
- No `samples/*` change required (Article→Category(→parent) M2O chain already exists).

## 12. Notes for the plan (TDD)

- Failing test first per behavior (translator recursion, schema shape, integration filter, integration
  sort, negatives), then implement.
- Keep the translator change purely additive: existing flat-filter tests must stay green as
  characterization (no behavior change for own-field-only filters).
- Verify the recursive input-type build against HotChocolate v16 (self-referential `CategoryFilterInput`
  must resolve — the `and`/`or` by-name pattern is the precedent).
