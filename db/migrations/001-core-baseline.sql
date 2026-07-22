-- db/migrations/001-core-baseline.sql
-- Date: 2026-07-22
-- Author: Audit 2026-07-21 Batch 5 — migration core rebaseline
-- Ticket: docs/audit-2026-07-21-remediation-tasklist.md — BL-5 / rebaseline
--          docs/superpowers/plans/2026-07-22-migration-core-rebaseline.md
--
-- CORE BOOTSTRAP BASELINE. Creates the complete core-framework schema — the 9 FrameworkEntityTypes
-- tables (languages / files / file_translations / users / roles / permissions / user_roles / revisions /
-- site_settings) plus their PKs, unique indexes, and hot-path indexes. (Uniqueness is enforced via
-- CREATE UNIQUE INDEX, not ADD CONSTRAINT UNIQUE — so pg_constraint shows only PKs, matching InitTables.)
-- Machine-generated from
-- InitTables(FrameworkEntityTypes.All) on PostgreSQL via `pg_dump --schema-only`, then hardened to be
-- idempotent (IF NOT EXISTS throughout; PKs inlined) and stripped of pg_dump session preamble.
--
-- This is the PRODUCTION bootstrap path: a downstream deploy applies it to an EMPTY database via
-- MigrationRunner (Database:MigrationsPath) with NO dependency on the Development-only InitTables.
-- In dev, InitTables has already created these tables and Database:MigrationsPath stays empty (BL-5),
-- so this file is not run there; it is fully idempotent regardless, so a manual re-run is a safe no-op.
--
-- SCOPE — CORE ONLY. Sample/business schema (the Blog demo: articles / tags / categories / article_tags /
-- article_translations) is intentionally NOT here. StruoCMS is a reusable CMS *template* (CLAUDE.md §0):
-- the sample is a demo built by InitTables in dev; a downstream fork deletes the sample and adds its own
-- NNN-… migrations for its own collections. Never add business-table DDL to this core baseline.
--
-- TIMESTAMP TYPES — faithful to InitTables. AuditableEntity's createdat/updatedat map to
-- `timestamp without time zone` (SqlSugar CodeFirst default; no [SugarColumn] type on the base class);
-- only site_settings.updatedat is `timestamptz` (its entity declares ColumnDataType, audit DB-12). This
-- baseline reproduces exactly what InitTables produces so dev (InitTables) and prod (this file) never
-- drift. Standardising AuditableEntity on timestamptz is a separate entity-level change (the DB-12 tail),
-- deliberately out of scope here — doing it only in this file would reintroduce a dev/prod parity gap.
--
-- IDENTIFIER NAMING — lowercase, unquoted; SqlSugar-emitted index names (index_*_unique / ix_*) are kept
-- verbatim so a dev InitTables schema and a prod baseline schema carry IDENTICAL index names. This fixes
-- the pre-rebaseline NAME divergence (the old 011 migration's unique index was `ux_file_translations_fk_locale`
-- while dev InitTables emitted `index_file_translations_fileid_locale_unique` for the same columns).
-- NOT eliminated: file_translations still carries a redundant pair over identical (fileid, locale) — the
-- unique index plus a plain btree `ix_file_translations_fk_locale` (both from FileTranslation.cs attributes,
-- faithfully reproduced here). Dropping the redundant btree is a Batch-5 LOW (edit the entity + regenerate;
-- fixing it in this file alone would reintroduce dev/prod drift).
--
-- PRE-EXISTING DATABASES — tracking is by filename in schema_migrations. A database that had applied the
-- old 001–013 filenames (none exist in this template today) keeps those rows AND additionally runs+records
-- `001-core-baseline.sql` once; that run is a harmless idempotent no-op. Fresh/empty databases just get this.

-- ----------------------------------------------------------------------------------------------------
-- Sequences (bigint identity PKs: languages, file_translations)
-- ----------------------------------------------------------------------------------------------------
CREATE SEQUENCE IF NOT EXISTS public.languages_id_seq
    START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

CREATE SEQUENCE IF NOT EXISTS public.file_translations_id_seq
    START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

-- ----------------------------------------------------------------------------------------------------
-- Tables
-- ----------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.languages (
    id        bigint                      NOT NULL DEFAULT nextval('public.languages_id_seq'::regclass),
    code      character varying(255)      NOT NULL,
    name      character varying(255)      NOT NULL,
    isdefault boolean                     NOT NULL,
    enabled   boolean                     NOT NULL,
    sort      integer                     NOT NULL,
    createdat timestamp without time zone NOT NULL,
    createdby uuid,
    updatedat timestamp without time zone NOT NULL,
    updatedby uuid,
    CONSTRAINT languages_pkey PRIMARY KEY (id)
);
ALTER SEQUENCE public.languages_id_seq OWNED BY public.languages.id;

CREATE TABLE IF NOT EXISTS public.files (
    id          uuid                        NOT NULL,
    storagekey  character varying(255)      NOT NULL,
    filename    character varying(255)      NOT NULL,
    contenttype character varying(255)      NOT NULL,
    size        bigint                      NOT NULL,
    width       integer,
    height      integer,
    status      character varying(255)      NOT NULL,
    createdat   timestamp without time zone NOT NULL,
    createdby   uuid,
    updatedat   timestamp without time zone NOT NULL,
    updatedby   uuid,
    version     bigint                      NOT NULL,
    CONSTRAINT files_pkey PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS public.file_translations (
    id     bigint                 NOT NULL DEFAULT nextval('public.file_translations_id_seq'::regclass),
    fileid uuid                   NOT NULL,
    locale character varying(255) NOT NULL,
    title  character varying(255) DEFAULT NULL::character varying,
    alt    character varying(255) DEFAULT NULL::character varying,
    CONSTRAINT file_translations_pkey PRIMARY KEY (id)
);
ALTER SEQUENCE public.file_translations_id_seq OWNED BY public.file_translations.id;

CREATE TABLE IF NOT EXISTS public.users (
    id                    uuid                        NOT NULL,
    email                 character varying(255)      NOT NULL,
    password              character varying(255)      NOT NULL,
    name                  character varying(255)      DEFAULT NULL::character varying,
    isactive              boolean                     NOT NULL,
    accesstoken           character varying(255)      DEFAULT NULL::character varying,
    accesstokencreatedat  timestamp without time zone,
    accesstokenlastusedat timestamp without time zone,
    createdat             timestamp without time zone NOT NULL,
    createdby             uuid,
    updatedat             timestamp without time zone NOT NULL,
    updatedby             uuid,
    version               bigint                      NOT NULL,
    CONSTRAINT users_pkey PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS public.roles (
    id          uuid                        NOT NULL,
    name        character varying(255)      NOT NULL,
    issuperadmin boolean                    NOT NULL,
    description character varying(255)      DEFAULT NULL::character varying,
    createdat   timestamp without time zone NOT NULL,
    createdby   uuid,
    updatedat   timestamp without time zone NOT NULL,
    updatedby   uuid,
    version     bigint                      NOT NULL,
    CONSTRAINT roles_pkey PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS public.permissions (
    id         uuid                        NOT NULL,
    roleid     uuid                        NOT NULL,
    collection character varying(255)      NOT NULL,
    canread    boolean                     NOT NULL,
    canwrite   boolean                     NOT NULL,
    candelete  boolean                     NOT NULL,
    createdat  timestamp without time zone NOT NULL,
    createdby  uuid,
    updatedat  timestamp without time zone NOT NULL,
    updatedby  uuid,
    version    bigint                      NOT NULL,
    CONSTRAINT permissions_pkey PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS public.user_roles (
    id        uuid                        NOT NULL,
    userid    uuid                        NOT NULL,
    roleid    uuid                        NOT NULL,
    createdat timestamp without time zone NOT NULL,
    createdby uuid,
    updatedat timestamp without time zone NOT NULL,
    updatedby uuid,
    version   bigint                      NOT NULL,
    CONSTRAINT user_roles_pkey PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS public.revisions (
    id             uuid                        NOT NULL,
    collectionname character varying(255)      NOT NULL,
    itemid         character varying(255)      NOT NULL,
    revisionnumber bigint                      NOT NULL,
    operation      character varying(255)      NOT NULL,
    snapshot       text                        NOT NULL,
    createdat      timestamp without time zone NOT NULL,
    createdby      uuid,
    CONSTRAINT revisions_pkey PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS public.site_settings (
    id         uuid                     NOT NULL,
    brandname  text                     NOT NULL,
    logofileid uuid,
    updatedat  timestamp with time zone NOT NULL,
    updatedby  uuid,
    CONSTRAINT site_settings_pkey PRIMARY KEY (id)
);

-- ----------------------------------------------------------------------------------------------------
-- Unique constraints (as unique indexes — matches InitTables UniqueGroupNameList emission)
-- ----------------------------------------------------------------------------------------------------
CREATE UNIQUE INDEX IF NOT EXISTS index_file_translations_fileid_locale_unique
    ON public.file_translations USING btree (fileid, locale);
CREATE UNIQUE INDEX IF NOT EXISTS index_permissions_roleid_collection_unique
    ON public.permissions USING btree (roleid, collection);
CREATE UNIQUE INDEX IF NOT EXISTS index_revisions_collectionname_itemid_revisionnumber_unique
    ON public.revisions USING btree (collectionname, itemid, revisionnumber);
CREATE UNIQUE INDEX IF NOT EXISTS index_roles_name_unique
    ON public.roles USING btree (name);
CREATE UNIQUE INDEX IF NOT EXISTS index_user_roles_userid_roleid_unique
    ON public.user_roles USING btree (userid, roleid);
CREATE UNIQUE INDEX IF NOT EXISTS index_users_accesstoken_unique
    ON public.users USING btree (accesstoken);
CREATE UNIQUE INDEX IF NOT EXISTS index_users_email_unique
    ON public.users USING btree (email);

-- ----------------------------------------------------------------------------------------------------
-- Hot-path btree indexes
-- ----------------------------------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_file_translations_fk_locale
    ON public.file_translations USING btree (fileid, locale);
CREATE INDEX IF NOT EXISTS ix_permissions_roleid
    ON public.permissions USING btree (roleid);
CREATE INDEX IF NOT EXISTS ix_user_roles_roleid
    ON public.user_roles USING btree (roleid);
CREATE INDEX IF NOT EXISTS ix_user_roles_userid
    ON public.user_roles USING btree (userid);
