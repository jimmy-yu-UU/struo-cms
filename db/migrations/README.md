# Schema migrations

Reviewed, forward-only SQL scripts that evolve the **PostgreSQL** production schema. `InitTables`
(SqlSugar CodeFirst) only *creates* missing tables in Development — it cannot alter/drop columns — so
every post-deploy schema change lives here as a checked-in, versioned script.

See [`docs/migration-strategy.md`](../../docs/migration-strategy.md) for the "why" (audit finding D3).

## Numbering convention

```
NNN-short-kebab-description.sql
```

- **`NNN`** — zero-padded, **contiguous, single series** starting at `001`. The numeric prefix is the
  apply order (ordinal filename sort), so it must be monotonic and gap-free.
- **`short-kebab-description`** — one logical change per file.
- The **next migration** is simply the highest existing number **+ 1**. As of this file the series ends
  at `011`, so the next script is `012-…`.
- One numbering scheme only. (Two legacy `0001__`/`0002__` files plus a separate legacy `001-`…`007-`
  series were folded into this single series under DB-6; each renamed file's header notes its original
  filename.)

### Renumber mapping (DB-6, audit Batch 2, 2026-07-15)

The unified `001`–`010` series was assembled from two pre-existing schemes. `docs/ROADMAP.md` phase
entries still quote the **old** names as a historical record; this is the authoritative old → new map:

| Old filename                                     | New filename                                  |
|--------------------------------------------------|-----------------------------------------------|
| `0001__widen_content_bearing_text_columns.sql`   | `001-widen-content-bearing-text-columns.sql`  |
| `0002__retroactive_add_version_columns.sql`       | `002-retroactive-add-version-columns.sql`     |
| `001-article-multivalue-columns.sql`             | `003-article-multivalue-columns.sql`          |
| `002-article-structured-columns.sql`             | `004-article-structured-columns.sql`          |
| `003-article-files-column.sql`                   | `005-article-files-column.sql`                |
| `004-article-repeater-column.sql`                | `006-article-repeater-column.sql`             |
| `005-soft-delete-columns.sql`                    | `007-soft-delete-columns.sql`                 |
| `006-revisions-table.sql`                        | `008-revisions-table.sql`                     |
| `007-hot-path-indexes.sql`                       | `009-hot-path-indexes.sql`                    |
| _(new in Batch 2 — no predecessor)_              | `010-revisions-unique-number.sql`             |
| _(new in Batch 4 — no predecessor)_              | `011-translation-unique-locale.sql`           |

Each file starts with a header comment: date, author, ticket/PR, and a one-line intent. Scripts must be
**idempotent** (`IF NOT EXISTS`, guarded `ALTER`, `DO $$ … $$` existence checks) so a re-run is safe.
Forward-only — no automatic `down`; write a compensating forward script if a rollback is needed.

## Timestamp convention (DB-7)

New columns and new tables that store an instant use **`timestamptz`** (timestamp *with* time zone),
never bare `timestamp`. Store UTC; let the client localise. The precedent is the runner's own tracking
table — `schema_migrations (filename text PRIMARY KEY, appliedat timestamptz)`, created by
`MigrationRunner` (whose code comment states the same convention). Any future `NNN-…` script that adds a
temporal column must follow it.

Existing `timestamp` columns are **not** retro-migrated to `timestamptz`: a retro-conversion re-anchors
already-stored values against the session time zone (a silent data shift for anything not written in
UTC), so the cost/risk outweighs the benefit for columns already in production. The convention binds new
schema only; leave historical columns as-is unless a specific defect requires a deliberate, reviewed
conversion script.

## Ordering constraints (do not reorder)

- `007-soft-delete-columns.sql` (adds `deletedat`) must precede `009-hot-path-indexes.sql` (partial
  indexes `WHERE deletedat IS NULL`).
- `008-revisions-table.sql` (creates `revisions`) must precede any future index on that table, including
  `010-revisions-unique-number.sql` (which replaces `ix_revisions_item` with a UNIQUE index).

### `010` InitTables ordering hazard (dev only)

`010-revisions-unique-number.sql` promotes the `revisions` lookup index to a composite **UNIQUE** index,
and the matching CodeFirst constraint now lives on the entity (`Revision.cs`,
`UniqueGroupNameList = ["ux_revisions_item_no"]`). Empirically verified (SqlSugarCore 5.1.4.215):
`InitTables` on an **existing** table whose entity just gained a `UniqueGroupNameList` attempts to add
the unique index and **throws if duplicate rows already exist**. Because dev order is
`InitTables → runner`, a pre-existing dev DB that already holds duplicate revision rows would crash
startup in `InitTables` before `010` can dedupe. The runner is **not** reordered ahead of `InitTables`
(migrations `001`/`003`–`007`/`009` touch sample tables that exist only after `InitTables` on a fresh
dev DB). Handling:

- **Fresh dev/test DB / existing dev DB without dupes** — no action; `InitTables` adds the unique index
  cleanly and `010` is a near no-op.
- **Existing dev DB *with* duplicate revision rows** — apply `010` manually (`psql -f`) to dedupe
  **before** restarting the app, or drop the dev `revisions` table (dev data is disposable).
- **Production / live PG** — `InitTables` never runs; `010` is the sole path and dedupes first, safely.

#### Accepted redundant dev index (decision 2026-07-15 live gate)

On **Development** databases `InitTables` also creates its *own* unique index from the entity's
`UniqueGroupNameList` — SqlSugar names it `index_revisions_collectionname_itemid_revisionnumber_unique`
— on the **same** three columns that `010` covers with `ux_revisions_item_no`. So a dev DB deliberately
carries **both** indexes (redundant but harmless; write-amplification is negligible at dev volumes).
**Production never runs `InitTables`, so it has only `ux_revisions_item_no`.** This redundancy is
**accepted deliberately — do not "clean up" either index**:

- dropping the SqlSugar-created one just gets it recreated on the next dev restart, and
- dropping `ux_revisions_item_no` would leave **production unprotected** if `010` were ever skipped.

## Applying — the runner

A lightweight runner (`MigrationRunner`, in `Struo.Infrastructure/Persistence`) applies pending scripts
at application startup:

- **PostgreSQL only.** On any other backend (e.g. the SQLite used by the test suite) it is a hard
  no-op — it neither reads nor creates the tracking table and runs no SQL.
- Enabled by configuration: set `Database:MigrationsPath` to this directory. Empty/absent (the default)
  disables it. It runs in **all** environments when configured (that is the point — reviewed scripts get
  applied to production), positioned after `InitTables` and before the dev seeders.
- Tracks applied filenames in a `schema_migrations (filename text PRIMARY KEY, appliedat timestamptz)`
  table it creates on first run. Already-recorded filenames are skipped, so it is safe to re-run.
- Each file runs inside its own transaction together with its tracking-row insert; the first failure is
  rolled back and aborts the run (later files are not attempted).

Example (`appsettings.json` / environment override):

```json
"Database": {
  "MigrationsPath": "db/migrations"
}
```

## Fresh environment (bootstrap)

1. Create the empty PostgreSQL database and set `Database:ConnectionString`.
2. Choose how the baseline schema is created:
   - **Dev / disposable:** run the app once in `Development` — `InitTables` creates all tables from the
     entity classes, then the runner (if `MigrationsPath` is set) applies any column/index migrations on
     top. The column-widening/backfill scripts (`001`–`002`) are idempotent and safely no-op on a
     freshly-`InitTables`'d schema.
   - **Production:** do **not** rely on `InitTables`. Apply a reviewed baseline plus these scripts in
     order via the runner (set `MigrationsPath`) or by hand in a maintenance window.
3. On every subsequent deploy, add the new `NNN-…` script(s); the runner applies only the pending ones.

## Manual apply (no runner)

Apply files in ascending `NNN` order with a reviewed SQL client, back up first, and record applied
files (the runner uses `schema_migrations`; by hand, keep an ops log or insert rows yourself).
