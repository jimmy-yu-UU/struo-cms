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
  at `009`, so the next script is `010-…`.
- One numbering scheme only. (Two legacy `0001__`/`0002__` files were folded into this series under
  DB-6; their headers note the original filename.)

Each file starts with a header comment: date, author, ticket/PR, and a one-line intent. Scripts must be
**idempotent** (`IF NOT EXISTS`, guarded `ALTER`, `DO $$ … $$` existence checks) so a re-run is safe.
Forward-only — no automatic `down`; write a compensating forward script if a rollback is needed.

## Ordering constraints (do not reorder)

- `007-soft-delete-columns.sql` (adds `deletedat`) must precede `009-hot-path-indexes.sql` (partial
  indexes `WHERE deletedat IS NULL`).
- `008-revisions-table.sql` (creates `revisions`) must precede any future index on that table.

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
