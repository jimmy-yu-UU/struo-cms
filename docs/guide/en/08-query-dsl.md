# 8. Query DSL

Every collection's read surface — REST list/get, GraphQL list/single, and relation `deep`
expansion — is driven by one shared model, `QueryModel`
(`src/Struo.Domain/Query/QueryModel.cs`), built by `QueryParser`
(`src/Struo.Application/Query/QueryParser.cs`) and whitelist-validated by `QueryValidator`
(`src/Struo.Application/Query/QueryValidator.cs`) before it ever reaches the database.

## Two request shapes

The same query model is reachable two ways:

- **`GET /api/items/{collection}`** — query-string form: `filter[field][op]=value` pairs (repeatable;
  implicitly AND-ed together), plus `sort=`, `limit=`, `offset=`, `search=`, `fields=`, `deep=`,
  `locale=`, `deleted=`.
- **`POST /api/items/{collection}/query`** — a JSON envelope with the same fields as proper JSON:
  `{ "filter": {...}, "sort": [...], "limit": n, "offset": n, "fields": [...], "deep": {...},
  "search": "..." }`. This is the only way to reach `_and`/`_or` logical composition or a `deep`
  relation's own nested `filter`/`sort`/`limit`/`offset` — the query-string `deep=` only accepts a
  bare, comma-separated list of relation names to expand with no further options.

Both are read-only and go through the same RBAC read check as any other read (`ItemsController` has
no `[Authorize]` on either action, and `ItemService` only ever checks `CanRead` for a query) — `POST
/query` needs no *write* grant despite the verb. It does, however, need the `X-Struo-CSRF` header when
cookie-authenticated: the CSRF rule keys on the HTTP **method**, not on read-versus-write, so a
read-only `POST` is guarded exactly like a mutation (chapter 9 has the rule and its rationale in full).
This is the one place that trips people up, so here it is live:

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" -b cookies.txt -d '{}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}
```

## Anatomy of a query

| Piece | Query-string | JSON envelope | Meaning |
|---|---|---|---|
| Filter | `filter[field][op]=value` (repeatable) | `"filter": { ... }` | Field conditions, AND-ed by default; see operators and logical composition below. |
| Sort | `sort=field,-field2` | `"sort": ["field", "-field2"]` | Comma list; a leading `-` means descending. |
| Pagination | `limit=`, `offset=` | `"limit"`, `"offset"` | No `page` parameter exists — pagination is purely offset-based (see below). |
| Fields | `fields=a,b,c` | `"fields": ["a","b","c"]` | Restricts which *own* fields are projected (relations and `translations` are unaffected — see below). |
| Deep | `deep=rel1,rel2` | `"deep": { "rel1": {...} }` | Relation expansion; chapter 7 covers this in full. |
| Search | `search=text` | `"search": "text"` | Free-text `LIKE` OR-ed across every `Searchable` field (chapter 4). |
| Soft-delete | `deleted=exclude\|only\|with` | *(query-string only — `GET`/`POST query` both read it from the URL)* | See below. |
| Locale | `locale=code` | *(query-string only, same as above)* | Effective query locale for translatable-field filter/sort/read (chapter 6). |

## The complete operator table

`QueryOperator` (`src/Struo.Domain/Query/QueryOperator.cs`) has exactly these 13 values, each with
exactly one query-string token and one JSON-envelope key (`QueryParser`'s `Operators` map). Every
example below ran against the framework's own `file` collection (three uploaded files:
`alpha-report.txt` 20 bytes in a folder, `beta-notes.txt` 23 bytes in the same folder,
`gamma-draft.txt` 35 bytes, unfoldered):

| `QueryOperator` | Token | Example | Result |
|---|---|---|---|
| `Eq` | `_eq` | `filter[fileName][_eq]=alpha-report.txt` | Matches `alpha-report.txt` only. |
| `Neq` | `_neq` | `filter[fileName][_neq]=alpha-report.txt` | Matches `beta-notes.txt` and `gamma-draft.txt`. |
| `In` | `_in` | `filter[fileName][_in]=alpha-report.txt,gamma-draft.txt` | Matches both named files. |
| `Nin` | `_nin` | `filter[fileName][_nin]=alpha-report.txt,gamma-draft.txt` | Matches `beta-notes.txt` only. |
| `Lt` | `_lt` | `filter[size][_lt]=23` | Matches the 20-byte file. |
| `Lte` | `_lte` | `filter[size][_lte]=23` | Matches the 20- and 23-byte files. |
| `Gt` | `_gt` | `filter[size][_gt]=23` | Matches the 35-byte file. |
| `Gte` | `_gte` | `filter[size][_gte]=23` | Matches the 23- and 35-byte files. |
| `Null` | `_null` | `filter[folderId][_null]=true` | Matches the unfoldered file. |
| `NNull` | `_nnull` | `filter[folderId][_nnull]=true` | Matches the two foldered files. |
| `Contains` | `_contains` | `filter[fileName][_contains]=report` | Matches `alpha-report.txt` (SQL `LIKE '%report%'`). |
| `StartsWith` | `_starts_with` | `filter[fileName][_starts_with]=beta` | Matches `beta-notes.txt` (`LIKE 'beta%'`). |
| `EndsWith` | `_ends_with` | `filter[fileName][_ends_with]=draft.txt` | Matches `gamma-draft.txt` (`LIKE '%draft.txt'`). |

Every one of these ran for real:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5BfileName%5D%5B_eq%5D=alpha-report.txt"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}],"meta":{"total":1,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bsize%5D%5B_lt%5D=23"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt","size":20, ...}],"meta":{"total":1,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5BfolderId%5D%5B_null%5D=true"
{"success":true,"data":[{"id":"...","fileName":"gamma-draft.txt","folderId":null, ...}],"meta":{"total":1,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5BfileName%5D%5B_ends_with%5D=draft.txt"
{"success":true,"data":[{"id":"...","fileName":"gamma-draft.txt", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

`_null`/`_nnull` render as a pure `IS [NOT] NULL` — no comparison value is sent to the database —
which is deliberate: an `IS NULL OR = ''` style check would throw a type error on PostgreSQL for a
non-text column. Every other operator that compares against a typed column (`Guid`, numeric,
`DateTime`, …) is cast via `ConditionalModel.CSharpTypeName`, so e.g. a `Guid` FK column is compared
as `uuid = uuid`, not `uuid = text` (which PostgreSQL rejects outright).

## Logical composition (`_and`/`_or`)

Distinct query-string `filter[...]` keys already AND together implicitly — including two conditions
on the *same* field under *different* operators (`filter[size][_gt]=20&filter[size][_lt]=35`, shown
below). What the query-string form cannot express at all is OR, or two conditions on the same field
under the *same* operator (`filter[size][_gt]` can only appear once as a dictionary key) — either of
those needs the JSON envelope's `_and`/`_or`. Only **one level** of nesting is supported there — a
`LogicalFilter` appearing as a child of another `LogicalFilter` is rejected outright, so `_or`
containing `_and` (or vice versa) does not work, even one level deep:

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"_or":[{"fileName":{"_eq":"alpha-report.txt"}},{"fileName":{"_eq":"gamma-draft.txt"}}]}}'
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...},{"id":"...","fileName":"gamma-draft.txt", ...}],"meta":{"total":2,"limit":25,"offset":0}}
```

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"_and":[{"_and":[{"fileName":{"_eq":"x"}}]}]}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Nested logical groups are not supported; use a single level of _and/_or over field conditions."}}
```

A flat `_and` of independent field conditions needs no `_and` key at all over the query-string
form — repeating `filter[...]` is already an implicit AND:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bsize%5D%5B_gt%5D=20&filter%5Bsize%5D%5B_lt%5D=35"
{"success":true,"data":[{"id":"...","fileName":"beta-notes.txt","size":23, ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

There is also a hard cap on total filter conditions per query — `StruoQueryOptions.MaxFilterConditions`,
default **50** — counted across every leaf `ComparisonFilter` the walk visits, regardless of how
they're nested under `_and`/`_or`; exceeding it throws `"Too many filter conditions (max 50)."` before
any query runs.

A second cap bounds something different: not the shape of the query, but how much intermediate work
answering it may take. A dotted (cross-relation) filter and a search over a translatable field are both
answered by resolving the condition to a set of root ids and rewriting it into an own-collection
`id IN (...)` — see chapter 7 for the walk. `StruoQueryOptions.MaxResolvedFilterIds` (default **5000**)
bounds every step of that resolution: the leaf lookup, each walk-back hop, and the translatable-search
union. It exists because the caps above bound the *result page*, not this intermediate set — without it a
deliberately wide condition (`?filter[category.name][_contains]=a`, `?search=a`) costs O(table) memory
plus one enormous SQL statement, and it is reachable by any caller holding a read grant, including an
anonymous one wherever `public` grants read.

Exceeding it is a client error, not a truncation:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bcategory.name%5D%5B_contains%5D=a"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Resolving 'category.name' matched too many rows (7412, limit 5000). Narrow the filter or search term, or raise Query:MaxResolvedFilterIds."}}
```

Truncating instead would silently drop matching rows and return quietly wrong results, so the query is
refused. Raise `Query:MaxResolvedFilterIds` if a fork's legitimate filters resolve to larger sets — the
cost is memory plus SQL statement size, roughly 40 bytes of statement text per uuid.

## Sorting

`sort=` is a comma list of field names, each optionally prefixed with `-` for descending, applied in
the given order (multi-key sort):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=-size,fileName"
{"success":true,"data":[{"id":"...","fileName":"gamma-draft.txt","size":35, ...},{"id":"...","fileName":"beta-notes.txt","size":23, ...},{"id":"...","fileName":"alpha-report.txt","size":20, ...}],"meta":{"total":3,"limit":25,"offset":0}}
```

A sort key may also be a dotted relation path, but only when every hop is many-to-one — chapter 7
covers this (and its to-many rejection) in full.

## Pagination and `meta`

There is no `page` number — pagination is `limit`/`offset` only. `QueryValidator.Validate` clamps an
unset or non-positive `limit` to `StruoQueryOptions.DefaultLimit` (25) and any larger value to
`StruoQueryOptions.MaxLimit` (100); `offset` floors at 0. Every list response carries a `meta` object
with the *effective* (post-clamp) `limit`/`offset` plus the total row count matching the filter
(before pagination):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=fileName&limit=2&offset=1"
{"success":true,"data":[{"fileName":"beta-notes.txt", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":3,"limit":2,"offset":1}}
```

## Field projection

`fields=` restricts which of the collection's **own** fields are projected — `id` and the optimistic-
concurrency `version` are always included regardless, and a collection's `translations` map (chapter
6) and any `deep`-expanded relations are entirely independent of `fields=` (they're attached by
separate stages, after `ItemProjector`'s own-field selection runs):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?fields=id,fileName&sort=fileName"
{"success":true,"data":[
  {"id":"...","version":1,"fileName":"alpha-report.txt","translations":{"en":{"title":"alpha-report","alt":null},"zh-TW":{"title":"alpha 報告","alt":"Alpha 報告圖示"}}},
  {"id":"...","version":2,"fileName":"beta-notes.txt","translations":{"en":{"title":"beta-notes","alt":null}}},
  {"id":"...","version":0,"fileName":"gamma-draft.txt","translations":{"en":{"title":"gamma-draft","alt":null}}}
],"meta":{"total":3,"limit":25,"offset":0}}
```

Note `contentType`/`size`/… are gone (not requested) but `translations` is still present even though
it wasn't named in `fields=`.

## Deep expansion

Covered in full in chapter 7; from the query DSL's point of view, `deep=` is just another `QueryModel`
field, resolved after filtering/sorting/pagination against the already-fetched page of parent rows:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deep=folder&filter%5BfileName%5D%5B_eq%5D=alpha-report.txt"
{"success":true,"data":[{"fileName":"alpha-report.txt", ..., "folder":{"id":"...","name":"Guides", ...}}], ...}
```

## Soft-delete filter (`deleted=`)

`DeletedFilter` (`src/Struo.Domain/Query/DeletedFilter.cs`) has three values —
`Exclude` (default), `Only`, `With` — read from `?deleted=exclude|only|with` on both `GET` endpoints.
Requesting anything other than the default requires delete permission on the collection
(`DeletedAccessGuard`, enforced in `ItemsController` itself, since `ItemService.QueryAsync`/`GetAsync`
only ever check read permission). Live-verified against `file` (which implements `ISoftDeletable`) by
trashing one row and re-querying all three modes:

```
$ curl -s -X DELETE http://localhost:5221/api/items/file/<beta-id> -H "X-Struo-CSRF: 1" -b cookies.txt
# 204 No Content

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=fileName"
{"success":true,"data":[{"fileName":"alpha-report.txt", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":2, ...}}   # exclude (default): trashed row hidden

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=only"
{"success":true,"data":[{"fileName":"beta-notes.txt", ...}],"meta":{"total":1, ...}}   # only: exclusively the trashed row

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=with&sort=fileName"
{"success":true,"data":[{"fileName":"alpha-report.txt", ...},{"fileName":"beta-notes.txt", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":3, ...}}   # with: all three
```

An invalid value is rejected outright: `"Query parameter 'deleted' must be exclude|only|with."`

## Validation: whitelisting, unknown paths, and the depth cap

Every own-field name a filter/sort/`fields=` entry names is checked against the collection's own
non-hidden `[CmsField]`s plus its declared many-to-one foreign keys (so you can filter/sort by e.g.
`folderId` even though it carries no `[CmsField]` of its own) — nothing else is reachable, and a
`Hidden` field is deliberately excluded even from this allowlist (chapter 5), since a credential-shaped
hidden field staying filterable would turn `meta.total` into a character-at-a-time extraction oracle.
An unknown own-field is rejected the same way regardless of whether it looks like a typo or a
deliberate probe:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bbogus%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogus' on collection 'file'."}}
```

A dotted relation path is parsed and whitelist-validated hop by hop against the actual relationship
graph (chapter 7): an unresolvable relation segment, an unresolvable leaf field on the terminal
collection, and a path exceeding the 6-hop depth cap are each rejected with their own precise
message — all three live-verified in chapter 7. `fields=` additionally disallows relation paths
entirely (`allowRelation: false`) — projection only ever selects a collection's own scalar fields,
never a nested relation's. Combining several conditions on the same to-many relation path is
each-exists, not same-row — see [chapter 7](07-relations.md#relation-filtering-across-dotted-paths).

A read grant does not travel across a relation hop. Every collection a dotted path traverses needs
its own read permission, so a role granted `article` but not `user` cannot reach user rows through
`author.email`: the request is refused (`FORBIDDEN`, or `UNAUTHORIZED` for an anonymous caller).
Refusing rather than ignoring the condition is deliberate — dropping it would answer with rows that
do not match the filter that was sent, and `meta.total` over an unreadable collection is a
character-at-a-time extraction oracle even when no row is ever returned. `deep=` follows the same
rule with the opposite failure mode: a relation whose target you cannot read is omitted from the
response rather than failing it, so a narrowly-granted role still gets its item, just without that
nested object. A translatable Image/File field resolves through the same grant — without read on
`file` the nested object comes back `null` and the raw `<name>Id` still does not.

The permission check runs *before* the path's leaf is resolved, so the field names of a collection
you may not read are not probeable through validation errors: you cannot tell a real field on an
unreadable collection from an invented one, because both stop at the hop. The many-to-one foreign
key itself (`categoryId`) does stay filterable — it is a column on a row you are already permitted
to read, and exposes an opaque id rather than anything from the target collection.

**This is a breaking change for an anonymous-read deployment.** If `Rbac:PublicReadCollections`
lists a collection whose public filters or `deep=` traverse into a second collection, that second
collection now needs its own entry, or those requests will start failing (filters) or returning
without the nested object (`deep=`).

## Worked end-to-end example

Putting several pieces together in one request — a comparison filter, descending sort, pagination,
and field projection, all in one `GET`:

```
$ curl -s -b cookies.txt \
    "http://localhost:5221/api/items/file?filter%5Bsize%5D%5B_gte%5D=20&sort=-size&limit=2&offset=0&fields=id,fileName,size"
{"success":true,"data":[
  {"id":"...","version":0,"fileName":"gamma-draft.txt","size":35,"translations":{"en":{"title":"gamma-draft","alt":null}}},
  {"id":"...","version":2,"fileName":"beta-notes.txt","size":23,"translations":{"en":{"title":"beta-notes","alt":null}}}
],"meta":{"total":3,"limit":2,"offset":0}}
```

## Next steps

- Chapter 6, [Internationalization](06-internationalization.md), for `?locale=` and how a
  translatable field is filtered/sorted at the effective query locale.
- Chapter 7, [Relations](07-relations.md), for dotted-path filtering, relation-path sorting, `deep`
  expansion, and the depth cap in full detail.
- Chapter 9, [REST API](09-rest-api.md), for the response envelope, error-code catalog, and the
  `X-Struo-CSRF` header this chapter's write/`POST query` examples used.
- Chapter 10, [GraphQL API](10-graphql-api.md), for the same filter/sort/pagination surface expressed
  as typed GraphQL arguments instead of query-string/JSON-envelope conventions.
