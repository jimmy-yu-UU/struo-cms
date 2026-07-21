-- Site Settings — singleton `site_settings` table backing in-app branding edits.
--
-- CONTEXT: InitTables (CodeFirst, dev/test only) creates this table from SiteSettings.cs on a fresh
-- Development database. This script provisions it on an existing production PostgreSQL database.
-- Identifiers are LOWERCASE and unquoted (SqlSugar emits unquoted identifiers on Postgres, which folds
-- them to lower case). timestamptz per the DB-7 timestamp convention. No seed row: absence means
-- "use appsettings defaults"; the first save inserts the singleton row.
CREATE TABLE IF NOT EXISTS site_settings (
    id         uuid        PRIMARY KEY,
    brandname  text        NOT NULL DEFAULT '',
    logofileid uuid        NULL,
    updatedat  timestamptz NOT NULL,
    updatedby  uuid        NULL
);
