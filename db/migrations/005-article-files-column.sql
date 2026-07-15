-- Phase 7g+ slice 3 — multi-file `gallery` column on the sample `Article` collection.
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES but never adds columns to an existing table.
-- Live databases provisioned before this merge already have an `articles` table, so the new `gallery`
-- field must be added here. A freshly provisioned database gets it from CodeFirst and does not need
-- this script.
--
-- COLUMN TYPE — `text`:
--   * `gallery` (Files field): an `IsJson` `text` column holding a serialized JSON array of file ids
--     (List<Guid> => ["<guid>","<guid>"]). `text`, NOT the `IsJson` default `varchar(1)` (which
--     truncates on Postgres — the slice-1 finding), and NOT NULL with a `'[]'` default (the CLR
--     property is `= []`, so an empty gallery serializes to "[]").
--
-- Identifiers are LOWERCASE and unquoted (SqlSugar emits unquoted identifiers; Postgres folds them to
-- lowercase — existing columns are `status`, `publishedat`, `regions`, `attributes`, `meta`, ...).
-- Idempotent.

ALTER TABLE articles ADD COLUMN IF NOT EXISTS gallery text NOT NULL DEFAULT '[]';
