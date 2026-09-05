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
the target collection before handing the array to `ManyToManySync`, which diffs it against the junction
rows already on file for that parent: rows for targets no longer present are deleted, rows for newly
added targets are inserted, and rows for targets that remain keep their primary key rather than being
dropped and recreated. An unknown id is rejected as `"One or more ids in '{relation}' do not exist in
'{target}'."`, and a repeated id is silently de-duplicated (a junction is a set). The next section covers
the richer write shape available when the junction itself carries payload beyond the two foreign keys.

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

A junction is a plain SqlSugar entity — not necessarily a `[CmsCollection]` at all. A bare junction
with no `[CmsCollection]` attribute at all works exactly as it always has: nothing in this section
applies to it. `[Navigate(typeof(JunctionType), parentFkName, targetFkName)]` on the *owning*
collection's list property is what the scanner needs to resolve a junction's shape either way — the
junction type itself carries no `[CmsRelation]`.

Every junction type — payload-bearing or bare — must declare exactly one `[SugarColumn(IsPrimaryKey =
true)]` property: the many-to-many sync updates existing junction rows by that primary key, so a
junction with a composite key or no key at all fails fast at startup with a `MetadataException` rather
than throwing on the first write.

### Junction payload

When a junction type *also* carries `[CmsCollection]` it becomes what this manual calls a **junction
collection**, and its `[CmsField]`s — other than the two foreign keys, the relation's `SortField`
(above), and any `IsSystem`/`ReadOnly` field — become the relation's **payload**: data that belongs to
the link itself rather than to either endpoint (a note on why two rows are linked, a display weight
distinct from ordering, an approval timestamp). `RelationshipGraph.JunctionPayloadOf`
(`src/Struo.Infrastructure/Metadata/RelationshipGraph.cs`) is the single place that computes a
relation's payload field list; the write-side mixed-array binder (REST, chapter 9), the `_junction`
read projection (below), revisions (chapter 13), and GraphQL (chapter 10) all read it from there
rather than re-deriving it themselves.

Both of a junction collection's foreign keys **must** be declared as writable `[CmsField]`s (e.g.
`Interface = FieldInterface.Uuid`) — the framework's own `UserRole`
(`src/Struo.Infrastructure/Identity/UserRole.cs`) does this too, though it declares no field beyond its
two FKs, so it carries no payload. `MetadataScanner.ValidateJunctionCollections`
(`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) fails startup with a `MetadataException` if
either FK isn't writable, because the generic CRUD API would otherwise be able to create junction rows
with empty keys; the message's first clause is `"Junction collection '{junction}' (used by
'{owner}.{relation}') must declare its foreign keys '{fkA}' and '{fkB}' as writable [CmsField]s (e.g.
Interface = FieldInterface.Uuid)"`.

`Hidden = true` on a junction collection (`UserRole`, and the sample's `ArticleTag` below) only keeps
it out of the admin sidebar — it stays a fully addressable collection everywhere else: `GET
/api/schema`, the RBAC permission matrix, and the generated GraphQL schema all include it exactly like
a non-hidden collection. `RelationMetadata.JunctionCollection` (`junctionCollection` in `/api/schema`'s
relation entry) names it, which is how a client discovers which collection needs its own write grant
before it can send junction payload (REST, chapter 9).

**Caveat for forks**: if you add your own `[Navigate]`/`[CmsRelation]` picker relation directly on a
junction entity (a many-to-one from the junction to some third collection — "linked by user", say),
that relation registers in the inbound-restrict index exactly like any other many-to-one, since
`OnDelete` defaults to `Restrict`: deleting that third collection's row while a junction row still
references it is blocked unless you declare `OnDelete = OnDelete.Cascade` on the picker relation.

The sample's `ArticleTag` (`samples/Struo.Sample.Blog/ArticleTag.cs`, chapter 16) is the shipped
example of a junction collection: `[CmsCollection("Article tag", Hidden = true)]`, both foreign keys
declared as writable `Uuid` fields, a `Note` text field as its payload, and a `Sort` number field wired
to `Article.Tags`' `[CmsRelation(SortField = nameof(ArticleTag.Sort))]`.

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

Both `deep` and the dotted filter/sort paths below need a read grant on **every** collection the
path traverses, not just the one being queried. `deep` omits a relation you may not read; a dotted
filter or sort path is refused outright. Chapter 8,
[Query DSL](08-query-dsl.md#validation-whitelisting-unknown-paths-and-the-depth-cap), states the rule
and its migration consequence for anonymous-read deployments in full.

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

### `_junction` on a payload-bearing many-to-many

When `deep` expands a many-to-many relation whose junction carries payload (above), `RelationExpander`
attaches a `_junction` object to each expanded target row, holding that row's non-`Hidden` payload
field values — a `Hidden` payload field is left out of `_junction` the same way a `Hidden` own field is
left out of the target row itself. A relation whose junction carries no payload gets no `_junction`
key at all, and `_junction` is omitted entirely (not sent as `null`) when the caller does not hold a
read grant on the junction collection — the same "omit, don't fail" stance `deep` already takes for a
relation the caller cannot read. Deep-expanding the sample's `Article.Tags` (chapter 16), the shape is
(illustrative — `sort` itself is excluded from `_junction` because it is the relation's `SortField`,
already reflected in array order, not payload):

```json
{
  "id": "<article-id>",
  "tags": [
    { "id": "<tag-id>", "name": "Guide", "_junction": { "note": "editor pick" } }
  ]
}
```

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

A filter field path containing a `.` is treated as a relation path. Rather than resolving it in two
steps — first fetch matching related ids, then rewrite the condition into `id IN (<literal ids>)` —
`FilterTranslator` (`src/Struo.Infrastructure/Query/FilterTranslator.cs`,
`FilterTranslator.Subquery.cs`) pushes the whole condition down into **one nested SQL subquery**,
shaped differently per relation kind but always ending in the same three forms
(`<col> IN (<sql>)` / `<col> NOT IN (<sql>)` / `(<col> IS NULL OR <col> NOT IN (<sql>))`) wrapped as a
single `ConditionalModel`. A list query is always exactly two SQL statements — one `COUNT`, one
`SELECT` — no matter how many relation conditions it carries, and nothing is ever materialized as an
in-memory id set:

- **many-to-one**: `<declaring>.<fk> IN (SELECT id FROM <target> WHERE …)`
- **one-to-many**: `<declaring>.id IN (SELECT <reverseFk> FROM <target> WHERE … AND <reverseFk> IS
  NOT NULL)`
- **many-to-many**: `<declaring>.id IN (SELECT <parentFk> FROM <junction> WHERE […] AND <targetFk>
  IN (SELECT id FROM <target> WHERE …))`

Each hop nests the next inside it, so a multi-hop path (`category.parent.name`) is one subquery
chain, not one query per hop. All three relation kinds are supported, live-verified from the
sample's `article`/`category`/`tag` (opted in temporarily, as above — `Engineering-u3doc0905` is a
category, `Guide-u3doc0905` a tag):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Bcategory.name%5D%5B_eq%5D=Engineering-u3doc0905"    # many-to-one
{"success":true,"data":[{"id":"...","translations":{"en":{"title":"Article B u3doc0905", ...}}}, {"id":"...","translations":{"en":{"title":"Article A u3doc0905", ...}}}],"meta":{"total":2,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags.name%5D%5B_eq%5D=Guide-u3doc0905"              # many-to-many
{"success":true,"data":[{"id":"...","translations":{"en":{"title":"Article B u3doc0905", ...}}}, {"id":"...","translations":{"en":{"title":"Article A u3doc0905", ...}}}],"meta":{"total":2,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Bname%5D%5B_contains%5D=u3doc0905&filter%5Barticles.status%5D%5B_eq%5D=published"   # one-to-many
{"success":true,"data":[{"id":"...","name":"Engineering-u3doc0905", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

Each returned exactly the expected rows. An unknown relation name in the path is rejected the same
way an unknown leaf field is (chapter 8 covers the full validation picture):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bbogus.name%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown relation 'bogus' on 'article' in path 'bogus.name'."}}
```

### Each-exists vs. same-row: dotted paths vs. `_some`/`_none`

**A plain dotted path is *each-exists*, not same-row.** Every `ComparisonFilter` on a relation path
is translated into its own, independent subquery; a `LogicalFilter` only recurses into its
children, so nothing correlates which related row satisfied which condition. Two conditions on the
same to-many relation path can each be satisfied by a *different* related row and the parent still
matches. `_some` and `_none` are the two relation **quantifiers** that fix this: `_some`'s inner
filter is translated as one single subquery over the target collection, so every condition inside
it must hold for the *same* related row; `_none` is the same subquery, negated (`NOT IN`, or
`IS NULL OR NOT IN` for many-to-one).

| Written as | Semantics |
|---|---|
| `filter[tags.name][_eq]=a&filter[tags.color][_eq]=red` | Each-exists: some tag is named `a` **and** some tag is red — possibly two different tags. |
| `filter[tags._some.name][_eq]=a&filter[tags._some.color][_eq]=red` | Same-row: **one** tag is both named `a` and red. |
| `filter[tags._none.name][_eq]=a` | No tag is named `a` — including an article with no tags at all. |
| `filter[category._none.name][_eq]=x` | The category is not named `x`, or there is no category. |
| `filter[tags._some._junction.note][_contains]=hero` | Some article↔tag link's own `note` contains "hero". |

Live-verified against a fixture with two tagged articles — `A` has one tag (`Guide-u3doc0905`,
junction `note: "hero"`), `B` has two tags (`Guide-u3doc0905` with `note: "plain"` and
`Misc-u3doc0905` with `note: "hero"`), `C` has no tags at all. The dotted (each-exists) form matches
both `A` and `B` — `B`'s `Guide` tag alone satisfies the name condition, its *different* `Misc` tag
alone satisfies the junction-note condition:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags.name%5D%5B_eq%5D=Guide-u3doc0905&filter%5Btags._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":2,"limit":25,"offset":0}}
```

The identical two conditions under `_some` match only `A`, whose single tag row satisfies both at
once:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags._some.name%5D%5B_eq%5D=Guide-u3doc0905&filter%5Btags._some._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<a-id>", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

`_none` matching the tag-less article (a relation quantifier is legal on a many-to-one too — see
below — and a `_none` on an empty to-many relation is true, not an error):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags._none.name%5D%5B_eq%5D=Guide-u3doc0905"
{"success":true,"data":[{"id":"<c-id>","translations":{"en":{"title":"Article C u3doc0905", ...}}}],"meta":{"total":1,"limit":25,"offset":0}}
```

**`_some`/`_none` are legal on a many-to-one relation too, not only to-many.** On a to-one, `_some`
is equivalent to a dotted path over the same inner filter; `_none` means "no matching target, or the
foreign key is null" — genuinely useful ("no category, or not named Archive"), verified against `C`
(no category at all) alongside `A`/`B` (categorized, but not as `Archive-u3doc0905`):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Bcategory._none.name%5D%5B_eq%5D=Archive-u3doc0905"
{"success":true,"data":[{"id":"<c-id>", ...},{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":3,"limit":25,"offset":0}}
```

That last result relies on a NULL-safety rule: a plain SQL `<fk> NOT IN (SELECT …)` goes
empty the instant its subquery ever returns a NULL, and a nullable foreign key on the *outer* row —
`C`'s `categoryId` — has the same failure mode with an ordinary `NOT IN`. `_none` on a many-to-one
therefore generates `(<fk> IS NULL OR <fk> NOT IN (<sql>))`, and the `IS NOT NULL` guard on the
*inner* side is scoped to exactly the columns that can actually be NULL, not applied uniformly: the
one-to-many hop's `<reverseFk>` projection (the bullet above) is always guarded, quantifier or not,
because that column is nullable regardless of which predicate is being translated. The many-to-one
hop's own `target.id` projection is guarded the same way only when it is translating a `_none`
predicate — a plain dotted path or `_some` on a many-to-one skips the guard, since a primary key is
never NULL and there is nothing for the guard to protect against there. The many-to-many hop's inner
`target.id` projection (feeding the `<targetFk> IN (…)` half of its junction subquery) is never
guarded, `_none` included, for the same reason; only its *outer* `junction.<parentFk>` projection
gets the same `_none`-only guard the many-to-one hop's `target.id` projection does.

**Soft-delete inside a subquery never widens with `?deleted=`.** A relation subquery's
`Where(...)` is built the same way regardless of the *outer* request's own `?deleted=only|with` —
the soft-delete filter on the target collection is never cleared for it. A trashed related row never
satisfies a dotted-path or quantifier condition, even when the caller is viewing the parent's trash.

### `_junction`: filtering by the link's own payload

For a many-to-many relation whose junction carries payload (above), `_junction.<field>` is a
pseudo-segment that filters by the *link's own* field rather than either endpoint's — dotted (each
article↔tag link, independently) or inside `_some`/`_none` (the same link the rest of that
predicate matched):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":2,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags._some.name%5D%5B_eq%5D=Misc-u3doc0905&filter%5Btags._some._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<b-id>", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

`_junction` must immediately follow a many-to-many relation that has an
[exposable payload](07-relations.md#junction-payload), and must be followed by exactly one
non-`Hidden` payload field name with no further hop; it does not count toward the depth cap of 6
above. It also needs its own read grant on the *junction* collection — a separate check from the
grant on the relation's target collection. On a many-to-one, where there is no junction collection at
all, it is rejected as `'_junction' is only valid after a many-to-many relation with a junction
collection.`:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bcategory._junction.note%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'category._junction.note': '_junction' is only valid after a many-to-many relation with a junction collection."}}
```

**Combining `_some`/`_none` with plain conditions works exactly like any other filter** — including
mixing a scalar condition with a relation predicate inside `_or`, and combining a relation
quantifier with a translatable own-field leaf (`title`), which is itself pushed down through the
identical mechanism:

```
$ curl -s -X POST http://localhost:5221/api/items/article/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"_or":[{"categoryId":{"_eq":"<engineering-category-id>"}},{"tags":{"_some":{"name":{"_eq":"Misc-u3doc0905"}}}}]}}'
{"success":true,"data":[{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":2,"limit":25,"offset":0}}
```

`_some`/`_none` recurse arbitrarily: the inner filter is a complete `filter` object over the target
collection, and can itself contain dotted paths, further `_some`/`_none` (nested quantifiers), and a
single level of `_and`/`_or` — see chapter 8 for the full grammar, the JSON-envelope form, and the
query-string folding rule.

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
| `Dropdown` | `RelationPicker` (vendored `ui/combobox`) | Single-value picker; debounced `search=` against the target collection as the user types. |
| `TagSelect` | `RelationPicker` (vendored `ui/combobox`) | Multi-value picker for many-to-many relations, same search behavior. |
| `TreeSelect` | `RelationPicker` (`form/TreeSelect`) | Single-value picker over a tree built from a self-referencing many-to-one's target rows. |
| `RelatedList` | `RelatedList` (vendored `data/DataTable`) | Read-only, paginated, lazy-loaded list of the target collection filtered by the relation's reverse FK; clicking a row navigates to that row's own item-edit page. Shows only after the parent has been saved (a brand-new, unsaved parent has no id to filter by yet). |

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
