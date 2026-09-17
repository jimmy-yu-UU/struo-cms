# 8. Relations

This chapter covers how one collection refers to another, how the referenced data comes back on
read, and what gets cleaned up when the target is deleted.

## Three kinds of relation

Every relation is declared on a navigation property carrying two attributes together: SqlSugar's
`[Navigate]` decides the join shape, and `[CmsRelation]` handles everything else CMS-specific.
Both are required — a property missing either one is skipped entirely by the scanner, not
rejected as an error.

`RelationKind` has exactly three values — `ManyToOne`, `OneToMany`, `ManyToMany` — and the
scanner infers which applies from the property's own shape: a scalar or nullable-scalar reference
is many-to-one; a list property defaults to one-to-many, and counts as many-to-many only when
`[Navigate]`'s first argument is a type.

### Many-to-one: `File.Folder`

The framework and the sample always use `NavigateType.OneToOne` for a many-to-one relation and
`NavigateType.OneToMany` for a one-to-many one, but that constant isn't what actually decides the
kind — the scanner only looks at whether the property is a list, and whether `[Navigate]`'s first
argument is a type. The framework's own many-to-one example is `File.Folder`:

```csharp
[SugarColumn(IsNullable = true)]
public Guid? FolderId { get; set; }

[Navigate(NavigateType.OneToOne, nameof(FolderId))]
[CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
[SugarColumn(IsIgnore = true)]
public MediaFolder? Folder { get; set; }
```

`MediaFolder.Parent` declares the identical pattern, used to model a folder tree.

A many-to-one relation must resolve a foreign key, and its target collection must actually exist,
or startup fails: `M2O relation '{collection}.{relation}' has no foreign key.`, `Relation
'{collection}.{relation}' targets unknown collection '{target}'.`

A self-referencing many-to-one (such as `MediaFolder.Parent`) carries an extra `SelfReferencing`
flag in its metadata, and gets an extra cycle guard on write: it walks the incoming parent's
ancestor chain — even an ancestor already in the trash still counts — and rejects an update that
would close a cycle. Create is exempt. That guard's own walk cap is 64 levels, purely as a
defensive stop; it has nothing to do with the query DSL's own relation-depth cap.

### One-to-many: the sample's `Category.Articles` and `Category.Children`

The ready-made example of one-to-many lives in the sample — `Category.Children` and
`Category.Articles`; the same declaration works identically in your own collections:

```csharp
[Navigate(NavigateType.OneToMany, nameof(ParentId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Category> Children { get; set; } = [];

[Navigate(NavigateType.OneToMany, nameof(Article.CategoryId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
[SugarColumn(IsIgnore = true)]
public List<Article> Articles { get; set; } = [];
```

A one-to-many relation carries no foreign key of its own: the metadata's `ForeignKey` is the
reverse foreign-key property name, read off the child side's `[Navigate]` argument, so the query
layer knows which column on the target collection to filter back by.

### Many-to-many: `User.Roles`

The framework's own many-to-many is `User.Roles`, junctioned through `UserRole`:

```csharp
[Navigate(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
[CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Role> Roles { get; set; } = [];
```

On write, a many-to-many relation accepts a plain array of target ids, keyed by the relation's own
camelCase name; every id in the array is checked against the target collection before the sync
runs, and an unknown id rejects the whole batch: `One or more ids in '{relation}' do not exist in
'{target}'.`

The sync diffs the incoming array against the junction rows already on file: rows whose target has
disappeared are deleted, rows for new targets are added, and rows for targets that remain keep
their original junction primary key rather than being dropped and recreated. The three cases each
resolve differently:

- The relation key is absent from the body: skipped entirely.
- The key is present but its value isn't an array: also skipped.
- The key is sent as an explicit `[]`: every junction row for that relation is deleted.

A many-to-one foreign key is never checked for existence on write — only many-to-many target ids
are. This schema layer also carries no database-level foreign-key constraints at all:
`OnDelete` and every reference check happen in application code, so a fork's own ETL or direct
database access bypasses all of it and can leave a dangling child row behind. A relation's label in
metadata is always the PascalCase CLR property name; no attribute member can override it.

## `[CmsRelation]` options

Every `[CmsRelation]` setting is a named parameter; there is no constructor. The first four decide
display and target picking:

| Member | CLR type | Default | Effect |
|---|---|---|---|
| `Interface` | `RelationInterface` | `Dropdown` | which admin picker is used |
| `DisplayTemplate` | `string?` | `null` | `{Field}` template; falls back to id when unresolved |
| `PickerQuery` | `string?` | `null` | saved to metadata; not read by the admin SPA |
| `SortField` | `string?` | `null` | many-to-many only; the junction's sort column |

The last four decide write and delete behavior:

| Member | CLR type | Default | Effect |
|---|---|---|---|
| `OnDelete` | `OnDelete` | `Restrict` | many-to-one only |
| `Editable` | `bool` | `true` | whether the admin picker accepts input |
| `DisplayColumns` | `string?` | `null` | no effect |
| `MaxDepth` | `int` | `1` | no effect |

Neither `DisplayColumns` nor `MaxDepth` is read into `RelationMetadata`, so neither reaches
`/api/schema`. There's no field for a per-relation depth override at all — the one cap that's
actually enforced is the global `Query:MaxRelationDepth`; see "Reading related data with `deep`"
below.

## Junction entities and payloads

A junction is a plain SqlSugar entity and doesn't need to be a `[CmsCollection]` at all; whether it
counts as many-to-many comes from the owning side's `[Navigate(typeof(Junction), ...)]`, and the
junction type itself carries no `[CmsRelation]`.

Every junction type must declare exactly one `[SugarColumn(IsPrimaryKey = true)]` property — the
sync updates junction rows by that key — and a composite or missing key fails at startup:
`Junction type '{type}' (used by '{owner}.{relation}') must declare exactly one
[SugarColumn(IsPrimaryKey = true)] property; the M2M sync updates junction rows by primary key.`

Uniqueness across a junction's two foreign keys isn't derived automatically: `UserRole` declares
its own unique index by hand, and the sample's `ArticleTag` declares none at all — without one,
there's no database-level duplicate protection.

When a junction type also carries `[CmsCollection]`, it becomes a junction collection: a readable
and writable collection in its own right. Aside from its two foreign keys, the field named by
`SortField`, and any `IsSystem`/`ReadOnly` field, every one of its own `[CmsField]`s counts toward
this relation's payload — data that belongs to the link itself, not to either endpoint.

That list is computed in exactly one place, so the same payload fields show up consistently on
write, in the `_junction` projection, in revision snapshots, and in the GraphQL links surface.

Both of a junction collection's foreign keys must be declared as writable `[CmsField]`s, or startup
fails; the requirement is writability, not a particular interface — `UserRole` declares both as
`Text`, the sample's `ArticleTag` as `Uuid`: `Junction collection '{junction}' (used by
'{owner}.{relation}') must declare its foreign keys '{fkA}' and '{fkB}' as writable [CmsField]s
(e.g. Interface = FieldInterface.Uuid); otherwise items created through the API store empty keys.`

A junction collection can carry `Hidden` too, and it behaves exactly like a collection-level
`Hidden` ([Chapter 5: Defining Collections](05-collections.md)): it only removes the junction
collection from the admin sidebar. It still appears fully in `/api/schema`, in the RBAC permission
matrix, and in the generated GraphQL schema. `UserRole` additionally carries `AdminOnly = true`,
which the sample's `ArticleTag` does not — so the two differ in who can write to them.

The relation's own schema entry names the junction collection, its payload field names, and its
sort field, which is how a caller discovers that it also needs a separate write grant on the
junction collection.

The sample's `ArticleTag` is exactly such a junction collection:

```csharp
[CmsCollection("Article tag", Icon = "tag", Group = "Content", Hidden = true)]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Article", Interface = FieldInterface.Uuid, Required = true, Sort = 1)]
    public Guid ArticleId { get; set; }

    [CmsField(Label = "Tag", Interface = FieldInterface.Uuid, Required = true, Sort = 2)]
    public Guid TagId { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Note", Interface = FieldInterface.Text, MaxLength = 200, Sort = 3)]
    public string? Note { get; set; }

    [CmsField(Label = "Sort", Interface = FieldInterface.Number, Sort = 4)]
    public int Sort { get; set; }
}
```

Of these four fields, only `Note` counts as payload: the two foreign keys are the link itself, and
`Sort` is the `SortField` for `Article.Tags`. `ArticleTag` also carries `Hidden = true`. A payload
field's `MaxLength` error is prefixed with the relation and the target: `Relation 'tags', target
'<id>': Field 'note' exceeds maximum length 200.`

GraphQL additionally exposes `<rel>Links: [<Parent><Rel>Link!]`, reading the same payload, with
each entry shaped `{ node, junction }`; the original `<rel>` field is untouched.

An element of the write array can be a bare id (link this target, leave its junction row's payload
untouched), or an object `{ id, ...payload }` (link it and merge the named payload fields) — any
other shape is rejected. When the same id appears more than once, an object beats a bare id, a
later object beats an earlier one, and the order of first appearance becomes the sort order.

The junction's sort column is always rewritten to that element's index in the merged list, so
array order is the sort order.

Junction payload never enforces `Required`: an element that omits a field keeps its stored value.
The junction's write grant — the junction collection's own write grant, plus super-admin when it's
`AdminOnly` — is checked before any payload value is bound, so a caller without it gets a `403`
rather than a `400` leaking out of the payload's own field rules.

## Every `OnDelete` value

`OnDelete` decides what happens when the target is deleted. Trashing (soft delete) and purging
(permanent removal) are two different moments — the table below describes purge behavior.
`OnDelete` only applies to many-to-one relations; declaring it on a one-to-many or many-to-many
relation is ignored.

| Value | Behavior |
|---|---|
| `Restrict` (default) | rejects the delete while referenced; returns `409 CONFLICT` |
| `SetNull` | nulls every referencing row's foreign key before purge |
| `Cascade` | recursively cleans up every row that references it |

`Restrict`'s message names the collection: `Cannot delete '{collection}/{id}': referenced by
'{sourceCollection}'.` The same guard also runs on a plain trash, and again at every recursion
level of a purge. The check takes no row lock, so a reference committed by another transaction in
between can still race past it.

A many-to-one that a junction entity declares on its own — pointing at a third collection, say —
counts as an ordinary many-to-one too: it defaults to `Restrict`, so that third collection's row
can't be deleted while any junction row still references it.

`SetNull` only runs at purge; a plain trash of the target leaves referencing rows untouched. The
sample's `Article.Category` and `Category.Parent` both declare `OnDelete.SetNull`.

`Cascade` also cleans up each referencing row's own junctions, translations, and revision history,
so a cycle can't send it into infinite recursion. Referencing rows are read with the soft-delete
filter bypassed, so a referrer already in the trash gets cascaded too, not skipped. Neither the
framework nor the sample declares any relation as `Cascade`.

## Reading related data with `deep`

There's only one parameter for expanding relations: `deep`. Expansion is batched per relation per
page — one extra query per relation, not one per row — and deliberately avoids the ORM's own
eager-include: a many-to-one costs one query, a one-to-many also one, and a many-to-many two (the
junction rows, then the targets).

The query-string form is single-level and names relations only: `?deep=folder,tags`, comma
separated. Per-relation field selection, a nested filter, a sort, `limit`/`offset`, and further
nesting only exist in the JSON envelope form of the query endpoint. Passing a filter, sort, limit,
or offset for a many-to-one relation is an error, not a silent no-op: `filter/sort/limit/offset are
only supported on to-many relations; '{relation}' on '{collection}' is many-to-one.`

A nested `limit` is clamped to `Query:MaxLimit`; without one, a relation returns every row for
every parent on the page — expanding a to-many relation with no limit is unbounded.

Per-parent sorting and pagination happen in memory over the rows already fetched, which is
exactly what keeps the batched fetch free of the N+1 problem: `null` sorts first, and a value
that can't be compared is treated as equal to keep the order stable.

With no nested sort, a one-to-many keeps fetch order and a many-to-many keeps junction order —
when the relation declares no `SortField`, or the column isn't numeric, it degrades to insertion
order rather than failing.

The whole `deep` tree is validated before any query actually runs, independent of row count — an
over-depth or unknown relation name fails even on an empty page: `Relation nesting too deep (depth
{n}); the maximum is {max}.`, `Unknown relation '{name}' on '{collection}'.` A negative nested
`limit`/`offset` is rejected, and a dotted nested sort path (crossing relations) is rejected too.

The relation-path depth cap is controlled by `Query:MaxRelationDepth`, default 6; see
[Chapter 4: Configuration Reference](04-configuration.md) for how to set it.

Both `deep` and a dotted filter or sort path need a read grant on every collection along the way,
but they differ in what happens without one: `deep` silently omits a relation the caller can't
read, while a dotted filter or sort path is refused outright: `Read not permitted on '{target}'.`
That refusal happens before the path is even parsed, so it can't be used to infer what fields an
unreadable collection has.

Pruning runs before validation: a nested filter or sort on an unreadable relation is never checked
against that collection's metadata, and if pruning removes every requested relation, the request
still succeeds with no expansion and no error.

A one-to-many expansion looks like this, with `articles` an array of full item projections:

```text
$ GET /api/items/category/01a08c68-5681-7e60-ade2-3f277347cc19?deep=articles
{"success":true,"data":{"id":"01a08c68-5681-7e60-ade2-3f277347cc19","version":0,"name":"Tutorials","createdAt":"2026-09-10T17:40:43.266118","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.266208","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","articles":[{"id":"01a08c68-57d6-7f25-80e7-c42a2a1702a4","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-10T17:40:43.607052","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.607133","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858"}]}}
HTTP_STATUS:200
```

When `deep` expands a many-to-many relation whose junction carries a payload, each expanded target
row gains an extra `_junction` object holding that row's non-`Hidden` payload values, keyed by the
fields' camelCase API names; a relation whose junction carries no payload gets no `_junction` key at
all.

`_junction` is omitted entirely — never sent as `null` — when the caller holds no read grant on
the junction collection, the same "omit, don't fail" stance `deep` takes for any unreadable
relation. `_junction` is a fixed, reserved key, not derived from the junction collection's own
name, and it's reachable only through `deep`.

A many-to-many expansion with payload looks like this, with each of `tags`' entries gaining a
`_junction`:

```text
$ GET /api/items/article/01a08c68-57d6-7f25-80e7-c42a2a1702a4?deep=tags
{"success":true,"data":{"id":"01a08c68-57d6-7f25-80e7-c42a2a1702a4","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-10T17:40:43.607052","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.607133","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","tags":[{"id":"01a08c68-56ed-7736-9b4a-bdd351d6b95b","version":0,"name":"howto","createdAt":"2026-09-10T17:40:43.374315","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.374443","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","_junction":{"note":"primary tag"}}],"translations":{"en":{"title":"Getting started with StruoCMS","body":"<p>Run the API and the admin SPA.</p>","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null},"zh-TW":{"title":"開始使用 StruoCMS","body":"<p>先把 API 與後台跑起來。</p>","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}}
HTTP_STATUS:200
```

## Filtering on relations (overview)

A filter field path with a dot in it is a relation path, and the whole condition is pushed down
into a nested subquery rather than resolved by first fetching an id set — so adding more relation
conditions never increases the query count. A path that points at a nonexistent relation is
rejected the same way as one pointing at a nonexistent field: `Unknown relation '{name}' on
'{collection}' in path '{path}'.`

Two dotted conditions can each be satisfied by a different relation row; requiring them to land on
the same row needs the "at least one" / "none" quantifier form instead. A junction's own payload has
a separate filtering syntax of its own, and sorting across a relation only works when the whole path
is many-to-one. The full filter and sort syntax is left to the query DSL and advanced query
chapters.

## The relation picker and junction list editor in the admin SPA

`RelationInterface` has four values — `Dropdown`, `TagSelect`, `TreeSelect`, `RelatedList` — and
the admin SPA wires each one to its own input component:

- `Dropdown`: a single-value dropdown, running a debounced search against the target collection.
- `TreeSelect`: a single-value tree picker, with the tree built from a self-referencing many-to-one
  relation.
- `RelatedList`: a read-only, paginated, lazily loaded list of the target collection, filtered by
  the reverse foreign key; each row navigates to that row's own edit page, and the list appears only
  once the parent row has been saved.
- `TagSelect`: splits into two looks depending on whether the junction carries a visible payload or
  a `SortField` — see below.

`TagSelect` is a plain multi-value tag picker when the relation's junction has no visible payload
and no `SortField`; as soon as either is true, it becomes a per-row links editor instead. In the
links editor, each selected target gets its own row, showing:

- that link's visible payload fields, rendered with the same field-type registry the item form uses
- a remove button
- reorder controls, which appear only when the relation declares a `SortField`

The dropdown underneath only adds and removes members.

Authorization has three tiers:

- No read grant on the junction: the row still appears, but no payload field is shown at all —
  the server already omits `_junction`, so there are no values to show.
- Read but no write grant (or an `AdminOnly` junction and a caller who isn't a super-admin): payload
  fields render read-only, and saving sends only bare ids — only membership and, when the relation
  has a `SortField`, order get updated.
- Otherwise: the payload is editable.

The parent form's own disabled state overrides all of the above.

A server-side payload validation error lands in the form's top-level error message; required and
maximum-length checks are pre-checked by the frontend before submit. A numeric or boolean payload
field left blank is saved as `null`, not an empty string, so such a junction field should either be
declared nullable or marked `Required` — the form blocks an empty required field before it's sent.

A hidden junction payload field never gets an editable version in the admin SPA at all, because the
API never sends it in `_junction` in the first place.

## What's next

With relation reads and writes settled, the next step is how versions get kept and how something
soft-deleted gets found again — the subject of
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md).
