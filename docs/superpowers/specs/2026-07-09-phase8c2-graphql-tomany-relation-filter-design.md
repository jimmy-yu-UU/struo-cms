# Phase 8c.2 — GraphQL to-many cross-relation filter (O2M / M2M, ANY/EXISTS)

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 8c (GraphQL advanced read querying). This is the **second** 8c slice, following
> 8c.1 (M2O cross-relation filter + sort).
> **Deferred to 8c.3:** multi-level (depth > 1) relation **nesting/expansion** and nested-list
> `filter/sort/limit/offset` arguments — both require engine work across Domain/Application/Infrastructure
> (extend `DeepRelationSpec` + recurse `RelationExpander`) and are therefore a separate slice.

## 0. Summary

Expose, on the GraphQL read API, **cross-relation (dotted-path) filtering over to-many relations** —
**one-to-many (O2M)** and **many-to-many (M2M)** — with **ANY / EXISTS** semantics, by reusing the
engine and validation machinery that already resolves these paths for the REST side. Concretely:

- `articles(filter: { tags: { name: { eq: "AI" } } })` — M2M: articles that have **at least one** tag
  named "AI".
- `categories(filter: { articles: { status: { eq: "published" } } })` — O2M: categories that have **at
  least one** published article.
- `categories(filter: { children: { name: { eq: "X" } } })` — O2M self-reference.
- Multi-hop, mixing relation kinds (composes with 8c.1's M2O work):
  `categories(filter: { articles: { category: { name: { eq: "Root" } } } })`.

The change is confined to the `Struo.Api/GraphQl` layer — two source edits that relax an existing
"M2O only" guard in the schema builder and in the resolver's relation-target delegate.
**Domain / Application / Infrastructure are untouched, no new packages are added, and no sample entity
changes are required.**

## 1. Why this is almost entirely wiring, not new engine

An exploration of the read pipeline established that the *engine* for to-many cross-relation filtering
already exists and already runs for the REST path; the gap is purely the GraphQL **input surface**:

| Capability | Engine / validation status | GraphQL surface status (pre-8c.2) |
|---|---|---|
| O2M / M2M dotted-path **filter** resolution | ✅ `RelationFilterResolver.HopAsync` already handles `OneToMany` (target-child ids → reverse FK → parent ids) and `ManyToMany` (junction targetFk IN → parentFk → parent ids) at `RelationFilterResolver.cs:125-136`; the leaf → root walk rewrites the dotted `ComparisonFilter` into an own-collection `id IN (…)` (or `id IS NULL` on empty match) | ❌ `CollectionSchemaBuilder.BuildFilterInput` emits a nested `{Target}FilterInput` **only for M2O** relations (`:212-218`) |
| Dotted-path **validation** (filter) | ✅ `QueryValidator.CheckField` calls `RelationPath.Parse` with `forSort: false`, which does **not** check `IsSortable`; `RelationPath.Parse` accepts **any** relation kind (uses `graph.Resolve`, no kind restriction) | — reused unchanged |
| Filter dictionary → dotted `FilterNode` | ✅ `FilterInputTranslator.Translate` is **relation-kind-agnostic** — line 69 only asks the `relationTarget` delegate "is this key a relation of this collection, and what is its target?" | ⚠️ `CollectionResolvers.RelationTargets` (the delegate) returns a target **only for M2O relations with a foreign key** (`:72-76`) |

Both the filter engine (`RelationFilterResolver`) and the validator (`QueryValidator` → `RelationPath`)
run inside the **shared** `ItemService.QueryAsync` path that GraphQL already uses. The engine already
resolves to-many paths; the translator is already agnostic. So the only things missing are (a) the schema
input field for O2M/M2M relations, and (b) the delegate recognising O2M/M2M keys.

## 2. Semantics — ANY / EXISTS (the only semantics the engine provides)

A to-many hop resolves via `RelationFilterResolver.HopAsync`:

- **O2M** (`Category.Articles`): child (Article) rows whose leaf matches → read the reverse FK
  (`Article.CategoryId`) → the set of parent (Category) ids. A category is included iff **at least one**
  of its articles matches. This is **ANY / EXISTS**.
- **M2M** (`Article.Tags`): tag rows whose leaf matches → junction rows whose `targetFk` is in that set →
  read `parentFk` → the set of parent (Article) ids. An article is included iff **at least one** of its
  tags matches. **ANY / EXISTS**.

There is no ALL / NONE / count semantics in this slice; ANY is what the id-set walk produces and it is
not configurable. (ALL/NONE would need aggregate/anti-join engine support — out of scope, not planned.)

Empty match (e.g. `tags: { name: { eq: "does-not-exist" } }`) resolves to an empty id set →
`RelationFilterResolver` emits an `id IS NULL` leaf → the collection returns an **empty list**, not an
error — identical to 8c.1.

## 3. Scope

### In scope
1. **O2M cross-relation filter input** (including O2M self-reference), multi-hop.
2. **M2M cross-relation filter input**, multi-hop.
3. Free composition with 8c.1's M2O nested filter (mixed-kind multi-hop paths).

### Out of scope (fenced to 8c.3 unless noted)
- Multi-level relation **nesting/expansion** (depth > 1) — a relation-of-a-relation still expands empty
  (unchanged Phase 8 behavior).
- Nested-list arguments (`filter/sort/limit/offset` on a related list field).
- Cross-relation **sort** across to-many relations — **remains rejected** (`QueryValidator` → "Sort across
  to-many relations is not supported"); this slice does not touch sort and does not change that.
- ALL / NONE / count quantifier semantics (only ANY/EXISTS).
- Typed `Select`/`Radio` enums, typed read-side `translations` (unrelated deferrals).

## 4. Architecture & dependency boundary

Pure `Struo.Api/GraphQl` change. Continues the Phase 8 / 8c.1 pattern (Api-owned adapter; core untouched).
No changes to `Struo.Domain`, `Struo.Application`, `Struo.Infrastructure`. No new NuGet packages.
`Directory.Packages.props` unchanged. No `samples/*` change.

Reused unchanged:
- `RelationFilterResolver` / `IRelationFilterResolver` — dotted → `id IN (…)`, all relation kinds.
- `QueryValidator` + `RelationPath.Parse` — multi-hop whitelist validation + depth cap.
- `FilterInputTranslator` — relation-kind-agnostic dotted flattening (delegate-driven).
- `GraphQlQueryBuilder` — threads the collection + relation-target delegate into the translator (8c.1).
- `StruoErrorFilter` — domain exception → GraphQL `code`.

## 5. Change 1 — schema generation (`CollectionSchemaBuilder.BuildFilterInput`)

Today (`CollectionSchemaBuilder.cs:212-218`) only M2O relations with a foreign key contribute both an FK
operator field and a nested target filter:

```csharp
foreach (var rel in meta.Relations)
    if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
    {
        config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")));
        var targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";
        config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse(targetFilter)));
    }
```

**Change to:** M2O keeps its FK field (`{fk}: IdFilter`) plus the nested `{rel.Name}: {Target}FilterInput`;
O2M and M2M contribute **only** the nested `{rel.Name}: {Target}FilterInput` (they carry no foreign key on
this collection):

```csharp
foreach (var rel in meta.Relations)
{
    string? targetFilter = null;
    if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
    {
        config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")));
        targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";
    }
    else if (rel.Kind is RelationKind.OneToMany or RelationKind.ManyToMany)
        targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";

    if (targetFilter is not null)
        config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse(targetFilter)));
}
```

The nested field is referenced **by name** (`{Target}FilterInput`), exactly like the existing
self-referential `and`/`or` (`:198-199`) and the 8c.1 M2O nested filter — so HotChocolate resolves
recursive / self-referential / cyclic input types without a build loop.

Result on the sample schema:
- `ArticleFilterInput` gains `tags: TagFilterInput` (M2M). It keeps `category: CategoryFilterInput` +
  `categoryId: IdFilter` (8c.1 M2O).
- `CategoryFilterInput` gains `children: CategoryFilterInput` (O2M **self-reference**) and
  `articles: ArticleFilterInput` (O2M). It keeps `parent: CategoryFilterInput` + `parentId: IdFilter`
  (8c.1 M2O).
- The Category ↔ Article cycle (`CategoryFilterInput.articles → ArticleFilterInput.category →
  CategoryFilterInput`) resolves by name — no build loop.
- No name collision: relation field names (`tags`, `children`, `articles`) differ from own-field names.

An M2O relation **without** a foreign key contributes nothing (unchanged): its engine hop
(`HopAsync` M2O branch) dereferences `seg.Relation.ForeignKey!`, so a FK-less M2O nested filter must not
be offered. O2M/M2M do not use a foreign key on the declaring collection (they use the reverse FK /
junction from the graph descriptor), so they are offered unconditionally.

## 6. Change 2 — relation-target delegate (`CollectionResolvers.RelationTargets`)

Today (`CollectionResolvers.cs:72-76`) the delegate returns a target only for M2O-with-FK relations, so
the translator descends only into M2O keys. Relax it to also recognise O2M and M2M:

```csharp
private static Func<string, string, string?> RelationTargets(IMetadataProvider metadata) =>
    (coll, key) => metadata.GetCollection(coll)?.Relations
        .FirstOrDefault(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase)
            && ((r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
                || r.Kind is RelationKind.OneToMany or RelationKind.ManyToMany))
        ?.TargetCollection;
```

`FilterInputTranslator` is unchanged: for any key the delegate resolves to a target, it translates the
nested dict relative to that target and prefixes every produced field path with `<relation>.` (multi-hop
stacks). Own-field and cross-relation comparisons mix under one implicit AND, exactly as for M2O in 8c.1,
and flow into the unchanged `QueryValidator` → `RelationFilterResolver.RewriteAsync`.

### 6.1 Nested `and`/`or` inside a to-many relation filter

Because the nested relation field reuses the target's full `{Target}FilterInput`, it also exposes
`and`/`or`. If a client nests logical groups ≥ 2 deep, the **existing** Phase-2 single-logical-level
guard in `QueryValidator` (`logicalDepth >= 2` → `QueryException`) rejects it → `BAD_USER_INPUT`, exactly
as on REST and as in 8c.1 §5.1. No new validation is added.

## 7. Error handling

All mapped through the existing `StruoErrorFilter`:

| Condition | Origin | GraphQL `code` |
|---|---|---|
| Unknown relation name in a dotted path | `RelationPath.Parse` → `QueryException` | `BAD_USER_INPUT` |
| Path exceeds `MaxRelationDepth` (default 5) | `RelationPath.Parse` | `BAD_USER_INPUT` |
| Leaf field not on the terminal collection | `RelationPath.Parse` | `BAD_USER_INPUT` |
| Nested logical groups (`and/or` depth ≥ 2) | `QueryValidator` | `BAD_USER_INPUT` |
| Sort across a to-many relation (unchanged) | `QueryValidator` | `BAD_USER_INPUT` |
| Empty to-many match | `RelationFilterResolver` → `id IS NULL` leaf | returns empty list (not an error) |

## 8. Security

To-many cross-relation filtering resolves ids by **querying the target collection**
(`RelationFilterResolver` walks leaf → root through the junction / reverse FK). This is **pre-existing
REST behavior** inherited unchanged; 8c.2 adds no new exposure and RBAC behavior is identical to the REST
path. The pre-existing blind-extraction-oracle property recorded in 8c.1 §8 (filtering by a field on a
collection the caller cannot read) applies equally to to-many relations; it predates 8c.2, applies equally
to REST, and any hardening is a separate cross-cutting concern tracked outside this slice.

Introspection / Nitro remain dev-only; HotChocolate max-execution-depth still guards malicious queries;
the input recursion is bounded at runtime by `MaxRelationDepth`.

## 9. Testing strategy

- **Unit — `FilterInputTranslator`** (SQLite-free, dictionary in → `FilterNode` out): the existing M2O
  and flat-filter tests stay green as **characterization** (the translator itself does not change). Add a
  test that, with a delegate resolving an O2M/M2M key, a nested to-many filter dict produces the same
  dotted `ComparisonFilter` shape (`tags.name`, `articles.status`) — proving the delegate, not the
  translator, is the only moving part.
- **Schema** (introspect the built schema):
  - `ArticleFilterInput` has `tags` of type `TagFilterInput`, and still has `category: CategoryFilterInput`
    + `categoryId: IdFilter`;
  - `CategoryFilterInput` has `children: CategoryFilterInput` (self-reference resolves) and
    `articles: ArticleFilterInput`, and still has `parent`/`parentId`;
  - the Category ↔ Article cyclic input references resolve (schema builds without a loop).
- **Integration (SQLite)** through the real resolver + `ItemService`:
  - M2M: `articles(filter: { tags: { name: { eq } } })` returns only articles having a matching tag (ANY);
  - O2M: `categories(filter: { articles: { status: { eq } } })` returns only categories having a matching
    article (ANY);
  - O2M self-reference: `categories(filter: { children: { name: { eq } } })`;
  - multi-hop mixed kind: `categories(filter: { articles: { category: { name: { eq } } } })`;
  - empty match → empty list;
  - to-many **sort** (`sort: ["tags.name"]`) → `BAD_USER_INPUT` (unchanged, regression guard);
  - unknown relation / over-depth → `BAD_USER_INPUT`.
- **Live gate (real Postgres `web-struo-cms-db`)** — the SQLite-green ≠ Postgres-correct discipline:
  1. seed categories (with children + articles) + articles with tags;
  2. M2M `articles(filter:{tags:{name:{eq:"…"}}})` returns the right set;
  3. O2M `categories(filter:{articles:{status:{eq:"published"}}})` returns the right set;
  4. multi-hop mixed-kind filter;
  5. **CJK** tag / category name filter round-trips code-point-exact;
  6. **discrimination** — a filter value that matches nothing → `total` 0 (proves the filter filters, not a
     no-op);
  7. unknown relation path → `BAD_USER_INPUT`;
  8. empty match → empty list (not error).

## 10. Acceptance criteria (verification gate)

- `dotnet build -warnaserror` clean (0 warnings); `dotnet test` all green with the new schema + integration
  tests added (backend count rises from the 526 baseline).
- Frontend untouched (237).
- Live gate on real Postgres passes all checks in §9 with evidence (queries + responses recorded),
  including at least one CJK round-trip verified by code point, one discrimination check, and at least one
  `BAD_USER_INPUT` negative.
- Guide updated: to-many cross-relation filter examples + the ANY/EXISTS semantics note + the unchanged
  to-many-sort-not-supported limitation cross-reference.

## 11. Files expected to change

- `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` — `BuildFilterInput`: emit nested filter input for
  O2M/M2M relations (M2O unchanged).
- `src/Struo.Api/GraphQl/CollectionResolvers.cs` — `RelationTargets`: recognise O2M/M2M keys.
- `tests/Struo.Tests/GraphQl/**` — new schema + integration tests (to-many filter); translator tests
  extended (mostly characterization).
- `docs/ROADMAP.md`, `docs/guide/*` — status + usage/semantics docs.
- No `samples/*` change (Article→Tags M2M and Category→Articles/Children O2M already exist).

## 12. Notes for the plan (TDD)

- Failing test first per behavior (schema shape for O2M/M2M, integration M2M filter, integration O2M
  filter, O2M self-ref, multi-hop, negatives), then implement the two-line-scope source change.
- Keep the change purely additive: existing M2O and flat-filter tests must stay green as characterization
  (no behavior change for M2O-only or own-field-only filters).
- Verify the recursive/cyclic input-type build against HotChocolate v16 (the Category ↔ Article cycle and
  the `children` self-reference must resolve — the `and`/`or` and 8c.1 M2O by-name patterns are the
  precedent).
- The engine is believed complete for to-many filter (it runs for REST), but the live gate on real
  Postgres is the authority — if a latent to-many resolution bug exists (SQLite-green ≠ Postgres-correct),
  the live gate catches it and any fix would land in `Struo.Infrastructure` (out of the nominal Api-only
  scope, tracked as a live-gate fix like prior phases).
