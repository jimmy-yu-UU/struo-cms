# 7. Relations

A relation links one collection's rows to another's. Every relation is declared with a pair of
attributes on a navigation property: SqlSugar's own `[Navigate]` (which side/shape the join is) and
the framework's `[CmsRelation]` (everything CMS-specific — picker UI, display, cascade behavior).
`MetadataScanner.ScanRelations` (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) requires
**both** attributes on a property to recognize it as a relation at all; either alone is ignored.

## Supported kinds

`RelationKind` (`src/Struo.Domain/Metadata/Enums/RelationKind.cs`) has exactly three values:
`ManyToOne`, `OneToMany`, `ManyToMany`. The scanner infers which one applies from the property's own
shape — a scalar (or nullable scalar) reference is always `ManyToOne`; a `List<T>`/enumerable
property is `OneToMany` unless `[Navigate]`'s constructor names a junction type, in which case it's
`ManyToMany`.

### Many-to-one — `File.Folder`

The framework's own `File` collection declares a real, live many-to-one to `MediaFolder`:

```csharp
// src/Struo.Infrastructure/Files/File.cs (excerpt)
[SugarColumn(IsNullable = true)]
public Guid? FolderId { get; set; }

[Navigate(NavigateType.OneToOne, nameof(FolderId))]
[CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
[SugarColumn(IsIgnore = true)]
public MediaFolder? Folder { get; set; }
```

(SqlSugar's own `NavigateType.OneToOne` constant is used for both many-to-one and one-to-many
navigation properties — the scanner, not the `NavigateType` value, is what decides `ManyToOne` vs.
`OneToMany`, based on whether the property is a scalar or a list.) The foreign key
(`folderId`) is a plain, nullable `Guid` column on the owning side; `[CmsRelation]`'s
`DisplayTemplate = "{Name}"` says what to show for a resolved target in pickers and breadcrumbs.
`MediaFolder` itself declares the identical pattern one level down, self-referencing to model a
folder tree:

```csharp
// src/Struo.Infrastructure/Files/MediaFolder.cs (excerpt)
[Navigate(NavigateType.OneToOne, nameof(ParentId))]
[CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
[SugarColumn(IsIgnore = true)]
public MediaFolder? Parent { get; set; }
```

`RelationMetadata.SelfReferencing` is `true` here (target type equals the declaring type). A
self-referencing many-to-one is additionally guarded on **write**: `SelfReferenceCycleGuard`
(`src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs`) walks the incoming parent's own
ancestor chain (bypassing the soft-delete filter, since a trashed ancestor's FK still counts) and
rejects an update that would close a cycle:

```
$ curl -s -X PUT http://localhost:5221/api/items/mediafolder/<docs-id> \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"name":"Docs","parentId":"<guides-id>"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'parentId' would create a cycle in 'mediafolder'."}}
```

(`<guides-id>` here was already a child of `<docs-id>`, so re-parenting `Docs` under `Guides` would
close a two-node loop.) This guard has its own internal 64-level walk cap purely as a defensive stop
against pre-existing corrupt data — an unrelated number from the query DSL's relation-path depth cap
covered later in this chapter.

### One-to-many — the sample's `Category.Articles`/`Category.Children`

No framework collection declares a one-to-many relation today — all seven framework collections
that are themselves `[CmsCollection]`s use only many-to-one and many-to-many. The `Struo.Sample.Blog`
demo (chapter 16) shows the pattern in full, and the same declaration works identically in your own
fork's collections:

```csharp
// samples/Struo.Sample.Blog/Category.cs (excerpt)
[Navigate(NavigateType.OneToMany, nameof(ParentId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Category> Children { get; set; } = [];

[Navigate(NavigateType.OneToMany, nameof(Article.CategoryId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
[SugarColumn(IsIgnore = true)]
public List<Article> Articles { get; set; } = [];
```

A one-to-many carries no foreign key on its own declaring side — `RelationMetadata.ForeignKey` is
instead the **reverse** FK property name, read off the child (target) side's `[Navigate]` argument
(`categoryId` for `Articles`, `parentId` for `Children`), so the query layer knows which column on
the target collection to filter by. Live-verified with a real `Category`/`Article` pair (sample
opted in for this check only):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/category/<engineering-id>?deep=articles"
{"success":true,"data":{"id":"<engineering-id>","name":"Engineering", ...,
  "articles":[{"id":"<article-id>","status":"published", ...}]}}
```

### Many-to-many — `User.Roles`

The framework's own user↔role assignment is a real, live many-to-many, junctioned through the
`UserRole` entity:

```csharp
// src/Struo.Infrastructure/Identity/User.cs (excerpt)
[Navigate(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
[CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Role> Roles { get; set; } = [];
```

`[Navigate]`'s first argument being a `Type` (rather than a `NavigateType`) is exactly what tells
the scanner this is `ManyToMany` rather than `OneToMany`, even though both are declared on a `List<T>`
property. The write side accepts a plain array of target ids under the relation's own camelCase name
— `"roles": ["<role-id>", ...]` — and `ItemWriteSideSync.SyncM2MAsync` validates every id exists in
the target collection before replacing the junction rows for that parent (a full replace, not a
diff/patch): an unknown id is rejected as `"One or more ids in '{relation}' do not exist in
'{target}'."`, and a repeated id is silently de-duplicated (a junction is a set).

## `[CmsRelation]` properties

`CmsRelationAttribute` (`src/Struo.Domain/Metadata/Attributes/CmsRelationAttribute.cs`) declares:

| Property | Type | Meaning |
|---|---|---|
| `Interface` | `RelationInterface` | Which admin-SPA picker/display component applies (below); defaults to `Dropdown`. |
| `DisplayTemplate` | `string?` | A `{FieldName}`-style template used to render a resolved target row as a label (pickers, breadcrumbs, `RelatedList` rows). |
| `PickerQuery` | `string?` | Captured into metadata but not read anywhere in the admin SPA's picker components today — the shipped `RelationPicker`/`RelatedList` always query the target collection unfiltered (aside from the `search=` term the user types) plus, for a `RelatedList`, the parent's own reverse-FK filter. |
| `SortField` | `string?` | For many-to-many only: the junction column used to order the target rows (`RelationExpander.JunctionSortKey`); falls back to insertion order if unset or non-numeric. |
| `OnDelete` | `OnDelete` | Cascade behavior when the *target* of a many-to-one is deleted (below); defaults to `Restrict`. |
| `Editable` | `bool` | Whether the admin item form's picker for this relation accepts input; defaults to `true`. |
| `DisplayColumns` | `string?` | Declared on the attribute but never read into `RelationMetadata` or anywhere else in `src/` — has no observable effect today (the same kind of unused-property caveat chapter 4 raised for `[CmsField(Display = ...)]`; don't rely on it). |
| `MaxDepth` | `int` (default `1`) | Also declared but never read into `RelationMetadata` — has no observable effect today. Do not confuse it with the query DSL's separate, actually-enforced relation-path depth cap of 6 covered later in this chapter. |

## Junction entities for many-to-many

A junction is a plain SqlSugar entity — not necessarily a `[CmsCollection]` at all. The framework's
`UserRole` (`src/Struo.Infrastructure/Identity/UserRole.cs`) happens to *also* be its own
`[CmsCollection]` (`Hidden = true`, so it never shows in the sidebar; `AdminOnly = true`, so generic
writes to it need a super-admin regardless of any per-collection grant); its own doc comment states
only that it "model[s] the user↔role many-to-many," with no further stated rationale for also being
a collection. A junction entity does not need to be a collection at all: the sample's `ArticleTag`
(`samples/Struo.Sample.Blog/ArticleTag.cs`) is a bare junction with no `[CmsCollection]` attribute.
Either way, `[Navigate(typeof(JunctionType), parentFkName, targetFkName)]` on the *owning*
collection's list property is what the scanner needs to resolve the junction's shape — the junction
type itself carries no `[CmsRelation]`.

## `OnDelete` semantics per value

`OnDelete` (`src/Struo.Domain/Metadata/Enums/OnDelete.cs`) has three values — `Restrict`, `Cascade`,
`SetNull` — and applies only to the **target** side of a many-to-one relation (the collection the FK
points at), governing what happens when a row on that target side is permanently deleted (purged).
`ItemPurgePipeline` (`src/Struo.Application/Query/Write/ItemPurgePipeline.cs`) is where all three are
enforced:

- **`Restrict`** (the attribute default) — the purge (and, sharing the same guard, a plain
  soft-delete/trash) is rejected with `RelationConflictException` (HTTP 409) if any row anywhere
  still references the target via that FK: `"Cannot delete '{collection}/{id}': referenced by
  '{sourceCollection}'."`. Live-verified deleting a `MediaFolder` that still holds a `File` (`File.Folder`
  is `OnDelete.Restrict`; `MediaFolder` is not itself soft-deletable, so its `DELETE` always attempts
  a purge):

  ```
  $ curl -s -X DELETE http://localhost:5221/api/items/mediafolder/<guides-id> -H "X-Struo-CSRF: 1" -b cookies.txt
  {"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediafolder/<guides-id>': referenced by 'file'."}}
  ```

- **`SetNull`** — every inbound row's FK is set to `null` before the target is purged. Used by the
  sample's `Article.Category` and `Category.Parent`. This branch only ever runs during a **purge**,
  never during a soft-delete/trash — trashing only ever runs the `Restrict` check above, so a
  `SetNull`/`Cascade` relation's referencing rows are left untouched by a plain trash of the target.
- **`Cascade`** — every inbound referencing row is recursively purged too (through the same pipeline,
  so *its* own junctions/translations/revisions are cleaned up, and a `visited` set guards against a
  cyclic cascade graph looping forever). No shipped framework or sample collection actually declares
  `OnDelete.Cascade` today — it exists and is exercised by the test suite, but every real relation in
  this codebase uses `Restrict` or `SetNull`.

## Reading related data with `deep`

`deep` requests read-time expansion of a relation onto its parent row, batched per relation per page
(one follow-up query per relation, not one per row — N+1-safe) by `RelationExpander`
(`src/Struo.Infrastructure/Query/RelationExpander.cs`). The simple query-string form just names the
relations to expand:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?deep=folder"
{"success":true,"data":{"id":"<id>","fileName":"alpha-report.txt", ...,
  "folder":{"id":"<folder-id>","name":"Guides", ...}}}
```

The JSON envelope form (`POST /api/items/{collection}/query`, chapter 8) additionally accepts, per
relation, a field whitelist, a nested `filter` (resolved against the *target* collection), an
own-field `sort`, and `limit`/`offset` — applied in-memory to that relation's already-fetched rows
per parent — plus a nested `deep` for multi-level expansion. A many-to-one relation ignores
filter/sort/limit/offset (there is at most one target row); they apply only to one-to-many and
many-to-many.

## The depth cap of 6

Every dotted relation path — whether in a filter, a sort key, or a nested `deep` — is parsed by
`RelationPath.Parse` (`src/Struo.Application/Query/RelationPath.cs`) against
`StruoQueryOptions.MaxRelationDepth`, which defaults to **6**. The count is of relation *hops*, not
counting the final leaf field: `folder.name` is 1 hop, `folder.parent.name` is 2, and so on. A path
whose hop count exceeds the cap is rejected before any query runs — purely a metadata-graph check,
independent of how many real rows actually exist along that chain:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bfolder.parent.parent.parent.parent.parent.parent.name%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation path 'folder.parent.parent.parent.parent.parent.parent.name' exceeds the maximum depth of 6."}}
```

(`folder` + six `parent` hops = 7, one over the cap.)

## Relation filtering across dotted paths

A filter field path containing a `.` is treated as a relation path and resolved by
`RelationFilterResolver` (`src/Struo.Infrastructure/Query/RelationFilterResolver.cs`): it walks the
path leaf-to-root, collecting target ids at each hop, and rewrites the original condition into a
plain `id _in [...]` (or an always-false `id _null` if nothing matched) against the *root*
collection — so the rest of the query pipeline never has to special-case relation paths. All three
relation kinds are supported as hops, live-verified from the sample's `article`/`category` (opted in
temporarily, as above):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bcategory.name%5D%5B_eq%5D=Engineering"    # many-to-one
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags.name%5D%5B_eq%5D=Guide"              # many-to-many
$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Barticles.status%5D%5B_eq%5D=published"   # one-to-many
```

Each returned exactly the expected row. An unknown relation name in the path is rejected the same
way an unknown leaf field is (chapter 8 covers the full validation picture):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bbogus.name%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown relation 'bogus' on 'article' in path 'bogus.name'."}}
```

**Sorting** across a relation path is narrower than filtering: only an all-many-to-one path is
sortable (`RelationPath.IsSortable`), since a to-many hop has no single well-defined order to sort a
parent row by:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?sort=category.name"
{"success":true, ...}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?sort=tags.name"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Sort across to-many relations is not supported: 'tags.name'."}}
```

## Admin pickers

`RelationInterface` (`src/Struo.Domain/Metadata/Enums/RelationInterface.cs`) has four values, and the
shipped admin SPA wires every one of them to a real input
(`frontend/src/lib/relationInputKind.ts`):

| `RelationInterface` | Admin component | Behavior |
|---|---|---|
| `Dropdown` | `RelationPicker` (PrimeVue `Select`) | Single-value picker; debounced `search=` against the target collection as the user types. |
| `TagSelect` | `RelationPicker` (PrimeVue `MultiSelect`) | Multi-value picker for many-to-many relations, same search behavior. |
| `TreeSelect` | `RelationPicker` (PrimeVue `TreeSelect`) | Single-value picker over a tree built from a self-referencing many-to-one's target rows. |
| `RelatedList` | `RelatedList` (PrimeVue `DataTable`) | Read-only, paginated, lazy-loaded list of the target collection filtered by the relation's reverse FK; clicking a row navigates to that row's own item-edit page. Shows only after the parent has been saved (a brand-new, unsaved parent has no id to filter by yet). |

`relationInputKind.ts` still has a final, unconditional fallback for an interface its map does not
recognize — a bare <span v-pre>`<span class="readonly-relation">{{ relation.label }} (read-only)</span>`</span> rather
than an input element, so `Editable` has nothing to apply to. Nothing in the shipped enum reaches it:
the schema contract gate (`schema/interfaces.json`, see `schema/README.md`) fails `pnpm test` if a
`RelationInterface` member has no entry in that map, so the fallback is unreachable by construction
rather than by convention. It matters only if you add a member and skip the frontend half — which is
exactly what the gate refuses to let you do. Earlier versions shipped three unmapped members
(`FilePicker`, `ImagePicker`, `FilesPicker`) that did land on it; they were removed, since file
references are modelled as `FieldInterface` `File`/`Image`/`Files` rather than as relations.

Every picker resolves a target row's display label from `[CmsRelation(DisplayTemplate = ...)]` via
`resolveDisplayLabel` — a plain `{FieldName}` substitution against the target's own projected
fields, falling back to the raw id if the template (or the referenced field) can't be resolved.

## Next steps

- Chapter 4, [Defining a Collection](04-defining-a-collection.md), for `[CmsField]` and how a
  collection's own fields feed `DisplayTemplate`.
- Chapter 8, [Query DSL](08-query-dsl.md), for the full filter/sort grammar `deep` and dotted paths
  sit inside, including every operator and the exact validation-error shapes.
- Chapter 12, [Authentication, SSO & RBAC](12-auth-and-rbac.md), for how `User.Roles` is used to
  resolve effective permissions.
- Chapter 13, [Revisions & Soft Delete](13-revisions-and-soft-delete.md), for how a purge's
  `Cascade`/`SetNull` sweep interacts with a collection's own revision history and soft-delete state.
- Chapter 16, [Sample Walkthrough](16-sample-walkthrough.md), for the full `Category`/`Article`/`Tag`
  relation graph this chapter draws its one-to-many and many-to-many examples from.
