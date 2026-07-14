# Phase 9c — Revisions (REST + GraphQL, backend-only slice)

> **Status:** design (brainstormed, approved 2026-07-14).
> **Slice of:** Phase 9 (soft delete / revisions / lifecycle hooks + unified response envelope). Phase 9
> was decomposed into four independent slices: 9a unified response envelope (done), 9b soft delete (done),
> **9c revisions (this spec)**, 9d lifecycle hooks. 9c was chosen after 9a/9b because it has the highest
> remaining product value and builds directly on the existing audit (`IAuditable`/AuditAop), optimistic
> `Version`, transactional write path, and soft-delete infrastructure.
> **Companion follow-up:** the Vue admin revision-history/revert UI is deferred to a later slice (**9c-fe**),
> mirroring how 9b shipped API-first (`9b` backend, then `9b-fe`).

## 0. Summary

A per-collection **revision history with revert**. A collection opts in via
`[CmsCollection(Revisions = true)]` (an attribute flag like `AdminOnly`, since — unlike soft delete —
there is nothing to add to the entity itself; the snapshots live in a separate framework-owned table).
For an opted-in collection:

- Every successful **create** and **update** appends one **revision**: a complete, revert-capable JSON
  snapshot of the item's post-write state (all `[CmsField]` values, M2O foreign-key ids, M2M relation id
  arrays, and every locale's translation field values).
- **`GET /api/items/{c}/{id}/revisions`** lists a revision's metadata (newest-first).
- **`GET /api/items/{c}/{id}/revisions/{n}`** returns one revision including its snapshot.
- **`POST /api/items/{c}/{id}/revisions/{n}/revert`** re-applies revision `n`'s snapshot as the item's new
  current state and appends a fresh revision (`revert`). Revert is **append-only**: it never deletes the
  forward history.
- GraphQL is brought to **full parity**: per revisioned collection, `xRevisions(id)` / `xRevision(id,
  revisionNumber)` queries and a `revertX(id, revisionNumber)` mutation.

Snapshots are stored in a **single framework-owned `revisions` table** (not a `[CmsCollection]` — it is an
internal table, never browsable/CRUD-able through the generic item API). Capture happens **inside the same
transaction** as the write, so a snapshot and its write commit atomically.

**No new NuGet packages.** Framework code never references `samples/*` (§2); the sample `Article` opts in to
demonstrate and live-verify the feature. One versioned migration adds the `revisions` table to live DBs.
This slice preserves the Phase 9a unified response envelope (revision endpoints return ordinary
`ObjectResult`s that the `EnvelopeResultFilter` wraps).

## 1. Where each concern attaches (current → change)

| Layer | Current behaviour | Change for 9c |
|---|---|---|
| Metadata attribute (`Domain/Metadata/Attributes/CmsCollectionAttribute`) | `AdminOnly` flag | **add** `bool Revisions` flag (opt-in) |
| Metadata (`CollectionMetadata`) | `AdminOnly`, `SoftDelete` | **add** `bool Revisions`; the scanner sets it from the attribute |
| Revision entity (`Infrastructure/Revisions/Revision.cs`) | none | **add** internal `[SugarTable("revisions")]` entity (not a `[CmsCollection]`) |
| Revision store port (`Application/Revisions/IRevisionStore`) | none | **add** capture / list / get operations |
| Revision store impl (`Infrastructure/Revisions/SqlSugarRevisionStore`) | none | **add** SqlSugar-backed implementation |
| Snapshot builder (`Application/Query/`) | `Project` (read projection only) | **add** a canonical, revert-capable snapshot assembler |
| Write path (`Application/Query/ItemService.CreateAsync`/`UpdateAsync`) | parent + M2M + translations in a transaction | **capture** a revision inside the same transaction when `meta.Revisions` |
| Revert (`ItemService`) | none | **add** `RevertAsync(collection, id, revisionNumber)` |
| Revision reads (`ItemService`) | none | **add** `ListRevisionsAsync` / `GetRevisionAsync` (RBAC-gated) |
| REST (`Api/Controllers/ItemsController`) | CRUD + restore | **add** `GET .../{id}/revisions`, `GET .../{id}/revisions/{n}`, `POST .../{id}/revisions/{n}/revert` |
| GraphQL schema (`Api/GraphQl/CollectionSchemaBuilder`, `StruoTypeModule`, `SchemaTypeMapper`) | typed per-collection queries + mutations | **add** a shared `Revision` object type, `xRevisions`/`xRevision` queries, `revertX` mutation per revisioned collection |
| GraphQL data source (`Api/GraphQl/GraphQlDataSource`) | read/write/restore | **add** revision list/get + revert methods delegating to `ItemService` |
| DI (`Infrastructure/DependencyInjection`) | existing registrations | **register** `IRevisionStore`; register `Revision` for `InitTables` CodeFirst |
| Migration (`db/migrations/`) | `005-soft-delete-columns.sql` | **add** `006-revisions-table.sql` |

## 2. Chosen approach — shared framework-owned table + attribute opt-in

**Storage (Approach A, chosen).** A single generic `revisions` table owned by the framework holds every
collection's snapshots. Rejected alternative: **per-collection sidecar tables** (like translation
sidecars) — far more scanner/DDL machinery, and the framework cannot own sample-defined sidecar types
(§2), running against the project's direction of generic, metadata-driven storage.

**Opt-in = attribute flag, not marker interface.** Soft delete used the `ISoftDeletable` *interface*
because it needed two real columns (`DeletedAt`/`DeletedBy`) on the entity — the interface is the single
source of truth for both the columns and the capability. Revisions add **nothing** to the entity (the
snapshot lives in the shared table), so an empty marker interface would carry no data; the natural,
consistent choice is a `[CmsCollection(Revisions = true)]` attribute flag alongside `AdminOnly`.

**Capture inside the write transaction.** The revision insert joins the existing `InTransactionAsync`
wrapping parent + M2M + translations, so the snapshot and the write it describes commit together or roll
back together — no orphan snapshots, no writes without a snapshot.

## 3. Revision entity & storage

```csharp
// Infrastructure/Revisions/Revision.cs — internal framework table, NOT a [CmsCollection]
[SugarTable("revisions")]
public sealed class Revision
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }          // UUIDv7, assigned at capture
    public string CollectionName { get; set; } = "";                          // e.g. "article"
    public string ItemId { get; set; } = "";                                  // the item PK, stringified
    public long RevisionNumber { get; set; }                                  // 1-based, monotonic per (collection,itemId)
    public string Operation { get; set; } = "";                               // "create" | "update" | "revert"
    [SugarColumn(ColumnDataType = "text")] public string Snapshot { get; set; } = ""; // canonical JSON — MUST be text
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
```

- **`Snapshot` MUST be a `text` column.** SqlSugar CodeFirst defaults strings to `varchar(255)` on
  Postgres; a realistic snapshot overflows it (Npgsql 22001 → 500). This is the recurring
  "SQLite-green ≠ Postgres-correct" bug class (7g / 7g+ slice 1); a DDL assertion locks it.
- The table is **not** `IAuditable` (append-only rows have no update, so `UpdatedAt`/`UpdatedBy` would be
  dead columns). `CreatedAt`/`CreatedBy` are stamped explicitly at capture from `ICurrentUserAccessor` +
  `DateTime.UtcNow` — the same actor contract as `CreatedBy` elsewhere (null when no user).
- `RevisionNumber` is a **dedicated per-item monotonic sequence** (1-based), decoupled from the entity's
  optimistic `Version` (which is captured *inside* the snapshot for reference). It is computed as
  `max(RevisionNumber for that (collection,itemId)) + 1` **inside the capture transaction**, so it is race-
  free for the serialized single-item write path.
- `ItemId` is stored as a string (items expose ids as strings through `ItemService`); this keeps the table
  PK-type-agnostic across collections.

## 4. Metadata & scanner

- `CmsCollectionAttribute` gains `public bool Revisions { get; set; }`.
- `CollectionMetadata` gains `public bool Revisions { get; init; }`; the scanner sets it from the
  attribute. Cached like all metadata (§17.6 — no per-request reflection).
- No fail-fast is required. A collection without the flag has `Revisions = false` and captures nothing.

## 5. Application layer

### 5.1 Revision store port

```csharp
// Application/Revisions/IRevisionStore.cs
public sealed record RevisionInfo(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy);
public sealed record RevisionRecord(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy, string Snapshot);

public interface IRevisionStore
{
    /// Assigns the next RevisionNumber for (collection,itemId), stamps CreatedAt/By, inserts. Runs on the
    /// scoped client, so when called inside ItemService's InTransactionAsync it commits with the write.
    Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson, CancellationToken ct = default);

    /// Newest-first metadata list (no snapshot payload). Empty when none / not revisioned.
    Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default);

    /// One revision incl. snapshot, or null when (collection,itemId,revisionNumber) has no row.
    Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default);
}
```

`SqlSugarRevisionStore` (Infrastructure) implements it against the scoped `ISqlSugarClient`.

### 5.2 Canonical snapshot builder — the crux

The snapshot must be **revert-capable**: feeding it back through `UpdateAsync` must reproduce the state.
The read projection (`Project`) is **insufficient** — it omits M2O FK ids (FKs are declared via
`[CmsRelation]`, not `[CmsField]`, so they are not projected) and it expands relations/images to nested
objects. A dedicated builder assembles, in the exact shape `UpdateAsync` consumes:

- **`[CmsField]` values** by camelCase name — scalars, multi-value (`List<string>`), `Json` (structured),
  `KeyValue` (verbatim keys), `Files` (raw `Guid` ids), `Repeater` (child list). (These come from
  `Project`, reused.)
- **M2O FK ids** by camelCase FK name (e.g. `categoryId`) — read directly off the entity for each
  `RelationKind.ManyToOne` relation with a non-null `ForeignKey`.
- **M2M relation id arrays** by relation name (e.g. `tags: [id, …]`) — the ordered target ids from the
  junction, per M2M descriptor.
- **`translations`** as `{ locale: { camelField: rawValue } }` for **every** locale — built from
  `LoadTranslationsAsync` raw values (image/file fields kept as **raw ids**, not resolved to objects, so
  the snapshot round-trips through the write path).
- **`version`** — the entity's optimistic token at capture (reference only; revert does not echo it, to
  avoid a self-inflicted 409).

Serialized with `JsonSerializerDefaults.Web` (camelCase; dictionary keys — KeyValue / translation locales —
stay verbatim, matching the existing write/read contract). The builder is a focused unit so it can be
tested independently against a fully-populated `Article`.

### 5.3 Write path — capture

`CreateAsync` / `UpdateAsync` are unchanged except: **inside** the existing `InTransactionAsync`, after the
parent write + `SyncM2MAsync` + `SyncTranslationsAsync`, when `meta.Revisions` is true:

```
build canonical snapshot of the just-written item (re-read within the transaction)
revisionStore.CaptureAsync(collection, itemId, operation, snapshotJson)   // "create" or "update"
```

A non-revisioned collection captures nothing (zero overhead). Capture is best-effort-free: any failure
throws and rolls back the whole write (atomicity over silent partial state).

### 5.4 Revert

```
RevertAsync(collection, itemId, revisionNumber):
  meta = Meta(collection)
  CanWrite guard + RequireSuperAdminForAdminOnly            // revert is a write
  rec = revisionStore.GetAsync(collection, itemId, revisionNumber)
  if rec is null: return null                               // unknown item or revision -> 404
  body = parse rec.Snapshot as JSON (drop "version" so no optimistic-concurrency echo)
  apply via the shared update core with operation label "revert"
     -> overlays fields + M2O FK, syncs M2M + translations, captures a NEW "revert" revision
  return the re-read reverted item projection
```

- **Append-only:** the new `revert` revision is appended; revisions after `revisionNumber` are **never
  deleted**. Full history is preserved and auditable.
- **Operation label:** `UpdateAsync`'s capture is refactored to a private write-core that takes an
  `operation` label; `UpdateAsync` passes `"update"`, `RevertAsync` passes `"revert"`. (Public
  `UpdateAsync` signature is unchanged.)
- **Translation semantics (overlay, documented nuance):** the revert applies the snapshot's locales via
  the existing replace-per-locale translation sync. Locales **added after** `revisionNumber` are left
  intact (they are absent from the snapshot payload, and the sync only touches locales it is given) — this
  matches the project's documented article-level partial-merge translation semantics (8b.2b). A strict
  revert that also *removes* newer-only locales is deliberately out of scope (§12).

### 5.5 Revision reads

```
ListRevisionsAsync(collection, itemId):  CanRead guard -> revisionStore.ListAsync
GetRevisionAsync(collection, itemId, n): CanRead guard -> revisionStore.GetAsync
```

Both are no-ops (empty / null) for a non-revisioned collection.

## 6. API — REST (`ItemsController`)

- `GET /api/items/{c}/{id}/revisions` → newest-first metadata list (`[{revisionNumber, operation,
  createdAt, createdBy}]`); requires `CanRead`. **No pagination** — returns the full list (a follow-up if a
  hot item ever needs it, §12). Non-revisioned collection → empty list.
- `GET /api/items/{c}/{id}/revisions/{n}` → one revision incl. `snapshot`; requires `CanRead`. Unknown
  `(id, n)` → `404`.
- `POST /api/items/{c}/{id}/revisions/{n}/revert` `[Authorize(CookieOrBearer)]` → requires `CanWrite`
  (+ super-admin if `AdminOnly`); body-less; returns the reverted item (`200`). Unknown `(id, n)` → `404`.
- **Response envelope:** unchanged from 9a — these are ordinary `ObjectResult`s the `EnvelopeResultFilter`
  wraps (`{ success, data }`). Bare `NotFound()` → `NOT_FOUND` envelope (as fixed in 9a).

## 7. API — GraphQL

Per revisioned collection (generated in `CollectionSchemaBuilder`/`StruoTypeModule`; snapshot exposed as
`Any`, consistent with the Json/KeyValue and read-side `translations` convention):

- A shared `Revision` object type: `revisionNumber: Int!`, `operation: String!`, `createdAt: DateTime!`,
  `createdBy: ID`, `snapshot: Any!`.
- Query `xRevisions(id: ID!): [Revision!]!` (newest-first; `CanRead`).
- Query `xRevision(id: ID!, revisionNumber: Int!): Revision` (`CanRead`; null when absent).
- Mutation `revertX(id: ID!, revisionNumber: Int!): X` — re-read via the mutation's selection set (same
  pattern as `restoreX`/`create`/`update`); `CanWrite` (+ super-admin if `AdminOnly`).

Resolvers reuse `CollectionResolvers`/`MutationResolvers` machinery and call the same `ItemService` methods
REST uses, via new `IGraphQlDataSource` methods. **Domain/Application engine is shared, not GraphQL-
specific.** Errors reuse the Phase 8 `StruoErrorFilter` mapping (FORBIDDEN / NOT_FOUND / BAD_USER_INPUT /
masked INTERNAL_SERVER_ERROR). Non-revisioned collections get **no** revision fields on their type
(nothing to expose).

## 8. Error handling

No new exception types. Reuse the existing mapping (Phase 9a `StruoExceptionHandler` +
`StruoErrorFilter`): permission → `403`/`FORBIDDEN` (401 anonymous); not-found → `404`/`NOT_FOUND`; bad
input → `400`/`BAD_USER_INPUT`; masked `500`/`INTERNAL_SERVER_ERROR`.

## 9. Testing strategy (TDD)

- **Unit:** scanner derives `Revisions` from the attribute; the canonical snapshot builder produces a
  revert-capable shape (M2O FK ids present, M2M as id arrays, all-locale raw translations, `version`
  captured); `CaptureAsync` assigns a monotonic per-item `RevisionNumber`; capture runs inside the write
  transaction (a forced capture failure rolls back the parent write — no orphan row, no snapshot-less
  write); RBAC (`ListRevisions`/`GetRevision` = read; `Revert` = write; `AdminOnly` revert → super-admin);
  a non-revisioned collection captures nothing.
- **Integration (SQLite, real engine):** create → 1 revision (`create`); update → 2nd (`update`); list
  newest-first; get returns the snapshot; **revert** re-applies the snapshot, appends a `revert` revision,
  and leaves the intervening revisions intact (append-only); revert restores M2M links + translations +
  M2O FK; revert of an `AdminOnly` collection without super-admin → 403; GraphQL read+revert parity.
- **DDL:** the `snapshot` column is `text` (the varchar bug-class assertion).
- **N+1 / cost:** capture adds a bounded, constant number of queries per write (re-read + one insert); no
  per-row explosion.
- **Live gate (real Postgres — the decisive gate):** on `web-struo-cms-db`: create → update → list (2
  revisions) → get snapshot → revert-to-v1 → item matches v1 + a 3rd (`revert`) revision appended;
  **CJK code-point-exact** inside the stored snapshot and after revert; M2M (`tags`) + translations
  (`en`+`zh-TW`) + M2O FK (`categoryId`) all round-trip through revert; a realistic (large) snapshot is
  **not truncated** (proves the `text` column); REST ↔ GraphQL parity (`revertArticle` via `/graphql`).
  Honors the standing "SQLite-green ≠ Postgres-correct" rule.

## 10. Migration & sample

- **Sample (host, not framework):** `Article` gains `[CmsCollection(..., Revisions = true)]` so the live
  gate can exercise capture/revert across its full field set (scalars, multi-value, Json, KeyValue, Files,
  Repeater, M2M `tags`, M2O `category`, i18n translations). Framework code never references `samples/*` (§2).
- **`db/migrations/006-revisions-table.sql`:** `CREATE TABLE revisions (...)` with `snapshot text` (lower-
  case unquoted identifiers, matching CodeFirst). `InitTables` creates it in dev (the `Revision` entity is
  registered for CodeFirst), but live DBs provisioned earlier need this script (same pattern as prior
  slices' migrations).

## 11. Acceptance gate

- `dotnet build -warnaserror` → 0 warnings; `dotnet test` → all green (635 baseline + new tests).
- Frontend untouched — `pnpm test` stays **267**.
- **Live gate PASSED on real Postgres** before the slice is declared done (the §9 flow).

## 12. Out of scope (recorded so they aren't lost)

- Vue admin revision-history + revert UI + E2E (**slice 9c-fe**).
- Diff / side-by-side compare between revisions.
- Retention / pruning policy (keep-all for now; a bulk prune / max-N cap is a later slice).
- Delete-triggered revisions (a hard delete removes the item; a soft delete has its own trash/restore).
  Snapshots track content states from create/update only.
- Strict revert that *deletes* locales added after the reverted revision (overlay semantics used; §5.4).
- Revisioning identity/`AdminOnly` tables by default (opt-in only; not enabled on them).
- Revision-list pagination (full newest-first list for now; §6).
- Single-item GraphQL fetch of a specific revision snapshot as a strongly-typed (non-`Any`) shape.
