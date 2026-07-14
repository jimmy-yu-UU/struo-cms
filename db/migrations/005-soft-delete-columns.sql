-- Phase 9b — soft-delete columns on opted-in collections (entities implementing ISoftDeletable).
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES but never adds columns to an existing table.
-- Live databases provisioned before this merge already have `articles`/`categories` tables, so the new
-- `deletedat`/`deletedby` columns must be added here. A freshly provisioned database gets them from
-- CodeFirst and does not need this script.
--
-- OPTED-IN COLLECTIONS (as of Phase 9b): the sample `Article` and `Category` entities implement
-- `ISoftDeletable` (Domain marker). No framework/base collection is soft-deletable by default — soft
-- delete is per-collection opt-in via the interface.
--
-- COLUMN NAMING — LOWERCASE, UNQUOTED, NO UNDERSCORE:
--   SqlSugar emits unquoted identifiers and Postgres folds them to lowercase, so the CLR properties
--   `DeletedAt`/`DeletedBy` map to columns `deletedat`/`deletedby` (matching the existing audit columns
--   `createdat`/`createdby`/`updatedat`/`updatedby` and `publishedat`). NOT snake_case.
--
-- COLUMN TYPES:
--   * `deletedat` — nullable `timestamp` (CLR `DateTime?`), matching the existing `createdat`/`updatedat`
--     audit columns' type. NULL = the row is live (never soft-deleted). No default.
--   * `deletedby` — nullable `uuid` (CLR `Guid?`), matching `createdby`/`updatedby`. No default.
--
-- Idempotent (safe to re-run). Verify column types against the live table first if unsure:
--   \d articles   (confirm `deletedat` matches `createdat`'s type)

ALTER TABLE articles   ADD COLUMN IF NOT EXISTS deletedat timestamp NULL;
ALTER TABLE articles   ADD COLUMN IF NOT EXISTS deletedby uuid      NULL;
ALTER TABLE categories ADD COLUMN IF NOT EXISTS deletedat timestamp NULL;
ALTER TABLE categories ADD COLUMN IF NOT EXISTS deletedby uuid      NULL;
