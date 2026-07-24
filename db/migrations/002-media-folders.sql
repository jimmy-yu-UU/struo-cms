-- 002-media-folders.sql
-- 2026-07-24 · Batch C (#5 media folders) · media_folders table + files.folderid organisational FK.
-- Idempotent, forward-only. DB-7: temporal columns of NEW tables are timestamptz.
CREATE TABLE IF NOT EXISTS media_folders (
    id uuid NOT NULL PRIMARY KEY,
    name character varying(255) NOT NULL,
    parentid uuid NULL,
    createdat timestamptz NOT NULL,
    createdby uuid NULL,
    updatedat timestamptz NOT NULL,
    updatedby uuid NULL,
    version bigint NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_media_folders_parentid ON media_folders (parentid);

ALTER TABLE files ADD COLUMN IF NOT EXISTS folderid uuid NULL;
CREATE INDEX IF NOT EXISTS ix_files_folderid ON files (folderid);
