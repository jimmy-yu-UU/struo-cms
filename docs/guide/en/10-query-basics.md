# 10. Querying: Filters, Sorting and Pagination

This chapter covers how to write a query string for a list endpoint, how filtering and sorting
combine, and how pagination is computed. REST and GraphQL share the same semantics.

## How a query is written

A list endpoint accepts two equivalent request shapes. Both parse into the same query, and both
pass through the same field allowlist before any SQL is built.

**Query string.** `GET /api/items/{collection}` reads repeated bracket-style keys:
`filter[<field or relation path>][<operator>]=<value>`. The next section, "What a query is made
of," lists every other parameter. Here is one filter, one descending sort, and pagination:

```text
$ GET /api/items/article?filter[status][_eq]=published&sort=-publishedAt&limit=1&offset=0
{"success":true,"data":[{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","publishedAt":"2026-06-01T00:00:00","heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.712477","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.712614","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":2,"limit":1,"offset":0}}
HTTP_STATUS:200
```

Only one row comes back, the published article that sorts first; `meta.total` is 2, the total row
count that matches the filter, while `limit`/`offset` are the effective, clamped pagination values.
Later examples in this chapter reuse the same test data — the ids are data-specific, and your own
environment will differ.

**JSON envelope.** `POST /api/items/{collection}/query` accepts the same model as a JSON body, with
keys matching the query-string parameters one for one. It is the only way to write `_and`/`_or`, and
the only way to give a `deep` relation its own `fields`/`filter`/`sort`/`limit`/`offset`, or to nest
`deep` further. The query above, written as a JSON body, is the same request:

```json
{
  "filter": { "status": { "_eq": "published" } },
  "sort": ["-publishedAt"],
  "limit": 1,
  "offset": 0
}
```

The rest of this chapter's examples use the query-string form. GraphQL does the same thing with
the same semantics, only the argument spelling differs — the operator `_eq` becomes `eq`, and the
quantifier `_some` becomes `some`. The spelling differences are left to
[the GraphQL chapter](14-graphql.md).

**Both are reads.** Neither action requires a signed-in caller — both require only a read grant on
the collection. `POST .../query` uses POST, but it still needs no write grant.

A repeated query-string key is joined with commas before parsing:
`filter[status][_eq]=a&filter[status][_eq]=b` becomes the single value `a,b`, not two separate
conditions — and it is not an error.

## What a query is made of

A query is made up of these groups of parameters:

- **`filter`** — filters rows by field or relation path; the subject of the rest of this chapter.
- **`search`** — an OR group of `LIKE` conditions over the collection's searchable, non-hidden
  fields, ANDed with `filter`. A collection with no searchable fields ignores it without error, and
  a translatable field is matched through the translation sidecar, using the locale this query
  actually resolves to.
- **`sort`** — orders by one or more fields; **`limit`/`offset`** — pagination. See "Sorting"
  and "Pagination and `meta`" below.
- **`fields`** — keeps only the collection's own fields; left to
  [the advanced query chapter](11-query-advanced.md).
- **`deep`** — expands relations, also left to
  [the advanced query chapter](11-query-advanced.md); [Chapter 8: Relations](08-relations.md)
  already covers the batched expansion mechanism.
- **`facets`/`aggregate`** — compute summary statistics over the same filtered result set, left to
  [the advanced query chapter](11-query-advanced.md).

## Operators

Every operator is the same token in the query string and in JSON; GraphQL spells each one
differently:

| REST | Meaning |
|---|---|
| `_eq` | equal to |
| `_neq` | not equal to |
| `_in` | in a list |
| `_nin` | not in a list |
| `_lt` | less than |
| `_lte` | less than or equal to |
| `_gt` | greater than |
| `_gte` | greater than or equal to |
| `_null` | is null |
| `_nnull` | is not null |
| `_contains` | contains a substring |
| `_starts_with` | starts with a substring |
| `_ends_with` | ends with a substring |

**Matching is case-sensitive.** `_EQ` or `_startsWith` is rejected with
`Unknown operator '<token>'.`. Field names are the opposite — case-insensitive everywhere.

**Accepted value shapes.** `_in`/`_nin` are a comma list in the query string and an array in JSON;
both merge into the same SQL `IN` list. `_contains`/`_starts_with`/`_ends_with` translate to
`LIKE '%v%'`, `LIKE 'v%'`, and `LIKE '%v'` respectively.

**`_null`/`_nnull` ignore the value.** These two operators translate to a plain
`IS NULL`/`IS NOT NULL`; the value itself is never read at all, so
`filter[publishedAt][_null]=false` and `=true` are the same request — it never flips to mean "is
not null":

```text
$ GET /api/items/article?filter[publishedAt][_null]=true
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":1,"limit":25,"offset":0}}
HTTP_STATUS:200
```

Swapping `_null]=true` for `_null]=false` returns the same row, and `meta.total` is still 1.

**Type conversion.** Except for `_null`/`_nnull`, every operator's value is converted to the
field's real CLR type before comparison — a `Guid` foreign key is compared `uuid = uuid`, not as
text. The conversion table covers `Guid`, `int`, `long`, `short`, `bool`, `DateTime`,
`DateTimeOffset`, `decimal`, `double`, and `float`; `string` and any uncovered type stay a text
comparison.

Values must always be sent in a culture-invariant format, and the host's own default culture is
pinned to invariant at startup too, so the two sides never read the same decimal or date
differently. The operator and the field's interface are independent of each other — `_gt` is
accepted on a `Text` field too, and is treated as a text comparison.

## `_and` and `_or`

In the query string, different `filter[...]` keys are ANDed by default, even when they target the
same field with different operators. The query string cannot express OR at all, and the same
field with the same operator can only appear as one key — both cases need the JSON envelope's
`_and`/`_or` instead, written like this:

```json
{"filter":{"_or":[{"status":{"_eq":"draft"}},{"categoryId":{"_eq":"01a08f92-3833-750e-bad3-6ee620f985c3"}}]}}
```

POST it to `/api/items/article/query`; all three articles match — the draft one satisfies the
first branch, and all three are in the `Guides` category, satisfying the second — `meta.total` is
3.

**Only one level of nesting.** A child of `_and`/`_or` that is itself another logical group is
rejected: `Nested logical groups are not supported; use a single level of _and/_or over field
conditions.`. This one-level limit is counted separately per scope: the inner filter of a
`_some`/`_none` quantifier is a fresh starting point, and can use one level of `_and`/`_or` of its
own.

**A cap on the number of conditions.** `Query:MaxFilterConditions` (default 50) counts every leaf
comparison in the whole request — including every `_and`/`_or` branch and every quantifier's inner
filter — and rejects the request once that cap is exceeded: `Too many filter conditions (max
50).`. A cross-relation condition has no similar cap, because a relation condition only adds one
more level of nested subquery; it doesn't need to be bounded the way fetching a set of ids does.

**Other shape errors.** An empty `filter` object is an error (`Empty filter object.`), while
omitting the `filter` key entirely is a valid no-op.

A field's value must be an operator object — `{"status":"draft"}` is rejected as `Filter for field
'status' must be an object of operators.`, and `{"status":{}}` as `Filter for field 'status' has no
operator.`. `_and`/`_or`'s value must be an array (`'_or' must be an array.`), and a query-string
key that isn't split into a field segment and an operator segment is `Malformed filter key
'filter[status]'.`.

## Relation paths: matched per segment vs. `_some`/`_none` on the same row

[Chapter 8: Relations](08-relations.md), in "Filtering on relations (overview)," already covers how
a dotted path gets pushed down into a nested subquery. This section spells out the difference
between "matched per segment" and "matched on the same row," and fills in `_junction`, the filter on
the link row itself.

**Matched per segment.** With a dotted path, two conditions can each be satisfied by a different
relation row. For example, `filter[tags.name][_eq]=howto&filter[tags._junction.note][_eq]=hero`
returns both "Release notes" and "Getting started" — it's enough for the article to have any tag
named `howto`, and any tag whose link note is `hero`; the two conditions don't have to be
satisfied by the same tag.

**`_some`/`_none` bind to the same row.** A quantifier's inner filter is turned into a single
subquery as a whole, so every condition under the quantifier must be satisfied by the same
relation row. `_none` is that subquery negated — an article with no related row at all also
satisfies `_none`. Turning the path above into a quantifier:

```text
$ GET /api/items/article?filter[tags._some.name][_eq]=howto&filter[tags._some._junction.note][_eq]=hero
{"success":true,"data":[{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","version":0,"status":"published","publishedAt":"2026-03-01T00:00:00","heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:20.153539","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:20.153611","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Getting started","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null},"zh-TW":{"title":"開始使用","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":1,"limit":25,"offset":0}}
HTTP_STATUS:200
```

Only "Getting started" is left: the note on its `howto` tag link is `hero`. "Release notes" also has
a `howto` tag, but the tag whose note is `hero` is a different one (`release`) — not the same tag
link. `_some`/`_none` are just as valid on a many-to-one relation: `_some` is equivalent to the same
inner filter written as a dotted path, and `_none` covers both "no target matches" and "the foreign
key itself is null."

**Reserved words and syntax.** `_and`, `_or`, `_some`, `_none`, `_junction`, plus the same set
spelled without underscores for GraphQL, can never be used as a field or relation name; the check
is case-insensitive. A violation fails at startup — it never waits for a request to come in.

In the JSON envelope, a quantifier is a key inside the field's object — `{"tags":{"_some":{...}}}` —
whose value is a complete filter, rooted at the relation's target collection, applying the same
grammar recursively. Inside it you can use a dotted path, another level of `_some`/`_none`,
`_junction.<field>`, and one level of `_and`/`_or`. A field object can hold both `_some` and `_none`
at once — they are independent conditions, ANDed together.

In the query string, a quantifier is a segment inside the path —
`filter[tags._some.name][_eq]=howto` — and conditions sharing the same (prefix, quantifier) are
grouped and folded into one quantifier; the prefix match is case-insensitive.

Each (prefix, quantifier) pair can appear only once per request; two independent same-row conditions
need the JSON envelope, for example
`{"filter":{"tags":{"_some":{"name":{"_eq":"howto"},"_junction.note":{"_contains":"hero"}}}}}`.

Each kind of malformed quantifier has its own message:

- The value must be a non-empty object: `'tags._some' must be a non-empty filter object.`
- A quantifier can't mix with scalar operators in the same field object: `'tags' mixes a relation
  quantifier with scalar operators; a relation path has no scalar operators.`
- A quantifier must be preceded by a relation name and followed by a condition: `'tags._some':
  '_some' must be followed by a condition on the related collection.`, `'_some.name': '_some' must
  follow a relation name.`

**`_junction`.** `_junction.<field>` filters the link's own payload, not either endpoint's data. It
can be used as a dotted path, or placed inside `_some`/`_none`.

It must immediately follow a many-to-many relation that has a junction collection, and it can only
be followed by one non-hidden payload field — no further segment. Using it on a many-to-one relation
is rejected: `'category._junction.note': '_junction' is only valid after a many-to-many relation
with a junction collection.`

`_junction` needs the caller to hold a read grant on the junction collection itself; this check
happens before the field name is even parsed, so an unreadable junction can never be probed for
what fields it has.

It doesn't count toward the relation-depth cap, since it's only an extra segment; a `_junction`
condition also can't be ORed with a condition on the target collection inside the same quantifier.

**Where soft delete stops.** A relation subquery always applies the target collection's own
soft-delete filter, regardless of the outer request's `deleted=` — see
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md).

## How translatable fields get filtered

Every operator above works on a translatable field too: the filter is rewritten into a subquery
against the translation sidecar, matched in the locale this query actually resolves to. You don't
have to pass `locale=` — without one, the site's default locale is used.

`GET /api/items/article?filter[title][_contains]=start`, with no `locale=`, matches against the site
default (`en`) and returns the article whose title contains `start`, and `meta.total` is 1.
Matching the Chinese title instead, with `locale=` explicitly set —
`GET /api/items/article?filter[title][_contains]=%E9%96%8B%E5%A7%8B&locale=zh-TW` — returns the
same article, but `translations` now holds only the `zh-TW` entry, and `meta.total` is still 1.

**Validating `locale=`.** `locale=` first passes a character allowlist; a malformed code is
rejected: `Locale '<code>' contains invalid characters. Codes must match [A-Za-z0-9_-]{1,35}.`. Past
the character check, it must also be on the list of enabled languages, or it's `Unknown or disabled
locale '<code>'.`.

With no `locale=`, the locale this query actually uses is always resolved to the site default — even
when the root collection itself has no translatable field, because a relation path along the way
might reach one that does.

## Sorting

`sort=` is a comma list; each field can take a `-` prefix for descending order, and fields apply
in the order you list them.

A sort key can be a dotted path, but every segment must be many-to-one; hitting a to-many segment
anywhere in the path is rejected:

```text
$ GET /api/items/article?sort=tags.name
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Sort across to-many relations is not supported: 'tags.name'."}}
HTTP_STATUS:400
```

A many-to-one path such as `sort=category.name` responds normally with 200.

Where `NULL` sorts is left to the database — the code never sends `NULLS FIRST`/`NULLS LAST`:
SQLite sorts `NULL` first on an ascending sort, PostgreSQL sorts it last.

**Default order.** With no `sort=`, the order is `createdAt DESC, id ASC`; a collection with no
`CreatedAt` falls back to plain `id ASC`. This is deliberate — PostgreSQL's heap order is unstable
after an update, and without this default an edited row would jump position for no reason.

The primary key is always appended as the last, ascending tiebreaker on every caller-supplied sort,
unless the caller already sorts by `id` itself — so paging never repeats or skips a row even when
the sort field isn't unique.

Sorting by a translatable field opens a locale-scoped relation subquery against the translation
sidecar; a row with no translation row for that locale sorts as `NULL`. The sort key allowlist is
the same one filtering uses — see the last section of this chapter for the details.

## Pagination and `meta`

Pagination has only two parameters, `limit`/`offset` — there is no `page`. A missing or
non-positive `limit` is clamped to `Query:DefaultLimit` (default 25); anything over the cap is
clamped to `Query:MaxLimit` (default 100). `offset` floors at 0.

A non-numeric value such as `limit=abc` isn't an error — it's treated exactly as if `limit` were
missing, and clamped to the default the same way: `GET /api/items/article?limit=abc` responds
with a `meta` of `{"total":3,"limit":25,"offset":0}` — `limit` falls back to the default 25,
rather than the request being rejected.

Every list response carries a `meta` object holding the effective, clamped `limit`/`offset`, and
`total` — the row count after the same `deleted=` mode is applied and before pagination, not the
whole table's row count. There is no cursor or keyset form of pagination, and no `Link` header —
deep pagination is plain `offset`.

## `deleted=`

`deleted=` is always read from the URL, on the list, `POST .../query`, and single-item reads
alike. The three accepted values, the default, and the permission rule are in
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md), in "The `deleted=` filter and
permissions." This check lives in the controller, not the service layer — calling
`ItemService.QueryAsync`/`GetAsync` directly does not apply it automatically.

## Which fields can be filtered and sorted, and what happens when it goes wrong

Whether a field can be filtered, sorted, or projected comes down to one shared allowlist: the
collection's own non-hidden `[CmsField]`s, plus any declared many-to-one foreign key —
`categoryId` can therefore be filtered even though it carries no `[CmsField]` of its own. `id` is
always valid, even though it isn't in the field list.

`Sortable` has no bearing on this allowlist: `[CmsField]`'s `Sortable` only decides whether the
admin list screen's column header can be clicked to sort — a separate concern from the `sort=`
allowlist here.

Hidden fields are deliberately excluded from the allowlist: if a hidden, password-like field could
still be filtered on, `meta.total` would turn into a character-by-character oracle for its content.
A nonexistent field on the collection itself is rejected the same way, whether or not it looks like
a typo:

```text
$ GET /api/items/article?filter[bogus][_eq]=x
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogus' on collection 'article'."}}
HTTP_STATUS:400
```

A dotted path is validated segment by segment against the relation graph: an unknown relation, an
unknown leaf field, and exceeding the depth cap each get their own message — `Unknown relation
'<rel>' on '<collection>' in path '<path>'.`, `Unknown field '<leaf>' on collection
'<collection>' in path '<path>'.`, `Relation path '<path>' exceeds the maximum depth of <max>.`.
The first one actually happens here:

```text
$ GET /api/items/article?filter[bogus.name][_eq]=x
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown relation 'bogus' on 'article' in path 'bogus.name'."}}
HTTP_STATUS:400
```

The leaf field at the end of a path is validated with the same visibility rule as a root field:
non-hidden fields plus `id`. The depth budget is shared: the segments a `_some`/`_none` quantifier
has already used are deducted from what's left for its inner path, but the error message always
reports the configured cap itself, not what remains after the deduction.

**Read grants don't propagate along the path.** Every collection a dotted path passes through needs
its own read grant — without one, the whole request is refused (`FORBIDDEN`, or `UNAUTHORIZED` for
an anonymous caller), rather than the condition being silently dropped. Dropping it would let the
caller see data that doesn't match the filter, and even a zero-row result leaves `meta.total` on an
unreadable collection open to probing.

This authorization check runs before the leaf field is even parsed, so a field that genuinely
exists on an unreadable collection and a made-up one get the same rejection.

A many-to-one foreign-key field itself stays filterable: it's a column on a row the caller can
already read, and it only exposes one opaque id — it leaks nothing about the target collection.

For a collection listed in `Rbac:PublicReadCollections`, if its public filter or `deep=` ever
reaches another collection, that collection needs to be listed too, or it runs into the
authorization rejection above.

The full shape of the error envelope is left to
[the REST API conventions chapter](12-rest-conventions.md).

## What's next

With filtering, sorting, and pagination settled, the next step is projecting fields, expanding
relations, and computing facets and aggregates — the subject of
[the advanced query chapter](11-query-advanced.md).
