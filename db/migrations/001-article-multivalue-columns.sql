-- Phase 7g+ slice 1 — multi-value select columns on the sample `Article` collection.
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES but never adds columns to an
-- existing table. Live databases provisioned before this merge already have an `articles`
-- table, so the three new multi-value fields (Regions/Audiences/Keywords) must be added by
-- this migration. A freshly provisioned database gets them from CodeFirst and does not need
-- this script.
--
-- The columns map to SqlSugar `IsJson` -> Postgres `jsonb`. The CLR default is an empty list
-- (`= []`), so the column is NOT NULL with a `'[]'::jsonb` default.
--
-- Column identifiers use the PascalCase property names (SqlSugar CodeFirst maps a property to a
-- same-named quoted column on Postgres, as the existing "Status"/"PublishedAt"/"HeroImageId"
-- columns already are). Confirm the exact casing against the live schema before applying if in
-- doubt (\d articles).
--
-- Idempotent: safe to run more than once.

ALTER TABLE "articles" ADD COLUMN IF NOT EXISTS "Regions"   jsonb NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE "articles" ADD COLUMN IF NOT EXISTS "Audiences" jsonb NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE "articles" ADD COLUMN IF NOT EXISTS "Keywords"  jsonb NOT NULL DEFAULT '[]'::jsonb;
