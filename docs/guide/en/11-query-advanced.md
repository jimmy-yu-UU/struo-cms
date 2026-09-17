# 11. Querying: Projection, Deep Expansion, Facets and Aggregates

The same list query can also ask for fewer fields back, expand relations a few levels further, and
compute a few summary numbers along the way — these are this chapter's three topics, and all three
build on [the query basics chapter](10-query-basics.md)'s query interface.

## Field projection `fields=`

`fields=` only restricts which of the collection's own fields get projected; `id` always comes
back, whether or not it's listed in `fields=`. A collection that inherits `AuditableEntity` also
always returns `version`, used for optimistic concurrency — a collection that only implements
`IAuditable`, or inherits no auditing base at all, never has a `version` key in any response.

`translations`, and any relation expanded by `deep`, are attached after projection — they show up
whether or not `fields=` names them:

```text
$ GET /api/items/article?fields=id,status
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":4,"status":"draft","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}},{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}},{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","version":7,"status":"published","translations":{"en":{"title":"Getting started","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null},"zh-TW":{"title":"開始使用","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":3,"limit":25,"offset":0}}
HTTP_STATUS:200
```

Even though only `id` and `status` were asked for, all three articles still carry `version` and the
full `translations` — `fields=` governs neither. The later examples in this chapter reuse the same
test data; the ids come from that data, so yours will differ.

The JSON envelope does the same thing; the key is `fields`, a string array — not `field`, not
`select`: `{"fields":["id","status"]}`.

`fields=` only projects the collection's own scalar fields; a relation path is rejected outright:

```text
$ GET /api/items/article?fields=category.name
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation paths are not supported in field selection: 'category.name'."}}
HTTP_STATUS:400
```

The whole `category.name` path is rejected — it isn't silently dropped, and it doesn't fall back to
projecting just `category`.

`fields=` only applies to the two list-shaped reads: the list endpoint and `POST .../query`. A
single-item read, `GET /api/items/{collection}/{id}`, only reads `deep` from the query string and
always projects every visible field of its own — `fields=` is silently ignored there.

## Deep expansion `deep=`

From the query grammar's point of view, `deep` is just another field on the model, processed after
filtering, sorting, and pagination are all done — its subject is the page of parent rows already
fetched, not the whole table.

That means a nested `filter` under `deep` only filters out the expanded relation rows; it never
shrinks the parent page itself: `{"deep":{"tags":{"filter":{...}}}}` reads like "only return
articles with a matching tag," but it isn't — this filter is applied only after the parent page is
already fixed. [Chapter 8: Relations](08-relations.md) already covers how the whole `deep` tree is
validated before any query actually runs, independent of row count.

A nested relation's own `filter` and `sort` go through the same `QueryValidator`, checked against
the target collection's own metadata.

A nested `sort` can't be a dotted path; that trips its own message, `Sort across relations is not
supported for nested lists: '<field>'.` It is not the message a top-level `sort=` gives on a to-many
path — that one is `Sort across to-many relations is not supported: '<path>'.`, covered in [the
query basics chapter](10-query-basics.md). One governs sorting inside a nested list, the other
top-level sorting.

The junction's payload (the `_junction` key) is already covered in
[Chapter 8: Relations](08-relations.md); it shows up again in this chapter's complete example at
the end.

**The depth cap.** The `deep` tree's depth cap shares the same configuration key as filtering and
sorting: `Query:MaxRelationDepth` (default 6); see
[Chapter 4: Configuration Reference](04-configuration.md) for how to change it.

The cap counts every hop, the first and the last included: `category` plus five more `parent` hops
is six relation hops, which validates and runs normally; only a seventh hop is rejected, with
`Relation nesting too deep (depth {n}); the maximum is {max}.`. Here is a request that hits the
seventh hop:

```text
$ POST /api/items/article/query
body:
{"deep":{"category":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{}}}}}}}}}}}}}}}}
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation nesting too deep (depth 7); the maximum is 6."}}
HTTP_STATUS:400
```

`category` plus six more `parent` hops is seven relation hops total; the depth is 7, over the cap
of 6, so it's rejected. Dropping the innermost `parent` back down to six hops validates and
responds normally.

## Facet counting

`facets=` computes an extra summary distribution over the same filtered result set; it never
changes `data` or pagination. When it isn't requested, nothing is computed at all — the key itself
is absent from `meta`, not present as `null`.

In the query string it's a comma list; in the JSON envelope it's a string array. A duplicate path
in the list is deduplicated down to its first occurrence — it's neither rejected nor counted twice.
Duplicates are detected by an exact comparison (`StringComparer.Ordinal`), not by the
case-insensitive matching field names normally get, so `status` and `Status` are two different paths
and each is counted on its own.

### Four path forms

A facet path takes exactly one of four forms; a facet path can cross at most one relation hop, and
it can never be a quantifier or a `_junction` segment:

| Form | Example | Grouped by |
|---|---|---|
| Own scalar field | `facets=status` | the field itself |
| Many-to-one foreign key | `facets=categoryId` | the root row's own foreign-key field |
| Bare relation name | `facets=tags` | the target id |
| One-hop relation + leaf field | `facets=category.name` | first by target id, then by leaf value |

For the bare-relation-name form, the target id is the foreign key for many-to-one, the child row's
own id for one-to-many, and the junction's target foreign key for many-to-many.

These field interfaces can be used as a facet; own fields and leaf fields share the same list:

- Text-like: `Text`, `Textarea`, `Slug`, `Email`, `Url`, `Color`, `Phone`.
- Choice-like: `Select`, `Radio`, `Boolean`, `Checkbox`.
- Numeric: `Number`, `Slider`, `Rating`.
- Date and identity: `Date`, `DateTime`, `Time`, `Uuid`, `File`, `Image`.

Long text (`RichText`/`Markdown`/`Code`), multi-value interfaces
(`MultiSelect`/`CheckboxGroup`/`Tags`/`Repeater`), structured data (`Json`/`KeyValue`/`Files`), and
`Hidden`/`Divider`/`Password` can't be used as a facet. A hidden field gets exactly the same message
as a field that doesn't exist, so a facet path can't be used to probe whether a hidden field exists.

For the one-hop-relation-plus-leaf-field form, a translatable leaf field always needs a valid query
locale — without `locale=`, the site default is used, so a value is always available.

### Pruning: why a facet counts rows that were filtered out

A facet answers "if I switched this path to each of its candidate values in turn, how many rows
would still match everything else in the request?" — not "how many rows in this result set already
carry this value?"

To get that, every facet computation first drops the conditions in its own family:

- Own-field facet: drops that field's own condition.
- Many-to-one foreign-key facet: drops the foreign-key field's own condition, any path starting
  with `<relation>.`, and any `_some`/`_none` on that relation. `categoryId` and `category.name`
  count as the same family.

This pruning only treats a many-to-one's own foreign-key field as the family's root — the foreign
key a one-to-many relation declares belongs to the child row, so a root-level filter that happens
to share the same name is never swept in.

Dropping conditions all the way down can leave `filter` with nothing left; an `_and`/`_or` group
emptied out this way collapses along with it, and pruning everything away leaves the request
equivalent to having no `filter` at all.

`search=` is never pruned either — a facet only prunes its own copy of `filter`; pruning and
`search` are separate, independent steps.

```text
$ GET /api/items/article?filter[categoryId][_eq]=01a08f92-3833-750e-bad3-6ee620f985c3&filter[status][_eq]=published&facets=status
{"success":true,"data":[...],"meta":{"total":2,"limit":25,"offset":0,"facets":{"status":[{"value":"published","count":2},{"value":"draft","count":1}]}}}
HTTP_STATUS:200
```

`data` above has been replaced with `...` — the point here is `meta`; the full content of the two
articles doesn't matter to this section. `data` and `total` only cover the two published articles,
matching what `filter` says — but `facets.status` still reports `published: 2`, `draft: 1`, because
`status`'s own condition was dropped before that facet was computed, so the draft still sitting in
the `Guides` category (`categoryId=<Guides>`) gets counted too.

You can check any of this yourself: for an own-field facet, `{"value": v, "count": n}` must equal
`meta.total` for the same request with that facet's own condition swapped for
`filter[<field>][_eq]=v`.

`count` counts distinct root rows, not raw rows on the relation table.

The to-many forms group by target id and count with `COUNT(DISTINCT ...)`: a root row linked to
two targets that happen to share a value is never counted twice. Own fields and foreign keys don't
need `DISTINCT` — each root row only ever falls into one group anyway.

The one-hop-relation-plus-leaf-field form is the exception. It runs an id/count query first, then a
separate leaf-value query, and merges groups sharing the same value in memory — a root row linked
to two targets that happen to share the same leaf value is counted twice in this case.

`Query:MaxFacetValues` (default 50) truncates once on the first stage's target-id grouping, and
again after merging — so even when the target collection has fewer distinct leaf values than the
cap, two values that only become equal after merging can still lose one of them to that first-stage
truncation.

`deleted=` only governs the root row itself: whenever a facet reaches the other end of a relation,
it always keeps the target collection's own soft-delete floor regardless of the outer request's
`deleted=`. The many-to-one foreign-key form is the one exception, because it counts the root
row's own field — a target already sitting in the trash still has its id counted.

If a fork swaps out `search=`'s candidate-id source (a search-provider topic covered in
[Chapter 18: Extension Points: Search Providers and Change Listeners](18-extension-points.md)),
facets and aggregates still see the same candidate set with the `deleted=` floor already applied —
swapping the source can't leak a row sitting in the trash.

### NULL bucket, ordering, and count caps

Own fields and many-to-one foreign keys both get their own `NULL` bucket, because the grouping field
in both cases is already part of the root row. The bare-relation-name form never gets a "no
relation" bucket — building one would need an anti-join, which the implementation doesn't have; the
question "how many rows have no relation" is already answered by `_none`.

The one-hop-relation-plus-leaf-field form's `null` carries a third, easiest-to-confuse meaning: the
target id resolved fine, there's just no translation row for this locale — it doesn't mean "no
target."

A non-translatable leaf field has a fourth case: if the target row itself has been hard-deleted, or
the id doesn't resolve to any row, that group is dropped entirely — it never gets merged into the
`null` bucket.

Every facet is ordered by `count` descending, then `value` ascending, and truncated at
`Query:MaxFacetValues` (default 50) — there's no `otherCount` remainder.

For the own-field, foreign-key, and bare-relation-name forms, ordering and truncation both happen
in the database, and where `NULL` lands on a tie is up to the database. The
one-hop-relation-plus-leaf-field form re-sorts and re-truncates its merged groups in memory, and
`null` always sorts last there, regardless of the database engine.

`Query:MaxFacets` (default 10) limits how many facet paths one request can list;
`Query:MaxAggregates` (default 10) limits the total op/field combinations an aggregate can request.
Both are set in [Chapter 4: Configuration Reference](04-configuration.md).

### Cost

A plain list query runs two SQL statements — one `COUNT`, one paginated `SELECT`. Facets and
aggregates leave those two alone; each just runs a few more of its own:

- Own field, foreign key, or bare relation name: one extra query each.
- One-hop relation plus leaf field: two extra queries; if the target collection is soft-deletable
  and the leaf field is translatable, one more query first checks which ids survive — three in
  total.
- Bare relation name: if the target collection is soft-deletable, one more query filters out
  targets already in the trash; one-to-many doesn't need this, because its own query already
  carries the soft-delete filter.

Aggregates are batched 10 op/field combinations at a time; the batch size is a hard-coded constant,
not `Query:MaxAggregates` — the default cap just happens to also be 10, so a typical request only
adds one query, and raising the cap is what splits it into more batches.

## Aggregates `aggregate[<op>]`

Like a facet, an aggregate is only computed when requested, and it never changes `data` or
pagination. In the query string, each op is its own key with a comma-separated field list as its
value — `aggregate[sum]=price&aggregate[max]=price,rating`; in the JSON envelope it's an object,
keyed by op, whose value is an array of field names. The five ops, the interfaces each allows, and
each one's empty result:

| Op | Allowed interfaces | Empty result |
|---|---|---|
| `count` | any own field (including many-to-one foreign keys) | `0` |
| `sum`/`avg` | `Number`, `Slider`, `Rating` | `null` |
| `min`/`max` | those three plus `Date`, `DateTime` | `null` |

`sum` keeps the field's own numeric type family — an integer widens to a bigger type, `decimal`
stays `decimal`, and `double`/`float` become `double`; `avg` is always `double`, regardless of the
field's type. A many-to-one foreign key only supports `count`: it has no `[CmsField]`, so the
validator falls back to the `Uuid` interface, which lands on neither the numeric nor the date side —
deliberately, since summing or averaging a foreign key means nothing.

An aggregate is never pruned, no matter what facets the same request also asks for — it always runs
against the request's complete, unpruned `filter`:

```text
$ GET /api/items/article?aggregate[count]=publishedAt&aggregate[max]=publishedAt
{"success":true,"data":[...],"meta":{"total":3,"limit":25,"offset":0,"aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-06-01T00:00:00"}}}}
HTTP_STATUS:200
```

`data` is again replaced with `...` — the point here is also `meta`. Only two of the three articles
have a `publishedAt`, so `count` reports 2; `max` is the later of those two timestamps — the draft
among the three has no `publishedAt`, so it doesn't affect either result.

## Errors

Every facet and aggregate validation error is `BAD_USER_INPUT`/400, with one exception: a
read-grant refusal, which — like the relation paths in
[the query basics chapter](10-query-basics.md) — is `FORBIDDEN`, or `UNAUTHORIZED` for an
anonymous caller. The error envelope's full shape is left to the next chapter. Below is each
message and what triggers it.

- An own field that doesn't exist, or a single-segment path that doesn't resolve: `Unknown field
  'bogusField' on collection 'article'.`
- More than one relation hop: `Facet paths support exactly one relation hop:
  'category.parent.name'.`
- An interface that can't be used as a facet: `Field 'regions' on collection 'article'
  (MultiSelect) cannot be used as a facet.`
- An aggregate op incompatible with the field's interface: `Aggregate 'sum' is not supported on
  field 'status' (Select).`
- An empty segment in the path: `Facet path must not be empty.`
- In the one-hop-relation-plus-leaf-field form, a relation segment that doesn't resolve: `Unknown
  relation 'bogusRelation' on collection 'article'.` — a different message from an own field that
  doesn't resolve.
- A segment in the path that's a quantifier or `_junction`: `Facet paths cannot contain quantifiers
  or '_junction': '<path>'.`
- Over a cap: `Too many facets (max 10).`, `Too many aggregate fields (max 10).`
- The relation segment's target collection isn't readable:
  `Read not permitted on '<collection>'.` — this one is a `FORBIDDEN`, or an `UNAUTHORIZED` for an
  anonymous caller, not a `BAD_USER_INPUT`; this check runs before the path form is parsed, so the
  error can't be used to infer which fields an unreadable relation has.
- A misspelled aggregate op name: `Unknown aggregate op 'bogus'.`
- Type checks on the JSON envelope's shape: `'facets' must be an array of strings.`, `'aggregate'
  must be an object.`, `'aggregate.<op>' must be an array of strings.`; the query string adds one
  more: `Malformed aggregate key '<key>'.`

## A complete example

Put `filter`, `sort`, `limit`/`offset`, `fields`, `deep`, `facets`, and `aggregate` all in the same
request, and each one's rules still hold — none of them interfere with each other:

```text
$ GET /api/items/article?filter[status][_eq]=published&sort=-publishedAt&limit=1&offset=0&fields=id,status&deep=category,tags&facets=status&aggregate[count]=publishedAt
{"success":true,"data":[{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","category":{"id":"01a08f92-3833-750e-bad3-6ee620f985c3","version":0,"name":"Guides","createdAt":"2026-09-11T08:25:19.670132","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:19.670271","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"},"tags":[{"id":"01a08f92-38f5-7f1b-a478-f8a1140f0b0b","version":0,"name":"howto","createdAt":"2026-09-11T08:25:19.861593","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:19.86169","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","_junction":{"note":null}},{"id":"01a08f92-396e-7bf7-a77c-149c9aa732b1","version":0,"name":"release","createdAt":"2026-09-11T08:25:19.98309","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:19.98318","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","_junction":{"note":"hero"}}],"translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":2,"limit":1,"offset":0,"facets":{"status":[{"value":"published","count":2},{"value":"draft","count":1}]},"aggregate":{"count":{"publishedAt":2}}}}
HTTP_STATUS:200
```

`meta` carries every summary for this request at once: `total` is 2, the row count after filtering,
not the row count on this page; `limit`/`offset` are the pagination values in effect after
clamping; `facets.status` still reports `published: 2`, `draft: 1`, unaffected by `filter[status]`'s
own condition, because that condition was dropped before this facet was computed;
`aggregate.count.publishedAt` counts against the complete, unpruned `filter`.

`fields=id,status` didn't block the two expanded relations `category` and `tags`, and it didn't
block `version` or `translations` either — `fields=` governs none of these four.

The extra `_junction` on each entry under `tags` holds the link's own note, a shape already covered
in [Chapter 8: Relations](08-relations.md). [The query basics chapter](10-query-basics.md)'s
`filter`, `sort`, `limit`, `offset` and this chapter's `fields`, `deep`, `facets`, `aggregate` are
parameters on the same query string, and can be carried together exactly as shown.

## What's next

With projection, expansion, facets, and aggregates covered, the next chapter shifts focus back to
the REST layer itself: what the response envelope looks like, how status codes map to error codes,
and how writes check for concurrency — the subject of
[the REST API conventions chapter](12-rest-conventions.md).
