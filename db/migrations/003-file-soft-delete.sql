-- 003-file-soft-delete.sql
-- 2026-07-27 · #12 · File soft-delete columns + retire the redundant 'archived' status.
-- Idempotent, forward-only. deletedat is `timestamp without time zone` to match the existing
-- files.createdat/updatedat columns (AuditableEntity maps to non-tz via InitTables; only
-- site_settings.updatedat is timestamptz — see 001-core-baseline.sql, audit DB-12).
ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedat timestamp without time zone NULL;
ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedby uuid NULL;

-- 'archived' is replaced by the recycle bin: existing archived files become trashed (reversible via
-- restore) and their status is normalised to 'draft' (a restored file is not auto-republished).
UPDATE files
   SET deletedat = COALESCE(deletedat, now()),
       status    = 'draft'
 WHERE status = 'archived';
