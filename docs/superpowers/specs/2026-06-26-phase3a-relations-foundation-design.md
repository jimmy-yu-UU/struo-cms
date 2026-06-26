# StruoCMS — Phase 3a (Relations Foundation) Design

> Date: 2026-06-26
> Source of truth: the StruoCMS master spec (§7.4, §8, §11, §18). Covers **Phase 3a only**.
> Phase 3 is split into **3a (this doc)** = relations foundation, and **3b** = cross-relation
> multi-level filter/sort (§7.6, spike-first, separate cycle). Builds on Phases 0–2 (merged).

## 1. Goal & scope

Make relations first-class: scan `[Navigate]`+`[CmsRelation]` into relation metadata, build and validate a relationship graph at startup, expose relations via the schema API, and support read-time `deep`/Includes expansion plus M2M assignment writes and OnDelete `Restrict` enforcement — across the full Blog relation set (M2O, O2M, M2M, self-referential tree).

**In scope (3a):**
- Full Blog relation sample set (Author, Category tree, Article↔Tag M2M, junction)
- Relation scanning → `RelationMetadata`; relationship graph + consistency validation (startup, fail-fast)
- Schema API emits `relations`
- `deep`/Includes read expansion (opt-in; M2O object, O2M/M2M arrays; depth cap)
- M2M assignment writes (sync junction)
- OnDelete in metadata; `Restrict` enforced on delete (409)
- §11 polymorphic data-model note (documentation only)

**Out of scope (deferred):**
- **Cross-relation filter/sort (`category.name`, `sort=-category.name`, to-many EXISTS) → Phase 3b** (filter/sort on relation paths stays rejected with 400).
- Deep nested-entity creation (inline-create via picker), i18n (Phase 4), files (Phase 5), Cascade/SetNull enforcement (metadata only this phase unless trivial), polymorphic behavior/UI.

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Phase split | 3a foundation (this) + 3b cross-relation query (spike-first) |
| Sample | Full Blog relation set: Author (M2O), Category (self-ref tree + M2O + O2M reverse), Article↔Tag M2M via ArticleTag(+SortOrder) |
| Writes | M2O via FK scalar (already works); M2M assignment now (sync junction); deep nested-create deferred |
| OnDelete | Emit in metadata; enforce `Restrict` (409); Cascade/SetNull metadata-only |
| `deep` | Opt-in expansion via SqlSugar `.Includes()`; list pages never auto-load relations (§8) |

## 3. Layer placement (§2)

- **Domain**: `RelationKind` enum; `RelationMetadata` record; `CollectionMetadata.Relations`. (Plain C#; `[Navigate]` is a SqlSugar attribute and lives only on sample entities, not Domain.)
- **Application**: `IRelationshipGraph` port (resolve relations per collection; segment lookup for 3b); deep-expansion request model; schema/item service extensions.
- **Infrastructure**: relation scanner (extends `MetadataScanner`); `RelationshipGraph` impl + consistency validation; repository `.Includes()` expansion + M2M junction sync + Restrict checks.
- **Api**: schema includes relations; items endpoints accept `deep`.

## 4. Sample relation entities (samples/Struo.Sample.Blog)

- `Author` : `IAuditable`, `[CmsCollection("Author")]`, `long Id`, `Name`.
- `Category` : `IAuditable`, `[CmsCollection("Category")]`, `long Id`, `Name`, `long? ParentId`, `[Navigate(OneToOne, nameof(ParentId))] Category? Parent` + `[CmsRelation(Interface=TreeSelect, OnDelete=SetNull)]`, `[Navigate(OneToMany, nameof(Category.ParentId))] List<Category> Children` (`[SugarColumn(IsIgnore=true)]`), and reverse `[Navigate(OneToMany, nameof(Article.CategoryId))] List<Article> Articles`.
- `ArticleTag` : junction, `[SugarTable("article_tags")]`, `long Id`, `ArticleId`, `TagId`, `int SortOrder`. (Not a CMS collection; used by the M2M Navigate.)
- `Article` gains: `long AuthorId`, `[Navigate(OneToOne, nameof(AuthorId))] Author Author` + `[CmsRelation(Interface=Dropdown, DisplayTemplate="{Name}", OnDelete=Restrict)]`; `long? CategoryId`, `[Navigate(OneToOne, nameof(CategoryId))] Category? Category` + `[CmsRelation(Interface=Dropdown, OnDelete=SetNull)]`; `[Navigate(typeof(ArticleTag), nameof(ArticleTag.ArticleId), nameof(ArticleTag.TagId))] List<Tag> Tags` + `[CmsRelation(Interface=TagSelect, SortField=nameof(ArticleTag.SortOrder))]` (`[SugarColumn(IsIgnore=true)]`).
- `Tag` gains reverse M2M: `[Navigate(typeof(ArticleTag), nameof(ArticleTag.TagId), nameof(ArticleTag.ArticleId))] List<Article> Articles` (`[SugarColumn(IsIgnore=true)]`).

FK rule (§8): FK columns live only on the M2O side (`AuthorId`, `CategoryId`, `ParentId`). O2M/M2M collections are `IsIgnore` navigation views.

## 5. Relation metadata (Domain)

```
enum RelationKind { ManyToOne, OneToMany, ManyToMany }

sealed record RelationMetadata {
  string Name;              // camelCase nav property, e.g. "author", "tags", "children"
  string Label;
  RelationKind Kind;
  string TargetCollection;  // camelCase target entity name, e.g. "author"
  RelationInterface Interface;
  string? ForeignKey;       // camelCase FK field for M2O (e.g. "authorId"); null otherwise
  string? DisplayTemplate;
  string? PickerQuery;
  OnDelete OnDelete;
  bool Editable;
  bool SelfReferencing;
}
// CollectionMetadata gains: IReadOnlyList<RelationMetadata> Relations
```

## 6. Relation scanner + relationship graph (§8)

- Extend `MetadataScanner` to read nav properties carrying `[Navigate]`+`[CmsRelation]`:
  - `Navigate(OneToOne, fk)` on a single-entity property → `ManyToOne` (FK on this side); target = property type; `SelfReferencing` if target == declaring type.
  - `Navigate(OneToMany, childFk)` on a `List<T>` → `OneToMany` (reverse view; no FK here).
  - `Navigate(typeof(junction), aId, bId)` on a `List<T>` → `ManyToMany`.
  - `TargetCollection` = camel(target entity type name); `ForeignKey` = camel(fk property) for M2O.
- `RelationshipGraph` (Infrastructure, built in `AddStruoMetadata` from the same scan) exposes `IRelationshipGraph`:
  - `IReadOnlyList<RelationMetadata> Relations(string collection)`
  - `RelationMetadata? Resolve(string collection, string relationName)` (used by `deep` validation now and 3b later).
- **Consistency validation at startup (throws `MetadataException`):** target entity is a `[CmsCollection]`; M2O `ForeignKey` property exists on the declaring entity; M2M junction has both FK properties named in `[Navigate]`; O2M reverse FK exists on the child; self-ref parent FK nullable. Fail-fast.

## 7. Schema emission

`GET /api/schema/{collection}` response gains `relations: [ {name,label,kind,targetCollection,interface,foreignKey,displayTemplate,pickerQuery,onDelete,editable,selfReferencing} ]`. Scalar `fields` unchanged. `RelationKind`/`OnDelete`/`RelationInterface` serialize as camelCase strings (existing enum converter).

## 8. `deep` / Includes read expansion (§7.4, §8)

- Transport: envelope `deep` key — `{ "deep": { "author": { "fields":["id","name"] }, "tags": { "fields":["id","name"], "limit":50 } } }`; query-string `deep=author,tags` (shallow form, default fields). Validated against `IRelationshipGraph`: each relation name exists; nesting depth ≤ `MaxRelationDepth`.
- Execution: repository builds `.Includes()` chains by runtime type (reflection/expression per relation), honoring inner `fields` and `limit` where applicable. M2O → nested object; O2M/M2M → nested array.
- Projection: expanded relation rendered as a nested camelCase dict / array under the relation name, alongside scalar fields. **List endpoints do not expand unless `deep` is supplied** (§8 — no auto-load).
- Result envelope unchanged (`{data, meta}` / `{data}`); related data nested inside each row.

## 9. M2M assignment writes

Create/update `Article` accepts a `tags` array of target ids (e.g. `"tags":[1,2,3]`). The repository syncs the `ArticleTag` junction: insert new pairs, delete removed pairs, set `SortOrder` by array order. M2O writes need no special handling (FK scalar column). The write path validates each id exists in the target collection (else 400).

## 10. OnDelete (§8)

- `OnDelete` emitted in `RelationMetadata`.
- **`Restrict` enforced**: before deleting a row, the repository checks for referencing rows via any inbound `Restrict` relation (e.g. deleting an `Author` referenced by `Article.authorId`); if found → **409 Conflict** `{error:{message}}` naming the blocking collection. `Cascade`/`SetNull` are metadata-only this phase.

## 11. Configuration

`StruoQueryOptions` gains `MaxRelationDepth` (default 5), bound from the `Query` section; consumed by `deep` validation.

## 12. Error handling

- Unknown `deep` relation / over-depth → 400 (`QueryException`).
- Restrict-blocked delete → 409.
- Startup graph inconsistency → fail-fast (`MetadataException`).
- M2M write referencing a nonexistent id → 400.

## 13. Testing (TDD)

- **Scanner**: builds correct `RelationMetadata` for M2O (`author`, FK `authorId`), O2M (`children`, `articles`), M2M (`tags`, junction), self-ref (`parent`/`children`, `SelfReferencing=true`); kind + target + interface + onDelete correct.
- **Graph validation**: a broken fixture (M2O FK missing / target not a collection) throws at scan.
- **Schema**: `/api/schema/article` includes `relations` with `author`(manyToOne), `category`(manyToOne), `tags`(manyToMany); `/api/schema/category` includes self-ref `parent`/`children`.
- **deep (integration, SQLite)**: create author+category+article; `GET /api/items/article/{id}?deep=author,category` → nested objects; M2M `deep=tags` → array; `category` tree `deep=children`; depth over cap → 400; unknown relation → 400.
- **M2M write**: PUT article with `tags:[t1,t2]` → junction rows synced (order = SortOrder); changing the set updates junction.
- **OnDelete**: delete an author referenced by an article (Restrict) → 409; delete category referenced (SetNull) → allowed (metadata-only, not enforced) — assert it is NOT blocked.
- **No auto-load**: list without `deep` returns rows without relation keys.

## 14. Verification gate (§18 Phase 3 — foundation portion)

- build/test green; schema shows relations (incl. self-ref tree).
- `deep` expands M2O object + O2M/M2M arrays + tree children; depth cap enforced.
- M2M picker assignment persists via junction.
- `Restrict` delete blocked (409).
- SQLite green; live Postgres re-exercised when relations land (the Phase-2 live check is substantially cleared: app starts cleanly against Postgres).
- **Deferred to 3b:** cross-relation filter/sort (to-one JOIN multi-level, to-many EXISTS, `sort=-category.name`).

## 15. Package & environment rules (§15)

- All DB access via SqlSugar ORM (`.Includes()`, junction CRUD); zero vendor SQL. Domain stays dependency-free. Packages via `dotnet add package` (latest, CPM).
