-- Phase 7g+ slice 1 — multi-value select columns on the sample `Article` collection.
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES but never adds columns to an
-- existing table. Live databases provisioned before this merge already have an `articles`
-- table, so the three new multi-value fields (regions/audiences/keywords) must be added by
-- this migration. A freshly provisioned database gets them from CodeFirst and does not need
-- this script.
--
-- COLUMN TYPE — `text`, NOT jsonb and NOT the default varchar. SqlSugar stores an `IsJson` List<>
-- as a serialized JSON string; the `SqlSugarClientFactory` convention widens it to `text` because
-- `IsJson` alone leaves the CodeFirst length unset and Postgres then makes it `varchar(1)` (default
-- length 1) — any JSON longer than one char fails with Npgsql 22001 "value too long for type
-- character varying(1)". SQLite ignores declared length, which is why this only bit on Postgres
-- (the phase 7g+ live-gate finding). Match the ORM's expected type (`text`) here so `InitTables`
-- does not diff the column and attempt a (failing) ALTER at startup.
--
-- Identifiers are LOWERCASE and unquoted: SqlSugar emits unquoted identifiers on Postgres, which
-- the server folds to lowercase (existing columns are `status`, `publishedat`, `heroimageid`,
-- `categoryid`, ...). Quoted PascalCase names ("Regions") are NOT found by the ORM (Npgsql 42703).
--
-- NOT NULL with a `'[]'` default (the CLR property is `= []`, so an empty list serializes to "[]").
-- Idempotent: safe to run more than once.

ALTER TABLE articles ADD COLUMN IF NOT EXISTS regions   text NOT NULL DEFAULT '[]';
ALTER TABLE articles ADD COLUMN IF NOT EXISTS audiences text NOT NULL DEFAULT '[]';
ALTER TABLE articles ADD COLUMN IF NOT EXISTS keywords  text NOT NULL DEFAULT '[]';
