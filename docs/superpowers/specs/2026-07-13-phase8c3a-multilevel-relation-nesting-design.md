# Phase 8c.3a — Multi-level (depth > 1) relation nesting / expansion (GraphQL + REST envelope)

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 8c (GraphQL advanced read querying). This is the **first** slice of the deferred
> 8c.3 work, following 8c.1 (M2O cross-relation filter + sort) and 8c.2 (to-many cross-relation filter).
> **Split from 8c.3:** the roadmap's 8c.3 bundled two capabilities — (A) multi-level (depth > 1) relation
> nesting/expansion and (B) nested-list `filter/sort/limit/offset` arguments. They are independent enough,
> and (B) carries a real "top-N-per-group" architectural risk, so they are split. **This spec is 8c.3a =
> (A) only.** (B) is deferred to **8c.3b**.

## 0. Summary

Expose, on both the GraphQL read API **and** the REST `deep` JSON envelope, **multi-level (depth > 1)
relation expansion** — a relation of a relation, to arbitrary depth up to `MaxRelationDepth`. Today
relation expansion stops at depth 1: a client can select `article { category { name } }` but
`article { category { parent { name } } }` returns `parent` empty (Phase 8 behaviour). After 8c.3a the
nested `parent` (and any deeper chain, including self-referential cycles like `category.parent.parent`)
resolves.

Concretely:

- GraphQL: `article(id) { category { parent { parent { name } } } }` resolves the full chain.
- GraphQL: `categories { items { children { articles { title } } } }` (O2M → O2M → own-field).
- REST: `GET /api/items/article?deep={"category":{"deep":{"parent":{}}}}` (nested envelope).

The change is a shared **engine** change (recursive `DeepRelationSpec` + recursive `RelationExpander`)
spanning **Domain / Application / Infrastructure**, plus a GraphQL selection-tree builder and a REST
envelope parser recursion in the Api/Application layers. **No GraphQL schema-shape change** (each
collection object type already exposes its relation fields returning other collection object types /
lists — Phase 8 only truncated the *resolution* at depth 1, not the *selection surface*). **No new NuGet
packages, no `samples/*` change.**

## 1. Why the schema needs no change (and where the depth-1 truncation lives)

An exploration of the read pipeline established:

| Layer | Current (depth-1) behaviour | Change for 8c.3a |
|---|---|---|
| GraphQL object type relation fields (`CollectionSchemaBuilder.BuildObjectType`, `:106-110`) | Relation field emits `Target` (M2O) or `[Target!]` (O2M/M2M); the resolver reads the value **pre-nested by ItemService deep expansion** from the parent dict — it does not care how deep | **unchanged** (the selection surface already permits arbitrary depth) |
| GraphQL selection → deep request (`CollectionResolvers.SelectionRelations`, `:82-110`) | Returns a **flat list** of relation names at the immediate child level only | **recurse** into nested relation selections → build a nested `DeepSpec` tree |
| Deep request shape (`Domain/Query/DeepSpec.cs`) | `DeepRelationSpec(Fields, Limit)` — no nested relations | **add** nested `DeepSpec? Deep` (recursive) |
| Engine expansion (`Infrastructure/Query/RelationExpander.ExpandAsync`) | Expands each relation **one level**, projects target rows via `projectTarget(...)`, then stops | **recurse** — after projecting a level's target rows, expand their sub-relations (batched per level) and merge |
| Depth validation (`Application/Query/ItemService.ExpandDeepAsync`, `:208-224`) | Caps on **relation count** (`deep.Relations.Count > MaxRelationDepth`); validates each name flatly | validate **actual nesting depth** ≤ `MaxRelationDepth`; validate each name at its level |
| REST deep parse (`Application/Query/QueryParser.ParseDeepEnvelope`, `:45-62`) | Flat `deep: { rel: { fields, limit } }` | **recurse** into a nested `deep` key |
| HotChocolate max-execution-depth (`GraphQlServiceCollectionExtensions`, `:48`) | `AddMaxExecutionDepthRule(12, …)` | **unchanged** — 12 comfortably accommodates `MaxRelationDepth` (default 5) hops |

## 2. Chosen approach (of three considered)

- **A. Recurse in `RelationExpander` (Infrastructure); `DeepRelationSpec` gains a nested `DeepSpec`.** ✅ **Chosen.**
  Change is localised; reuses the existing M2O/O2M/M2M branch logic; the same metadata projection
  (field whitelist, hidden/readable rules) is applied at every level automatically via the existing
  `projectTarget` delegate.
- **B. Iterative level-by-level expansion driven by `ItemService` (Application).** Rejected: `ItemService`
  discards the target **entities** once projected to dicts, so the expander would have to return entities
  as well as dicts — a dirtier interface for no benefit.
- **C. SqlSugar `.Includes()` eager loading.** Rejected: the expander deliberately avoids `.Includes()`
  so that related rows honour the same metadata projection (whitelist / hidden rules) and N+1 batching as
  top-level rows. `.Includes()` would bypass both. (Consistent with the existing `RelationExpander`
  design remarks and CLAUDE.md §17.4.)

## 3. Scope

### In scope
1. **Multi-level (depth > 1) relation expansion** on GraphQL, all relation kinds (M2O / O2M / M2M),
   mixed-kind chains, and self-referential cycles (`category.parent.parent`, `category.children.children`)
   up to `MaxRelationDepth`.
2. **REST `deep` JSON envelope nesting** (`deep: { rel: { deep: { subRel: {} } } }`) — same engine.
3. Recursive **depth validation** (nesting depth, not relation count) and per-level relation-name
   validation, mapping to `BAD_USER_INPUT`.
4. N+1-safe batched recursion (one follow-up query per relation-node in the tree, independent of the
   number of rows at each level).

### Out of scope (fenced)
- **Nested-list `filter/sort/limit/offset` arguments** on a related list field — **8c.3b**. (The `Limit`
  field on `DeepRelationSpec` stays present but **unused**, exactly as today; 8c.3b will use it.)
- Cross-relation **filter** / **sort** at the *root* — already shipped (8c.1 / 8c.2), unchanged here.
- REST **query-string** `deep=a,b` nesting — stays depth-1 (flat comma list); nesting is offered **only**
  via the richer JSON envelope. (The query-string form has no syntax for nesting and none is invented.)
- Any per-target-collection RBAC read-gate on deep-expanded rows — this is a pre-existing property (see §8),
  not introduced or changed by 8c.3a.
- ALL/NONE/count semantics, typed enums, typed read-side `translations` — unrelated deferrals.

## 4. Architecture & dependency boundary

Shared-engine change (the first 8c slice to touch core, by design — 8c.1/8c.2 were Api-only wiring):

- **Domain** — `DeepRelationSpec` gains a nullable recursive `DeepSpec? Deep`. Pure data; no external
  packages (Domain stays package-free per §2 dependency rule).
- **Application** — `ItemService.ExpandDeepAsync` recursive depth/name validation; `QueryParser`
  recursive envelope parse. Depends only on Domain.
- **Infrastructure** — `RelationExpander.ExpandAsync` recursive expansion. Already depends on
  `IItemRepository` + `RelationshipGraph`.
- **Api (GraphQL)** — `CollectionResolvers` recursive selection→`DeepSpec`; `GraphQlQueryBuilder`
  signature takes `DeepSpec?`.

No new NuGet packages. `Directory.Packages.props` unchanged. No `samples/*` change.

Reused unchanged: `IItemRepository` batch methods (`QueryWhereInAsync`, `QueryEntityWhereInAsync`),
`RelationshipGraph` descriptors, the `projectTarget` metadata projection delegate, `StruoErrorFilter`,
the GraphQL object-type relation-field resolvers, HotChocolate max-execution-depth (12), RBAC/whitelist.

## 5. Change 1 — recursive deep-spec (Domain, `DeepSpec.cs`)

```csharp
/// <summary>
/// Per-relation deep-expansion options: an optional field whitelist for the expanded target rows,
/// an optional limit (reserved — used by 8c.3b nested-list args), and an optional nested
/// <see cref="DeepSpec"/> for multi-level (depth > 1) expansion of the target's own relations.
/// </summary>
public sealed record DeepRelationSpec(
    IReadOnlyList<string>? Fields, int? Limit, DeepSpec? Deep = null);

public sealed record DeepSpec(IReadOnlyDictionary<string, DeepRelationSpec> Relations);
```

`Deep` defaults to `null` so every existing construction site (`new DeepRelationSpec(fields, limit)`,
`new DeepRelationSpec(null, null)`) compiles unchanged and keeps depth-1 semantics. `DeepSpec` itself is
unchanged (already a map); recursion rides on the per-relation `Deep`.

## 6. Change 2 — recursive engine expansion (Infrastructure, `RelationExpander.ExpandAsync`)

`ExpandAsync` becomes recursive. The existing per-kind branches (M2O / O2M / M2M) are preserved; after a
branch has resolved a relation's target rows for the page, a **post-projection recursion** runs:

1. The branch already produces, per parent, the projected target dict(s). Retain, alongside each
   projected dict, the **target entity** it came from (the branches already have the entities in hand —
   `byId` for M2O, `children`/`grouped` for O2M, `targets` for M2M).
2. If `spec.Deep is not null`, gather the **distinct target entities** for this relation across the whole
   page, and recurse: `ExpandAsync(rel.TargetCollection, distinctTargets, spec.Deep, projectTarget,
   parentId, readProp, ct)`. This is **one batched call per relation-node**, so the query count is the
   number of relation-nodes in the tree, independent of row counts (N+1-safe, breadth-first per level).
3. Merge the recursion's result (keyed by target id) into each target dict: for each nested relation
   name, set `targetDict[nestedRelName] = nestedValue`. The projected dicts are the concrete mutable
   `Dictionary<string, object?>` that `projectTarget` returns (verified: `ItemService.ExpandDeepAsync`
   already casts the projection to `Dictionary<string, object?>`), so the merge is an in-place add.

The `parentId` / `readProp` / `projectTarget` delegates are collection-agnostic (they reflect off the
runtime entity type / route to the target collection's metadata projection), so they are reused verbatim
at every level. The recursion terminates because the `DeepSpec` tree is finite (built from a finite
selection set / finite envelope) and depth is capped by §7.

### 6.1 N+1 / batching invariant (locked by test)
For a page of `P` parents and a nested spec expanding relation `r` (with a nested relation `s` on the
target), the engine issues: 1 query for `r`'s targets (across all `P` parents) + 1 query for `s`'s
targets (across all of `r`'s target rows) — **not** `P` queries and not per-row queries. A test asserts
the follow-up query count equals the relation-node count of the tree, not a function of the row count.

## 7. Change 3 — recursive depth & name validation (Application, `ItemService.ExpandDeepAsync`)

Replace the current relation-**count** cap with a recursive **nesting-depth** cap and per-level name
validation:

- Compute the tree depth of `deep` (a leaf relation = depth 1; a relation with a nested `Deep` = 1 + max
  child depth). If it exceeds `options.MaxRelationDepth`, throw
  `QueryException($"Relation nesting too deep (…); the maximum is {MaxRelationDepth}.")`.
- Walk the tree: at each node, resolve the relation name against **that node's collection** graph
  (root uses `collection`; a nested node uses its parent relation's `TargetCollection`). Unknown name →
  `QueryException($"Unknown relation '{relName}' on '{coll}'.")`. This validates before any query runs
  (the expander also throws on an unknown descriptor as a backstop — the messages agree).

**Documented behaviour change:** the old cap rejected *more than `MaxRelationDepth` relations in one
request regardless of nesting* (e.g. a flat request selecting 6 sibling relations threw when
`MaxRelationDepth = 5`). The new cap measures nesting depth, so 6 sibling relations at depth 1 are now
allowed. This is the intended, more-correct semantics. Breadth is not separately capped: it is bounded
by the schema's relation count per collection and, for GraphQL, by HotChocolate's max-execution-depth
(12); REST envelopes are explicitly authored. (No separate breadth cap is added — YAGNI; recorded here so
the absence is a decision, not an oversight.)

## 8. Change 4 — GraphQL selection recursion (Api, `CollectionResolvers` + `GraphQlQueryBuilder`)

- Add `internal static DeepSpec? SelectionDeepSpec(IResolverContext ctx, string collection,
  IMetadataProvider metadata)` that recursively walks the selection tree:
  - Determine the element object type + its child selections exactly as `SelectionRelations` does today
    (handling the direct-element vs `XList { items }`-wrapped cases).
  - For each child selection whose `Field.Name` is a relation of `collection` (case-insensitive; keep the
    existing aliased-duplicate `Distinct` dedup **per level**), resolve the relation's `TargetCollection`
    and target object type (`s.Field.Type.NamedType()`), recurse with `ctx.GetSelections(targetType, s)`
    to build the child `DeepSpec?`, and emit `DeepRelationSpec(Fields: null, Limit: null, Deep: childSpec)`.
  - Return `null` when no relations are selected (unchanged empty-case behaviour).
- `GraphQlQueryBuilder.BuildQuery` takes `DeepSpec? deep` instead of `IReadOnlyList<string>
  requestedRelations` (it currently maps the flat list into a flat `DeepSpec`; now it receives the
  already-built nested spec). The two resolver call sites (`ResolveSingle`, `ResolveList`) call
  `SelectionDeepSpec(...)` instead of `SelectionRelations(...)`.
- `Fields` stays `null` per level (as in Phase 8): HotChocolate filters the output dict to the actual
  selection, so per-relation field pruning is not needed for correctness. (Over-projection is unchanged
  from depth-1 and not a regression.)

`SelectionRelations` may be retained if any test references it, or replaced; the plan decides. The
aliased-duplicate rationale (HC keeps aliased duplicates as distinct selections with the same
`Field.Name`) is preserved at every level.

## 9. Change 5 — REST envelope recursion (Application, `QueryParser.ParseDeepEnvelope`)

Make `ParseDeepEnvelope` recursive: for each relation object, in addition to `fields` and `limit`, read
an optional nested `deep` object and recurse to build the child `DeepSpec`:

```
deep: {
  "category": { "fields": ["id","name"], "deep": { "parent": { "deep": { "parent": {} } } } },
  "tags": {}
}
```

The query-string form (`ParseDeepQueryString`, `deep=a,b`) is **unchanged** (flat, depth-1). Only the
JSON envelope gains nesting.

## 10. Error handling

All mapped through the existing `StruoErrorFilter` (GraphQL) / global exception handling (REST):

| Condition | Origin | Code |
|---|---|---|
| Unknown relation name at any level | `ItemService.ExpandDeepAsync` (pre-validate) / `RelationExpander` (backstop) → `QueryException` | `BAD_USER_INPUT` / 400 |
| Nesting depth > `MaxRelationDepth` | `ItemService.ExpandDeepAsync` → `QueryException` | `BAD_USER_INPUT` / 400 |
| GraphQL query exceeds HC max-execution-depth (12) | HotChocolate | GraphQL validation error (backstop; our depth cap fires first for depths ≤ 12) |
| Empty related set at a level | `RelationExpander` (O2M/M2M produce `[]`, M2O `null`) | not an error (empty list / null) |

## 11. Security

Every level projects through the same `projectTarget` metadata projection, so the field whitelist and
hidden/readable rules apply identically at depth N as at depth 1. Deep expansion reads rows from related
collections; whether the caller has an explicit RBAC **read** grant on each *target* collection is the
**same pre-existing property** as Phase 8 depth-1 expansion and the 8c.1 §8 blind-extraction note — it
predates 8c.3a, applies equally to REST, and any hardening is a separate cross-cutting concern tracked
outside this slice. 8c.3a adds **depth**, not a new exposure class. Introspection / Nitro remain
dev-only; HotChocolate max-execution-depth (12) and `MaxRelationDepth` (5) bound the recursion.

## 12. Testing strategy

- **Unit — depth/name validation** (`ItemService.ExpandDeepAsync`, no DB): a depth-`(MaxRelationDepth+1)`
  nested spec throws `QueryException` (→ 400); an unknown nested relation name throws; a valid
  depth-`MaxRelationDepth` spec passes validation.
- **Unit — deep-spec construction**: `DeepRelationSpec` default `Deep = null` keeps existing call sites
  compiling and depth-1 behaviour intact (characterization).
- **Integration — engine (SQLite, real `RelationExpander` + repository + `ItemService`)**:
  - depth-2 M2O (`article.category.parent` → nested parent projected);
  - depth-2 O2M (`category.children.<own-field>` and `category.articles.<own-field>`);
  - depth-2 M2M (`article.tags.<own-field>` then a nested relation on the tag if available, else a
    mixed-kind chain `category.articles.tags`);
  - mixed-kind multi-hop (`category.articles.category.name`);
  - self-referential cycle to depth 3 (`category.parent.parent`);
  - **N+1 invariant**: assert the follow-up query count equals the relation-node count, independent of
    the number of rows (seed multiple rows per level and confirm the count does not grow with rows).
- **Integration — REST envelope**: `deep={"category":{"deep":{"parent":{}}}}` round-trips the nested
  parent; unknown nested relation → 400; over-depth → 400.
- **GraphQL — schema + execution**: a nested selection (`{ category { parent { name } } }`) produces a
  nested `DeepSpec` (translator/selection unit test) and round-trips through the real resolver
  (execution test); an over-depth selection → `BAD_USER_INPUT` (or HC depth error as backstop);
  existing depth-1 selections stay green (characterization).
- **Live gate (real Postgres `web-struo-cms-db`)** — SQLite-green ≠ Postgres-correct discipline:
  1. seed a category chain (root → child → grandchild) + articles with tags;
  2. GraphQL depth-2/3 M2O chain (`article { category { parent { parent { name } } } }`) resolves the
     full chain;
  3. GraphQL depth-2 O2M (`categories { items { children { name } } }`) resolves;
  4. mixed-kind multi-hop resolves;
  5. **CJK** name at a nested level round-trips code-point-exact;
  6. self-referential cycle (`category.parent.parent`) resolves without loop;
  7. over-depth nested selection → `BAD_USER_INPUT`;
  8. REST nested envelope (`deep={"category":{"deep":{"parent":{}}}}`) resolves on real PG.

## 13. Acceptance criteria (verification gate)

- `dotnet build -warnaserror` clean (0 warnings); `dotnet test` all green with the new tests added
  (backend count rises from the 534 baseline).
- Frontend untouched (237).
- The N+1 batching invariant test passes (query count = relation-node count, independent of row count).
- Live gate on real Postgres passes all §12 checks with evidence (queries + responses recorded),
  including ≥1 CJK round-trip verified by code point, the self-referential cycle, and the over-depth
  `BAD_USER_INPUT` negative.
- Guide updated: multi-level nesting examples (GraphQL nested selection + REST nested envelope), the
  `MaxRelationDepth` cap, and the depth-vs-count semantics change note.

## 14. Files expected to change

- `src/Struo.Domain/Query/DeepSpec.cs` — `DeepRelationSpec` gains `DeepSpec? Deep = null`.
- `src/Struo.Application/Query/ItemService.cs` — `ExpandDeepAsync` recursive depth/name validation.
- `src/Struo.Application/Query/QueryParser.cs` — `ParseDeepEnvelope` recursion (query-string unchanged).
- `src/Struo.Infrastructure/Query/RelationExpander.cs` — `ExpandAsync` recursive expansion + merge.
- `src/Struo.Api/GraphQl/CollectionResolvers.cs` — `SelectionDeepSpec` recursive selection→`DeepSpec`.
- `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs` — `BuildQuery` takes `DeepSpec?`.
- `tests/Struo.Tests/**` — unit + integration + GraphQL + REST tests per §12.
- `docs/ROADMAP.md`, `docs/guide/*` — status + usage docs.
- No `samples/*` change (Article↔Category↔Tag + Category self-reference already exist).

## 15. Notes for the plan (TDD)

- Failing test first per behaviour: recursive depth validation (over-depth → throw), engine depth-2 M2O
  expansion, engine depth-2 O2M, self-ref cycle, N+1 query-count invariant, REST nested envelope, GraphQL
  nested selection→spec, GraphQL depth-2 execution round-trip, over-depth negative.
- Keep changes additive: existing depth-1 GraphQL/REST tests and the flat-`deep` tests stay green as
  characterization (`Deep = null` default; count-cap behaviour change is the one intentional exception —
  update or replace the specific count-cap test).
- Verify the recursion is genuinely batched (breadth-first per level), not per-parent — the N+1 invariant
  test is the guard.
- The engine change is core (Domain/App/Infra); the live gate on real Postgres is the authority for any
  SQLite-green ≠ Postgres-correct latent bug (e.g. id-type equality across levels, junction ordering at
  depth). Any such fix lands in Infrastructure and is tracked as a live-gate fix like prior phases.
