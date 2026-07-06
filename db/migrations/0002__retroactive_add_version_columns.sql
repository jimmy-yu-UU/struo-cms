-- 0002__retroactive_add_version_columns.sql
-- Date: 2026-07-06
-- Author: Claude (phase7g live-gate remediation, documenting prior audit finding D2)
-- Ticket: architecture-audit-2026-07-06, finding D2 (optimistic-concurrency `version` column)
--
-- Intent: an earlier architecture-audit-remediation merge (56e9a70) added `AuditableEntity.Version`
-- (long, D2 optimistic-concurrency compare-and-swap column) to every entity deriving from
-- AuditableEntity. CodeFirst's plain `ADD COLUMN version bigint NOT NULL` (no default) fails on
-- PostgreSQL against a non-empty table (23502: contains null values), so any environment with
-- pre-existing rows in these tables needs a manual backfill-safe ALTER.
--
-- NOTE: this script is RETROACTIVE documentation for OTHER environments. The `web-struo-cms-db`
-- dev database already had these ALTERs applied by hand during the phase7g live gate on
-- 2026-07-06 (see .superpowers/sdd/live-gate-7g/EVIDENCE.md) — do not re-run against a database
-- where these columns already exist without the IF NOT EXISTS guard below (it is safe to re-run;
-- IF NOT EXISTS makes it a no-op there).
--
-- Affected tables (all AuditableEntity-backed collections as of 2026-07-06):
--   articles, tags, categories, files, users, roles, permissions, user_roles

ALTER TABLE articles     ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE tags         ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE categories   ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE files        ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE users        ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE roles        ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE permissions  ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
ALTER TABLE user_roles   ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 0;
