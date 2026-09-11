# 9. Revisions and Soft Delete

Every write leaves behind a snapshot you can look back at, and deleting something becomes an act
you can undo. This chapter covers both.

## Enabling revisions per collection

Revisions turn on with `Revisions = true` on `[CmsCollection]`, independently of soft delete — a
collection can have neither, either, or both; see
[Chapter 5: Defining Collections](05-collections.md).

Every revision lives in one shared table, keyed by collection name, item id and version number. No
framework collection turns it on; the sample's `Article` does, and it also implements
`ISoftDeletable`, making it the only collection with both.

Turning it on needs no further call: create, update, revert to a version, trash and restore from the
trash all write a revision inside the same transaction as the write itself — the write path is the
only entry point.

## What a snapshot holds, and when it's captured

A snapshot is built from the already-persisted entity, right after the row is written to the
database, and it is deliberately kept whole: no field-level RBAC filtering applies, and no hidden
field is skipped, because a later revert must be able to restore the item's entire state, whoever
eventually reads the revision.

A snapshot is one flat JSON object, with a fixed shape:

- `id`.
- `version` — present only when the entity derives from `AuditableEntity`.
- Every own `[CmsField]`, except system-managed and translatable ones; a `Json`-interface field's
  raw text is parsed back into a real JSON value rather than stored as a string.
- Every many-to-one relation's foreign-key id, keyed by the relation's camelCase name.
- Every many-to-many relation, keyed by the relation name.
- `translations`, holding every locale the item has a row for — the full translation state, not
  just the locale used for the current query.

Within many-to-many relations, one whose junction carries no payload is stored as an ordered id
array; one whose junction does carry payload is stored as an ordered array of `{ id, ...payload }`
objects, one per linked target, including any hidden payload field's value. When the relation
declares a sort field, order follows that field.

A snapshot's shape deliberately matches an update request body, so a revert can go straight
through the ordinary update path instead of needing a mechanism of its own. A
bare-id-array snapshot reverts as a membership change only — a payload value already sitting on
the junction row is left untouched.

Create, update, revert, trash and restore all write a revision, and every capture happens inside
the same database transaction as the write it describes, so a rolled-back write never leaves an
orphaned revision behind.

A revert's own revision additionally carries the version number that was re-applied; for the
other four operations that column is always empty.

Trash and restore captures carry one more condition: they record only when that soft-delete
update actually affected a row, so two calls racing to trash the same row never each record a
duplicate revision, and re-trashing an item already in the trash leaves no revision behind
either.

A capture failure rolls the whole trash or restore back, so there is no half-trashed row with no
matching revision.

An update that changes nothing still writes an update revision — the write path always issues the
update and always bumps the version number; it never compares old and new values; keep this in
mind when sizing this table. The version number is the current maximum for that
collection-and-item pair plus one, starting at 1, recorded together with the current time and
actor.

The table that holds revisions is a plain framework table, not a `[CmsCollection]`, so it is
never browsable through the generic item API. It is only ever written, never updated, and it does
not implement the audit interface — it has no updated-at or updated-by.

The table carries a composite unique index over collection name, item id and version number, the
last line of defense against a concurrent race for the same number; a Development startup check
verifies it separately. The table-creation process creates that index only while the table itself
does not yet exist — it is not retrofitted onto a table that already exists, with the same
exception as a translation sidecar's index: a Development environment with
`Database:AutoSyncSchema` enabled tries to add it during its full sync.

Revisions are kept forever: the list endpoint returns every revision for the item, unpaginated,
with no retention period and no cleanup schedule. The only action that ever removes a revision is
a permanent delete (`?purge`, called purge in this chapter). A frequently written, revisioned
collection grows this shared table without bound.

### Redacting hidden fields from a snapshot

Because a snapshot captures the whole item including hidden fields, the copy handed to an
external caller must never leak one of them. An external read gets a redacted copy, with these
removed:

- Any top-level key whose field is hidden.
- Any hidden translatable key inside the translation object.
- Any hidden payload key inside a payload-carrying many-to-many relation's `{ id, ...payload }`
  elements — a bare-id element has no payload to redact in the first place.

Everything else passes through unchanged.

This redaction applies only to the external single-revision read path; a revert reads the raw,
unredacted snapshot, because a revert has to be able to actually restore a hidden field's value.
A hidden value therefore leaves the server only through the effect it has on that row, never
inside any response body.

Redaction applies to every past revision, not only the newest one. In the sample,
`Article.InternalNote` (top-level) and `ArticleTranslation.InternalSlug` (per-locale) are the
fields declared hidden.

## Listing, viewing and reverting

Revisions are exposed as three REST endpoints:

- `GET /api/items/{collection}/{id}/revisions` — lists metadata newest-first
  (`revisionNumber`, `operation`, `createdAt`, `createdBy`, `sourceRevisionNumber`), with no
  snapshot content.
- `GET /api/items/{collection}/{id}/revisions/{n}` — returns one revision, together with a
  redacted `snapshot`, rendered as structured JSON rather than a string.
- `POST /api/items/{collection}/{id}/revisions/{n}/revert` — applies that snapshot; it takes no
  body.

Listing and viewing need only the collection's ordinary read grant, and reverting needs its
ordinary write grant — revisions have no permission tier of their own. For a collection with
revisions off, the list returns an empty array rather than 404, while the single-revision view is
404, and so is an unknown version number or item id. The list is always ordered by version
number, not by creation time.

GraphQL adds the matching three surfaces from the same flag, with no per-collection code needed: a
revisions list field, a single-revision field, and a revert mutation, sharing one `Revision` type
whose fields match REST. On the GraphQL list field, `snapshot` is `null`.

The admin SPA's revision panel is a thin interface over those three REST endpoints, not GraphQL — a
list, a single-snapshot view, and a revert action mounted on the item form. It appears only for an
already-saved item on a collection with revisions on, and the revert action appears only when the
caller has write access to the collection. The panel's operation labels cover only create, update
and revert; a revision left by trash or restore falls back to a generic label.

For an item that was created and then updated once, listing its revisions returns only metadata,
no snapshot content, newest first:

```text
$ GET /api/items/article/01a08c68-57d6-7f25-80e7-c42a2a1702a4/revisions
{"success":true,"data":[{"revisionNumber":2,"operation":"update","createdAt":"2026-09-10T17:40:44.397818","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","sourceRevisionNumber":null},{"revisionNumber":1,"operation":"create","createdAt":"2026-09-10T17:40:43.741968","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","sourceRevisionNumber":null}]}
HTTP_STATUS:200
```

The snapshot itself is visible only by calling the single-revision endpoint separately.

A revert reads the target version's raw snapshot, strips its `version` key first — so a stale
concurrency token does not get compared against the current row and spuriously conflict — and
then applies the result through the exact same update path an ordinary write uses, tagged
`revert`.

A revert itself writes one more new `revert` revision rather than rewinding history — the
timeline only ever accumulates forward, and undoing a revert means reverting again to an earlier
version number. Beyond that, a revert is an ordinary write: the same write grant and super-admin
rule, the same optimistic concurrency control, the same item response shape.

Reverting a snapshot with junction payload applies those objects just like a direct payload
write: it also needs the junction collection's own write grant, as
[Chapter 8: Relations](08-relations.md) describes. A revert is also the one write path tolerating
a many-to-many target trashed after the snapshot was captured, restoring the reference rather than
rejecting it as every other write path does.

A revert restores only what the snapshot captured at the time — its own fields, relation state,
and every locale's full translation. System-managed fields and the item's soft-delete state are
not part of the snapshot, so a revert never touches them. A revert never changes any other item.

Because a revert goes through exactly the same path as an ordinary update, reverting an item that
is currently in the trash gets a plain 404 — the update path's read of the current row already
excludes trashed data. The right order is restore first, then revert; the reverse does not work.

## Soft delete: `ISoftDeletable`

`ISoftDeletable` is a marker interface that needs no extra package, carrying `DateTime? DeletedAt`
and `Guid? DeletedBy`, echoing the audit interface's no-attributes-needed design: a `null`
`DeletedAt` means the row is still live, and the framework fills in both values itself at the
moment something is trashed.

Among framework collections only `File` implements it; the sample's `Article` and `Category`
implement it too, and neither member carries a `[SugarColumn]` of its own — both rely on the
framework's inference for nullable types.

Neither member carries a `[CmsField]`, so soft-delete state is not part of field metadata at all
— it never appears in the item projection, and it cannot be written through the generic item API.

Underneath sits one globally registered query filter, condition "deleted-at is null", applied
once for every database connection scope. It covers every query over a soft-deletable entity with
no code needed at the call site: lists, single-item reads, relation expansion, cross-relation id
resolution, many-to-many existence checks, and delete-restriction checks all exclude a trashed row
by default.

A read that genuinely needs to see deleted data clears this filter for that one query only, rather
than switching it off wholesale — staying blind to trash by default is the safer direction to fail
in. The filter is cleared individually in these cases:

- A single-item read that explicitly asks for deleted data.
- A list or count deciding whether to see the trash, driven by the `deleted=` parameter.
- Purge recursively looking for the data it must clean up along with it.
- The file-deletion flow's own internal query for what it needs to purge.

This filter affects only queries, not update statements: the trash and restore writes carry their
own explicit condition instead of relying on it. Both are a single atomic update guarded on the
current state — deleted-at is null, or is not null — rather than a read followed by a write, so
re-trashing an item already in the trash, or restoring one that is already live, affects zero rows
instead of two calls racing each other.

For a collection that derives from `AuditableEntity`, the same statement also bumps the version
number, so a caller holding a stale version number correctly fails with a conflict against a row
that was just trashed or just restored.

A unique index runs into trouble against a row sitting in the trash, with no way around it today:
the filter operates at the query level, not as a condition on the index, so a trashed row still
occupies its unique value, and a new, live row wanting the same value is blocked until the old row
is purged for good. Neither the framework nor the sample declares a unique index on a soft-deletable
entity today, so this only bites a project that adds one.

The framework's only inputs for producing an index are the field list, whether it is unique, the
unique group name, and `IndexName` — there is no concept of a conditional or filtered index at all,
so a partial unique index that excludes deleted rows cannot be expressed through the table-creation
process; it needs a database migration script of your own.

## `DELETE`, `?purge`, and restore

`DELETE /api/items/{collection}/{id}` moves the item to the trash on a soft-deletable collection,
and permanently deletes it on any other; `?purge=true` forces the permanent path instead of the
soft-delete update. A collection with no soft-delete tier is always permanently deleted whether or
not this parameter is present, because it has no intermediate state to land in.
`POST .../restore` clears those two fields for any soft-deletable collection.

Purge does these steps in order, inside one transaction:

1. Check any restrict-guarded foreign keys.
2. Null out the foreign keys of rows that point at it.
3. Recursively clean up data that should genuinely cascade with it.
4. Clean up its own junction rows and any other collection's junction rows pointing at it.
5. Clean up its translation sidecar rows.
6. Clean up its revisions.
7. Delete the row itself.

The items endpoint only takes the permanent path when this parameter's value is exactly `true`,
case-insensitively: for a soft-deletable collection, `?purge=1` or a bare `?purge` is treated as
an ordinary trash with no error. The files endpoint uses ordinary parameter binding instead, where
the same `1` does count — the two surfaces are not consistent here. Restore needs the
collection's delete permission, not its write permission.

Trashing something twice never errors: trashing an already-trashed item again still counts as
success, because the row does exist — only an unknown id is a 404. `DELETE` returns 204 on
success and 404 otherwise; `restore` returns the projected item on success and 404 otherwise.

A trash listing is just the ordinary list with `?deleted=only`, and a purge is just that
parameter on `DELETE` — neither needs a separate endpoint. Restore touches only those two fields
and the version number; relations, junction rows and translations were never touched by the trash
in the first place, so there is nothing left to restore.

Here is a complete trash round-trip: trash it, find it with `?deleted=only`, then restore it (the
list also narrows to this one row with `filter[id][_eq]`, because the development database still
holds other trashed articles):

```text
$ DELETE /api/items/article/01a08c68-5909-7513-8a9c-906f1fc22943

HTTP_STATUS:204

$ GET /api/items/article?deleted=only&filter[id][_eq]=01a08c68-5909-7513-8a9c-906f1fc22943
{"success":true,"data":[{"id":"01a08c68-5909-7513-8a9c-906f1fc22943","version":7,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-10T17:40:43.913263","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.913276","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","translations":{"en":{"title":"Scratch article","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":1,"limit":25,"offset":0}}
HTTP_STATUS:200

$ POST /api/items/article/01a08c68-5909-7513-8a9c-906f1fc22943/restore
{"success":true,"data":{"id":"01a08c68-5909-7513-8a9c-906f1fc22943","version":8,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-10T17:40:43.913263","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.913276","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858"}}
HTTP_STATUS:200
```

The `restore` response's version number moved from 7 to 8. `deletedAt` never appears in the
projection at any point: trash state is only ever discoverable through `?deleted=`, never by
reading the item itself.

## The `deleted=` filter and permissions

`deleted=` accepts three values, on the list, the query endpoint and single-item reads: `exclude`
(the default, matching the underlying filter), `only`, `with`. The value is matched
case-insensitively; a missing or blank value counts as `exclude`, and anything else is a 400 whose
message lists the three accepted spellings:

```text
Query parameter 'deleted' must be exclude|only|with.
```

Even for the query endpoint's JSON-envelope form, this parameter is always read from the query
string — the form is `POST .../query?deleted=only`; `all`, `true` and `trash` are none of the
three accepted spellings.

Requesting anything other than `exclude` needs the collection's delete permission, not merely its
read permission — this check is deliberately stricter than an ordinary query, because the fact
that a row was ever deleted can itself be information a read-only caller should not have. The
message is:

```text
Viewing deleted items requires delete permission.
```

Both `only` and `with` clear the underlying filter for the outermost query; `only` additionally
adds a "deleted-at is not null" condition. For a collection with no soft-delete tier, this
parameter has no effect. GraphQL spells the same three values in upper case as an enum argument,
and applies the identical permission gate.

Relation filtering takes a different path. Whatever the outer request's `?deleted=`
is, a relation's subquery always applies the target collection's own soft-delete filter as usual,
and never clears it — so even while viewing the parent's trash, a trashed related row never
satisfies a dotted-path or quantifier condition.

## How trash and revisions interact

Only when a collection has both soft delete and revisions on do trash and restore each leave
behind a revision, written inside the same transaction as that atomic update and gated the same
way — recorded only when it actually affected a row, so a no-op trash leaves nothing behind. That
snapshot records the field values at the moment of the trash or restore.

Purge takes a completely different path: it is a permanent delete through the purge process, so
only trashing ever leaves a delete-related revision — purge itself does not. Purge also clears
the item's entire revision history, so a permanently deleted item leaves behind no orphaned
snapshot nobody can reach; a store implementation that wired in its own storage but forgot this
cleanup gets an exception thrown straight at it, forcing the omission into the open.

The revision list and view pay no attention to soft-delete state at all: an item's history, even
while it sits in the trash, is visible with just the collection's read permission.

## What's next

With revisions and the trash settled, the next step is how to filter, sort and search content —
the subject of chapter 10, the query DSL.
