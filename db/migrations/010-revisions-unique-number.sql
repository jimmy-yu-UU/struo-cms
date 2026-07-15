-- db/migrations/010-revisions-unique-number.sql
-- Audit Batch 2 (Task 2) — DB-4 (=CS-6): UNIQUE backstop for per-item revision numbers.
--
-- AUDIT REFERENCE: docs/architecture-audit-2026-07-15.md, findings DB-4 / CS-6. The `revisions` table
-- (Phase 9c, see 008-revisions-table.sql) assigns `revisionnumber` as max()+1 per
-- (collectionname, itemid) inside ItemService's write transaction (SqlSugarRevisionStore.CaptureAsync).
-- That is race-free only as long as the single-item write path stays serialized; a lost-update race
-- (two concurrent writes computing the same max()+1) would otherwise silently duplicate a revision
-- number. This migration promotes the existing non-unique lookup index to a composite UNIQUE index so
-- such a duplicate can never physically land — the capture's INSERT fails, and because capture runs
-- inside the item's write transaction, the whole write rolls back. Fail closed; no retry logic (YAGNI).
--
-- IDENTIFIER NAMING — lowercase, unquoted (SqlSugar emits unquoted identifiers; Postgres folds to
-- lowercase), matching 008/009. Columns: collectionname / itemid / revisionnumber.
--
-- INDEX REPLACEMENT: the prior non-unique `ix_revisions_item (collectionname, itemid, revisionnumber)`
-- (008) has the SAME leading columns as the new unique index, so the unique version fully serves every
-- query the old one did (item revision listing + the max()+1 sequence lookup). We drop the non-unique
-- one and create the unique one rather than keeping both.
--
-- DEDUPE FIRST: creating a UNIQUE index fails if duplicate (collectionname, itemid, revisionnumber)
-- rows already exist. Any such rows are a pre-existing data defect (the very race this backstops). We
-- keep, per duplicate group, the row with the EARLIEST `createdat` (tie-break: smallest `id`) and
-- delete the rest — the earliest capture is the authoritative one; later duplicates are the spurious
-- lost-update artifacts.
--
-- ============================================================================================
-- INITTABLES ORDERING HAZARD (dev only) — READ BEFORE APPLYING TO AN EXISTING DEV DATABASE
-- ============================================================================================
-- The matching CodeFirst constraint now lives on the entity
-- (Revision.cs: [SugarColumn(UniqueGroupNameList = ["ux_revisions_item_no"])] on the three columns).
-- Dev startup order is InitTables -> this migration runner -> seeders (Program.cs). Empirically
-- verified (SQLite, SqlSugarCore 5.1.4.215): InitTables on an EXISTING table whose entity gained a
-- UniqueGroupNameList DOES attempt to add the unique index, and THROWS if duplicate rows are already
-- present ("UNIQUE constraint failed" / on PG a 23505 unique_violation). Because InitTables runs BEFORE
-- this migration in dev, it would crash startup on a pre-existing dev DB that already holds duplicate
-- revision rows — before this migration ever gets the chance to dedupe.
--
-- Why the runner is NOT reordered ahead of InitTables to sidestep this: migrations 001/003-007/009
-- ALTER / index the sample tables (articles, article_translations, categories, article_tags, ...),
-- which on a fresh dev DB exist ONLY after InitTables. Running the runner first would fail those on a
-- clean dev database. So InitTables-first is kept, and this ordering hazard is handled operationally:
--
--   * Fresh dev/test DB   — InitTables creates `revisions` WITH the unique index (no rows -> no dupes ->
--                           no crash); this migration is then a near no-op (IF NOT EXISTS / IF EXISTS).
--   * Existing dev DB, no dupes — InitTables adds the unique index cleanly; this migration is a no-op.
--   * Existing dev DB WITH dupes — apply THIS migration manually (psql -f) to dedupe BEFORE restarting
--                           the app, or just drop the dev `revisions` table (dev data is disposable).
--                           Otherwise dev startup crashes in InitTables on the pre-existing duplicates.
--   * Production / live PG — InitTables never runs; this migration is the sole path and is safe to
--                           apply directly (it dedupes first, then swaps the index).
-- See db/migrations/README.md for the same note.
--
-- ACCEPTED REDUNDANT DEV INDEX (decision 2026-07-15 live gate) — on Development DBs, InitTables ALSO
-- creates its own unique index from the entity's UniqueGroupNameList, which SqlSugar names
-- `index_revisions_collectionname_itemid_revisionnumber_unique`, over the SAME three columns as
-- `ux_revisions_item_no` below. Dev DBs therefore carry BOTH indexes (redundant but harmless;
-- write-amplification negligible at dev volumes). Production never runs InitTables, so it has ONLY
-- `ux_revisions_item_no`. This redundancy is DELIBERATE — do NOT drop either: dropping the SqlSugar
-- one just gets recreated on the next dev restart; dropping `ux_revisions_item_no` would leave
-- production unprotected if this migration were ever skipped.
-- ============================================================================================
--
-- IDEMPOTENT (safe to re-run): the DELETE is a no-op once no duplicates remain; DROP INDEX IF EXISTS /
-- CREATE UNIQUE INDEX IF NOT EXISTS are guarded.

-- 1. Dedupe: keep earliest createdat per group, tie-break smallest id; delete the rest.
DELETE FROM revisions r
USING revisions keep
WHERE r.collectionname = keep.collectionname
  AND r.itemid         = keep.itemid
  AND r.revisionnumber = keep.revisionnumber
  AND (r.createdat > keep.createdat
       OR (r.createdat = keep.createdat AND r.id > keep.id));

-- 2. Replace the non-unique lookup index with the composite UNIQUE backstop.
DROP INDEX IF EXISTS ix_revisions_item;
CREATE UNIQUE INDEX IF NOT EXISTS ux_revisions_item_no
    ON revisions (collectionname, itemid, revisionnumber);
