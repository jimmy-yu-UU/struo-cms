# StruoCMS — Phase 3b (Cross-Relation Filter/Sort) Design

> Date: 2026-06-26
> Source of truth: the StruoCMS master spec (§7.6, §7.7, §8, §18). Covers **Phase 3b only**.
> Phase 3 split: **3a (merged)** = relations foundation; **3b (this doc)** = cross-relation
> multi-level filter/sort. **Spike-first** — the SqlSugar sort mechanism is validated before the plan.

## 1. Goal & scope

Enable the query DSL to filter and sort across relations using dotted field paths
(`category.name`, `category.parent.name`, `tags.name`). Paths resolve through the
`IRelationshipGraph` built in Phase 3a. Phase 2 left dotted `FieldPath`/`Field` in the
AST but rejected them with HTTP 400 (`QueryValidator.cs:17`); 3b replaces that rejection
with graph-based validation and execution.

**In scope (3b):**
- **To-one filter** on a relation path (`category.name _eq 'News'`), any depth ≤ cap.
- **To-one sort** (`sort=-category.name`), path must be entirely to-one.
- **To-many EXISTS filter** (`tags.name _eq 'C#'` → rows having ≥1 matching child), for
  O2M and M2M segments, any depth ≤ cap.
- Multi-level paths bounded by `StruoQueryOptions.MaxRelationDepth` (existing, default 5).
- Relation-path leaf conditions accept the full Phase-2 `QueryOperator` set.
- Composition with the existing single-level `_and`/`_or` groups and own-collection
  conditions, lossless.

**Out of scope (deferred / rejected):**
- **To-many sort** or aggregates (e.g. sort by tag count) → rejected.
- **Sort on a path containing any to-many segment** → 400.
- Cross-relation `search` term (search stays on the collection's own searchable fields).
- Nested logical groups deeper than one level (unchanged Phase-2 limitation).
- Inline nested-entity create (Phase 3a deferral), i18n (Phase 4), files (Phase 5).

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Engine | **Approach A (hybrid)**: filters via two-phase id-resolution (`id IN (…)`); sort via JOIN/correlated-subquery (spike-chosen) |
| Capabilities | to-one filter + to-one sort + to-many EXISTS filter; **to-many sort rejected** |
| Path depth | multi-level, bounded by `MaxRelationDepth` |
| Spike | yes — settle the **sort** mechanism (and sanity-check `Subqueryable` for filters) before the plan |
| Empty match | render `id IS NULL` (`EqualNull`) as a portable always-false leaf — never `IN ()` |
| SQL surface | zero vendor SQL; all hops are SqlSugar `Queryable`/`ConditionalModel` |

## 3. Spike (throwaway, not merged)

Run before the implementation plan. Against SQLite + the Blog sample, in a scratch
location (deleted afterward):

1. **to-one sort** via `OrderBy(string)` hosting a correlated subquery built from column
   names — confirm SqlSugar accepts it and it renders portably on SQLite **and** PostgreSQL.
2. **fallback**: a narrow `LeftJoin` used only for the sort path.
3. **`SqlFunc.Subqueryable<Child>().Where(...).Any()`** for a to-many filter — confirm
   whether it is clean enough to optionally also drive filters in one statement.
4. **multi-level to-one sort** (`category.parent.name`) — the riskiest case.

**Output:** spike findings recorded in the plan; the chosen sort mechanism fixed. If
nested correlated subqueries prove unreliable, sort depth is reduced to a single hop and
that reduction is recorded as a decision (deeper sort paths then 400).

## 4. Layer placement (§2)

- **Domain**: unchanged. `FilterNode.FieldPath` and `SortField.Field` are already strings
  that may be dotted (Phase 2 AST). `QueryException` reused.
- **Application**:
  - `RelationPath` helper — parse a dotted path into ordered relation segments + a leaf
    field; resolve each segment via `IRelationshipGraph`; classify each segment kind
    (to-one / to-many); validate depth and the terminal leaf field; expose
    `IsSortable` (true iff every segment is to-one).
  - `QueryValidator.Validate(...)` gains an `IRelationshipGraph` parameter; dotted-path
    rejection is replaced with `RelationPath` validation.
- **Infrastructure**:
  - Relation-filter resolver — two-phase leaf→root id resolution producing a root-id set;
    reuses the Phase-3a batched-`IN` pattern (`QueryEntityWhereInAsync` / `RelationExpander`).
  - New repository method `QueryIdsAsync(collection, leafCondition)` for the leaf step.
  - `BuildOrderBy` extended to handle to-one dotted sort via the spike-chosen mechanism.
- **Api**: no endpoint change — transport already accepts dotted filter/sort; 3b stops
  rejecting them.

## 5. Filter composition mechanism

Each dotted filter condition resolves to a single
`ConditionalModel { FieldName = "id", ConditionalType = In, FieldValue = "<root ids>" }`.
Because this is itself a valid `ConditionalModel` leaf, it slots **in place** of the
original leaf inside its single-level `_and`/`_or` group — composing losslessly with the
Phase-2 pipeline and own-collection conditions.

- **Empty result set** (no matches anywhere up the chain): emit `id IS NULL`
  (`ConditionalType.EqualNull`) instead of an illegal `IN ()`. Always false for a PK,
  portable across SQLite/PostgreSQL. Under `_and` the group is false; under `_or` the
  branch contributes nothing — exactly EXISTS semantics.

Because resolution requires async DB round-trips, a pre-pass walks the filter AST,
resolves every dotted leaf into an id set, and hands the translator a substitution map;
the translator emits the `id IN (…)` / `id IS NULL` leaf when it encounters a dotted node.

## 6. Two-phase id-resolution algorithm (filter)

Path `r1.r2…rn.leaf` rooted at collection R. Compute the leaf set, then walk back toward
the root, one batched query per hop.

- **Leaf**: in terminal collection T, query ids where `leaf <op> value` → set `S`.
  Reuses the existing translator to build the leaf `ConditionalModel`, selecting only id
  (`QueryIdsAsync`).
- **Each hop back** (relation direction declared from the root's perspective):
  - **to-one (M2O, FK on parent)** — e.g. `category` on Article: parent ids =
    `Article.id where Article.categoryId IN S`.
  - **to-many (O2M, FK on child)** — e.g. `articles` on Category: parent ids =
    `distinct Article.categoryId where Article.id IN S`.
  - **to-many (M2M, via junction)** — e.g. `tags` on Article: parent ids =
    `distinct ArticleTag.articleId where ArticleTag.tagId IN S`.
- Final root-id set → `id IN (…)` (empty → `id IS NULL`).

Every hop is SqlSugar `Queryable`/`ConditionalModel` (the Phase-3a batched-`IN` form);
zero vendor SQL; to-one, to-many, and multi-level are handled uniformly.

**Guards:**
- depth bounded by `MaxRelationDepth`;
- `MaxRelationFilterIds` (new, default 5000) caps each intermediate id set — exceeding it
  → 400 "relation filter too broad". (Final value fixed in the plan; removable if the
  user prefers no cap.)

## 7. Sort (to-one only)

- Validate the entire sort path is to-one; otherwise 400
  "sort across to-many relations is not supported".
- Mechanism is spike-chosen (leading candidate: `OrderBy(string)` + correlated subquery;
  fallback: a narrow `LeftJoin` for the sort path). Multi-level to-one sort bounded by
  `MaxRelationDepth`; if the spike shows nested subqueries are unreliable, sort is reduced
  to a single hop and that decision is recorded.

## 8. Configuration

- Reuse `StruoQueryOptions.MaxRelationDepth` (default 5) as the path-depth cap.
- Add `StruoQueryOptions.MaxRelationFilterIds` (default 5000), bound from the `Query`
  section, consumed by the filter resolver.

## 9. Error handling

- Unknown relation segment / unknown leaf field / over-depth path → 400 (`QueryException`).
- Sort path containing a to-many segment → 400.
- Intermediate or root id set exceeds `MaxRelationFilterIds` → 400.
- Empty id set → always-false leaf (`id IS NULL`), not an error; returns 0 rows.

## 10. Testing (TDD)

- **`QueryValidator` unit**: valid dotted path (against graph) passes; unknown segment →
  throw; unknown leaf field → throw; depth over cap → throw; sort across to-many → throw.
- **`RelationPath` unit**: segment parsing/classification; `IsSortable` (all-to-one).
- **Filter integration (SQLite + Blog)**: to-one (`category.name`); multi-level
  (`category.parent.name`); M2M EXISTS (`tags.name`); O2M (`articles.title`); a
  relation-path condition combined with a scalar condition under `_and` and under `_or`;
  empty match → 0 rows with no SQL error.
- **Sort integration**: `sort=-category.name` orders correctly; sort across to-many → 400.
- **Regression**: Phase-2 scalar filter/sort stay green; the existing
  `QueryValidatorTests.Dotted_relation_path_throws` is replaced with a now-passing test.

## 11. Verification gate (§18 — Phase 3 remaining portion)

- build/test green (SQLite).
- Cross-relation filter returns correct rows for to-one, to-many EXISTS, and multi-level.
- to-one sort orders correctly; sort across a to-many segment → 400.
- Relation-path conditions compose correctly inside `_and`/`_or` with scalar conditions.
- Empty-match relation filter returns 0 rows (no SQL error).
- Live PostgreSQL re-exercised (the cross-relation queries run against real Postgres).

## 12. Package & environment rules (§15)

All DB access via SqlSugar ORM (`Queryable`, `ConditionalModel`, optional `LeftJoin`/
`Subqueryable` per spike); zero vendor SQL. Domain stays dependency-free. Packages via
`dotnet add package` (latest, CPM).
