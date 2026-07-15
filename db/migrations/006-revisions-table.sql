-- db/migrations/006-revisions-table.sql
-- Phase 9c — the framework `revisions` table (per-item snapshot history).
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES on a freshly provisioned (dev) database, but
-- live databases provisioned before this merge need the table created here.
--
-- IDENTIFIER NAMING — lowercase, unquoted (SqlSugar emits unquoted identifiers; Postgres folds to
-- lowercase), matching how the CLR properties map (e.g. `CollectionName` -> `collectionname`,
-- `RevisionNumber` -> `revisionnumber`, `CreatedAt` -> `createdat`) and consistent with the
-- audit/soft-delete columns (`createdat`/`createdby`/`deletedat`). NOT snake_case.
--
-- COLUMN TYPES:
--   * `id`             uuid       (CLR Guid, UUIDv7 assigned at capture) — primary key.
--   * `collectionname` varchar(255) (the collection name, e.g. "article"; plain string -> CodeFirst
--                                  default varchar(255), matched here).
--   * `itemid`         varchar(255) (the item PK, stringified — keeps the table PK-type-agnostic;
--                                  a stringified Guid is 36 chars, well under 255).
--   * `revisionnumber` bigint     (CLR long; 1-based, monotonic per (collectionname, itemid)).
--   * `operation`      varchar(255) ("create" | "update" | "revert"; plain string -> varchar(255)).
--   * `snapshot`       text       (canonical revert-capable JSON — MUST be `text`; a realistic
--                                  snapshot overflows varchar, so the entity sets ColumnDataType="text").
--   * `createdat`      timestamp  (CLR DateTime; stamped at capture, not via audit AOP).
--   * `createdby`      uuid NULL  (CLR Guid?; the acting user, null when no user — same contract as
--                                  createdby elsewhere).
--
-- Idempotent (safe to re-run). Before applying, confirm the physical column names SqlSugar CodeFirst
-- emits for `Revision` against a dev-provisioned table (`\d revisions`) and match the DDL to them.

CREATE TABLE IF NOT EXISTS revisions (
    id             uuid         NOT NULL PRIMARY KEY,
    collectionname varchar(255) NOT NULL,
    itemid         varchar(255) NOT NULL,
    revisionnumber bigint       NOT NULL,
    operation      varchar(255) NOT NULL,
    snapshot       text         NOT NULL,
    createdat      timestamp NOT NULL,
    createdby      uuid      NULL
);

-- Fast lookup of an item's revisions (list newest-first) + the max()+1 sequence in CaptureAsync.
CREATE INDEX IF NOT EXISTS ix_revisions_item ON revisions (collectionname, itemid, revisionnumber);
