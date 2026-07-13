# Phase 9b — Soft delete (REST + GraphQL, backend-only slice)

> **Status:** design (brainstormed, approved).
> **Slice of:** Phase 9 (soft delete / revisions / lifecycle hooks + unified response envelope). Phase 9
> was decomposed into four independent slices: **9b soft delete (this spec)**, 9a unified response
> envelope, 9c revisions, 9d lifecycle hooks. 9b was chosen first (highest product value, backend
> self-contained, builds on the existing `OnDelete`/Restrict + audit infrastructure).
> **Companion follow-up:** the Vue admin trash/restore/purge UI is deferred to a later slice (**9b-fe**),
> mirroring how the Phase 8 GraphQL series shipped API-first with the frontend untouched (237 tests).

## 0. Summary

A per-collection **soft delete** capability. A collection opts in by having its entity implement a new
`ISoftDeletable` interface (mirroring how `IAuditable`/`AuditableEntity` already work — one source of
truth, no parallel attribute flag). For an opted-in collection:

- **`DELETE /api/items/{c}/{id}`** marks the row deleted (`DeletedAt`/`DeletedBy` set) instead of
  removing it. **`DELETE ...?purge=true`** permanently removes it (today's hard-delete path).
- **`POST /api/items/{c}/{id}/restore`** clears the deletion marker.
- Reads **exclude** soft-deleted rows by default. `?deleted=exclude|only|with` (gated by the collection's
  **delete** permission) surfaces them.
- GraphQL is brought to **full parity**: a `deleted: DeletedFilter` argument on list queries, `deleteX(id,
  purge: Boolean)` soft-deletes (or purges), and a new `restoreX(id)` mutation.

The exclusion is enforced by a **SqlSugar global query filter** (`QueryFilter.AddTableFilter<ISoftDeletable>`)
registered on the request-scoped client — so **every** read path (list, get-by-id, deep expansion,
cross-relation id-resolution, M2M existence checks, inbound-Restrict checks) excludes trashed rows *by
default*, with no per-path plumbing. Only the top-level `QueryAsync`/`GetAsync` accept a `DeletedFilter`
mode that lifts the floor.

**No new NuGet packages.** Framework code never references `samples/*` (§2); the sample `Article` and
`Category` entities opt in to demonstrate and live-verify the feature. One versioned migration adds the two
nullable columns to opted-in tables. **The unified response envelope stays out of scope — that is slice
9a.** This slice preserves the current `{ data }` / `{ error: { message } }` REST shape verbatim.

## 1. Where each concern attaches (current → change)

| Layer | Current behaviour | Change for 9b |
|---|---|---|
| Domain marker (`Domain/Auditing/`) | `IAuditable` on `AuditableEntity` (all collections) | **add** `ISoftDeletable { DateTime? DeletedAt; Guid? DeletedBy; }` — opt-in, entity implements it directly |
| Metadata (`CollectionMetadata`) | no soft-delete concept | **add** `bool SoftDelete`; the scanner derives it from `entityType.IsAssignableTo(ISoftDeletable)` |
| Delete filter mode (`Domain/Query/`) | none | **add** `enum DeletedFilter { Exclude, Only, With }` |
| Delete (`Application/Query/ItemService.DeleteAsync`) | hard delete + inbound-Restrict check | **branch**: soft-deletable & not purge → set `DeletedAt`/`DeletedBy`, repo soft-delete; else → existing hard delete |
| Restore (`ItemService`) | none | **add** `RestoreAsync(collection, id)` — clears the marker (idempotent) |
| Read (`ItemService.QueryAsync`/`GetAsync`) | no mode | **add** `DeletedFilter mode = Exclude` parameter, threaded to the repository |
| Repository (`IItemRepository`) | `DeleteAsync` = hard delete; reads have no mode | **add** soft-delete + restore ops; `QueryAsync`/`GetByIdAsync` gain a `DeletedFilter mode = Exclude`; purge = existing `DeleteAsync` |
| SqlSugar client (`Infrastructure/Persistence/SqlSugarClientFactory`) | no global filter | **register** `db.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null)` on the scoped client |
| REST (`Api/Controllers/ItemsController`) | `DELETE` hard-deletes; no restore; no `deleted` param | **add** `?deleted=` on List/Get, `?purge=` on Delete, `POST .../{id}/restore` |
| GraphQL schema (`Api/GraphQl/CollectionSchemaBuilder`, `StruoTypeModule`) | `deleteX(id)`; no `restoreX`; no `deleted` arg | **add** `DeletedFilter` enum, `deleted:` arg on list queries, `purge:` arg on `deleteX`, `restoreX(id)` mutation |
| GraphQL resolvers (`Api/GraphQl/MutationResolvers`, `CollectionResolvers`) | delete = hard | **wire** delete→soft, purge, restore, and the read `deleted` mode |

## 2. Chosen approach — SqlSugar global query filter (Approach A)

Three enforcement strategies were considered for excluding trashed rows across all read paths:

- **A. SqlSugar global query filter.** ✅ **Chosen.** `QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null)`
  on the scoped client applies to *every* `Queryable` over an `ISoftDeletable` entity — list, get-by-id,
  deep expansion, cross-relation id-resolution, M2M existence, inbound-Restrict — with no per-path code.
  - **Pros:** can't-forget coverage (the property that matters most for a correctness/safety filter);
    idiomatic SqlSugar (satisfies §17.4 "zero vendor SQL"); Restrict-live-only and relation exclusion come
    **for free**.
  - **Cons:** one live-verification risk — how the global filter composes with the existing raw ORDER-BY
    correlated subquery and `IN` queries on real Postgres (audit D9). Closed by the §8 live gate.
- **B. Explicit filter injection at each repository method.** Rejected: must remember every read path
  (Query/GetById/QueryWhereIn/QueryWhereInFiltered/QueryIds/deep expander/M2M existence); missing one is a
  **silent** correctness hole — exactly the class this project has been burned by ("SQLite-green ≠
  Postgres-correct").
- **C. Hybrid** (global filter + explicit opt-out plumbing). Rejected as unnecessary complexity (YAGNI).

**The global filter is the floor.** Deep expansion, relation id-resolution, M2M existence, and the
inbound-Restrict check always run in the default (Exclude) state and thus always exclude trashed rows —
these paths need **no** change. Only the top-level `QueryAsync`/`GetAsync` accept a `DeletedFilter` mode:

- `Exclude` (default): the global filter applies unchanged.
- `With`: the query calls `.ClearFilter<ISoftDeletable>()` for that queryable (both live + trashed).
- `Only`: `.ClearFilter<ISoftDeletable>()` **plus** an explicit `DeletedAt != null` predicate.

`RestoreAsync` and `purge` operate by id on rows that are (by definition) trashed, so their repository
operations also run with the filter cleared — otherwise the global floor would hide the very row they
target.

## 3. Domain layer

```csharp
// Domain/Auditing/ISoftDeletable.cs — package-free, mirrors IAuditable
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
```

- Opt-in = the entity implements `ISoftDeletable`. No `[CmsCollection(SoftDelete = …)]` attribute — the
  interface is the single source of truth, consistent with `IAuditable` on `AuditableEntity`.
- The two properties are plain (no SqlSugar attributes) so Domain stays package-free (§2). Column mapping
  (nullable `timestamptz` / `uuid`) is handled by SqlSugar CodeFirst / the migration (§9); `null` =
  the row is live.
- `enum DeletedFilter { Exclude, Only, With }` lives in `Domain/Query/`.

## 4. Metadata & scanner

- `CollectionMetadata` gains `bool SoftDelete { get; }`.
- The startup scanner sets it from `descriptor.EntityType.IsAssignableTo(typeof(ISoftDeletable))`. Cached
  like all metadata (§17.6 — no per-request reflection).
- No fail-fast is required (there is no attribute/interface pair to reconcile). A collection that does not
  implement the interface simply has `SoftDelete = false` and keeps today's hard-delete semantics.

## 5. Application layer — `ItemService`

**Delete / purge:**
```
DeleteAsync(collection, id, purge = false):
  CanDelete guard + RequireSuperAdminForAdminOnly    // unchanged
  inbound-Restrict check                              // unchanged (now excludes trashed sources for free)
  if meta.SoftDelete && !purge:
      set DeletedAt = now (UTC), DeletedBy = currentUser?.Id
      return repository.SoftDeleteAsync(collection, id)   // UPDATE; false if id not found / already gone
  else:
      return repository.DeleteAsync(collection, id)        // existing hard delete (purge or non-soft coll)
```
- Purge finds the (trashed) row with the filter cleared, then hard-deletes. `purge=true` on a
  non-soft-delete collection is a plain hard delete (harmless).
- `DELETE` (non-purge) on an already-trashed row is **idempotent**: re-stamps / no-ops and returns success
  (still trashed).

**Restore:**
```
RestoreAsync(collection, id):
  CanDelete guard + RequireSuperAdminForAdminOnly
  load row with filter cleared; null → return null (→ 404)
  if DeletedAt is null: return the row (idempotent no-op, already live)
  set DeletedAt = null, DeletedBy = null; repository.RestoreAsync(...); re-read + Project
```

**Read mode:** `QueryAsync`/`GetAsync` gain `DeletedFilter mode = Exclude`, passed to the repository. The
default preserves every existing caller. `Only`/`With` are only reachable from the API after a
delete-permission check (§6).

**Actor:** `DeletedBy` is read from the existing `ICurrentUserAccessor` (null when no user — same contract
as `CreatedBy`/`UpdatedBy`).

## 6. API — REST (`ItemsController`)

- `GET /api/items/{c}` and `GET /api/items/{c}/{id}`: parse `?deleted=exclude|only|with`
  (default `exclude`; unknown value → `400`). `only`/`with` require `CanDelete(collection)` → else
  `403`/`401`. For a non-soft-delete collection, `only`/`with` degrade to `exclude` (tolerant).
- `DELETE /api/items/{c}/{id}`: soft-deletes an opted-in collection → `204`; `?purge=true` → permanent →
  `204`. Permission = existing `CanDelete`.
- `POST /api/items/{c}/{id}/restore` `[Authorize(CookieOrBearer)]` → `CanDelete`; body-less; returns
  `{ data: <restored row> }` (`200`); unknown/never-existed id → `404`.
- **Response envelope unchanged** (`{ data }` / `{ error: { message } }`). Unifying it is slice 9a.

## 7. API — GraphQL

- Add `enum DeletedFilter { EXCLUDE, ONLY, WITH }` to the schema.
- List query `xs(...)`: add `deleted: DeletedFilter = EXCLUDE`. `ONLY`/`WITH` require `CanDelete` → else
  `FORBIDDEN`. Single-item `x(id)` stays default-exclude (fetching a specific trashed node is a REST
  `?deleted=with` / a future enhancement; not needed for parity of the core flows).
- `deleteX(id, purge: Boolean = false)`: soft-deletes an opted-in collection, or purges when `purge:true`.
  Return type stays `Boolean`.
- Add `restoreX(id)` mutation → returns the restored node (re-read via the mutation's selection set, same
  as create/update).
- All schema generation stays in `CollectionSchemaBuilder`/`StruoTypeModule`; argument reading reuses the
  existing `CollectionResolvers`/`MutationResolvers` machinery. **Domain/Application engine is not touched
  for GraphQL** — the resolvers call the same `ItemService` methods REST uses.

## 8. Error handling

No new exception types. Reuse the existing exception → status mapping (`Program.cs` middleware +
`StruoErrorFilter`): permission → `403`/`FORBIDDEN`; not-found → `404`/`NOT_FOUND`; inbound-Restrict →
`409`/`CONFLICT`; bad input (e.g. unknown `deleted` value) → `400`/`BAD_USER_INPUT`.

## 9. Testing strategy (TDD)

- **Unit:** scanner derives `SoftDelete` from the interface; `ItemService` delete→soft vs purge→hard
  branch; `RestoreAsync` idempotency + not-found; `DeletedBy` set from the current user; `DeletedFilter`
  threaded to the repository.
- **Integration (SQLite, real engine):** global filter excludes trashed rows from list + get-by-id;
  `only`/`with` lift it; restore makes a row reappear; purge removes it; **deep expansion excludes trashed
  children**; **M2M existence check excludes trashed targets**; inbound-Restrict counts **live** references
  only (a trashed referrer no longer blocks); permission gating (`only`/`with`/restore without delete →
  403); GraphQL read+write parity (`deleted` arg, `deleteX` soft, `deleteX(purge)`, `restoreX`).
- **N+1 invariant:** reuse the existing execution-spy; confirm the global filter adds no extra queries to
  deep expansion.
- **Live gate (real Postgres — the decisive gate):** Approach A's one risk — the global filter composing
  with the raw ORDER-BY correlated subquery and `IN` queries on real PG. Full flow on `web-struo-cms-db`:
  create → soft-delete → list excludes → `?deleted=only` shows it → `restore` → reappears in default list →
  `purge` → gone (get → 404); CJK round-trips code-point-exact; inbound-Restrict live-only (trashed Article
  no longer blocks deleting its Category; a live Article still 409s); deep/relation exclusion; REST ↔
  GraphQL parity. Honors the standing "SQLite-green ≠ Postgres-correct" rule.

## 10. Migration & sample

- **Sample (host, not framework):** `Article` implements `ISoftDeletable` (primary content type);
  `Category` also implements it so the live gate can exercise the inbound-Restrict live-only interaction
  (`Article → Category` is M2O). Framework code never references `samples/*` (§2).
- **`db/migrations/005-soft-delete-columns.sql`:** `ALTER TABLE` each opted-in table to add
  `deleted_at timestamptz NULL` and `deleted_by uuid NULL`. `InitTables` adds tables, not columns, so live
  DBs provisioned earlier need this script (same pattern as prior slices' migrations).

## 11. Acceptance gate

- `dotnet build -warnaserror` → 0 warnings; `dotnet test` → all green (573 baseline + new tests).
- Frontend untouched — `pnpm test` stays **237**.
- **Live gate PASSED on real Postgres** before the slice is declared done (the §8 flow).

## 12. Out of scope (recorded so they aren't lost)

- Unified response envelope (**slice 9a**).
- Vue admin trash/restore/purge UI + E2E (**slice 9b-fe**).
- Unique-constraint collisions between a trashed row and a new row (e.g. a soft-deleted `slug` still
  occupying the unique index) — a real concern, deferred; the pragmatic answer is usually purge-before-reuse
  or a partial index, both larger than this slice.
- Bulk empty-trash / scheduled auto-purge of old trashed rows.
- Soft-cascade (soft-deleting children when a parent is soft-deleted) — deliberately excluded; soft delete
  marks the parent only.
- Single-item GraphQL `x(id)` fetch of a specific trashed node (list `deleted: ONLY/WITH` covers the
  admin-trash flow; add later if needed).
