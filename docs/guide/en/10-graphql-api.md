# 10. GraphQL API

StruoCMS exposes a second, fully-typed API surface alongside REST: a single GraphQL endpoint whose
entire schema is generated from the same startup-cached collection metadata REST and the admin SPA
already share — there is no hand-written SDL file anywhere in the codebase. This chapter covers how
that generation works, how to explore the live schema, the query/filter/mutation surface it produces,
and its error shape and limits. Filter/sort/pagination *semantics* (operator meanings, the depth cap on
relation paths, `deleted`) are chapter 8's subject; this chapter covers how that same DSL is expressed as
typed GraphQL arguments instead of query-string/JSON-envelope conventions.

## Schema generation: no hand-written SDL

The whole schema is built at startup by `StruoTypeModule` (`src/Struo.Api/GraphQl/StruoTypeModule.cs`),
a HotChocolate `ITypeModule` registered in `GraphQlServiceCollectionExtensions.AddStruoGraphQl`
(`src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`). Its `CreateTypesAsync` reads
`IMetadataProvider.GetCollections()` — the same metadata `SchemaController`'s `GET /api/schema` and
`QueryValidator`'s whitelists (chapter 8) already read — and, per collection, `CollectionSchemaBuilder`
(`src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`) emits:

- an object type (e.g. `File`), with `id`/`version` always present, one field per non-`Hidden`,
  non-excluded own field, one field per relation, and a `translations: [Translation!]` field when the
  collection has a translation sidecar. A `File`/`Image`-interface own field additionally gets a
  companion resolver field alongside its own `ID`/`[ID!]` scalar — `<name-stripped-of-trailing-Id>:
  File` for a single `File`/`Image` field, `<name>Files: [File!]` for a `Files` field — which batches
  every id referenced across the whole response into one query via `FileFieldResolvers`' DataLoader (no
  N+1). None of the seven live framework collections declares an own field with any of these three
  interfaces, so this companion-field behavior isn't reachable on this host, but a fork adding e.g.
  `[CmsField(Interface = FieldInterface.Image)] public Guid? Cover { get; set; }` gets a `cover: ID` plus
  a `coverFile: File` for free;
- a list wrapper type (`FileList { items: [File!]!, total: Int! }`);
- a filter input (`FileFilterInput`) with `and`/`or`/`id` plus one operator-input field per filterable
  own field and per relation (cross-relation filtering, below);
- a create input (`FileCreateInput`) and an update input (`FileUpdateInput`, which additionally carries
  the optimistic-concurrency `version: Long`);
- two root `Query` fields (`file(id, locale)`, `files(filter, sort, limit, offset, search, locale,
  deleted)`) and four root `Mutation` fields (`createFile`, `updateFile`, `deleteFile`, `restoreFile`) —
  plus `fileRevisions`/`fileRevision` query fields and a `revertFile` mutation **only** when the
  collection declares `Revisions = true` (none of the seven framework collections do — see Revisions
  below).

`SchemaTypeMapper` (`src/Struo.Api/GraphQl/SchemaTypeMapper.cs`) is the single source of the
`FieldInterface` → SDL type mapping (e.g. `RichText`/`Markdown`/`Text` → `String`, `Number` → `Int`/
`Long`/`Float` depending on the CLR numeric type, `Files` → `[ID!]`, `Json`/`KeyValue` → the `Any`
scalar). `Password`, `Hidden` and `Divider` are excluded entirely — a `Password` field never appears in
the GraphQL schema at all, matching chapter 5's `SchemaTypeMapper.Excluded` set. Naming is entirely
mechanical (`Pascal`/`Camel`/a simple documented English pluraliser), so e.g. `mediaFolder` becomes
`mediaFolder`/`mediaFolders`/`MediaFolder`/`MediaFolderFilterInput` and `category` would become
`categories` (not `categorys`) — every name below was read from the live schema, not guessed.

## Endpoint and exploring it

The single endpoint is `POST /graphql`, mapped in `MapStruoGraphQl`
(`src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`). Two things are gated on environment,
both read from `GraphQlServiceCollectionExtensions`:

- **Introspection** — `.DisableIntrospection(!env.IsDevelopment())`: introspection queries (`__schema`,
  `__type`, …) work in Development and are refused outside it. This host is running in Development, so
  the schema details below were read directly off the live server via standard introspection queries,
  not transcribed from the type-module source.
- **Nitro IDE** (HotChocolate's bundled in-browser GraphQL explorer) — `options.Tool.Enable =
  app.Environment.IsDevelopment()`: same rule, browser tool only in Development.

**A third disclosure route exists, and it is not the one you would guess.** `GET /graphql?sdl` is a
HotChocolate built-in on the same `/graphql` path that serves the complete schema as plain SDL text —
anonymously, with no session cookie and no `X-Struo-CSRF` header (it is a safe `GET`, so
`CsrfProtectionMiddleware` never even considers it). Crucially, `.DisableIntrospection(...)` does
**not** cover it: that gate governs introspection *queries* (`__schema`/`__type` selections inside a
normal GraphQL request) and says nothing about the query-string route, and `MapGraphQL("/graphql")`
carries no environment gate of its own.

Earlier versions of this template gated only the former. Measured against a real Production-mode
instance, that shipped as: introspection query correctly refused (`HC0046`), browser IDE correctly gone
(`404`) — and `?sdl` still serving the entire schema to an anonymous caller. The lesson outlives the
bug, which is why it is recorded here: **"introspection is disabled" is not the same claim as "the
schema is not readable."**

**Both disclosure routes are now gated by one flag.** `GraphQl:ExposeSchema` (chapter 3) resolves once
in `GraphQlServiceCollectionExtensions.ResolveExposeSchema` and feeds both
`.DisableIntrospection(!exposeSchema)` and HotChocolate's `GraphQLServerOptions.EnableSchemaRequests`,
which is the option that actually governs the `?sdl` route. Unset — the default — means
Development-only, so nothing changes for a local install. Re-measured against Production-mode and
Development-mode hosts:

| Route | Development | Production (default) |
|---|---|---|
| `GET /graphql?sdl` | `200`, full SDL | **`404`, empty body** |
| `POST /graphql` introspection query | `200` | `400` `HC0046` |
| `POST /graphql` ordinary query | `200`, data | **`200`, data** |
| Nitro browser IDE (`GET /graphql`) | enabled | `404` |

**Closing disclosure does not close execution.** `?sdl` and introspection are schema *discovery*,
whereas a client executes through `POST /graphql` with a query document — and a client that already
knows its queries never reads the schema at runtime. The third row is the one that matters if you
consume this API from a GraphQL frontend: it is unaffected. `SchemaExposureGateTests` asserts it
explicitly, so a future change cannot quietly break the consumption path while tightening disclosure.

What closing it *does* affect is schema-dependent **tooling** — codegen, Postman/Insomnia schema
import, Apollo Sandbox, IDE plugins. Point those at a Development or staging instance, where both
routes are open.

If you are deliberately publishing a public GraphQL API and want the schema readable in production, turn
it back on by configuration alone, no rebuild:

```
GraphQl__ExposeSchema=true
```

That re-enables introspection *and* `?sdl` together, deliberately: whether a schema is public is one
decision, not two. The Nitro browser IDE stays Development-only regardless of this setting — shipping a
browser IDE is a much larger decision than serving SDL text. Blocking the route at the reverse
proxy/ingress remains a reasonable belt-and-braces measure, but it is no longer the only option.

A cookie-authenticated request to `/graphql` needs the same `X-Struo-CSRF` header REST writes need
(chapter 9). Because the rule keys on the HTTP method and GraphQL-over-HTTP is always `POST`, it
applies to a read-only *query* exactly as much as a mutation:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -b cookies.txt -d '{"query":"query { languages { total } }"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"query":"query { languages { items { code name isDefault } total } }"}'
{"data":{"languages":{"items":[{"code":"zh-TW","name":"繁體中文","isDefault":false},{"code":"en","name":"English","isDefault":true}],"total":2}}}
```

**A bearer token does authenticate `/graphql`.** `MapGraphQL` carries no `[Authorize]` attribute of its
own, but authentication itself no longer depends on one being present: the default authenticate scheme
is `AuthSchemes.Adaptive` (`src/Struo.Api/Auth/AuthWiring.cs`), a forwarding policy scheme that resolves
to the `Bearer` handler whenever the request's `Authorization` header starts with `Bearer `, and to
`Cookie` otherwise — on every endpoint, `/graphql` included. A bearer-only client is therefore resolved
as **itself**, with its own roles' grants (unioned with the `public` floor, chapter 12), exactly as a
cookie session would be. `CsrfProtectionMiddleware` exempts any request whose `Authorization` header
starts with `Bearer ` from the `X-Struo-CSRF` requirement above (chapter 9's CSRF section) — that check
runs, and returns, before the middleware ever looks for a session cookie, so the exemption holds even if
the request also happens to carry one. A `/graphql` call driven purely by a bearer token therefore needs
neither a session cookie nor the CSRF header.

Every GraphQL example in this chapter still runs against a cookie session with the CSRF header, because
that's the natural shape of a browser-based GraphQL client (Nitro IDE, a SPA) — not because a
bearer-only client is unable to drive `/graphql`. It can, on equal footing with a cookie session,
including every mutation below. Proof, not assertion — a bearer token minted for a role that grants
only `mediaFolder` read, with no cookie and no CSRF header, driving a `mediaFolders` query:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "Authorization: Bearer <token>" -d '{"query":"{ mediaFolders { items { id } } }"}'
{"data":{"mediaFolders":{"items":[{"id":"019fac90-2300-78da-8a3c-f281dac532e0"},{"id":"019fac8f-fb2b-77ae-a152-f25fddf54ef8"}]}}}
```

### Reading the live schema

An introspection query against the running server, restricted to the root types, confirms exactly the
seven collections wired into this host and nothing from the (un-opted-in) sample:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"{ __schema { queryType { fields { name } } mutationType { fields { name } } } }"}'
{"data":{"__schema":{"queryType":{"fields":[{"name":"_service"},{"name":"language"},{"name":"languages"},{"name":"permission"},{"name":"permissions"},{"name":"role"},{"name":"roles"},{"name":"user"},{"name":"users"},{"name":"userRole"},{"name":"userRoles"},{"name":"file"},{"name":"files"},{"name":"mediaFolder"},{"name":"mediaFolders"}]},"mutationType":{"fields":[{"name":"_service"},{"name":"createLanguage"},{"name":"updateLanguage"},{"name":"deleteLanguage"},{"name":"restoreLanguage"},{"name":"createPermission"},{"name":"updatePermission"},{"name":"deletePermission"},{"name":"restorePermission"},{"name":"createRole"},{"name":"updateRole"},{"name":"deleteRole"},{"name":"restoreRole"},{"name":"createUser"},{"name":"updateUser"},{"name":"deleteUser"},{"name":"restoreUser"},{"name":"createUserRole"},{"name":"updateUserRole"},{"name":"deleteUserRole"},{"name":"restoreUserRole"},{"name":"createFile"},{"name":"updateFile"},{"name":"deleteFile"},{"name":"restoreFile"},{"name":"createMediaFolder"},{"name":"updateMediaFolder"},{"name":"deleteMediaFolder"},{"name":"restoreMediaFolder"}]}}}}
```

`Query` carries 14 collection-derived fields (plus the `_service` anchor) — `language`/`languages`,
`permission`/`permissions`, `role`/`roles`, `user`/`users`, `userRole`/`userRoles`, `file`/`files`,
`mediaFolder`/`mediaFolders` — and `Mutation` carries 28 (plus its own `_service` anchor): `create`/
`update`/`delete`/`restore` × each of the same seven collections. No `revert*` mutation and no
`*Revisions`/`*Revision` query field appears anywhere (see Revisions below).

## Queries: single item, list, arguments

Every collection gets exactly two root query fields (`CollectionResolvers.SingleField`/`ListField`,
`src/Struo.Api/GraphQl/CollectionResolvers.cs`):

- **`{collection}(id: ID!, locale: String)`** — a single item, or `null` for an unknown/soft-deleted-by-
  default id (no error — GraphQL null, matching REST's `404` in intent but not in shape).
- **`{collection}s(filter, sort: [String!], limit: Int, offset: Int, search: String, locale: String,
  deleted: DeletedFilter, facets: [String!], aggregate: AggregateInput)`** — a page, returned as
  `{ items: [X!]!, total: Int!, facets: [FacetResult!]!, aggregate: Any }`. `deleted` is the SDL enum
  `EXCLUDE`/`ONLY`/`WITH`, bound directly onto the same `Struo.Domain.Query.DeletedFilter` REST uses;
  requesting `ONLY`/`WITH` is gated by the identical `DeletedAccessGuard.EnsureCanViewDeleted` check
  REST's `ItemsController` uses (chapter 8) — it requires delete permission on the collection. `facets`/
  `aggregate` are the same feature REST's `facets=`/`aggregate[<op>]=` expose (chapter 8's "Facets and
  aggregates" has the full syntax, semantics, and error catalog); `facets`/`aggregate` on the response
  are covered later in this section. Only the two root list fields carry these two arguments — a nested
  to-many list field (`article.tags(filter: …)`, below) does not.

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { user(id: \"019fa8b2-4d09-7155-b641-2c3e2519233b\") { id email name roles { id name } } }"}'
{"data":{"user":{"id":"019fa8b2-4d09-7155-b641-2c3e2519233b","email":"admin@admin.com","name":"Administrator","roles":[{"id":"019fa8b2-4edb-702d-80fc-27ea33c18b6a","name":"admin"}]}}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { files(filter: { size: { gte: 20 } }, sort: [\"-size\"], limit: 2, offset: 0) { items { id fileName size } total } }"}'
{"data":{"files":{"items":[{"id":"...","fileName":"gamma-draft.txt","size":35},{"id":"...","fileName":"beta-notes.txt","size":23}],"total":3}}}
```

`facets`/`aggregate` on the list field's response — `facets: [FacetResult!]!` (empty array when the
`facets` argument was omitted) and `aggregate: Any` (`null` when the `aggregate` argument was omitted)
— share the two output types `SharedFacetTypes.Build` (`src/Struo.Api/GraphQl/SharedFacetTypes.cs`)
builds once for every collection: `FacetResult { field: String!, values: [FacetValue!]! }` and
`FacetValue { value: Any, count: Int! }`, mirroring the domain `FacetResult`/`FacetBucket` records. The
argument type `AggregateInput` has one `[String!]` field per aggregate op (`count`/`sum`/`min`/`max`/
`avg`), built from the same op set `QueryParser.AggregateOps` uses for REST's `aggregate[<op>]=` keys.
Live, against the same fixture as chapter 8 (one category holding three articles — two `published`, one
`draft`, two sharing a tag):

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { articles(filter: { categoryId: { eq: \"<category-id>\" } }, facets: [\"status\", \"tags\"], aggregate: { count: [\"publishedAt\"], max: [\"publishedAt\"] }) { total facets { field values { value count } } aggregate } }"}'
{"data":{"articles":{"total":3,"facets":[{"field":"status","values":[{"value":"published","count":2},{"value":"draft","count":1}]},{"field":"tags","values":[{"value":"<tag-id>","count":2}]}],"aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-09-03T00:00:00"}}}}}
```

`FacetValue.value`'s `Any` type keeps its original JSON kind — a numeric facet's bucket values come
back as GraphQL numbers, not stringified — the same way `aggregate`'s `Any` keeps `count`'s integer and
`max`'s `DateTime` string distinct rather than coercing both to one scalar type.

A soft-deleted row's single-item field resolves to `null` (not an error), and `deleted: ONLY` surfaces
it in the list field:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { file(id: \"<id>\") { id fileName } }"}'
{"data":{"file":null}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { files(deleted: ONLY) { items { id fileName } total } }"}'
{"data":{"files":{"items":[{"id":"...","fileName":"gamma-draft.txt"}],"total":1}}}
```

## Filters, including cross-relation dotted paths

`FileFilterInput` carries `and`/`or` (each `[FileFilterInput!]`, exactly one level — same restriction as
REST's JSON-envelope `_and`/`_or`, chapter 8), `some`/`none` (each a bare `FileFilterInput` — the
relation quantifiers, covered below; meaningless at this root level and rejected if used here), `id:
IdFilter`, one operator-input field per filterable own field (`StringFilter`/`IntFilter`/`FloatFilter`/`DateTimeFilter`/`BooleanFilter`/`IdFilter` — built
once in `SharedFilterTypes.cs` and reused across every collection), and — the cross-relation part — a
relation's own foreign-key column as an `IdFilter` (parity with REST's FK-filtering allowlist, chapter
8) **plus** a nested filter input on the relation's target type. `FilterInputTranslator`
(`src/Struo.Api/GraphQl/FilterInputTranslator.cs`) flattens a nested relation filter into the same
dotted `FieldPath` (`"folder.name"`) chapter 8's `FilterTranslator` pushes down into a subquery —
the GraphQL and REST cross-relation paths converge on identical `FilterNode` trees before either
one reaches the query validator:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { users(filter: { roles: { name: { eq: \"Editor\" } } }) { items { id email roles { name } } total } }"}'
{"data":{"users":{"items":[{"id":"...","email":"editor@example.com","roles":[{"name":"Editor"}]}],"total":1}}}
```

`isNull: Boolean` on every operator input renders the same pure `IS [NOT] NULL` chapter 8 documents for
`_null`/`_nnull` — no comparison value reaches the database for it.

### Relation quantifiers: `some`/`none`, and `_junction` via `<Parent><Rel>RelationFilterInput`

Every `<T>FilterInput` additionally carries `some: <T>FilterInput` and `none: <T>FilterInput` — the
same relation quantifiers chapter 7/8 cover for REST, reserved the same way `and`/`or` are. They are
only meaningful **nested inside a relation's own filter dict**; used at the root of any `filter`
argument (own-field or a nested-list's own `filter` arg) they are rejected:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { article(id: \"<id>\") { id tags(filter: { some: { name: { eq: \"Guide\" } } }) { id } } }"}'
{"errors":[{"message":"'some' is only valid inside a relation filter.","path":["article"],"extensions":{"code":"BAD_USER_INPUT"}}],"data":{"article":null}}
```

(That last query's `tags(filter: ...)` is a to-many *nested-list* argument, resolved by the same
`deep`-expansion machinery below — its own `filter` argument's type is the plain, unchanged
`TagFilterInput`, not the relation-specific input described next; `some`/`none` there are evaluated
at that argument's own root, which is exactly the "root, no relation context" case the error above
names.)

For a many-to-many relation whose junction carries an exposable payload (chapter 7's junction
payload and `_junction` read projection), the parent type's filter field for that relation is **not** the
plain `<Target>FilterInput` — it is a relation-specific `<Parent><Rel>RelationFilterInput`, which adds
a `junction: <Parent><Rel>JunctionFilterInput` field (one operator-input field per exposable payload
field) alongside the target's own filterable fields and `and`/`or`/`some`/`none` of itself. The
sample's `Article.tags` (payload: `note`) is the shipped example — introspection confirms the type
names (`ArticleTagsRelationFilterInput`/`ArticleTagsJunctionFilterInput`, following
`SchemaTypeMapper.RelationFilterInputName`/`JunctionFilterInputName`), and `some` + `junction` compose
in one query exactly like REST's `_some`/`_junction`:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { articles(filter: { tags: { some: { name: { eq: \"Guide-u3doc0905\" }, junction: { note: { eq: \"hero\" } } } } }) { items { id } total } }"}'
{"data":{"articles":{"items":[{"id":"<a-id>"}],"total":1}}}
```

A relation whose junction carries no exposable payload keeps the plain, shared `<Target>FilterInput`
— this relation-specific renaming is the one GraphQL type name that changes under this feature: a
payload-bearing relation's filter field used to carry the plain, shared `<Target>FilterInput` type
(`TagFilterInput` for `Article.tags`) and now carries the relation-specific
`ArticleTagsRelationFilterInput` instead — a client that declares that field's type explicitly as a
GraphQL variable (rather than letting the query embed it inline) must update it.

## Nested to-many lists and their arguments

A to-many relation (`OneToMany`/`ManyToMany`) field on an object type carries its own
`filter`/`sort`/`limit`/`offset` arguments, resolved by `CollectionResolvers.BuildDeep` walking the
client's selection tree into a nested `DeepSpec` — the same relation-expansion machinery chapter 7's
`deep=` uses, just driven by GraphQL selections instead of a query-string list. `User.roles` (the
framework's one live many-to-many, `user` ↔ `role` via the `userRole` junction) demonstrates it:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { user(id: \"<editor-id>\") { id email roles(sort: [\"name\"], limit: 1) { id name } } }"}'
{"data":{"user":{"id":"...","email":"editor@example.com","roles":[{"id":"...","name":"Editor"}]}}}
```

A many-to-one relation (e.g. `File.folder`, `MediaFolder.parent`) stays a bare object field with no
arguments at all — `CollectionSchemaBuilder` only attaches `filter`/`sort`/`limit`/`offset` to
`OneToMany`/`ManyToMany` fields, since an M2O side resolves to at most one row.

Unlike the two root list fields above, a nested to-many field's argument list stops at
`filter`/`sort`/`limit`/`offset` — `facets`/`aggregate` are v1-scoped to the root list fields only
(`AddRelationField`, `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`, never adds them to a relation
field), so `article.tags(facets: [...])` is not a schema error away from working, it is simply not part
of the generated schema at all.

## Mutations: create, update, delete — typed inputs and partial-update semantics

Every collection gets `create{X}(input: XCreateInput!, locale: String): X`,
`update{X}(id: ID!, input: XUpdateInput!, locale: String): X`, `delete{X}(id: ID!, purge: Boolean):
Boolean`, and `restore{X}(id: ID!): X` (`MutationResolvers`,
`src/Struo.Api/GraphQl/MutationResolvers.cs`). `create`/`update` convert the typed input back into the
same `JsonElement` `ItemService.CreateAsync`/`UpdateAsync` already accept
(`MutationInputMapper.ToJsonElement`), then **re-read** the row after the write so the returned node has
the same shape a query would produce (relations/translations resolvable) — a re-read denied by RBAC
falls back to the raw write result rather than surfacing as an error, so a successful write is never
hidden behind a permissions failure on the follow-up read.

**Partial-update semantics carry a real subtlety.** HotChocolate's coerced input dictionary backfills
*every* declared input field with `null` when the client didn't send it — which would otherwise make a
partial `updateX` indistinguishable from "explicitly null out everything I didn't mention." The
resolvers guard against this with `SentFieldsOnly`, which re-derives the *actually sent* key set from
the request's own argument literal (recursing into nested inputs — a Repeater/Translation sub-object —
so an omitted nested field isn't backfilled either) before handing the pruned dictionary to
`ItemService`, whose own `bodyKeys` merge (chapter 9) then overlays only those keys onto the existing
row. **Chapter 9's `Required`-field semantics apply identically here**: the pruned dictionary only
carries the keys `SentFieldsOnly` found in the request literal, and `ItemService.UpdateCoreAsync`
validates `Required` against the entity that merge produces (chapter 9), not against the raw input — so
`updateRole` can omit `name` (`Required=true`) and still succeed, keeping the role's stored name, while
explicitly sending `name: null` still fails with `BAD_USER_INPUT`. This is not a REST-vs-GraphQL
difference, it's the same shared write path:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { createRole(input: { name: \"Reviewer\", description: \"Docs demo role\" }) { id name description } }"}'
{"data":{"createRole":{"id":"...","name":"Reviewer","description":"Docs demo role"}}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { updateRole(id: \"<id>\", input: { description: \"Updated via GraphQL\" }) { id name description } }"}'
{"data":{"updateRole":{"id":"...","name":"Reviewer","description":"Updated via GraphQL"}}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { deleteRole(id: \"<id>\") }"}'
{"data":{"deleteRole":true}}
```

A many-to-many relation is written as a plain `[ID!]` array of target ids on the create/update input
(`roles` on `UserCreateInput`/`UserUpdateInput`), sharing REST's underlying M2M sync end to end
(chapter 9): the array is diffed against what's already linked rather than deleted and reinserted
wholesale, so a junction row for a target that stays linked keeps its own primary key:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { updateUser(id: \"<id>\", input: { email: \"editor@example.com\", name: \"Editor Person\", roles: [\"<role-id>\"] }) { id email name isActive roles { id name } } }"}'
{"data":{"updateUser":{"id":"...","email":"editor@example.com","name":"Editor Person","isActive":true,"roles":[{"id":"...","name":"Editor"}]}}}
```

### `<rel>Links`: reading and writing junction payload

For a many-to-many relation whose junction declares at least one exposable payload field (non-`Hidden`,
scalar-mappable — chapter 7), `CollectionSchemaBuilder` additively generates a parallel, richer surface
alongside the plain `<rel>`/`[ID!]` one above; a relation whose junction has no exposable payload (like
`User.Roles`) gets none of this — only the plain surface. On the read side, `<rel>Links:
[<Parent><Rel>Link!]` sits next to `<rel>: [<Target>!]`, where `<Parent><Rel>Link = { node: <Target>!,
junction: <Parent><Rel>Junction }` — `node` is the same target row `<rel>` would return, and `junction`
is an object of the relation's non-`Hidden` payload fields, or `null` when the caller cannot read the
junction collection. On the write side, the create/update input gains `<rel>Links:
[<Parent><Rel>LinkInput!]` next to the existing `<rel>: [ID!]`, where `<Parent><Rel>LinkInput = { id:
ID!, <writable payload fields...> }`. Selecting `<rel>Links` in a query drives the same deep expansion
`<rel>` does; sending `<rel>Links` in a mutation folds straight into REST's mixed-array write shape
(chapter 9) under the hood — `MutationResolvers.FoldLinks` rewrites it into the `<rel>` key before the
request reaches `ItemService`. If a mutation sends **both** `<rel>` and `<rel>Links` for the same
relation, `<rel>Links` wins outright, including an explicit `<rel>Links: null`, which discards a
simultaneously-sent `<rel>` array rather than leaving it alone.

The sample's `Article.Tags` (chapter 16) is the shipped example — its junction, `ArticleTag`, exposes
`note` as payload, so the generated schema additionally carries, on `Article`, `tagsLinks:
[ArticleTagsLink!]` where `ArticleTagsLink { node: Tag!, junction: ArticleTagsJunction }` and
`ArticleTagsJunction { note: String }`, and, on `ArticleCreateInput`/`ArticleUpdateInput`, `tagsLinks:
[ArticleTagsLinkInput!]` where `ArticleTagsLinkInput = { id: ID!, note: String }`. Shape (illustrative —
schema type/field names, matching `SchemaTypeMapper`'s
`<Parent><Rel>Link`/`<Parent><Rel>Junction`/`<Parent><Rel>LinkInput` type naming and `<rel>Links` field
naming):

```graphql
type ArticleTagsJunction { note: String }
type ArticleTagsLink { node: Tag!, junction: ArticleTagsJunction }
input ArticleTagsLinkInput { id: ID!, note: String }

# on Article: tags: [Tag!]  (unchanged)  +  tagsLinks: [ArticleTagsLink!]
# on ArticleUpdateInput: tags: [ID!]  (unchanged)  +  tagsLinks: [ArticleTagsLinkInput!]
```

```json
{ "tagsLinks": [{ "id": "<tag-id>", "note": "editor pick" }] }
```

`AdminOnly`-collection writes (`permission`/`role`/`user`/`userRole`) require super-admin here exactly
as in REST — `ItemService.RequireSuperAdminForAdminOnly` is the same check both protocols call through:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b editor-cookies.txt \
    -d '{"query":"mutation { updateRole(id: \"<id>\", input: { name: \"Editor\", description: \"hack\" }) { id } }"}'
{"errors":[{"message":"Writes to 'role' require a super-admin.","path":["updateRole"],"extensions":{"code":"FORBIDDEN"}}],"data":{"updateRole":null}}
```

A delete blocked by `OnDelete.Restrict` (chapter 7) surfaces the identical `CONFLICT` code REST returns:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { deleteMediaFolder(id: \"<guides-id>\") }"}'
{"errors":[{"message":"Cannot delete 'mediaFolder/<guides-id>': referenced by 'file'.","path":["deleteMediaFolder"],"extensions":{"code":"CONFLICT"}}],"data":{"deleteMediaFolder":null}}
```

`delete`/`restore` return REST-404-parity `null`/`false` for an unknown id rather than a GraphQL error —
the same "unknown id is data, not a fault" stance the query fields take.

## Translations in mutations

A collection with a translation sidecar gets a `translations: [XTranslationInput!]` field on both
create and update inputs, where `XTranslationInput = { locale: String!, fields: XTranslationFieldsInput!
}` — the read side's `[Translation!]` shape mirrored back as an input. `MutationResolvers.FoldTranslations`
converts that list into the locale-keyed object `ItemService.SyncTranslationsAsync` expects
(`{ "<locale>": { <field>: <value> } }`) after `SentFieldsOnly` has already pruned each entry down to
its client-sent sub-fields — a duplicate locale in the list is last-wins, and create still enforces a
default-locale translation being present (chapter 6), same as REST:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { updateFile(id: \"<id>\", input: { translations: [ { locale: \"en\", fields: { title: \"Gamma Draft (GraphQL)\", alt: \"edited via GraphQL\" } } ] }) { id fileName translations { locale fields } } }"}'
{"data":{"updateFile":{"id":"...","fileName":"gamma-draft.txt","translations":[{"locale":"en","fields":{"title":"Gamma Draft (GraphQL)","alt":"edited via GraphQL"}}]}}}
```

## Revisions through GraphQL

When a collection declares `Revisions = true`, `StruoTypeModule` adds two query fields
(`{collection}Revisions(id: ID!): [Revision!]!`, `{collection}Revision(id: ID!, revisionNumber: Int!):
Revision`) and one mutation (`revert{X}(id: ID!, revisionNumber: Int!): X`) — `RevisionResolvers.cs`. The
shared `Revision` type is `{ revisionNumber, operation, createdAt, createdBy, sourceRevisionNumber, snapshot }`; the list field
returns `snapshot: null` on every entry (metadata only, mirroring REST's list endpoint), and the single
field populates it, parsed into the `Any` scalar. **None of the seven live framework collections declares
`Revisions = true`**, so none of these fields exist in this host's schema at all right now — confirmed
directly off the introspected `Query`/`Mutation` field lists above (no `*Revisions`, no `*Revision`, no
`revert*`). A fork that opts a collection into `[CmsCollection(Revisions = true)]` gets this surface for
free, with no GraphQL-layer code to write; REST's identical revisions endpoints (chapter 9) are the only
place this behavior is currently exercisable live, and only in their empty/`404` "no revisions configured"
form there too.

## Error shape (`StruoErrorFilter`)

`StruoErrorFilter` (`src/Struo.Api/GraphQl/StruoErrorFilter.cs`) is the GraphQL-side twin of REST's
`StruoExceptionHandler`: it maps a **resolver** exception through the same `DomainErrorMap` and stamps
the result's `extensions.code` with the identical stable code string REST uses (`UNAUTHORIZED`,
`FORBIDDEN`, `NOT_FOUND`, `CONFLICT`, `VERSION_CONFLICT`, `BAD_USER_INPUT`, `PAYLOAD_TOO_LARGE`,
`SEARCH_UNAVAILABLE`, `SESSION_REVOCATION_FAILED`, `INTERNAL_SERVER_ERROR` — `DomainErrorMap.Map`
makes no distinction between REST and GraphQL callers, so every mapped exception type,
`PayloadTooLargeException` included, stamps the same code regardless of which protocol's
resolver/action threw it). This case — an exception thrown while a resolver is
actually running against a syntactically/structurally valid request — keeps the transport HTTP status
at `200`; the caller is expected to inspect `extensions.code` per error rather than the status line.
`SEARCH_UNAVAILABLE` (chapter 8's [Search providers](08-query-dsl.md#search-providers)) is no
exception to this: GraphQL still answers with `200` and the code in `extensions.code`, even though
REST maps the same `SearchUnavailableException` to HTTP `503` instead. The transcript below shows the
`200`-plus-`extensions.code` shape with a permission error:

```
$ curl -s -i -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -d '{"query":"query { users { total } }"}'
HTTP/1.1 200 OK
{"errors":[{"message":"Read not permitted.","path":["users"],"extensions":{"code":"UNAUTHORIZED"}}],"data":null}
```

An unmapped exception here is masked to the generic message and logged server-side, exactly as REST
does. A request-level error with **no** `Exception` attached at all — the document doesn't parse, names
a field that doesn't exist, or (see Depth limits below) violates the execution-depth/cost rules before
any resolver runs — never reaches `StruoErrorFilter`'s exception branch, carries no `code` extension,
and HotChocolate's own ASP.NET Core integration answers it with HTTP `400` instead of `200`:

```
$ curl -s -i -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { bogusThing { id } }"}'
HTTP/1.1 400 Bad Request
{"errors":[{"message":"The field `bogusThing` does not exist on the type `Query`.","locations":[{"line":1,"column":9}],"extensions":{"type":"Query","field":"bogusThing","responseName":"bogusThing","specifiedBy":"https://spec.graphql.org/September2025/#sec-Field-Selections"}}]}
```

## Depth limits

Three independent limits apply, all configured in `AddStruoGraphQl`:

- **Execution depth** — `.AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)`: a request whose
  selection tree nests more than 12 levels deep is rejected before any resolver runs, introspection
  exempted.
- **Cost analysis** — `.AddCostAnalyzer()` with `MaxFieldCost = 150.0` / `MaxTypeCost = 150.0`: the
  alias-amplification defense. HotChocolate 16.6.0's validation rule set has no dedicated
  alias/operation-count rule (checked directly against the `HotChocolate.Validation` 16.6.0 assembly),
  so cost analysis carries this defense instead — every aliased selection accrues its own field cost,
  so repeating an expensive list field under many aliases costs roughly proportionally and is rejected
  the same way. Calibrated against the project's own GraphQL test suite: the heaviest legitimate
  query measured `fieldCost = 33`; a 50-alias amplification measured `fieldCost = 550`; `150` sits
  between the two.
- **Relation-path depth** — chapter 8's 6-hop cap on a dotted relation path applies unchanged here,
  since `FilterInputTranslator`'s cross-relation filters and `BuildDeep`'s nested-list expansion both
  flow into the identical `QueryValidator` REST uses.

A query nesting the framework's one self-referencing relation (`MediaFolder.parent`) past the
execution-depth limit is rejected with both the depth-rule error and (since the walk itself still runs
partway) a HotChocolate internal cycle-guard error — a request-level rejection like this one is another
`HTTP 400` case, same as the unknown-field example above:

```
$ curl -s -i -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { mediaFolder(id: \"019fac8f-fb2b-77ae-a152-f25fddf54ef8\") { id parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { id } } } } } } } } } } } } } } }"}'
HTTP/1.1 400 Bad Request
{"errors":[{"message":"The GraphQL document has an execution depth of 15 which exceeds the max allowed execution depth of 12.","locations":[{"line":1,"column":1}],"extensions":{"allowedExecutionDepth":12,"detectedExecutionDepth":15}},{"message":"Maximum allowed coordinate cycle depth was exceeded.","locations":[{"line":1,"column":97}],"path":["mediaFolder","parent","parent","parent"],"extensions":{"code":"HC0087"}}]}
```

## Next steps

- Chapter 8, [Query DSL](08-query-dsl.md), for the filter operators, the `deleted`/`locale` semantics,
  and the 6-hop relation-path depth cap this chapter's arguments compile down to.
- Chapter 7, [Relations](07-relations.md), for `OnDelete.Restrict`, many-to-many junctions, and
  self-referencing trees (the `MediaFolder.parent` example above).
- Chapter 9, [REST API](09-rest-api.md), for the response envelope, error-code catalog, and the
  `X-Struo-CSRF`/bearer-authentication details this chapter shares verbatim with REST.
- Chapter 13, [Revisions & Soft Delete](13-revisions-and-soft-delete.md), for `[CmsCollection(Revisions
  = true)]` and what a live revisioned collection's GraphQL surface looks like once one is opted in.
