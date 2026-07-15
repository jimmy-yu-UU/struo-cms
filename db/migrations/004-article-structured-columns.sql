-- Phase 7g+ slice 2 — structured-editor columns on the sample `Article` collection.
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES but never adds columns to an existing
-- table. Live databases provisioned before this merge already have an `articles` table, so the two
-- new fields (attributes/meta) must be added here. A freshly provisioned database gets them from
-- CodeFirst and does not need this script.
--
-- COLUMN TYPES — both `text`:
--   * `attributes` (Json field): a plain `text` column holding RAW JSON text. It is NOT an `IsJson`
--     column — the Json field is a `string?` property (SqlSugarCore 5.1.4 materializes an `IsJson`
--     System.Text.Json.JsonElement back DISPOSED via Newtonsoft, so we store the raw text and parse it
--     on read). It is NULLABLE (absent / JSON null => SQL NULL).
--   * `meta` (KeyValue field): an `IsJson` `text` column holding a serialized Dictionary. `text`, NOT
--     the `IsJson` default `varchar(1)` (which truncates on Postgres — the slice-1 finding), and NOT
--     NULL with a `'{}'` default (the CLR property is `= new()`, so an empty map serializes to "{}").
--
-- Identifiers are LOWERCASE and unquoted (SqlSugar emits unquoted identifiers; Postgres folds them to
-- lowercase — existing columns are `status`, `publishedat`, `regions`, `audiences`, `keywords`, ...).
-- Idempotent: safe to run more than once.

ALTER TABLE articles ADD COLUMN IF NOT EXISTS attributes text NULL;
ALTER TABLE articles ADD COLUMN IF NOT EXISTS meta       text NOT NULL DEFAULT '{}';
