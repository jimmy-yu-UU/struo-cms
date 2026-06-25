# StruoCMS — Phase 2 (Generic CRUD + Single-Table Query DSL) Design

> Date: 2026-06-26
> Source of truth: the StruoCMS master spec (§7, §15, §16, §18). Covers **Phase 2 only**.
> Builds on Phase 0 (foundation) + Phase 1 (metadata core), both merged to `main`.

## 1. Goal & scope

Expose generic, metadata-driven REST CRUD plus a single-table query DSL (filter/sort/fields/pagination/search) over any `[CmsCollection]` entity, with whitelist validation and DoS caps, all via SqlSugar ORM (zero vendor SQL).

**In scope (master spec §18, Phase 2):**
- Ports + SqlSugar implementation (`IItemRepository`, `IPermissionService`)
- DSL parser (JSON envelope + bracket query-string) + normalized AST + §7.7 validation
- REST CRUD + `POST /api/items/{collection}/query`
- Single-table filter / sort / fields / pagination
- DoS caps (limit ceiling, condition count)
- Portable multi-field LIKE `search`
- Allow-all `IPermissionService` seam

**Out of scope (deferred):** relations & cross-relation filter/sort, `deep` expansion (Phase 3); locale/translations (Phase 4); files (Phase 5); auth/RBAC real impl (Phase 6); soft delete + unified error envelope (Phase 9); PATCH-partial updates; full-text search.

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Write strategy | Deserialize JSON body to the strongly-typed entity (STJ camelCase); strip system/read-only; validate `Required` from metadata |
| Response shape | Lightweight `{data, meta}` — lists/query: `{data:[...], meta:{total,limit,offset}}`; single: `{data:{...}}`. Full error envelope is Phase 9 |
| Row projection | Metadata-driven projection to camelCase dictionaries (honors `fields`, excludes hidden, always includes PK as `id`, includes system fields read-only) |
| Delete | Hard delete this phase; soft delete is Phase 9 |
| Update verb | `PUT` full-replace (deserialize to entity). PATCH-partial deferred |
| Dynamic query | SqlSugar `List<IConditionalModel>` / `ConditionalCollections` + `.OrderBy(string)` + `.ToPageList`; by-`Type` via `MakeGenericMethod` |

## 3. Layer placement (§2)

- **Domain** `Query/`: AST (`FilterNode`→`LogicalFilter`/`ComparisonFilter`), `QueryOperator`, `LogicalOperator`, `SortField`, `QueryModel`, `QueryException`.
- **Application** `Query/`: `QueryParser`, `QueryValidator`, ports `IItemRepository`/`IPermissionService`, `ItemService`; `Configuration/StruoQueryOptions`; `Metadata/IEntityRegistry` port.
- **Infrastructure**: `SqlSugarItemRepository`, `ConditionalModelTranslator`, `EntityRegistry`, `AllowAllPermissionService`, DI wiring.
- **Api**: `ItemsController`.

Domain stays dependency-free (AST + exception are plain C#). `Type` appears in the Application `IEntityRegistry` port (BCL type — allowed).

## 4. DSL AST & operators (§7.3, §7.5)

```
enum QueryOperator { Eq, Neq, In, Nin, Lt, Lte, Gt, Gte, Null, NNull, Contains, StartsWith, EndsWith }
enum LogicalOperator { And, Or }

abstract record FilterNode;
sealed record LogicalFilter(LogicalOperator Op, IReadOnlyList<FilterNode> Children) : FilterNode;
sealed record ComparisonFilter(string FieldPath, QueryOperator Op, object? Value) : FilterNode;

sealed record SortField(string Field, bool Descending);

sealed record QueryModel(
    IReadOnlyList<string>? Fields,
    FilterNode? Filter,
    IReadOnlyList<SortField> Sort,
    int Limit,
    int Offset,
    string? Search);
```

`ComparisonFilter.FieldPath` may be dotted (e.g. `category.name`) at the AST level; the Phase 2 validator rejects dotted paths (Phase 3 enables them). Transport operators are `_`-prefixed (`_eq`, `_and`, …).

## 5. Transport & parsing (§7.2)

- **JSON envelope** (POST `/query` body): `{ "fields":[...], "filter":{...}, "sort":["-createdAt","title"], "limit":25, "offset":0, "search":"kw" }`. `filter` per §7.3: `{ "_and":[...] }` | `{ "_or":[...] }` | `{ "<field>": { "<op>": value } }`.
- **Bracket query-string** (GET): `?filter[status][_eq]=published&sort=-createdAt&limit=25&offset=0&fields=id,title&search=kw`. Normalized to the same `QueryModel`. Nested `_and`/`_or` only in the JSON envelope; GET bracket = implicit-AND of field conditions.
- `QueryParser` produces an unvalidated `QueryModel` (string field names, raw values); validation is a separate step.

## 6. Validation (§7.7)

`QueryValidator.Validate(QueryModel, CollectionMetadata, permission)`:
- Every field in `fields`/`filter`/`sort` exists in the collection metadata → else `QueryException` (400).
- Any dotted/relation path → `QueryException` (400, "relation paths are supported from Phase 3").
- Permission (allow-all seam): readable/sortable fields filtered via `IPermissionService` (no-op now).
- `search` scans only fields with `Searchable == true` (string interfaces). If none, `search` is a no-op.
- DoS caps from `StruoQueryOptions`: `Limit` clamped to `[1, MaxLimit]` (default cap 100; default page size 25 when omitted); total comparison conditions ≤ `MaxFilterConditions` (default 50).
- Property→column mapping via SqlSugar `EntityMaintenance.GetDbColumnName(entityType, propertyName)`; camelCase field name → property via `EntityRegistry`.

## 7. Query execution — zero vendor SQL (§15)

`ConditionalModelTranslator` maps the validated AST to SqlSugar:
- `ComparisonFilter` → `ConditionalModel { FieldName = column, ConditionalType, FieldValue }` (`Eq`→Equal, `Neq`→NoEqual, `Contains`→Like, `StartsWith`→LikeRight, `EndsWith`→LikeLeft, `In`→In, `Nin`→NotIn, `Lt/Lte/Gt/Gte`→LessThan/LessThanOrEqual/GreaterThan/GreaterThanOrEqual, `Null`→IsNullOrEmpty/IsNull, `NNull`→IsNot null).
- `LogicalFilter` → `ConditionalCollections` (nested And/Or).
- `search` → an OR group of `Like` conditionals over searchable columns.

`SqlSugarItemRepository` resolves the entity `Type` from `EntityRegistry` and invokes a generic `QueryRunner.Run<T>(db, conditionals, orderBy, limit, offset)` via `MakeGenericMethod`; inside, strongly typed: `db.Queryable<T>().Where(conditionals).OrderBy(orderBy).ToPageList(pageNumber, pageSize, ref total)`. Ordering string is built from validated columns (`"col ASC, col2 DESC"`). No raw SQL anywhere.

CRUD (by `Type`, via SqlSugar object/generic APIs):
- **GetById**: `Queryable<T>().InSingle(id)` (reflected).
- **Create**: deserialize body → entity `Type` (STJ camelCase); null out system/read-only props; `db.Insertable(entity).ExecuteReturnEntity()` (audit AOP stamps); re-project.
- **Update (PUT)**: 404 if id missing; deserialize → entity, set PK = id, null system/read-only; `db.Updateable(entity).ExecuteCommand()` (audit AOP restamps UpdatedAt/By); re-project.
- **Delete**: `db.Deleteable<T>().In(id).ExecuteCommand()` (reflected); hard delete; 404 if absent.

> Exact SqlSugar member names (ConditionalType values, `ToPageList` signature, `InSingle`, `Deleteable().In`) are verified against the installed SqlSugarCore at implementation time; the intent above governs.

## 8. Endpoints (`ItemsController`, route `api/items/{collection}`)

| Method | Route | Body | Success |
|---|---|---|---|
| GET | `/api/items/{collection}` | — (bracket query) | 200 `{data,meta}` |
| POST | `/api/items/{collection}/query` | envelope | 200 `{data,meta}` |
| GET | `/api/items/{collection}/{id}` | — | 200 `{data}` / 404 |
| POST | `/api/items/{collection}` | entity JSON | 201 `{data}` |
| PUT | `/api/items/{collection}/{id}` | entity JSON | 200 `{data}` / 404 |
| DELETE | `/api/items/{collection}/{id}` | — | 204 / 404 |

Unknown `{collection}` → 404 everywhere. `QueryException` / validation → 400. camelCase JSON throughout.

## 9. Projection & envelope

`ItemService` projects each entity to a camelCase `Dictionary<string,object?>`:
- Always include the primary key as `id` (PK resolved from `EntityRegistry`).
- Include the validated `fields` if provided; otherwise all non-hidden fields.
- Exclude `Hidden` fields; include system (read-only) fields.
- Lists/query: `{ data: [ ... ], meta: { total, limit, offset } }`. Single: `{ data: { ... } }`.

## 10. Configuration

`StruoQueryOptions` (bound from `Query` section): `MaxLimit` (100), `DefaultLimit` (25), `MaxFilterConditions` (50). Registered in DI; consumed by `QueryValidator`.

## 11. Registry extension

`AddStruoMetadata` additionally builds and registers `IEntityRegistry` (impl `EntityRegistry`, Infrastructure) from the same startup scan: collection name → `EntityDescriptor { Type EntityType; IReadOnlyDictionary<string,string> FieldToProperty; string IdProperty }`. No new per-request attribute reflection; the only per-request reflection is the cached `MakeGenericMethod` dispatch.

## 12. Permission seam (§13.3)

`IPermissionService` (Application): `bool CanRead(string collection)`, `bool CanWrite(string collection)`, `bool CanDelete(string collection)`, `IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> all)`. `AllowAllPermissionService` returns true / all fields. `ItemService` calls it before query/CRUD and when projecting fields. Phase 6 swaps the implementation.

## 13. Error handling

- `QueryException` (parse/validate) → HTTP 400 with a clear message (minimal `{error:{message}}`; full envelope Phase 9).
- Unknown collection → 404; unknown id → 404.
- Required-field violation on write → 400.
- All other framework defaults until Phase 9.

## 14. Testing (TDD)

- **Unit**: `QueryParser` (JSON + bracket → QueryModel); `QueryValidator` (unknown field → throw; dotted path → throw; limit clamp; condition-count cap; search restricted to searchable); `ConditionalModelTranslator` (each operator → correct ConditionalType; And/Or nesting).
- **Integration** (WebApplicationFactory + SQLite): CRUD round-trip on `article` (create→get→update→delete); filter (`_eq`,`_contains`,`_in`,`_gt`), sort (`-createdAt`), `fields` projection (only requested keys + `id`), pagination `meta.total`, `search` over title; unknown field → 400; unknown collection → 404; create stamps audit fields; hidden fields excluded.

## 15. Verification gate (§18 Phase 2)

- `dotnet build` clean + `dotnet test` green.
- Single-table filter / sort / pagination work; nonexistent field → 400.
- SQLite (tests) pass; a representative query verified live against Postgres.
- Zero vendor SQL (all access via SqlSugar ORM / conditional models).
- Allow-all `IPermissionService` seam wired into `ItemService`.

## 16. Package & environment rules (§15)

- Any new package via `dotnet add package` (latest, CPM). All DB access via SqlSugar ORM; no vendor SQL/functions. Domain stays dependency-free.
