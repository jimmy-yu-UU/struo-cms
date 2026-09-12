# 14. GraphQL API

The GraphQL schema is generated from the same metadata REST reads — how to query it, how to write
to it, and where it diverges from REST are this chapter's subject.

## Schema generated from metadata

GraphQL reads the same collection metadata cache REST does, built once at startup; it's a second,
fully typed API over that metadata, and nothing in the schema is hand-written SDL. Startup logic
generates, per collection, one object type, one list wrapper, one filter input, one create input,
one update input, plus two root query fields and four root mutation fields.

A junction collection is an ordinary collection in the schema, with its own type and mutations like
any other: `Hidden` only affects the admin sidebar, and has no effect on schema generation.

Naming is entirely mechanical, so a fork can predict every generated name from the rule: types are
PascalCase, fields are camelCase, and a list field's name follows a simple English pluralization
rule — a `y` after a consonant becomes `ies`; a name ending `s`/`x`/`z`/`ch`/`sh` gets `es`;
everything else gets a plain `s`, so `category` becomes `categories`, never `categorys`. Using the
Blog sample's `article` as the example:

| Generated | Name pattern | Example |
|---|---|---|
| Object type | `<X>` | `Article` |
| List wrapper | `<X>List` | `ArticleList` |
| Filter input | `<X>FilterInput` | `ArticleFilterInput` |
| Create/update input | `<X>CreateInput` / `<X>UpdateInput` | `ArticleCreateInput` |
| Junction read type | `<X><Rel>Link` / `<X><Rel>Junction` | `ArticleTagsLink` |
| Junction write input | `<X><Rel>LinkInput` | `ArticleTagsLinkInput` |
| Relation filter input | `<X><Rel>RelationFilterInput` | `ArticleTagsRelationFilterInput` |
| Field name (junction read/write) | `<rel>Links` | `tagsLinks` |

An object type always carries `id: ID!` and `version: Long`, one field for every non-hidden,
non-excluded own field, one more for every relation, and one more, `translations: [Translation!]`,
for a collection with a translation sidecar. `version` is declared on every type, but it only
actually resolves a value for a collection that derives from the audit base; on every other
collection this field is always `null`.

A `File`/`Image` field, besides its own `ID` scalar, gets a companion field that resolves the file
itself (`heroImageId` → `heroImage`; a name with no `Id` suffix instead gets a `File` suffix
appended), and a `Files` field gets an extra `<name>Files: [File!]`. The list wrapper carries four
fixed fields: `items`, `total`, `facets`, `aggregate`.

Every other interface has a fixed SDL mapping: text-like interfaces map to `String`,
`Boolean`/`Checkbox` map to `Boolean`, `Date`/`DateTime` map to the type of the same name, `Time`
also maps to `String`, a multi-value field maps to an array, `Json`/`KeyValue` map to `Any`, and
`File`/`Image`/`Uuid` map to `ID`; `Number`/`Slider`/`Rating` look at the actual CLR type rather
than the interface itself — an integer CLR type maps to `Int` or `Long`, everything else to
`Float`.

`Tags` maps to `[TagItem!]`. `Repeater` generates its own type, `<X><Field>Item`, based on its
sub-fields (omitted entirely when there are no sub-fields); `Password`, `Hidden`, and `Divider`
never appear in the schema at all.

`Translation { locale: String!, fields: Any! }` fixes the read shape of a translation as an array,
one entry per locale — not REST's locale-keyed object; the write shape is left to this chapter's
"Translations" section.

## Endpoint and discovery

The single endpoint is `POST /graphql`, mounted in the same web application as everything REST
serves. Schema exposure runs on two separate paths, both gated by the same `GraphQl:ExposeSchema`
switch: built-in introspection (`__schema`, `__type`), and HotChocolate's own `GET /graphql?sdl` — a
safe `GET` that returns the whole schema as plain SDL text, which the CSRF middleware ignores
entirely.

Left unset, the switch defaults to open only in Development; turning it on in Production is just an
environment variable, `GraphQl__ExposeSchema=true`, with no rebuild needed.

Schema exposure and query execution are two separate concerns: an anonymous `POST /graphql` query
still returns data normally in Production. What the switch actually gates is tooling that depends
on the schema — codegen, schema import, IDE plugins — which should point at a Development or
staging environment instead. Nitro, the built-in browser IDE, ignores this switch altogether and
only ever appears in Development.

| Route | Development | Production |
|---|---|---|
| Introspection, `?sdl` | On (default) | Per `GraphQl:ExposeSchema` |
| Nitro IDE | On | Always off |

Authentication rules are shared with REST: a cookie-authenticated `/graphql` request still needs the
`X-Struo-CSRF` header, because GraphQL over HTTP is always `POST` — the rule applies equally to a
read-only query and to a mutation, see [the REST API conventions chapter](12-rest-conventions.md).

A bearer token authenticates `/graphql` the same way, even though the route carries no `[Authorize]`
of its own; a bearer-carrying caller is resolved to its own identity and needs no CSRF header
either.

## Querying: single items, lists, arguments

A single-item field is `{collection}(id: ID!, locale: String)`; an id that's missing, or already
soft-deleted, returns `null` rather than an error — GraphQL's `null` corresponds to REST's 404, same
intent, different shape.

A list field takes the plural name and nine arguments: `filter`, `sort: [String!]`, `limit: Int`,
`offset: Int`, `search: String`, `locale: String`, `deleted: DeletedFilter`, `facets: [String!]`,
`aggregate: AggregateInput` — there is no `fields` and no `deep`; projection and relation expansion
are decided entirely by the client's own selection set.

`deleted` is the SDL enum `EXCLUDE`/`ONLY`/`WITH`, the same shared enum REST uses; `ONLY`/`WITH`
go through the identical delete-permission gate.

`facets` and `aggregate` exist only on root list fields, never on a nested to-many list. The
response's `facets` is `[FacetResult!]!` — an empty array, not an omitted key, when not requested;
`aggregate` is `Any`, `null` when not requested.

`FacetResult { field: String!, values: [FacetValue!]! }` and
`FacetValue { value: Any, count: Int! }` are two shared output types, and `AggregateInput` gives
each of REST's aggregate operators its own `[String!]` field.

The `Any` scalar preserves the original JSON kind, so a numeric facet value comes back as a
number, not a string. `FacetValue.count` and a list field's `total` are both non-nullable `Int!`;
only `count` also carries an extra checked cast on top, throwing rather than silently overflowing
when a bucket count is unreasonably large — `total` just shares the same 32-bit ceiling.

The query below shows a single-item field, a many-to-one relation (with no arguments), and a
nested to-many list with its own `sort`/`limit` arguments; later examples reuse the same test
data — the id is data-specific, and your own environment will differ:

```graphql
query { article(id: "01a08f92-3a18-7c5d-99a3-5b14cd1279ea") { id status version category { name } tags(sort: ["name"], limit: 1) { name } } }
```

The actual response:

```json
{"data":{"article":{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","status":"published","version":7,"category":{"name":"Guides"},"tags":[{"name":"howto"}]}}}
```

`article` with only `id` returns that same item; `category` is many-to-one, straight to an object
with no arguments at all; `tags` is many-to-many, carrying its own `sort: ["name"], limit: 1`, so
only one comes back. The read shape of the `translations` array is already covered in this chapter's
opening section, "Schema generated from metadata." A list field taking `sort`, `limit`, `facets`,
and `aggregate` all at once is equally legal:

```graphql
query { articles(filter: { status: { eq: "published" } }, sort: ["-publishedAt"], limit: 1, facets: ["status"], aggregate: { count: ["publishedAt"] }) { total facets { field values { value count } } aggregate } }
```

The response shows `total` at 2, and `facets.status` reporting two `published` and one `draft` —
facet and aggregate math is identical to [chapter 11](11-query-advanced.md).

## Filtering

Filtering is the same semantics as chapter 10 — the same validator, the same filter tree, the
same depth and condition caps, see [Querying: Filters, Sorting and Pagination](10-query-basics.md)
— this section only lists the spelling differences:

- Operator tokens: `_eq`→`eq`, `_neq`→`neq`, `_in`→`in`, `_nin`→`nin`, `_lt`→`lt`,
  `_lte`→`lte`, `_gt`→`gt`, `_gte`→`gte`, `_contains`→`contains`,
  `_starts_with`→`startsWith`, `_ends_with`→`endsWith`.
- `_null`/`_nnull` → a synthesized `isNull: true`/`isNull: false` argument.
- `_some`/`_none` → `some`/`none`.
- `_junction.<field>` → a nested `junction: { <field>: … }`.
- `_and`/`_or` → `and`/`or`.
- A relation's filter input: a relation whose junction has an exposable payload gets an extra
  one (see below).

Beyond the spelling, two things are genuine behavior differences. The first is `isNull`: unlike
REST's `_null`/`_nnull`, this argument's value is actually read.

The second is which field interfaces can be filtered at all: REST places no restriction on which
interface can be filtered, checking only whether the field name is on the allowlist, while GraphQL
only lets scalar interfaces be filtered — `RichText`, `Markdown`, `Code`, `Json`, `KeyValue`,
`MultiSelect`, `CheckboxGroup`, `Tags`, `Files`, `File`, `Image`, and `Repeater` fields have no
filter fields in GraphQL at all.

A many-to-one relation contributes two fields to the filter input: its own foreign key as an
`IdFilter` (one of the generated scalar filter input types, dedicated to id-typed fields), plus the
target type's own nested filter input; a one-to-many or many-to-many relation contributes only the
nested filter input.

Cross-relation nested filtering flattens down to the same dotted path REST uses, and both converge
on the same filter tree. GraphQL has no sort grammar of its own — `sort: [String!]` takes the same
`-field` strings REST's `sort=` does.

`some`/`none` is a reserved word every filter input carries, and it's only meaningful inside a
relation's own inner filter. Using it on the root, or on a nested list's own `filter` argument, is
rejected (`'some' is only valid inside a relation filter.`), as is nesting one quantifier directly
inside another — express that as one level of nested relation filter instead. An empty or non-object
value is rejected too.

A nested to-many list's own `filter` argument takes the plain target filter input, not the
relation-specific one, and `some`/`none` used there is rejected the same way: each is its own
independent root.

When a many-to-many relation's junction carries an exposable payload, the field's type on the
parent's filter input switches to `<Parent><Rel>RelationFilterInput`, gaining an extra
`junction: <Parent><Rel>JunctionFilterInput` field alongside the target's own filterable fields and
its own `and`/`or`/`some`/`none`; when the junction has no filterable fields, that whole `junction`
field and its input type are omitted together.

A submitted `junction` filter flattens down to REST's `_junction.<field>`. The query below shows
both a same-row quantifier (`some` paired with `junction`) and `tagsLinks`' own read shape:

```text
$ POST /graphql
body:
{"query": "query { articles(filter: { tags: { some: { name: { eq: \"howto\" }, junction: { note: { eq: \"hero\" } } } } }) { total items { id status tagsLinks { node { name } junction { note } } } } }"}
{"data":{"articles":{"total":1,"items":[{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","status":"published","tagsLinks":[{"node":{"name":"howto"},"junction":{"note":"hero"}}]}]}}}
HTTP_STATUS:200
```

`total` is 1, the same quantifier query as chapter 10's; `tagsLinks` returns both `node` (the target
itself) and `junction` (the link's own payload).

## Nested to-many list arguments

A nested to-many relation field carries its own four arguments — `filter`, `sort: [String!]`,
`limit: Int`, `offset: Int` — computed separately from the root list field's own set, resolved by
walking the client's own selection tree into the same nested-expansion spec REST's `deep=` uses.

A many-to-one relation goes the other way: it's always an argument-free object field, since at most
one row is ever resolved; the `tags(sort: ["name"], limit: 1)` seen earlier is this argument set in
actual use.

Selecting the same relation twice at the same level — `tags` and `tagsLinks`, or a repeated alias
— merges the nested expansion, but only the first occurrence's `filter`/`sort`/`limit`/`offset`
counts; it isn't resolved twice, independently.

## Mutations: create, update, delete

Every collection gets four fixed mutations; a collection with revisions turned on gets one more,
`revert<X>`.

| Mutation | Signature |
|---|---|
| `create<X>` | `(input: <X>CreateInput!, locale: String): <X>` |
| `update<X>` | `(id: ID!, input: <X>UpdateInput!, locale: String): <X>` |
| `delete<X>` | `(id: ID!, purge: Boolean): Boolean` |
| `restore<X>` | `(id: ID!): <X>` |
| `revert<X>` | `(id: ID!, revisionNumber: Int!): <X>` |

The `locale` argument only appears on the single-item query, the list query, `create`, and
`update` — `delete`/`restore`/`revert` all have none. A create input carries writable own fields,
many-to-one foreign keys, many-to-many id arrays, and a typed translation list; an update input is
the same, plus a `version: Long`.

`create` and `update` convert the typed input back into the JSON body the item service already
consumes, then re-read that row after writing, so the returned node's shape matches what a query
would produce; when that re-read is blocked by RBAC, it falls back to the original write result
instead — a successful write is never blocked by a subsequent read-permission check that would
otherwise force the caller to misread it as a failure and retry.

HotChocolate fills every declared input field with `null` regardless of whether the caller sent
it, which would make a partial update indistinguishable from "clear everything else." So both
resolvers only pass along the keys the caller actually sent to the service layer, nested inputs
included. [The REST API conventions chapter](12-rest-conventions.md)'s rule — omitting a key keeps
the existing value, and an explicit `null` is still rejected — applies here identically.

A many-to-many relation on the create and update inputs is a plain `[ID!]` array of target ids,
sharing the same sync logic as REST.

An `AdminOnly` write still requires a super-admin, and a delete blocked by a restrict-guarded
relation still returns `CONFLICT`; `delete`/`restore` against an unknown id return `false`/`null`
rather than an error — like a query, "id doesn't exist" is data, not a fault; deleting an item
already in the trash again is also `false`. `delete<X>(purge:)` defaults to `false`, and the trash
and purge rules match REST.

GraphQL has no counterpart to the parsing quirks of `POST .../query`, `fields=`, or `?purge=`, and
no file endpoint at all — a file upload can only go through REST.

### `<rel>Links`

As soon as a many-to-many relation's junction declares at least one exposable payload field, the
schema additionally generates a more complete read/write field pair, alongside the plain-id
`<rel>`/`[ID!]`; a relation with no exposable payload never gets this pair at all.

On read, `<rel>Links: [<Parent><Rel>Link!]` sits next to `<rel>: [<Target>!]`, and the link type is
`{ node: <Target>!, junction: <Parent><Rel>Junction }` — `junction` is `null` when the caller can't
read the junction collection, which is also why this field isn't marked non-nullable.

On write, the create and update inputs gain an extra `<rel>Links: [<Parent><Rel>LinkInput!]`, and
the link input is `{ id: ID!, …writable payload fields }`. Using the Blog sample's `Article.tags`
as an example (excerpted from `GET /graphql?sdl`'s actual response):

```graphql
type ArticleTagsJunction {
  note: String
}

type ArticleTagsLink {
  node: Tag!
  junction: ArticleTagsJunction
}

input ArticleTagsLinkInput {
  id: ID!
  note: String
}
```

`Article`'s type gains two matching fields:

```graphql
tags(filter: TagFilterInput, sort: [String!], limit: Int, offset: Int): [Tag!]
tagsLinks: [ArticleTagsLink!]
```

A submitted `<rel>Links` converts to REST's mixed-array write shape; if one mutation sends both
`<rel>` and `<rel>Links` at once, `<rel>Links` wins outright — even an explicit `<rel>Links: null`
overrides a simultaneously sent `<rel>` array rather than leaving that array in place.

Selecting `<rel>Links` triggers the same expansion selecting `<rel>` would, even without selecting
the `node` sub-field; the whole extra field pair is omitted as soon as every payload field is hidden
or unmappable. Below is the actual response after sending `tagsLinks`:

```text
$ POST /graphql
body:
{"query": "mutation { updateArticle(id: \"01a08f92-3a18-7c5d-99a3-5b14cd1279ea\", input: { tagsLinks: [{ id: \"01a08f92-396e-7bf7-a77c-149c9aa732b1\", note: \"editor pick\" }] }) { id tagsLinks { node { name } junction { note } } } }"}
{"data":{"updateArticle":{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","tagsLinks":[{"node":{"name":"release"},"junction":{"note":"editor pick"}}]}}}
HTTP_STATUS:200
```

The `release` tag gets linked, with the note "editor pick." Sending the mutation again, this time
with both `tags: ["<howto's id>"]` and `tagsLinks: null`, results in `tagsLinks`' `null` winning —
the link stays unchanged, and nothing from the `tags` array's content is ever applied.

## Translations

A collection with a translation sidecar gets one more field on both create and update inputs,
`translations: [<X>TranslationInput!]`, each entry shaped
`{ locale: String!, fields: <X>TranslationFieldsInput! }` — mirroring the read side's array shape
back onto the write side exactly.

The resolver converts this array back into the locale-keyed object the write path already consumes,
applying the same "only the keys actually sent" pruning before that conversion; when the same locale
appears twice, the later entry wins.

A translatable own field never appears on the create or update input directly — it's only
reachable through this typed `translations` input; create still requires one translation for the
default locale.

Changing an article's `en` translation title and leaving everything else alone, `updateArticle`'s
returned `translations` array shows only `title` changed to the new value — the `zh-TW` entry is
completely untouched.

## Revisions

A collection declaring `Revisions = true` gets three extra fields:
`{collection}Revisions(id: ID!): [Revision!]!`,
`{collection}Revision(id: ID!, revisionNumber: Int!): Revision`,
`revert{X}(id: ID!, revisionNumber: Int!): X`.

The shared `Revision` type is
`{ revisionNumber, operation, createdAt, createdBy, sourceRevisionNumber, snapshot }`; on the list
field, `snapshot` is always `null`, and only the single-item field fills it in, matching REST;
`sourceRevisionNumber` is only ever filled on the one record a revert operation leaves behind,
and stays empty for every other operation.

The Blog sample's `article` turns this option on: `articleRevisions` returns a newest-to-oldest
metadata list with no snapshot content, `articleRevision` additionally takes a revision number and
returns the structured `snapshot` along with it, and `revertArticle` applies that snapshot and
returns the item itself, reverted.

## Error shapes

An anonymous caller sending a query with no read grant hits the thing this chapter's readers find
most surprising:

```text
$ POST /graphql (no cookie)
body:
{"query":"{ users { total } }"}
HTTP/1.1 200 OK
Content-Type: application/graphql-response+json; charset=utf-8
Date: Fri, 11 Sep 2026 08:25:38 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"errors":[{"message":"Read not permitted.","path":["users"],"extensions":{"code":"UNAUTHORIZED"}}],"data":null}
HTTP_STATUS:200
```

The response status is `200`, not `401`; `data` is `null`, and the error itself sits inside the
`errors` array, with `extensions.code` at `UNAUTHORIZED`. A caller has to check each error's own
`extensions.code`, never the status line.

GraphQL's error filter is the counterpart of REST's exception handler: it folds whatever exception a
resolver throws into the same lookup table, stamping on the identical stable code string, but the
transport-layer status always stays at `200`. `SEARCH_UNAVAILABLE` works the same way — REST maps it
to 503, and here it's still `200` paired with this code.

An exception with no entry in the lookup table is masked to a fixed, generic message, still fully
logged server-side; a search-provider failure is masked the same way.

There's a second kind of failure on a different path entirely. A document that doesn't parse at
all, names a field that doesn't exist, or violates the depth or cost rules fails before any
resolver ever runs, carries no exception, and so has no `extensions.code` — HotChocolate returns
plain HTTP `400`:

```text
$ POST /graphql
body:
{"query": "query { bogusThing { id } }"}
HTTP/1.1 400 Bad Request
Content-Type: application/graphql-response+json; charset=utf-8
Date: Fri, 11 Sep 2026 08:25:38 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"errors":[{"message":"The field `bogusThing` does not exist on the type `Query`.","locations":[{"line":1,"column":9}],"extensions":{"type":"Query","field":"bogusThing","responseName":"bogusThing","specifiedBy":"https://spec.graphql.org/September2025/#sec-Field-Selections"}}]}
HTTP_STATUS:400
```

The error message names the nonexistent field `bogusThing`, but carries no `extensions.code` —
that's the dividing line for whether a GraphQL error should be read by status code or by
`extensions.code`: a request-level structural error is `400` with no code, while an exception a
resolver throws at execution time is `200` with a code.

Every REST validation message listed in chapter 10 and [chapter 11](11-query-advanced.md) — an
unknown field, an unknown relation, exceeding the depth cap, an unreadable relation — is stamped
`BAD_USER_INPUT` on GraphQL too, since both protocols share the same validator, taking the first
path: `200` paired with a code.

## Depth caps

Three independent caps each guard their own concern:

| Limit | Value | Guards against |
|---|---|---|
| Execution depth | 12 levels | Selection sets nested too deep; introspection fields exempt |
| Cost (`MaxFieldCost`/`MaxTypeCost`) | 150 | Alias amplification: one costly field under many aliases |
| Relation path depth (`Query:MaxRelationDepth`) | 6 by default | Cross-relation filters and expansion; shared with REST |

## What's next

Upload, download, and transcoding all stay on the REST side; files and media are the next topic.
