-- db/migrations/013-identity-unique-constraints.sql
-- Date: 2026-07-21
-- Author: Audit remediation (Batch 1 / B-group — DB + migrations track)
-- Ticket: docs/audit-2026-07-21-remediation-tasklist.md — DB-13 (MED)
--
-- DB-13: identity unique constraints exist ONLY as CodeFirst `UniqueGroupNameList` attributes
-- (User.cs / Role.cs / Permission.cs / UserRole.cs, all under src/Struo.Infrastructure/Identity/) —
-- migrations 001-012 never created a matching index for any of them, so a production database
-- provisioned from the reviewed migration series alone (never having run CodeFirst `InitTables`) has
-- NONE of these constraints: duplicate emails can land (e.g. an SSO JIT-provisioning bug creating two
-- accounts for the same address), the bearer-token lookup (`AccessToken`) runs both unindexed and
-- unenforced-unique, role names can collide, and the two RBAC junction/grant tables can carry duplicate
-- grants. Same class of gap as 010 (DB-4) / 011 (DB-10) — a CodeFirst-only correctness constraint with no
-- migration counterpart — closed the same way.
--
-- CODEFIRST CONVERGENCE — names differ, columns/uniqueness match. Every index below enforces the SAME
-- column set and uniqueness as the entity's `UniqueGroupNameList`, but NOT under the same index name.
-- `UniqueGroupNameList` is a grouping key, not an index name: SqlSugar's InitTables auto-names the index
-- it builds from that grouping (the same pattern already documented for the `revisions` composite — see
-- 010-revisions-unique-number.sql:54-57's `index_revisions_collectionname_itemid_revisionnumber_unique`
-- — the analogous name here would be e.g. `index_users_email_unique`), NOT the `uq_*` name this migration
-- assigns. A dev/test database that runs BOTH CodeFirst InitTables and this migration therefore ends up
-- with TWO unique indexes per constraint below — one SqlSugar-named, one `uq_*`-named — over the
-- identical columns. This is the SAME "accepted redundant dev index" decision already recorded for
-- 010/011 in db/migrations/README.md: harmless, do NOT "clean up" either one — dropping the SqlSugar one
-- just gets it recreated on the next dev restart, and dropping the `uq_*` one would leave production
-- (which never runs InitTables) unprotected. What converges — and what actually matters — is the
-- enforced constraint itself (same columns, same uniqueness), not the name:
--   users.email                      -> uq_users_email                 (User.cs:21)
--   users.accesstoken (nullable)     -> uq_users_accesstoken            (User.cs:35)
--   roles.name                       -> uq_roles_name                   (Role.cs:16)
--   permissions(roleid, collection)  -> uq_permissions_role_collection  (Permission.cs:19,23)
--   user_roles(userid, roleid)       -> uq_user_roles_user_role         (UserRole.cs:21,25)
--
-- IDENTIFIER NAMING — lowercase, unquoted, matching 009/010/011/012 (SqlSugar emits unquoted
-- identifiers; Postgres folds them to lowercase). No `lower(email)` functional index here: the CodeFirst
-- attribute sits on the raw `Email` property, so this migration replicates a plain btree UNIQUE on the
-- raw column, not a case-insensitive one. A `lower(email)` functional index for case-insensitive login
-- lookups is a separate, not-yet-decided performance item (audit finding DB-16) — out of this
-- migration's scope.
--
-- TABLE-EXISTENCE ASSUMPTION — like 001/003-007/009 before it, this migration only adds an index to a
-- table it assumes already exists; it does not create `users`/`roles`/`permissions`/`user_roles`
-- themselves (none of 001-012 do either — those tables are today created solely by CodeFirst
-- `InitTables`). Producing a reviewed, checked-in baseline schema so a migration-only bootstrap is
-- possible end-to-end is the audit's own noted mid-term follow-up, not part of DB-13.
--
-- DEDUPE FIRST, SAME TEMPLATE AS 010/011: creating a UNIQUE index fails if duplicate rows already exist.
-- Per duplicate group we keep the EARLIEST `createdat` (tie-break: smallest `id`) and delete the rest —
-- the same "authoritative = earliest capture" rule 010 uses. UNLIKE 010/011 (which dedupe technical
-- sidecar rows), a users/roles duplicate is a real business entity (a user account / a role), so deleting
-- one is not purely cosmetic. This is accepted on the same basis the audit finding frames it: identity
-- tables are expected to be empty or small at the point this migration first runs — a production database
-- being bootstrapped from the migration series, before real accounts/roles accumulate — not a
-- silent-data-loss risk applied blind to an already-populated, long-running production identity table.
-- `accesstoken` is nullable; NULL values are excluded from that dedupe scan (standard SQL: NULL <> NULL,
-- so a plain UNIQUE index already permits unlimited NULLs — no row is a "duplicate" over the common
-- no-token case).
--
-- ORPHAN NOTE: this repo currently has zero FK constraints anywhere (audit finding DB-14, accepted
-- app-only), so deleting a duplicate `users` row in step 1/2 below does not cascade — any
-- `user_roles`/`permissions` rows still referencing that deleted user's id become orphaned, FK-less rows.
-- Harmless under the same first-run/near-empty-table assumption this whole dedupe already relies on
-- (nothing reads a `user_roles`/`permissions` row for a `userid` that no longer exists in `users`), and
-- no worse than the pre-existing no-FK posture.
--
-- IDEMPOTENT (safe to re-run): the DELETEs are no-ops once no duplicates remain; CREATE UNIQUE INDEX IF
-- NOT EXISTS statements are guarded.

-- 1. users.email
DELETE FROM users u
USING users keep
WHERE u.email = keep.email
  AND (u.createdat > keep.createdat
       OR (u.createdat = keep.createdat AND u.id > keep.id));
CREATE UNIQUE INDEX IF NOT EXISTS uq_users_email ON users (email);

-- 2. users.accesstoken (nullable — NULLs never collide; only non-null values are scanned for dupes)
DELETE FROM users u
USING users keep
WHERE u.accesstoken IS NOT NULL
  AND u.accesstoken = keep.accesstoken
  AND (u.createdat > keep.createdat
       OR (u.createdat = keep.createdat AND u.id > keep.id));
CREATE UNIQUE INDEX IF NOT EXISTS uq_users_accesstoken ON users (accesstoken);

-- 3. roles.name
DELETE FROM roles r
USING roles keep
WHERE r.name = keep.name
  AND (r.createdat > keep.createdat
       OR (r.createdat = keep.createdat AND r.id > keep.id));
CREATE UNIQUE INDEX IF NOT EXISTS uq_roles_name ON roles (name);

-- 4. permissions(roleid, collection)
DELETE FROM permissions p
USING permissions keep
WHERE p.roleid     = keep.roleid
  AND p.collection = keep.collection
  AND (p.createdat > keep.createdat
       OR (p.createdat = keep.createdat AND p.id > keep.id));
CREATE UNIQUE INDEX IF NOT EXISTS uq_permissions_role_collection ON permissions (roleid, collection);

-- 5. user_roles(userid, roleid)
DELETE FROM user_roles ur
USING user_roles keep
WHERE ur.userid = keep.userid
  AND ur.roleid = keep.roleid
  AND (ur.createdat > keep.createdat
       OR (ur.createdat = keep.createdat AND ur.id > keep.id));
CREATE UNIQUE INDEX IF NOT EXISTS uq_user_roles_user_role ON user_roles (userid, roleid);
