# Phase 8c.3b — Nested-list `filter` / `sort` / `limit` / `offset` arguments (GraphQL + REST envelope)

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 8c (GraphQL advanced read querying). This is the **second** slice of the deferred
> 8c.3 work, following 8c.3a (multi-level depth > 1 relation nesting/expansion). It completes the pair the
> roadmap's 8c.3 originally bundled: (A) multi-level nesting — shipped in 8c.3a — and (B) nested-list
> `filter/sort/limit/offset` arguments — **this spec**.
> **Preceding 8c slices:** 8c.1 (M2O cross-relation filter + sort), 8c.2 (to-many cross-relation filter),
> 8c.3a (multi-level nesting). This slice reuses their engine machinery (`RelationFilterResolver`,
> `QueryValidator`/`RelationPath`, `FilterInputTranslator`, the recursive `DeepSpec`) rather than building new.

## 0. Summary

Let a client shape a **nested to-many relation list** — not just select it. On both the GraphQL read API
and the REST `deep` JSON envelope, a related **list** field (one-to-many or many-to-many) gains four
optional controls:

- **`filter`** — a full cross-relation (dotted-path) predicate on the target collection (reuses the
  8c.1/8c.2 `RelationFilterResolver` engine): own-fields + `_and`/`_or` + relation paths.
- **`sort`** — `[String!]` tokens (`field`, `-field`) over the target's **own** fields.
- **`limit`** — a **per-parent** top-N cap.
- **`offset`** — a **per-parent** skip, applied before `limit`.

Concretely:

- GraphQL: `article(id) { tags(filter: { name: { _contains: "AI" } }, sort: ["name"], limit: 5) { name } }`
- GraphQL: `categories { items { articles(filter: { status: { _eq: "published" } }, limit: 3, offset: 0) { title } } }`
- REST: `GET /api/items/article?deep={"tags":{"filter":{"name":{"_contains":"AI"}},"sort":["name"],"limit":5}}`

Today (post-8c.3a) a nested list returns **all** rows, unfiltered/unsorted/unpaginated;
`DeepRelationSpec.Limit` is parsed from the REST envelope but **never consumed**. After 8c.3b the four
controls apply at **every** nesting level, up to `MaxRelationDepth`.

The change is a shared **engine** change spanning **Domain / Application / Infrastructure** plus GraphQL
argument wiring and REST envelope parsing in the Api/Application layers. **No new NuGet packages, no
`samples/*` change.** The `{Target}FilterInput` GraphQL types it needs already exist (built for top-level
+ 8c.2 nested filter inputs).

## 1. Where each control attaches (current → change)

| Layer | Current (post-8c.3a) behaviour | Change for 8c.3b |
|---|---|---|
| Deep request shape (`Domain/Query/DeepSpec.cs`) | `DeepRelationSpec(Fields, Limit, Deep)` — `Limit` present but unused; no `Filter`/`Sort`/`Offset` | **add** `FilterNode? Filter`, `IReadOnlyList<SortField>? Sort`, `int? Offset`; **consume** `Limit` |
| REST deep parse (`Application/Query/QueryParser.ParseDeepObject`) | reads `fields`, `limit`, nested `deep` | **also** read `filter` (→ `ParseFilter`), `sort` (→ `ParseSortToken`), `offset` |
| Deep validation (`Application/Query/ItemService.ValidateDeepTree`) | validates nesting depth + relation names | **also** validate: to-many-only (reject args on M2O), nested-filter paths (whitelist), nested-sort own-field-only, `limit`/`offset` ≥ 0 |
| Engine expansion (`Infrastructure/Query/RelationExpander.ExpandAsync`) | batched fetch-all + group per parent; no filter/sort/limit/offset | **push** `filter` into the batched WHERE (via `RelationFilterResolver` rewrite); apply `sort`/`limit`/`offset` **in-memory per group** |
| Item repository (`Application/Query/IItemRepository`) | `QueryWhereInAsync(collection, prop, values)` — no extra predicate | **add** a batched "WHERE prop IN values **AND** extraFilter" method (or an optional-filter overload) |
| GraphQL relation fields (`CollectionSchemaBuilder`) | O2M/M2M field emits `[Target!]` with **no arguments** | **add** `filter: {Target}FilterInput`, `sort: [String!]`, `limit: Int`, `offset: Int` on O2M/M2M fields only |
| GraphQL selection → deep (`CollectionResolvers.BuildDeep`) | emits `DeepRelationSpec(null, null, nested)` per selected relation | **read** the selection's arguments → translate `filter` (`FilterInputTranslator`, target collection), parse `sort`, read `limit`/`offset` into the spec |
| HotChocolate max-execution-depth | `AddMaxExecutionDepthRule(12, …)` | **unchanged** |

## 2. Chosen approach (execution strategy)

Two strategies were considered for the per-parent `limit`/`offset` ("top-N-per-group"):

- **A. Hybrid — push `filter` to SQL; apply `sort`/`limit`/`offset` in-memory per group.** ✅ **Chosen.**
  `filter` is a **uniform** predicate across all parents on a page, so it ANDs cleanly into the existing
  batched `WHERE … IN (parentIds)` query (one query per relation-node, unchanged). `sort`/`limit`/`offset`
  are inherently **per-parent** and are applied after the batched fetch + group, in memory.
  - **Pros:** preserves the N+1-safe batched invariant (one query per relation-node per level, locked by
    the 8c.3a query-count test); reuses the whitelist-validated filter engine
    (`RelationFilterResolver` + `ConditionalModelTranslator`); **no vendor SQL / no window functions**, so
    it honours CLAUDE.md §17.4 and dodges audit D9 (raw ORDER-BY subqueries are PG/SQLite-shaped).
  - **Cons (accepted, documented):** at high cardinality the batched query fetches all *filtered* children
    for the page's parents before the in-memory `offset`/`limit` trim — an over-fetch relative to a true
    per-partition SQL top-N. Bounded by clamping an explicit `limit` to `[1, MaxLimit]`; acceptable for a
    CMS delivery API at typical cardinalities.
- **B. Full SQL push-down (`ROW_NUMBER() OVER (PARTITION BY parent ORDER BY …)` / LATERAL).** ❌ Rejected.
  No over-fetch, best at scale, **but** requires raw vendor SQL (violates §17.4 "all DB access via SqlSugar
  ORM, zero vendor SQL"), is PG/SQLite-shaped and unverified on MySQL/SqlServer/Oracle (D9), and the M2M
  junction hop makes the window function substantially more complex. The project rules effectively forbid it.
- **C. Per-parent queries.** ❌ Rejected outright — reintroduces N+1 and breaks the 8c.3a query-count invariant.

## 3. Scope

### In scope
1. **`filter` / `sort` / `limit` / `offset` arguments on to-many (O2M / M2M) nested relation list fields**,
   on both GraphQL and the REST `deep` JSON envelope, at **every** nesting level up to `MaxRelationDepth`.
2. **`filter`** = full cross-relation dotted-path predicate on the target collection (reuses
   `RelationFilterResolver.RewriteAsync` + `QueryValidator`/`RelationPath` whitelist validation), own-fields
   + `_and`/`_or` + relation paths.
3. **`sort`** = `[String!]` tokens over the target's **own** fields (asc / `-`desc), multi-key.
4. **`limit`** (per-parent top-N; explicit → clamp `[1, MaxLimit]`) and **`offset`** (per-parent skip ≥ 0,
   before limit).
5. Recursive **validation** (to-many-only, whitelist filter paths, own-field sort, non-negative
   limit/offset) mapping to `BAD_USER_INPUT`.
6. N+1-safe batched execution preserved (filter pushed into the existing per-relation-node query; no new
   per-parent query).
7. Guide + roadmap docs.

### Out of scope (fenced)
- **Cross-relation `sort` on a nested list** (e.g. sorting `article.tags` by a field of a relation of
  `tags`) — **rejected** as `BAD_USER_INPUT`, consistent with the existing to-many sort restriction
  (8c.1/8c.2) and D9. Nested sort is own-field only.
- **`filter`/`sort`/`limit`/`offset` on an M2O (single-object) relation field** — not applicable;
  GraphQL does not declare the arguments on M2O fields, and the REST envelope rejects them
  (`BAD_USER_INPUT`).
- **A default `limit` when omitted** — omitting `limit` returns **all** rows (preserves the 8c.3a nested-list
  contract and its live-verified tests; no silent truncation of existing consumers). Only an explicit
  `limit` paginates.
- **REST query-string `deep=a,b` nesting/args** — stays depth-1, flat, no args (no syntax for it; none
  invented). Args are offered only via the richer JSON envelope and GraphQL.
- **Total/hasMore metadata for a nested list** (a `XList`-style `{items,total}` wrapper on nested lists) —
  nested lists stay bare `[Target!]`; a per-parent nested total is a separate enhancement.
- Typed read-side `translations`, typed Select/Radio enums, ALL/NONE/count semantics — unrelated deferrals.

## 4. Architecture & dependency boundary

Shared-engine change (same layering as 8c.3a):

- **Domain** — `DeepRelationSpec` gains `FilterNode? Filter`, `IReadOnlyList<SortField>? Sort`, `int? Offset`.
  Pure data; `FilterNode`/`SortField` are existing Domain types; no external packages (§2).
- **Application** — `QueryParser.ParseDeepObject` reads the new envelope keys; `ItemService.ValidateDeepTree`
  gains the per-level arg validation (reusing `QueryValidator`/`RelationPath`); `IItemRepository` gains the
  batched-with-filter fetch method. Depends only on Domain.
- **Infrastructure** — `RelationExpander.ExpandAsync` consumes the new fields (filter rewrite + in-memory
  sort/limit/offset); gains an `IRelationFilterResolver` dependency and a `locale` parameter; the repository
  impl (`SqlSugarItemRepository`) implements the new batched-with-filter method via
  `ConditionalModelTranslator`.
- **Api (GraphQL)** — `CollectionSchemaBuilder` declares the four arguments on O2M/M2M relation fields;
  `CollectionResolvers.BuildDeep` reads them per selection into `DeepRelationSpec` (filter via
  `FilterInputTranslator` against the target collection).

No new NuGet packages. `Directory.Packages.props` unchanged. No `samples/*` change (reuse `Article↔Tag`
M2M and `Category` O2M/self-ref).

Reused unchanged: `RelationFilterResolver.RewriteAsync`, `QueryValidator`/`RelationPath.Parse`,
`FilterInputTranslator` (metadata-aware, with prefix), `ConditionalModelTranslator`, `SortField`,
`StruoQueryOptions.MaxLimit`, `StruoErrorFilter`, the recursive `DeepSpec` walker, RBAC/whitelist.

## 5. Change 1 — deep-spec gains filter/sort/offset (Domain, `DeepSpec.cs`)

```csharp
/// <summary>
/// Per-relation deep-expansion options: an optional field whitelist for the expanded target rows,
/// an optional nested-list <see cref="Filter"/> (cross-relation, resolved against the target
/// collection), an optional own-field <see cref="Sort"/>, per-parent <see cref="Limit"/>/<see cref="Offset"/>,
/// and an optional nested <see cref="DeepSpec"/> for multi-level (depth &gt; 1) expansion.
/// Filter/Sort/Limit/Offset apply to to-many (O2M/M2M) list relations only; they are null for M2O.
/// </summary>
public sealed record DeepRelationSpec(
    IReadOnlyList<string>? Fields,
    FilterNode? Filter,
    IReadOnlyList<SortField>? Sort,
    int? Limit,
    int? Offset,
    DeepSpec? Deep = null);
```

Positional-record signature change touches every construction site — `QueryParser.ParseDeepObject`,
`QueryParser.ParseDeepQueryString` (passes `null` for all new fields), `CollectionResolvers.BuildDeep`, and
existing tests. This is the intended engine surface change; all call sites are updated. `DeepSpec` itself
is unchanged (still a map). `Deep` keeps its `= null` default so multi-level call sites read naturally.

## 6. Change 2 — REST envelope parsing (Application, `QueryParser.ParseDeepObject`)

Extend the per-relation object parse to read four more optional keys, reusing existing helpers:

```
deep: {
  "tags": {
    "filter": { "name": { "_contains": "AI" } },
    "sort":   ["name", "-createdAt"],
    "limit":  5,
    "offset": 0,
    "fields": ["id","name"],
    "deep":   { ... }          // multi-level still works; args allowed at each level
  }
}
```

- `filter` (object) → `ParseFilter(...)` (the same parser used for top-level filters, so `_and`/`_or` and
  dotted relation keys compose exactly as at the root).
- `sort` (array of strings) → `ParseSortToken(...)` each.
- `limit` / `offset` (int) → `int?`.

`ParseDeepQueryString` (`deep=a,b`) stays flat/depth-1/no-args.

## 7. Change 3 — recursive validation (Application, `ItemService.ValidateDeepTree`)

At each tree node, for each relation spec, in addition to the existing name + depth checks:

- **To-many only:** if the relation is **M2O** and any of `Filter`/`Sort`/`Limit`/`Offset` is non-null →
  `QueryException($"Filter/sort/limit/offset are only supported on to-many relations; '{relName}' on '{coll}' is many-to-one.")`.
  (On GraphQL the arguments do not exist on M2O fields, so this primarily guards the REST envelope.)
- **Filter whitelist:** if `Filter` is non-null, validate it against the **target collection** via the
  existing `QueryValidator` (own-field + dotted relation paths resolve through `RelationPath.Parse` against
  the target's metadata graph). Unknown field/relation → `QueryException` → `BAD_USER_INPUT`.
- **Own-field sort:** if `Sort` is non-null, each token's field must be a **non-relation own field** of the
  target collection (reuse the same check that rejects to-many sort at the root). A dotted / relation sort
  token → `QueryException("Sort across relations is not supported for nested lists")`.
- **Bounds:** `Limit` (when present) and `Offset` (when present) must be `>= 0` → else `QueryException`.
  (`limit == 0` is treated as "no limit" for parity with the top-level `Limit == 0 → default/all` convention;
  the plan pins the exact convention and locks it with a test.)

All validation runs **before any query executes** and independent of result-set size (the 8c.3a discipline:
an over-depth / bad-arg request is rejected even on a zero-row parent match). Validation stays in Application
(pure); the async filter **rewrite** (DB round-trips) stays in Infrastructure (§8).

## 8. Change 4 — engine execution (Infrastructure, `RelationExpander.ExpandAsync`)

`ExpandAsync` gains an `IRelationFilterResolver` dependency and a `locale` parameter (threaded from the
top-level read so translatable-field filters rewrite at the correct locale). Per relation node with a
to-many kind:

1. **Filter push-down.** If `spec.Filter is not null`, call
   `RelationFilterResolver.RewriteAsync(rel.TargetCollection, spec.Filter, locale, ct)` → a pure
   own-collection `FilterNode?` (dotted paths already collapsed to `id IN (…)` conditions). Translate it to
   `ConditionalModel`s and AND it into the batched fetch:
   - **O2M:** the existing `WHERE reverseFk IN (parentIds)` **AND** rewritten-filter — one batched query.
   - **M2M:** the target query `WHERE id IN (targetIds)` **AND** rewritten-filter — the junction query is
     unchanged; the filter narrows the target rows.
   This needs one new `IItemRepository` method: a batched "WHERE `property` IN `values` AND `extraFilter`"
   (implemented in `SqlSugarItemRepository` via the existing `ConditionalModelTranslator`; `null` filter =
   today's plain `QueryWhereInAsync`, so existing callers are unaffected).
2. **Group per parent.** Unchanged (O2M group-by reverse FK; M2M order by junction then map).
3. **In-memory sort / offset / limit, per parent group** (applied to the target **entities** before/at
   projection, so sort keys read real CLR values via `readProp`):
   - **sort:** if `spec.Sort` is non-empty, stable multi-key ordering by the named own-fields (asc/desc);
     else the existing default (O2M: fetch order; M2M: junction sort — preserved).
   - **offset/limit:** `group.Skip(offset).Take(limit)` when `limit > 0`, else `group.Skip(offset)`.
4. **Recurse** into `spec.Deep` over the (already trimmed) distinct target entities — unchanged 8c.3a
   breadth-first batched recursion. (Trimming before recursing means a deeper level only expands the rows
   that survived this level's limit — the intuitive and cheaper semantics.)

### 8.1 N+1 / batching invariant (locked by test)
Pushing `filter` into the existing per-relation-node query adds **no** query; `sort`/`limit`/`offset` are
pure in-memory. So the follow-up query count is still the relation-node count of the tree, independent of
row counts and independent of whether args are present. The 8c.3a query-count test is **extended** to assert
this with args in play.

## 9. Change 5 — GraphQL arguments (Api, `CollectionSchemaBuilder` + `CollectionResolvers`)

- **Schema (`CollectionSchemaBuilder.BuildObjectType`, the relation-field loop):** for each **O2M / M2M**
  relation field (currently emitted as `[Target!]` with no args), add four arguments:
  `filter: {Target}FilterInput`, `sort: [String!]`, `limit: Int`, `offset: Int`. The `{Target}FilterInput`
  type is the one already built for the root/8c.2 nested filter surface (referenced by name → self-ref /
  cyclic types resolve). **M2O relation fields get no arguments** (single object).
- **Selection walker (`CollectionResolvers.BuildDeep`):** for each selected relation, read the selection's
  argument values (filter / sort / limit / offset) off the `ISelection` (HotChocolate v16 — argument values
  per selected field; the plan pins the exact API, e.g. `ISelection.Arguments` / coerced literals). Then:
  - `filter` → `FilterInputTranslator.Translate(filterDict, rel.TargetCollection, RelationTargets(metadata))`
    → `FilterNode?` (metadata-aware, fresh prefix at the target collection — the same translator 8c.1/8c.2
    use, so dotted cross-relation keys work).
  - `sort` → `GraphQlQueryBuilder.ParseSort(sortTokens)`.
  - `limit` / `offset` → `int?`.
  - Emit `DeepRelationSpec(Fields: null, Filter, Sort, Limit, Offset, Deep: nested)`.
  The aliased-duplicate "first wins per level" dedup is preserved (an aliased duplicate with different args
  is an edge case; first-wins is the documented, consistent rule).

`GraphQlQueryBuilder.BuildQuery` is unchanged (it already threads the pre-built `DeepSpec?`).

## 10. Error handling

All mapped through the existing `StruoErrorFilter` (GraphQL) / global exception handling (REST):

| Condition | Origin | Code |
|---|---|---|
| Args on an M2O relation (REST envelope) | `ItemService.ValidateDeepTree` → `QueryException` | `BAD_USER_INPUT` / 400 |
| Nested `filter` references an unknown field/relation of the target | `QueryValidator`/`RelationPath` → `QueryException` | `BAD_USER_INPUT` / 400 |
| Nested `sort` token is a relation / dotted path | `ItemService.ValidateDeepTree` → `QueryException` | `BAD_USER_INPUT` / 400 |
| Negative `limit` / `offset` | `ItemService.ValidateDeepTree` → `QueryException` | `BAD_USER_INPUT` / 400 |
| GraphQL nested `filter` names an unknown input field | HotChocolate v16 query validation | GraphQL validation error (earlier + stronger, as in 8c.2) |
| Empty nested match after filter | `RelationExpander` (O2M/M2M produce `[]`) | not an error (empty list) |

## 11. Security

Unchanged exposure surface vs. 8c.3a. Every nested level still projects through the same `projectTarget`
metadata projection (field whitelist, hidden/readable). The nested `filter` is whitelist-validated through
the same `QueryValidator`/`RelationPath` machinery as root filters (§17.7 — no ORM leak, field/relation
paths validated), so a nested filter cannot reference a non-whitelisted column or inject a raw path. The
per-target-collection RBAC read-gate question is the **same pre-existing property** as Phase 8 / 8c.3a deep
expansion — not introduced or changed here. The in-memory `offset`/`limit` trim and the `MaxLimit` clamp
bound per-parent result size; the batched query is still bounded by the page of parents. Introspection /
Nitro remain dev-only.

## 12. Testing strategy

- **Unit — deep-spec construction** (`DeepRelationSpec`): new fields default sensibly; existing 3-arg-style
  construction is updated; a characterization test pins that an all-null spec == 8c.3a behaviour.
- **Unit — REST envelope parse** (`QueryParser`): `deep` object with `filter`/`sort`/`limit`/`offset`
  parses into the spec; multi-level with args at two levels; query-string form still flat/no-args.
- **Unit — validation** (`ItemService.ValidateDeepTree`, no DB): args on M2O → throw; unknown nested-filter
  path → throw; relation/dotted nested-sort token → throw; negative limit/offset → throw; a valid
  own-field-filter + own-field-sort + limit/offset passes.
- **Integration — engine (SQLite, real `RelationExpander` + `RelationFilterResolver` + repository +
  `ItemService`)**:
  - nested O2M `filter` narrows the list (e.g. `category.articles(filter: status eq published)`);
  - nested M2M `filter` narrows the list (`article.tags(filter: name contains …)`);
  - nested cross-relation `filter` on the list (dotted path resolves via `RelationFilterResolver`);
  - nested `sort` orders the list (asc + desc, multi-key);
  - nested `limit`/`offset` paginate **per parent** (seed two parents with different child counts; assert
    each parent's list is independently trimmed — not a global limit);
  - **N+1 invariant with args**: follow-up query count == relation-node count, independent of row counts
    and unchanged by the presence of `filter`/`sort`/`limit`/`offset` (extend the 8c.3a query-count test);
  - trim-before-recurse: a nested `deep` under a limited list only expands surviving rows.
- **Integration — REST envelope**: `deep={"tags":{"filter":{...},"sort":["name"],"limit":2}}` round-trips
  filtered/sorted/limited; args on an M2O relation → 400; bad filter path → 400.
- **GraphQL — schema + execution**: the schema exposes `filter`/`sort`/`limit`/`offset` on O2M/M2M relation
  fields and **not** on M2O fields (schema assertion); an execution-spy confirms the selection's args reach
  the `DeepRelationSpec`; a real-engine execution test round-trips a nested filtered+sorted+limited list;
  existing arg-less nested selections stay green (characterization).
- **Live gate (real Postgres `web-struo-cms-db`)** — SQLite-green ≠ Postgres-correct discipline:
  1. seed a category with several articles (mixed status) + an article with several tags (incl. CJK names);
  2. GraphQL nested `filter` narrows a to-many list on real PG (control row absent → proves real filtering);
  3. nested `sort` orders on real PG (asc + desc);
  4. nested `limit`/`offset` trim **per parent** (a second parent with a different child set trims
     independently);
  5. **CJK** value in a nested filter / result round-trips code-point-exact;
  6. args on an M2O relation (REST envelope) → `BAD_USER_INPUT`;
  7. bad nested-filter path → `BAD_USER_INPUT`;
  8. REST nested envelope with `filter`+`sort`+`limit` resolves on real PG;
  9. arg-less nested selection still returns all rows (8c.3a back-compat) on real PG.

## 13. Acceptance criteria (verification gate)

- `dotnet build -warnaserror` clean (0 warnings); `dotnet test` all green with the new tests added
  (backend count rises from the **549** post-8c.3a baseline).
- Frontend untouched (**237**).
- The N+1 batching invariant test passes **with args present** (query count == relation-node count,
  independent of row count and of args).
- Live gate on real Postgres passes all §12 checks with evidence (queries + responses recorded), including:
  a nested filter that discriminates (control row absent), independent per-parent `limit`/`offset`, ≥1 CJK
  round-trip by code point, the M2O-args and bad-path `BAD_USER_INPUT` negatives, and the arg-less
  back-compat case.
- Guide updated: nested-list `filter`/`sort`/`limit`/`offset` examples (GraphQL args + REST envelope), the
  per-parent `limit`/`offset` + `MaxLimit` semantics, the omitted-limit-returns-all back-compat note, and
  the "nested sort is own-field only / M2O has no args" rules.

## 14. Files expected to change

- `src/Struo.Domain/Query/DeepSpec.cs` — `DeepRelationSpec` gains `Filter`/`Sort`/`Offset`.
- `src/Struo.Application/Query/QueryParser.cs` — `ParseDeepObject` reads the new envelope keys.
- `src/Struo.Application/Query/ItemService.cs` — `ValidateDeepTree` per-level arg validation; thread `locale`
  into the expander call.
- `src/Struo.Application/Query/IItemRepository.cs` — batched "WHERE prop IN values AND extraFilter" method.
- `src/Struo.Infrastructure/Query/RelationExpander.cs` — consume filter (rewrite + push-down) + in-memory
  sort/limit/offset; `IRelationFilterResolver` dependency + `locale` param.
- `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` — implement the batched-with-filter method.
- `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` — declare the four arguments on O2M/M2M relation fields.
- `src/Struo.Api/GraphQl/CollectionResolvers.cs` — `BuildDeep` reads the selection's args into the spec.
- `tests/Struo.Tests/**` — unit + integration + GraphQL + REST tests per §12.
- `docs/ROADMAP.md`, `docs/guide/*` — status + usage docs.
- No `samples/*` change.

## 15. Notes for the plan (TDD)

- Failing test first per behaviour: envelope parse of the four keys; validation rejections (M2O args, bad
  filter path, relation sort token, negative bounds); engine nested filter (O2M, M2M, cross-relation);
  engine nested sort (asc/desc/multi-key); engine per-parent limit/offset (two parents, independent trims);
  N+1 query-count invariant **with args**; trim-before-recurse; REST envelope round-trip; GraphQL schema
  arg presence (and M2O absence); GraphQL execution round-trip; arg-less back-compat characterization.
- Keep changes additive: existing arg-less nested GraphQL/REST tests and the 8c.3a flat/nested `deep` tests
  stay green as characterization (all-null new fields == 8c.3a). The `DeepRelationSpec` signature change is
  the one broad mechanical edit — update every construction site in the same commit.
- Pin the `limit == 0` convention explicitly (treat as "no limit", mirroring the top-level `Limit`
  convention) and lock it with a test so the meaning is unambiguous.
- Confirm the HotChocolate v16 API for reading a **sub-selection's** argument values during `BuildDeep`
  (this is the one genuinely new HC-surface interaction; everything else reuses shipped machinery). If the
  arg-reading API differs from the root resolver's `ctx.ArgumentValue<T>`, isolate it behind a small helper
  and unit-test it.
- The engine change is core (Domain/App/Infra); the live gate on real Postgres is the authority for any
  SQLite-green ≠ Postgres-correct latent bug (e.g. filter push-down SQL shape, in-memory sort key coercion
  across id/enum types, junction ordering interacting with an explicit sort). Any such fix lands in
  Infrastructure and is tracked as a live-gate fix like prior phases.
