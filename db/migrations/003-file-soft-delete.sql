-- 003-file-soft-delete.sql
-- 2026-07-27 · #12 · File soft-delete columns + retire the redundant 'archived' status.
-- Idempotent, forward-only. DB-7: temporal columns are timestamptz.
ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedat timestamptz NULL;
ALTER TABLE files ADD COLUMN IF NOT EXISTS deletedby uuid NULL;

-- 'archived' is replaced by the recycle bin: existing archived files become trashed (reversible via
-- restore) and their status is normalised to 'draft' (a restored file is not auto-republished).
UPDATE files
   SET deletedat = COALESCE(deletedat, now()),
       status    = 'draft'
 WHERE status = 'archived';
