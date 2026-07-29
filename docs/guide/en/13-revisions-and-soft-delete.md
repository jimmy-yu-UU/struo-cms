# 13. Revisions & Soft Delete

Two independent, opt-in write-side features live behind the generic `ItemService`: a per-collection
revision history that snapshots every create/update/delete/restore/revert, and per-collection soft
delete that trashes instead of destroying a row by default. Neither is on by default for any collection,
and they are independent opt-ins — a collection can have neither, either, or (in principle) both.
Chapters 8 and 9 already document their wire surface (`deleted=`, the `.../revisions` endpoints); this
chapter covers the mechanism, what a snapshot actually contains, what revert does and does not restore,
and how the two features interact when a collection has both.

## Enabling revisions per collection

`[CmsCollection("X", Revisions = true)]`
(`src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs:29-35`) is the entire opt-in: every
successful create/update on that collection appends a full snapshot of the item's post-write state to
the framework `revisions` table, and any past revision can later be re-applied via revert. It costs
nothing extra to declare — no interface to implement, no extra column on the entity — since revision rows
live in one shared table keyed by `(collectionName, itemId, revisionNumber)`, not on the entity itself.

**None of the seven core framework collections (`language`, `permission`, `role`, `user`, `userRole`,
`file`, `mediaFolder`) declares `Revisions = true`** — confirmed directly against the source (no
occurrence of `Revisions = true` anywhere under `src/`). The sample Blog's `Article` collection (chapter
16) does: `[CmsCollection("Article", ..., Revisions = true)]`, and it additionally implements
`ISoftDeletable` — the walkthrough below uses it to show the full create/update/list/view/revert cycle
against a real, running collection, exactly as it would work for any collection a fork opts in the same
way.

## What a snapshot contains, and when it is captured

A snapshot is built by `RevisionSnapshotBuilder.BuildAsync`
(`src/Struo.Application/Query/RevisionSnapshotBuilder.cs`) from the **already-persisted** entity, right
after the row itself is written, and is deliberately full-fidelity — no RBAC field filtering, no
hidden-field skip — because a revert must be able to restore the entire item state regardless of who is
viewing a revision later. It assembles a single JSON object containing:

- `id` and, for an `AuditableEntity`, `version` (present for reference; stripped again before revert
  re-applies the snapshot — see below).
- Every own `[CmsField]` **except** system-managed fields (`IsSystem`) and translatable fields (which
  live under `translations` instead) — a `Json`-interface field's raw string is parsed back into a
  structured `JsonElement` so it serializes as JSON, not a quoted string.
- Every many-to-one relation's foreign-key id, under its camelCase name (e.g. `folderId`) — these are
  declared via `[CmsRelation]`, not `[CmsField]`, so the own-field pass above doesn't already cover them.
- Every many-to-many relation as an **ordered id array** under the relation name (sorted by the
  relation's declared sort property when one exists).
- `translations`: `{ locale: { camelField: value } }` for every locale the item has a translation row
  for — the full sidecar state, not just the query-effective locale.

This is exactly the shape `ItemService.UpdateAsync` consumes as a request body — which is precisely the
point: a snapshot round-trips through the *normal write path* on revert, rather than needing a
special-cased restore routine. Creating an `article` and inspecting its first revision shows the shape
live — `categoryId` (an M2O foreign key, absent from `[CmsField]`s) and `tags` (an M2M id array) are
present even though this particular item set neither, and `translations.en` carries the full sidecar row:

```
$ curl -s -X POST http://localhost:5221/api/items/article -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt -d '{
      "status": "draft",
      "internalNote": "secret-note-v1",
      "translations": { "en": { "title": "Original Title", "body": "Original body text.", "internalSlug": "original-slug-v1" } }
    }'
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":0,"status":"draft", ...}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions/1"
{"success":true,"data":{"revisionNumber":1,"operation":"create","createdAt":"2026-07-29T08:47:18.943677","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b","snapshot":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"categoryId":null,"tags":[],"translations":{"en":{"title":"Original Title","body":"Original body text.","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null}}}}}
```

(Notice `internalNote` — set on the create request above — does not appear anywhere in this snapshot
response, and `translations.en` has no `internalSlug` key either. Both fields are `Hidden`; see Redaction
below for why, and for proof the value was still captured.)

**Capture happens inside the same database transaction as the write it describes**
(`ItemService.CreateAsync`/`UpdateCoreAsync`, `src/Struo.Application/Query/ItemService.cs`):

| Operation | Where captured | Recorded `operation` value |
|---|---|---|
| Create | Inside `repository.InTransactionAsync` in `CreateAsync`, immediately after the M2M/translation sync (`ItemService.cs:129-133`) | `"create"` |
| Update | Inside `repository.InTransactionAsync` in `UpdateCoreAsync`, same position (`ItemService.cs:214-218`) | `"update"` (or `"revert"` — see below) |
| Trash (soft delete) | Via `CaptureRevisionAsync`, inside the same transaction as the atomic trash UPDATE, only if it actually affected a row (`ItemService.cs:257-297`) | `"delete"` |
| Restore | Via `CaptureRevisionAsync`, inside the same transaction as the atomic restore UPDATE (`ItemService.cs:320-341`) | `"restore"` |
| Revert | Re-applies the snapshot as a normal update through `UpdateCoreAsync` (see below), which itself captures a new snapshot | `"revert"` |

Committing capture inside the same transaction as the write means a revision row can never exist for a
write that itself rolled back, and — for trash/restore specifically — the capture is conditioned on the
underlying atomic `WHERE deletedat IS NULL`/`IS NOT NULL` update having actually affected a row, so two
concurrent trash/restore calls racing the same row cannot each record a spurious duplicate revision (the
same TOCTOU-safe pattern chapter 9's optimistic-concurrency section describes for the write path
generally).

`SqlSugarRevisionStore.CaptureAsync` (`src/Struo.Infrastructure/Revisions/SqlSugarRevisionStore.cs`)
assigns `RevisionNumber` as `max(existing) + 1` for that `(collection, itemId)` pair — race-free because
the single-item write path is already serialized by the time capture runs inside the same transaction —
and stamps `CreatedAt`/`CreatedBy` from the current request. The `revisions` table itself
(`src/Struo.Infrastructure/Revisions/Revision.cs`) is a plain framework table, **not** a
`[CmsCollection]` — it is never browsable or CRUD-able through the generic item API — append-only (rows
are inserted, never updated), and carries no `ISoftDeletable`/audit shape of its own.

## Redaction of hidden fields in snapshots

Because a snapshot captures the **entire** item including any field flagged `Hidden` (chapter 12), a
snapshot handed to an external caller must never leak one. `RevisionSnapshotRedactor.RedactHidden`
(`src/Struo.Application/Query/RevisionSnapshotRedactor.cs`) produces a redacted **copy** — omitting any
top-level key whose field metadata is `Hidden`, and, inside `translations.{locale}`, any key belonging to
a field that is both `Hidden` and `Translatable` — leaving every other value (nested objects, arrays,
numbers, booleans, nulls) copied through unchanged. This redaction is applied **only** on the external
single-revision read path (`ItemService.GetRevisionAsync`, REST's `GET
.../revisions/{n}` and GraphQL's `{collection}Revision`) — `RevertAsync` deliberately reads the **raw,
unredacted** snapshot straight from the store, because a revert has to be able to restore a `Hidden`
field's value too (e.g. a revisioned collection with a hidden credential-shaped field must have that
credential actually restored by a revert, not nulled out). A hidden value therefore only ever leaves the
process through the revert path's effect (rewriting the live row), never through a snapshot response
body.

`Article.InternalNote` (top-level) and `ArticleTranslation.InternalSlug` (per-locale) are both `Hidden`.
Updating the article above to different `internalNote`/`internalSlug` values, then reading back revision
1's snapshot again, still shows neither key — redaction applies uniformly to every past revision, not
just the newest:

```
$ curl -s -X PUT http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27 \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{
      "status": "published",
      "internalNote": "secret-note-v2-CHANGED",
      "translations": { "en": { "title": "Updated Title", "body": "Updated body text.", "internalSlug": "updated-slug-v2-CHANGED" } }
    }'
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":1,"status":"published", ...}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions/2"
{"success":true,"data":{"revisionNumber":2,"operation":"update","createdAt":"2026-07-29T08:47:40.66141","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b","snapshot":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":1,"status":"published","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"categoryId":null,"tags":[],"translations":{"en":{"title":"Updated Title","body":"Updated body text.","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null}}}}}
```

Neither redacted snapshot shows `internalNote` or `translations.en.internalSlug` — but the values were
genuinely captured, not dropped: reverting to revision 1 (next section) restores `internalNote` to
`"secret-note-v1"` and `internalSlug` to `"original-slug-v1"`, confirmed directly against the
database, even though neither value is ever visible through any API response.

## Listing, viewing and reverting revisions

**REST** (chapter 9, full endpoint table) — `GET /api/items/{collection}/{id}/revisions` (list,
newest-first metadata only: `revisionNumber`, `operation`, `createdAt`, `createdBy`), `GET
.../revisions/{n}` (one revision plus its redacted `snapshot`), `POST .../revisions/{n}/revert` (applies
it). All three require the collection's ordinary `CanRead`/`CanWrite` grant respectively — there is no
extra permission tier specific to revisions. Listing `article`'s two revisions so far (newest first):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions"
{"success":true,"data":[{"revisionNumber":2,"operation":"update","createdAt":"2026-07-29T08:47:40.66141","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b"},{"revisionNumber":1,"operation":"create","createdAt":"2026-07-29T08:47:18.943677","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b"}]}
```

**GraphQL** (chapter 10, `RevisionResolvers.cs`) — when a collection declares `Revisions = true`,
`StruoTypeModule` adds `{collection}Revisions(id: ID!): [Revision!]!`, `{collection}Revision(id: ID!,
revisionNumber: Int!): Revision`, and a `revert{X}(id: ID!, revisionNumber: Int!): X` mutation, all
generated with no collection-specific GraphQL code required — the shared `Revision` type is `{
revisionNumber, operation, createdAt, createdBy, snapshot }`. Confirmed by introspecting `article`'s own
generated schema — `articleRevisions`/`articleRevision` on `Query`, `revertArticle` on `Mutation`,
alongside the ordinary generated `article`/`articles`/`createArticle`/`updateArticle`/`deleteArticle`/
`restoreArticle` — and by actually calling the generated query field:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"{ articleRevisions(id: \"019fad0e-8904-7ac7-a20e-796f1c50ea27\") { revisionNumber operation createdAt } }"}'
{"data":{"articleRevisions":[{"revisionNumber":2,"operation":"update","createdAt":"2026-07-29T08:47:40.66141Z"},{"revisionNumber":1,"operation":"create","createdAt":"2026-07-29T08:47:18.943677Z"}]}}
```

No collection-specific resolver code exists for any of this — it is generated purely from
`Revisions = true` on the entity's `[CmsCollection]` attribute, identically to the REST endpoints above.

**Admin drawer** — the admin SPA's `RevisionHistoryDrawer.vue` component
(`frontend/src/components/revisions/RevisionHistoryDrawer.vue`) talks to the REST endpoints above (not
GraphQL) via `itemsApi.listRevisions`/`getRevision`/`revert`
(`frontend/src/api/itemsApi.ts:63-70`) — a list view, a snapshot detail view
(`RevisionSnapshotView.vue`), and a revert action wired to the item form. It is a thin client over the
same three REST endpoints documented above; it has no server-side behavior of its own beyond what
`ItemService` already enforces.

## What revert does and does not restore

`RevertAsync` (`ItemService.cs:366-388`) reads the target revision's raw snapshot, strips its `version`
key (so the revert doesn't echo a now-stale optimistic-concurrency token and spuriously `409` against the
current row), and re-applies the result through the **exact same** `UpdateCoreAsync` path an ordinary
`PUT` uses, tagged with `operation = "revert"` instead of `"update"`. Reverting `article` above to
revision 1 (its original `create` snapshot) after the `update` above changed `status` to `"published"`
and every field to its `"…-CHANGED"` value:

```
$ curl -s -X POST http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions/1/revert \
    -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":2,"status":"draft", ...}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27"
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":2,"status":"draft", ...,"translations":{"en":{"title":"Original Title","body":"Original body text.", ...}}}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions"
{"success":true,"data":[{"revisionNumber":3,"operation":"revert","createdAt":"2026-07-29T08:47:59.288629", ...},{"revisionNumber":2,"operation":"update", ...},{"revisionNumber":1,"operation":"create", ...}]}
```

`status` and `translations.en.title`/`body` are back to their revision-1 values, `version` moved forward
(1 → 2, not back to 0), and the revision list grew to three entries with the newest being `"revert"` —
history 1/2/3 all still present. And, read directly from the database (never through any API, since both
are `Hidden`) — proof that a `Hidden` field's value really is restored by revert, not merely left alone
or nulled:

```
$ docker exec struo-postgres psql -U struo -d struo -c \
    "select status, internalnote from articles where id='019fad0e-8904-7ac7-a20e-796f1c50ea27';"
 status | internalnote
--------+----------------
 draft  | secret-note-v1

$ docker exec struo-postgres psql -U struo -d struo -c \
    "select title, internalslug from article_translations where articleid='019fad0e-8904-7ac7-a20e-796f1c50ea27' and locale='en';"
      title      |   internalslug
------------------+-------------------
 Original Title   | original-slug-v1
```

Both hidden values are back to their revision-1 (`"secret-note-v1"` / `"original-slug-v1"`) contents —
not the `"…-CHANGED"` values the intervening update set, and not null either. Consequences that follow
directly from reusing the normal update path:

- A revert **appends** a new `"revert"` revision rather than deleting or rewinding history — the
  timeline is append-only forward; there is no way to "undo a revert" other than reverting again to an
  earlier number.
- A revert **is** a normal write for every other purpose: it goes through the same `CanWrite` (+
  super-admin-if-`AdminOnly`) check, the same optimistic-concurrency machinery, and produces the same
  `200`-with-updated-item response shape as any other update.
- Many-to-many sync during a revert **tolerates** a target row that was trashed since the snapshot was
  captured (`includeDeleted: operation == "revert"`, `ItemService.cs:210-212`) — every other write path
  stays strict about this. A revert to a snapshot referencing a since-trashed related row therefore
  restores that reference rather than failing outright.
- A revert restores exactly what the snapshot captured: own fields, M2O/M2M relation state, and
  all-locale translations. It does **not** restore anything the builder excludes by construction —
  system-managed fields, or the item's soft-delete status (`DeletedAt`/`DeletedBy` are not part of the
  snapshot at all, since `RevisionSnapshotBuilder` only walks `[CmsField]`s/relations/translations) — and
  it does not retroactively change any *other* item's state, even one the reverted item relates to.

## Soft delete

### Opting in with `ISoftDeletable`

`ISoftDeletable` (`src/Struo.Domain/Auditing/ISoftDeletable.cs`) is a package-free marker interface —
`DateTime? DeletedAt` + `Guid? DeletedBy` — mirroring `IAuditable`'s "no framework attributes required"
design. An entity implementing it is soft-deleted (its `DeletedAt`/`DeletedBy` stamped) instead of
physically removed by an ordinary `DELETE`; a `null` `DeletedAt` means the row is live. `File`
(`src/Struo.Infrastructure/Files/File.cs`) is the **only** framework collection that implements it —
chapter 11 is the concrete file-specific walkthrough (upload/trash/restore/purge); this section is the
general mechanism.

### The global query filter

The floor is registered once, at `SqlSugarClient` construction, for every inner context the connection
scope creates (`SqlSugarClientFactory.cs:140-150`):

```csharp
db.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null);
```

This applies to **every** `Queryable<T>` over an `ISoftDeletable` entity with no per-call-site code
required — list, get, deep-expansion, cross-relation id-resolution, M2M existence checks, and inbound
`OnDelete.Restrict` checks (chapter 7) all silently exclude a trashed row by default. A read that
genuinely needs trashed rows (`?deleted=only|with`, restore, purge) clears the filter explicitly for that
one query (`.ClearFilter<ISoftDeletable>()`, `SqlSugarItemRepository.cs`) rather than the floor being
opt-out globally — trashed-by-default is the safe direction to fail in.

The trash/restore writes themselves (`SoftDeleteAsync`/`RestoreAsync`,
`src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs:580-663`) run as a single atomic `UPDATE ...
WHERE deletedat IS [NOT] NULL`, not a pre-read-then-write — so trashing an already-trashed row (or
restoring an already-live one) is a no-op at the SQL level (zero rows affected) rather than a race two
concurrent callers could each "win". For an `AuditableEntity`, the same `UPDATE` also bumps `Version`
(`version = version + 1`), so the optimistic-concurrency history keeps moving and a client holding a
stale `version` correctly `409`s against a since-trashed-or-restored row.

### `DELETE` vs. `?purge`, and restore

`ItemsController.Delete` defaults to trash (soft delete) for any collection where `meta.SoftDelete` is
true; `?purge=true` forces a permanent hard delete instead, going through the collection's actual delete
pipeline (cascade/restrict checks, relation cleanup) rather than the soft-delete UPDATE. A collection with
**no** soft-delete tier at all (six of the seven framework collections) always purges regardless of the
query string — there is no partial/soft state to land in. `POST .../restore` clears `DeletedAt` the same
way for any soft-deletable collection; chapter 11 shows this live end-to-end against `file` (trash →
`deleted=only` → restore → back in the ordinary list; then a second file trashed and permanently purged
via `?purge=true`, confirmed absent even from `?deleted=with`).

### The `deleted=exclude|only|with` filter, and who may use it

`DeletedFilter` (`src/Struo.Domain/Query/DeletedFilter.cs`, chapter 8) has three values —
`Exclude` (the default, matching the global floor above), `Only`, `With` — read from `?deleted=` on both
`GET` list/query actions and the single-item `GET`. Requesting anything other than `Exclude` requires
**delete** permission on the collection, not merely read — `DeletedAccessGuard.EnsureCanViewDeleted`
(`src/Struo.Application/Query/DeletedAccessGuard.cs`), enforced by `ItemsController` itself (and the
GraphQL resolvers, chapter 8) rather than inside `ItemService.QueryAsync`/`GetAsync`, which only ever
check `CanRead`. This is a deliberate stricter gate: seeing which rows are trashed is treated as more
sensitive than seeing the live set, since the identity of a deleted row can itself be information a plain
reader shouldn't have. Chapter 8 shows the live PostgreSQL 3-way split (`exclude`/`only`/`with`) against
`file`; chapter 11's trash/restore/purge walkthrough exercises the same filter values against fresh data
created for this chapter.

## Interaction between trash and revisions

Both features are independent opt-ins, and **no core framework collection combines them** — `file` is
soft-deletable but not revisioned; no framework collection declares `Revisions = true` at all (see
above). The sample Blog's `article` collection (chapter 16) does combine both — it implements
`ISoftDeletable` and declares `Revisions = true` — but the specific trash/restore/revision interaction
below is described from source rather than exercised live in this chapter, since it fell outside the
create/update/list/view/revert cycle demonstrated above:

- `ItemService.DeleteAsync`/`RestoreAsync` call `CaptureRevisionAsync` (`ItemService.cs:297`, `:341`)
  precisely when **both** `meta.SoftDelete` and `meta.Revisions` are true for the collection being
  trashed/restored — a `"delete"`/`"restore"` revision is recorded in the exact same transaction as the
  atomic trash/restore `UPDATE`, using the identical affected-rows gate described above (so a no-op trash
  of an already-trashed row records no spurious revision either).
- A **purge** (`?purge=true`) on a collection that is both soft-deletable and revisioned does not go
  through this path at all — it's a hard delete via the collection's normal delete pipeline, not the
  soft-delete `UPDATE`, so no `"delete"` revision is captured for a purge specifically (only for a trash).
  `IRevisionStore.DeleteForItemAsync` exists specifically to remove a purged item's entire revision
  history so a permanently-deleted item doesn't leave orphaned, un-RBAC'd snapshot history behind — its
  default implementation deliberately throws rather than silently no-opping, so a store that forgot to
  wire this up fails loudly instead of leaking history.
- Reverting a trashed-and-revisioned item's history is, mechanically, just another `UpdateCoreAsync` call
  — it does not itself restore the item from the trash (`DeletedAt` is not part of a snapshot, as noted
  above), so a revert against a currently-trashed row would still need an explicit `restore` afterward if
  the goal is a fully-live item at a past field state.

## Next steps

- Chapter 8, [Query DSL](08-query-dsl.md), for `DeletedFilter`/`deleted=` as a general query-DSL concept,
  live-verified against `file`.
- Chapter 9, [REST API](09-rest-api.md), for the full `.../revisions*` and file-trash/restore/purge
  endpoint tables, status codes, and route constraints.
- Chapter 10, [GraphQL API](10-graphql-api.md), for the generated `{collection}Revisions`/
  `{collection}Revision`/`revert{X}` GraphQL surface a revisioned collection gets automatically.
- Chapter 11, [Files, Media & Image Transforms](11-files-and-media.md), for the live, concrete
  trash/restore/purge walkthrough against `file` — the one framework collection this chapter's soft-delete
  section describes in the abstract.
- Chapter 12, [Authentication, SSO & RBAC](12-auth-and-rbac.md), for the `Hidden` field flag
  `RevisionSnapshotRedactor` reads, and the `CanRead`/`CanWrite`/`CanDelete` grants this chapter's guards
  build on.
