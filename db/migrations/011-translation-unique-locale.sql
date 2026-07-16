-- db/migrations/011-translation-unique-locale.sql
-- Audit Batch 4 (Task 4) — DB-10: UNIQUE (fk, locale) backstop for translation sidecars.
--
-- AUDIT REFERENCE: docs/architecture-audit-2026-07-15.md, finding DB-10. A translation sidecar
-- (article_translations / file_translations) holds one row per (parent, locale); overlay reads assume
-- at most one row per locale. Nothing physically enforced that, so a duplicate (fk, locale) could land
-- and make the overlay read non-deterministic (which of the two rows wins?). This migration promotes
-- each table to a composite UNIQUE index over (fk column, locale) so a duplicate can never physically
-- exist. Fail closed; no retry logic (YAGNI).
--
-- IDENTIFIER NAMING — lowercase, unquoted (SqlSugar emits unquoted identifiers; Postgres folds to
-- lowercase), matching 008/009/010. Columns: articleid / fileid / locale.
--
-- KEEP THE EXISTING NON-UNIQUE LOOKUP INDEX: unlike 010 (which dropped ix_revisions_item), the
-- non-unique ix_article_translations_fk_locale / ix_file_translations_fk_locale from 009 are RETAINED.
-- The matching CodeFirst entity now carries BOTH the non-unique [SugarIndex] AND the
-- UniqueGroupNameList, so dev InitTables emits both; keeping both here avoids a CodeFirst/migration
-- divergence. This is the same accepted-redundant-dev-index decision recorded for 010 in
-- db/migrations/README.md — the redundancy is harmless (write-amplification negligible), and a leading
-- non-unique index colocated with an identical-column unique index costs nothing to keep.
--
-- DEDUPE FIRST: creating a UNIQUE index fails if duplicate (fk, locale) rows already exist. Any such
-- rows are a pre-existing data defect. Per duplicate group we keep the row with the GREATEST `id` (the
-- most recently written translation — the last writer wins) and delete the rest.
--
-- INITTABLES ORDERING HAZARD (dev only): identical to 010's — InitTables on an EXISTING dev table whose
-- entity just gained a UniqueGroupNameList attempts to add the unique index and THROWS if duplicate
-- rows are already present, and InitTables runs BEFORE this runner. Fresh dev DBs create the index
-- cleanly (no rows -> no dupes); an existing dev DB WITH dupes must apply this migration manually
-- (psql -f) first, or drop the dev translation tables (dev data is disposable). Production never runs
-- InitTables, so this migration is the sole path and dedupes safely. See db/migrations/README.md.
--
-- IDEMPOTENT (safe to re-run): the DELETEs are no-ops once no duplicates remain; the CREATE UNIQUE
-- INDEX statements are guarded with IF NOT EXISTS.

-- 1. article_translations: dedupe (keep greatest id per (articleid, locale)), then the UNIQUE backstop.
DELETE FROM article_translations a
USING article_translations keep
WHERE a.articleid = keep.articleid
  AND a.locale    = keep.locale
  AND a.id        < keep.id;
CREATE UNIQUE INDEX IF NOT EXISTS ux_article_translations_fk_locale
    ON article_translations (articleid, locale);

-- 2. file_translations: same shape (fileid, locale).
DELETE FROM file_translations f
USING file_translations keep
WHERE f.fileid = keep.fileid
  AND f.locale = keep.locale
  AND f.id     < keep.id;
CREATE UNIQUE INDEX IF NOT EXISTS ux_file_translations_fk_locale
    ON file_translations (fileid, locale);
