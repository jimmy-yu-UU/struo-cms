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
| Search | `search=text` | `"search": "text"` | Free-text `LIKE` OR-ed across every `Searchable` field (chapter 4); combines with `filter` on the same request as AND — a row must satisfy the filter *and* match the search term, not either one. When a fork registers an `ISearchProvider` and it answers this request, a candidate id set replaces the `LIKE` search outright instead — see [Search providers](#search-providers) below. |
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
they're nested under `_and`/`_or`, and including every leaf inside a `_some`/`_none` predicate's own
inner filter (below) — a quantifier's inner filter does not get its own separate budget; exceeding it
throws `"Too many filter conditions (max 50)."` before any query runs.

A dotted (cross-relation) filter and a search over a translatable field are both answered by pushing
the condition down into a nested SQL subquery — see chapter 7 for the exact shapes — rather than by
resolving an intermediate id set in memory first. Because of that, there is no cap here analogous to
`MaxFilterConditions` above: a cross-relation condition costs one subquery regardless of how many
rows it could match, not memory proportional to the size of some intermediate set — there is no
longer an intermediate set at all for a cap to bound.

## Relation quantifiers: `_some`/`_none`

Chapter 7 covers the semantics (each-exists vs. same-row, `_junction`, the many-to-one case, the
NULL-safety and soft-delete rules) in full; this section is the grammar reference for both request
shapes plus the reserved-token and error catalog.

**Reserved tokens.** `_some`, `_none`, `_junction` (query-string/JSON-envelope spelling) and `some`,
`none`, `junction` (GraphQL spelling) are reserved exactly like `_and`/`_or` — no field or relation
may be named any of these (`FilterReservedTokens.All`,
`src/Struo.Application/Query/FilterReservedTokens.cs`); a collection that violates this fails fast at
startup, before any request is served.

**JSON envelope (the complete form).** A field object's key is `_some` or `_none`, and its value is a
complete `filter` object rooted at the relation's target collection — recursively the same grammar as
any top-level `filter`, so it may itself contain dotted paths, nested `_some`/`_none`, `_junction`,
and a single level of `_and`/`_or`:

```json
{"filter":{"tags":{"_some":{"name":{"_eq":"Guide"},"_junction.note":{"_contains":"hero"}}}}}
{"filter":{"tags":{"_none":{"name":{"_eq":"internal"}}}}}
```

A field object may carry both `_some` and `_none` as two independent predicates (AND-ed), but may
not mix either with a plain scalar operator (`_eq`, `_contains`, …) in the same field object — a
relation path has no scalar operators of its own:

```
$ curl -s -X POST http://localhost:5221/api/items/article/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"tags":{"_some":{"name":{"_eq":"Guide-u3doc0905"}},"_eq":"someval"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'tags' mixes a relation quantifier with scalar operators; a relation path has no scalar operators."}}
```

**Query string (the folding shorthand).** `filter[<prefix>._some.<inner path>][<op>]=<value>` (or
`_none`) is folded, after ordinary query-string parsing, into the same `_some`/`_none` tree
(`RelationQuantifierFolder.Fold`, `src/Struo.Application/Query/RelationQuantifierFolder.cs`): every
condition whose path contains a `_some`/`_none` segment is grouped by (the path prefix *before* that
segment, the quantifier), and every condition sharing one such group becomes one predicate, AND-ed
together — a nested quantifier (`a._some.b._none.c`) folds again, recursively, on the inner group.
**One request can express only a single group per (prefix, quantifier) pair on the query string** —
two conditions with the same prefix and quantifier always land in the same group, however many
`filter[...]` keys they're spread across; there is no query-string way to express two *independent*
same-row predicates against the same relation (e.g. "some tag named `a`, or a *different* tag that is
red, under an `_or`") — that needs the JSON envelope instead, whose `_some` value is a single filter
object you construct explicitly.

A quantifier segment must be followed by at least one more path segment (the condition it quantifies)
— it cannot be the path's last segment:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags._some%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'tags._some': '_some' must be followed by a condition on the related collection."}}
```

Nor can two quantifier segments follow each other directly (`a._some._none.b`) — express that as a
nested `_some`/`_none` inside the envelope form's inner filter instead.

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

## Search providers

`search=` (above) is answered by a built-in `LIKE` scan by default. A fork may instead plug its own
search engine (Meilisearch, Elasticsearch, PostgreSQL full-text, …) into this same request by
registering `ISearchProvider` (`src/Struo.Application/Search/ISearchProvider.cs`) — the deliberate
extension seam this section covers; chapter 1's "What is replaceable" places it alongside
`IFileStorage`. The default registration, `NullSearchProvider`, never handles a search, so with no
fork-supplied provider the `LIKE` path above runs completely unchanged.

### The contract

`ItemService.QueryAsync` asks the registered `ISearchProvider` once per list request — only when
`search=` is non-blank, and never for a single-item `GET` — with a `SearchRequest`
(`src/Struo.Application/Search/SearchRequest.cs`):

| Field | Meaning |
|---|---|
| `Collection` | The collection's canonical camelCase name (`CollectionMetadata.Name`, e.g. `article`) — regardless of how the REST route segment itself was cased (`/api/items/Article` resolves case-insensitively), the provider always sees the one canonical form. |
| `Term` | The `search=` value verbatim — not trimmed, not lower-cased. |
| `Locale` | Effective query locale (explicit `locale=`, or the site default). |
| `SearchableFields` | Core's `Searchable && !Hidden` field names — a hint only; a provider may index other fields entirely. |

The provider answers with a `SearchOutcome` (`src/Struo.Application/Search/SearchOutcome.cs`), one of
two states:

- **`SearchOutcome.NotHandled`** — core's built-in `LIKE` search runs exactly as it would with no
  provider registered.
- **`SearchOutcome.Candidates(ids)`** — the given root ids (as strings) become an `id IN (...)`
  condition that replaces the `LIKE` search outright; the search term itself is not otherwise
  consulted. An **empty** candidate list is a handled zero-hit search (`id IS NULL`), never a
  fallback to `LIKE` — a provider that legitimately found nothing must still return
  `Candidates([])`, not `NotHandled`.

### How it composes with the rest of the request

- **AND with `filter`, not a replacement for it.** The candidate condition and `filter` both apply —
  a row must satisfy the filter *and* be one of the candidate ids, not either one.
- **Independent of `deleted=`, permissions, and `Hidden`.** Core's read permission check is
  collection-level, not per-row (`ItemService.QueryAsync` already ran `CanRead` and would have thrown
  `PermissionDeniedException` before the provider is ever asked), so there is no per-row RBAC for a
  candidate id to bypass; `Hidden` field handling is likewise per-field, unrelated to which rows are
  eligible. The one thing that does filter candidate rows downstream is the soft-delete mode: a
  candidate id for a row currently outside the requested `deleted=` mode (e.g. a trashed row under the
  default `exclude`) is excluded exactly like any other row would be.
- **The list, every facet, and the aggregate all share the same candidate set** (see "Facets and
  aggregates" below) — `ItemService.QueryAsync` resolves candidates once and threads the same
  `QueryModel` through the page query, each facet, and the aggregate, so all three report against
  identical rows.
- **Candidate order does not affect result order.** The candidates only narrow *which* rows are
  eligible; `sort=` (or its absence) still decides row order exactly as it would for any other
  request. A provider's own relevance ranking is not preserved in this version — preserving it is a
  possible follow-up, not something this version provides.

### The id trust boundary and the cap

The provider's returned ids are a **trust boundary**, not user input: `SearchCandidateResolver`
(`src/Struo.Application/Search/SearchCandidateResolver.cs`) parses every id to the collection's
primary-key CLR type before it ever reaches SQL, because `FilterTranslator` renders the resulting
`id IN (...)` as typed literals. Only a `Guid` or an integer primary key (`long`/`int`/`short`) is
supported; any other PK type is refused. The candidate count is also capped by
`Query:MaxSearchCandidates` (chapter 3, default **1000**). Both an unparsable id and a count over the
cap are **provider contract violations, not user mistakes** — they throw `InvalidOperationException`
(→ `INTERNAL_SERVER_ERROR`/500), never `QueryException` (→ `BAD_USER_INPUT`/400), because the caller
did nothing wrong; the fork's provider did. `StruoExceptionHandler.Map` logs every
`INTERNAL_SERVER_ERROR` case at `Error` server-side, so a misbehaving provider's violation is not
silent to the operator even though the client only ever sees the masked generic message. Duplicate ids
across a provider's response are silently deduplicated.

### When the provider itself is down

A provider whose engine cannot answer at all (connection refused, timeout, missing index) should
throw `SearchUnavailableException` (`src/Struo.Domain/Query/SearchUnavailableException.cs`) rather
than returning `NotHandled` — the request then fails with `SEARCH_UNAVAILABLE`/503 (chapter 9) instead
of silently degrading to a `LIKE` scan the caller might not expect. A provider that would rather
degrade gracefully to `LIKE` can catch its own exception internally and return `NotHandled` instead;
both are legitimate choices and the seam does not force one over the other.

### Registering a provider

`AddStruoData()` registers `NullSearchProvider` with `TryAddScoped`, so a fork only needs to add its
own registration *after* calling `AddStruoData()` (a registration made *before* is also honored,
since `TryAddScoped` only backs off when something is already registered):

```csharp
public sealed class StaticSearchProvider : ISearchProvider
{
    public Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        if (request.Collection != "article") return Task.FromResult(SearchOutcome.NotHandled);
        IReadOnlyList<string> ids = MyIndex.Lookup(request.Term, request.Locale); // your engine call
        return Task.FromResult(SearchOutcome.Candidates(ids));
    }
}
// Program.cs, after AddStruoData():
builder.Services.AddScoped<ISearchProvider, StaticSearchProvider>();
```

Register with a scoped (or transient) lifetime unless the provider is genuinely stateless: a provider
registered as a singleton that captures a scoped dependency (a `DbContext`, `ISqlSugarClient`, or
similar) is a captive dependency — it either throws at startup under `ValidateScopes`, or, worse,
silently reuses the first request's scoped instance for the lifetime of the app.

### What core does not do

Keeping a fork's search index in sync with writes (create/update/delete) is deliberately **not**
part of core — indexing strategy (synchronous, queued, batched) is specific to the search engine a
fork chooses, so this is the fork's own responsibility today; a write-side sync hook is a possible
future addition, not something this version provides.

## Facets and aggregates

Two more list-only parameters compute summary data over the *whole* filtered result set alongside the
current page: `facets=` (a value/count breakdown per requested path) and `aggregate[<op>]=` (`sum`/
`avg`/`min`/`max`/`count` over own fields). Neither changes `data` or pagination — they only add keys
to `meta` (REST) or fields to the list wrapper (GraphQL, chapter 10) — and neither is computed at all
unless requested: an ordinary request's SQL and response are unchanged (`QueryValidator`/`ItemService`
short-circuit on `null`).

```
?facets=status,categoryId,tags,category.name
?aggregate[sum]=price&aggregate[max]=price,rating&aggregate[count]=publishedAt
```

- `facets` — a comma list of facet paths (query string) or a JSON array of the same strings
  (envelope). Each path is one of the four forms below.
- `aggregate[<op>]` — one query-string key per op (`count`/`sum`/`min`/`max`/`avg`), value a
  comma list of own fields; the envelope form is `"aggregate": {"sum": ["price"], "max": [...]}`, one
  array per op. An unknown op is rejected: `"Unknown aggregate op 'x'."`.

Both compose with everything else in this chapter — `filter`, `search`, `sort`, `limit`/`offset`,
`deep`, `locale`, `deleted` — and are validated by the same `QueryValidator` that whitelists
`filter`/`sort`/`fields` (`ValidateFacets`/`ValidateAggregate`,
`src/Struo.Application/Query/QueryValidator.cs`), so an unknown path, an unfacetable field, or an
incompatible aggregate op all fail the request with `BAD_USER_INPUT` before any facet/aggregate SQL
runs — same as an unknown filter field. `deleted=` (see "Soft-delete filter" below for the base
semantics) composes only with the **root** collection's own rows, though: a to-many facet's
related/junction-side query never lifts the soft-delete filter, regardless of the outer request's
`deleted=` mode — a `deleted=with` request's `tags` facet, for example, still excludes a trashed tag
even while the article rows themselves include trashed ones.

### The four facet path forms

`FacetPathResolver.Resolve` (`src/Struo.Application/Query/FacetPath.cs`) accepts exactly these
shapes — never more than one relation hop, never a quantifier or `_junction` segment:

| Form | Example | Groups by | `value` |
|---|---|---|---|
| Own scalar field | `status` | the root field | the field's own value; `null` gets its own bucket |
| Many-to-one foreign key | `categoryId` | the root FK column | target id as a string; `null` gets its own bucket |
| Relation name (any kind) | `category`, `tags`, `articles` | the target id (M2O: root FK; O2M: child's own id; M2M: junction's target FK) | target id as a string; **no** "no relation" bucket |
| One relation hop + a leaf field | `category.name`, `tags.name`, `articles.title` | same target-id grouping, then the leaf value swapped in | the leaf's value; a translatable leaf uses the sidecar row at the query's effective locale, and an id with no translation row at that locale gets a `null` bucket |

A facetable field's interface must be one of `Text`, `Textarea`, `Slug`, `Email`, `Url`, `Color`,
`Phone`, `Select`, `Radio`, `Number`, `Slider`, `Rating`, `Boolean`, `Checkbox`, `Date`, `DateTime`,
`Time`, `Uuid`, `File`, `Image` (`FacetPathResolver.Facetable`) — own field and leaf field alike. Long-
form text (`RichText`/`Markdown`/`Code`), every multi-value interface (`MultiSelect`/`CheckboxGroup`/
`Tags`/`Repeater`), structured payloads (`Json`/`KeyValue`/`Files`), and `Hidden`/`Divider`/`Password`
are all rejected — a `Hidden` field is rejected with the same `"Unknown field"` message an unresolvable
name gets, so a facet path can never be used to probe for a hidden field's existence.

### Disjunctive counts and the pruning rule

A facet answers "if I picked each candidate value of this path instead, how many rows would match
everything else in my request?" — not "how many rows in my *current* result set have this value?". To
get that, `FacetFilterPruner.Prune` (`src/Struo.Application/Query/FacetFilterPruner.cs`, a pure
function over the validated `FilterNode` tree) removes, for each facet, every condition on that same
field/relation *family* before counting: an own field's condition on itself; a many-to-one FK's
condition on the FK column *and* on any `<relation>.`-prefixed dotted path *and* on a
`_some`/`_none` predicate against that relation — `categoryId` and `category.name` share one family and
are pruned together. `search` is never pruned; **`aggregate` is never pruned** — it always runs against
the request's full, unpruned filter. A pruned-to-empty logical group collapses (0 children → the whole
node disappears, 1 surviving child → the group is replaced by it), so an entirely category-scoped
filter can prune away to no filter at all when faceting on `category`/`categoryId`/`category.*`.

Live, against a small fixture (one category "Facet Demo" holding three articles — two `published`, one
`draft`, two sharing a tag): filtering to *only* the drafts still reports both statuses in the facet,
because the `status` condition is removed before the `status` facet is counted:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5BcategoryId%5D%5B_eq%5D=<category-id>&filter%5Bstatus%5D%5B_eq%5D=draft&facets=status"
{"success":true,"data":[{"id":"...","status":"draft", ...}],"meta":{"total":1,"limit":25,"offset":0,"facets":{"status":[{"value":"published","count":2},{"value":"draft","count":1}]}}}
```

`data`/`total` are the one matching draft, as `filter` says — but `facets.status` shows `published: 2`
and `draft: 1`, the split that would result from swapping the `status` condition for each candidate
value in turn while leaving `categoryId` in place. This is also the acceptance oracle: for an own-field
facet, `{"value": v, "count": n}` must equal `meta.total` of the same request with the facet's own
condition replaced by `filter[field][_eq]=v` — confirmed above (`filter[status][_eq]=published` on this
same category independently returns `"total":2`).

Faceting on the *pruned* family itself demonstrates the "prunes away to nothing" case: requesting
`category.name` alongside `filter[categoryId][_eq]=<category-id>` removes that filter entirely before
counting `category.name`, so the facet spans every category in the database, not just the one the list
was filtered to — live against this host's shared dev fixtures (unrelated categories left over from
other verification runs included, since the pruned filter has nothing left to scope by):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5BcategoryId%5D%5B_eq%5D=<category-id>&facets=status,tags,category.name&aggregate%5Bcount%5D=publishedAt&aggregate%5Bmax%5D=publishedAt"
{"success":true,"data":[ ... 3 articles ... ],"meta":{"total":3,"limit":25,"offset":0,
  "facets":{
    "status":[{"value":"published","count":2},{"value":"draft","count":1}],
    "tags":[{"value":"<tag-id>","count":2}],
    "category.name":[{"value":"Facet Demo","count":3},{"value":"GateCat A 1783050271","count":3},{"value":"catA-LG8c3b","count":3},{"value":"catC-LG8c3b","count":3},{"value":"E2E Guard Save b21784859754","count":2},{"value":"E2E Guard Save e2e saved","count":2},{"value":"Cat9c-1343377572","count":1},{"value":"Cat9c-1885194788","count":1},{"value":"GateCat B 1783050752","count":1},{"value":"GateCat B 1783051807","count":1},{"value":"catB-LG8c3b","count":1},{"value":"cjkDiagCat","count":1},{"value":"diagCat-r3","count":1}]
  },
  "aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-09-03T00:00:00"}}
}}
```

`status` and `tags` stay scoped to the category (their family — an own field, and a relation unrelated
to `category`/`categoryId` — is untouched by pruning `category.name`'s own family), but `category.name`
spans every category on the host. `aggregate` ignores pruning altogether: `count`/`max` over
`publishedAt` are computed against the *original* `categoryId` filter, matching only this category's two
published articles, regardless of which facets were also requested in the same query.

The JSON-envelope form (`POST /api/items/article/query`) is byte-for-byte equivalent to the query
string above for the same request:

```
$ curl -s -X POST http://localhost:5221/api/items/article/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"categoryId":{"_eq":"<category-id>"}},"facets":["status","tags","category.name"],"aggregate":{"count":["publishedAt"],"max":["publishedAt"]}}'
# identical response to the query-string request above
```

A translatable leaf falls back to a `null` bucket per id with no translation row at the effective
locale — live against the category, faceting on its `articles.title` (a one-hop O2M leaf onto the
translation sidecar), with only one of the three articles carrying a `zh-TW` translation:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Bname%5D%5B_eq%5D=Facet%20Demo&facets=articles.title"
{"success":true,"data":[{"id":"...","name":"Facet Demo", ...}],"meta":{"total":1,"limit":25,"offset":0,"facets":{"articles.title":[{"value":"Facet Demo Article One","count":1},{"value":"Facet Demo Article Three Draft","count":1},{"value":"Facet Demo Article Two","count":1}]}}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Bname%5D%5B_eq%5D=Facet%20Demo&facets=articles.title&locale=zh-TW"
{"success":true,"data":[{"id":"...","name":"Facet Demo", ...}],"meta":{"total":1,"limit":25,"offset":0,"facets":{"articles.title":[{"value":null,"count":2},{"value":"Facet Demo Article Two zh","count":1}]}}}
```

At the default locale all three titles show (each article has an `en` row); at `zh-TW` only the one
translated article keeps its own value and the other two — which have no `zh-TW` row — collapse into
one `{"value": null, "count": 2}` bucket.

### Count definition and a known limitation

A facet's `count` is always the number of **distinct root rows** that would match that value, never a
raw row count off a related table. For a to-many relation (`tags`) or a to-many leaf
(`articles.title`), the underlying SQL groups by the **target id** — never the leaf value directly,
even for the leaf form (previous section's "one hop + leaf" row) — with
`COUNT(DISTINCT <root-referencing column>)` (`SqlFunc.AggregateDistinctCount`) rather than plain
`COUNT(*)` — otherwise a root row joined to two targets sharing one value would be counted twice. An
own-field or many-to-one-FK facet needs no `DISTINCT` at all: each root row contributes to exactly one
group already.

**Known limitation**, inherent to the one-hop-plus-leaf form: it groups root rows by target *id* first
in one query, then swaps in the leaf value and merges same-value groups in memory (a second query) —
deliberately, so no join machinery is needed and a translatable leaf's sidecar lookup falls out of the
same shape. That means a root row linked to **two different targets that happen to share the same leaf
value** (e.g. two tags both named `"Guide"` on one article) is counted twice for that shared value —
the two id-keyed groups (each already a correct distinct-root count under its own target id) both
collapse into the merged leaf bucket and their counts are summed, rather than the union of root ids
being re-deduplicated after the merge. This is a deliberate trade-off, not a defect: fixing it would
require carrying root-id sets through the merge instead of counts, which no longer fits in two queries.

A second, related consequence of the two-step shape: `MaxFacetValues` (next section) caps the
**target-id** buckets from the first query, *before* the leaf-value merge runs — not the final,
merged leaf-value buckets. A leaf value can therefore be missing from the response even when the
target collection has fewer than `MaxFacetValues` distinct leaf values in total, if enough
higher-count target ids were kept by the first cap that the id(s) carrying that leaf value fell
outside it.

### NULL buckets

An own field or many-to-one FK's `NULL` value gets its own bucket (`{"value": null, "count": n}`) —
own-field and FK facets group by a column that is genuinely part of the root row, so `NULL` is a real,
countable group. A bare relation-name facet (`category`, `tags`, `articles`) never gets a "no relation"
bucket: computing "how many roots have *no* related row" needs an anti-join the current implementation
doesn't build, and in practice a caller wanting that count already has `_none` for it (chapter 7/8
above). A translatable leaf's `null` bucket (previous section) is a third, distinct case: it means "a
target id resolved fine, but no translation row exists at this locale," not "no target."

### Ordering and the value cap

Every facet is returned ordered by count **descending**, then value **ascending** as the tie-breaker,
truncated to `StruoQueryOptions.MaxFacetValues` (default 50); there is no `otherCount` remainder. For
the own-field, FK, and bare-relation-name forms this ordering and truncation happens in the database
(`OrderBy` + `Take`, `FacetQueries.Group`), and where the `NULL` bucket lands on an exact count tie is
engine-dependent, since neither SQLite nor PostgreSQL is asked for an explicit `NULLS FIRST`/
`NULLS LAST`: SQLite's default `ORDER BY value ASC` places `NULL` first, PostgreSQL's places it last.

The one-hop-plus-leaf form is different: its id-keyed buckets are ordered/truncated in the database
same as above, but the leaf-value merge that follows (previous section) re-sorts and re-truncates the
*merged* buckets **in memory** (`SwapLeafValues`, `FacetQueries.Leaf.cs`) — count descending, then a
custom `LeafValueComparer` for the value tie-break, which places `null` **last** unconditionally, on
every engine, rather than depending on the database's own NULL-ordering default the other three forms
are subject to.

### How many queries this costs

A plain list is 2 statements (`COUNT` + the page `SELECT`), unchanged by this feature. Each requested
facet adds exactly 1 more (own field, FK, or bare relation name) or 2 (a one-hop-plus-leaf facet — the
id/count query plus the leaf-value lookup); `aggregate` adds 1 more statement per 10 requested op/field
slots, batched by the fixed constant `AggregateRow.SlotCount = 10`
(`src/Struo.Infrastructure/Query/FacetRow.cs`, `AggregateQueries`'s `slots.Chunk(AggregateRow.SlotCount)`)
— **not** by `Query:MaxAggregates`, which only caps how many op/field slots a request may name at all
(`ValidateAggregate`) and is unrelated to the chunk size. At the default `Query:MaxAggregates` (10),
every valid request's aggregate fields fit in the one 10-slot chunk, so it always costs exactly one
aggregate statement; a fork that raises `Query:MaxAggregates` above 10 gets one additional aggregate
statement per additional 10 fields requested, since the chunking constant itself does not change.
`StruoQueryOptions.MaxFacets` (default 10) bounds the facet count, so the worst case for one request at
the defaults is `2 + 2·MaxFacets + 1` — or, more generally,
`2 + 2·MaxFacets + ⌈aggregate field count / 10⌉`. A registered `ISearchProvider` answering the
candidate path does not change any of this count: the provider call happens once, outside the
database, before the query runs — it replaces the `LIKE` group with an `id IN (...)` condition inside
the same statements, adding no extra query.

### Aggregate ops

| Op | Works on | Result |
|---|---|---|
| `count` | any visible own field, including a many-to-one FK — counts non-`NULL` rows | integer (`0` on an empty set, never `null`) |
| `sum`, `avg` | `Number`, `Slider`, `Rating` | `sum`: the field's own numeric type (an integer field → a wider integer, `decimal` → `decimal`, `double`/`float` → `double`); `avg`: always a `double`, regardless of the field's own type |
| `min`, `max` | the three interfaces above, plus `Date`, `DateTime` | the field's own type |

Every op other than `count` returns `null` over an empty matching set rather than `0`/`NaN`. Live,
`count`/`max` over `publishedAt` for the two published articles in the fixture category (the third is
still a draft, so its `publishedAt` is `null` and does not count):

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { articles(filter: { categoryId: { eq: \"<category-id>\" } }, facets: [\"status\", \"tags\"], aggregate: { count: [\"publishedAt\"], max: [\"publishedAt\"] }) { total facets { field values { value count } } aggregate } }"}'
{"data":{"articles":{"total":3,"facets":[{"field":"status","values":[{"value":"published","count":2},{"value":"draft","count":1}]},{"field":"tags","values":[{"value":"<tag-id>","count":2}]}],"aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-09-03T00:00:00"}}}}}
```

GraphQL's `aggregate` field is typed `Any` — chapter 10 covers the full shared-type surface
(`AggregateInput`/`FacetResult`/`FacetValue`) and its own transcript.

### Errors

Four live examples — an unknown facet field, a facet path with more than one relation hop, a facet on
an unfacetable interface (`Article.regions` is `MultiSelect`), and an aggregate op incompatible with the
field's interface (`sum` over `Article.status`, a `Select`):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?facets=bogusField"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogusField' on collection 'article'."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?facets=category.parent.name"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Facet paths support exactly one relation hop: 'category.parent.name'."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?facets=regions"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'regions' on collection 'article' (MultiSelect) cannot be used as a facet."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?aggregate%5Bsum%5D=status"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Aggregate 'sum' is not supported on field 'status' (Select)."}}
```

Two more `FacetPathResolver.Resolve` messages round out the catalog, both `BAD_USER_INPUT`: an empty
path segment (`facets=` with a blank entry in the comma list, or an empty string in the envelope's
array) is `"Facet path must not be empty."`, and a one-hop-plus-leaf path whose *relation* segment
(not the leaf) doesn't resolve — e.g. `facets=bogusRelation.name` — is `"Unknown relation
'bogusRelation' on collection 'article'."`, distinct from the plain `"Unknown field"` message an
unresolvable own field or a one-segment path gets.

A facet count exceeding `MaxFacets`, or an aggregate field count exceeding `MaxAggregates`, is rejected
the same way (`"Too many facets (max 10)."` / `"Too many aggregate fields (max 10)."`) before any
facet/aggregate SQL runs; a duplicate facet path is silently deduplicated rather than rejected or
computed twice. A facet whose relation hop targets a collection the caller cannot read fails
permission-first, exactly like a dotted filter path: `FORBIDDEN`/`UNAUTHORIZED`
(`"Read not permitted on '<collection>'."`), checked before the path's shape is even resolved, so an
unreadable relation's field names stay unprobeable through facet validation errors the same way they
are through filter validation errors (see "Validation" below).

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
never a nested relation's. Combining several plain (non-quantified) conditions on the same to-many
relation path is each-exists, not same-row — `_some`/`_none` above are the quantifiers that bind a
relation path's conditions to one related row instead — see
[chapter 7](07-relations.md#each-exists-vs-same-row-dotted-paths-vs-some-none) for the full semantics
table and live-verified transcripts.

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

`facets=`/`aggregate[<op>]=` go through this same `QueryValidator`: `ValidateAggregate` checks each
field against the identical own-field-plus-FK allowlist (`Known`) `CheckField` uses, and `ValidateFacets`
denies an unreadable facet target permission-first — its own single-segment
`DenyUnreadableFacetTarget`, the facet-path analogue of `DenyUnreadableHops` above — before resolving
the path's shape at all, so an unknown/hidden facet field and an unreadable relation target fail
exactly the way an unknown filter field or an unreadable dotted filter path do.

A registered `ISearchProvider`'s returned candidate ids skip `QueryValidator` entirely — they are not
user input, so there is no whitelist to check them against. Instead `SearchCandidateResolver`
(previous section) parses each one to the collection's primary-key CLR type, and a parse failure is
`InvalidOperationException`/500, not `QueryException`/400: the fork's provider is at fault, not the
caller.

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
