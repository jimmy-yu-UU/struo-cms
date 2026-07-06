-- 0001__widen_content_bearing_text_columns.sql
-- Date: 2026-07-06
-- Author: Claude (phase7g live-gate remediation)
-- Ticket: phase7g-advanced-richtext live gate, Check 1 (round-trip preservation) FAIL
--
-- Intent: SqlSugar CodeFirst maps every C# `string` property to `varchar(255)` on PostgreSQL
-- (no entity in the repo declared a column type before this fix). Fields whose [CmsField]
-- Interface is content-bearing by nature (RichText, Textarea, Markdown, Code, Json) are
-- unbounded — a realistic RichText body (tables, styled spans, multiple paragraphs) trivially
-- exceeds 255 characters and fails with Npgsql 22001 "value too long for type character
-- varying(255)". SQLite masked this (TEXT affinity ignores declared length), so it only
-- surfaced on the phase7g live gate against real Postgres.
--
-- The application-side fix (SqlSugarClientFactory EntityService hook) makes CodeFirst emit
-- `text` for these interfaces going forward for any FRESH table. This script retroactively
-- widens the columns on databases that already have the old varchar(255) columns from a prior
-- InitTables run, so existing (non-empty) tables get the same column type without a drop/recreate.
--
-- Affected columns (found by grepping framework + samples for [CmsField(Interface = ...)] on a
-- string property, RichText/Textarea/Markdown/Code/Json only; Text fields are intentionally left
-- alone pending the MaxLength feature, phase 7g.5):
--   article_translations.body                 (ArticleTranslation.Body,             RichText)
--   article_translations.seometadescription   (SeoTranslation.SeoMetaDescription,   Textarea; inherited by ArticleTranslation)
--
-- Guarded with information_schema existence checks so this script is idempotent and safe to run
-- against an environment where the table/column doesn't exist yet (e.g. a fresh or partially
-- provisioned database), or where it has already been applied.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'article_translations' AND column_name = 'body'
    ) THEN
        ALTER TABLE article_translations ALTER COLUMN body TYPE text;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'article_translations' AND column_name = 'seometadescription'
    ) THEN
        ALTER TABLE article_translations ALTER COLUMN seometadescription TYPE text;
    END IF;
END $$;
