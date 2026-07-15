-- Phase Audit-Batch1 (Task 6) — DB-3: hot-path indexes for FKs, translation keys, soft-delete flag.
--
-- AUDIT REFERENCE: docs/architecture-audit-2026-07-15.md, finding DB-3 (HIGH). As of that audit the
-- entire schema has exactly one index (`ix_revisions_item`, see 006). Every M2O FK, M2M junction FK,
-- translation `(fk,locale)` lookup key, and the `deletedat` soft-delete flag is seq-scanned on every
-- hot read path:
--   * article_translations(articleid, locale) — overlay read / translatable sort subquery /
--     translatable search (the latter is O(rows x full scan) without this).
--   * article_tags(articleid) / (tagid)       — M2M expansion (RelationExpander) + sync-on-write.
--   * articles.categoryid                     — M2O relation read (Category dropdown) + Restrict
--     on-delete check.
--   * categories.parentid                     — self-referencing M2O (TreeSelect) + RelatedList.
--   * file_translations(fileid, locale)       — same overlay/read pattern as article_translations.
--   * user_roles(userid) / (roleid), permissions(roleid) — RBAC effective-permission resolution,
--     evaluated on every authenticated request.
--   * deletedat                                — every list/get on a soft-deletable collection
--     carries an implicit `WHERE deletedat IS NULL` (the SqlSugar global query filter, Phase 9b).
--
-- SCHEMA SURFACE VERIFIED AGAINST (2026-07-15, current entities — not stale docs):
--   samples/Struo.Sample.Blog/{Article,Category,Tag,ArticleTag,ArticleTranslation,FaqItem}.cs
--   src/Struo.Infrastructure/Identity/{User,Role,UserRole,Permission}.cs
--   src/Struo.Infrastructure/Files/{File,FileTranslation}.cs
--   src/Struo.Infrastructure/Localization/Language.cs
-- No [SugarColumn(ColumnName=...)] overrides exist anywhere in the above (verified by grep), so every
-- column below is the plain lowercase fold of the CLR property name — no snake_case, no corrections
-- needed against the task-brief template. FaqItem is a nested Repeater POCO (stored as JSON on
-- articles.faqs), not its own table — no index applies. Tag/Role/Permission/Language/File/User carry
-- no FK columns of their own and (Tag/Role/Permission/Language/File/User) do not implement
-- ISoftDeletable, so none of them need entries here beyond what's listed.
--
-- IDENTIFIER NAMING — lowercase, unquoted (SqlSugar emits unquoted identifiers; Postgres folds to
-- lowercase; matches the convention documented in 007/008's headers). Index names follow
-- `ix_<table>_<cols>`.
--
-- SOFT-DELETE SCOPE: only `Article` and `Category` implement `ISoftDeletable` as of this audit (see
-- 007-soft-delete-columns.sql header) — partial indexes below cover exactly those two tables. If a
-- future collection opts into ISoftDeletable, add `ix_<table>_live` for it here.
--
-- OUT OF SCOPE (considered, deliberately excluded):
--   * users.email — has a CodeFirst UniqueGroupNameList unique constraint (uq_users_email), and the
--     login lookup path (SqlSugarExternalUserStore/SqlSugarUserCredentialStore) queries
--     `WHERE lower(email) = @lowered`, which a plain btree index on `email` would not serve anyway
--     (would need a functional/expression index). Not called out by audit finding DB-3 (which scopes
--     to FK/translation-key/soft-delete-flag indexes only) — left for a dedicated identity-perf pass.
--   * articles.heroimageid — a File-picker scalar (FieldInterface.Image), not a [CmsRelation]/[Navigate]
--     relation; never traversed by RelationExpander/RelationFilterResolver, so it is not a join/filter
--     hot path today.
--
-- IDEMPOTENT (safe to re-run; `CREATE INDEX IF NOT EXISTS`).
--
-- CODEFIRST PARITY (DB-5, batch 2): the nine PLAIN btree indexes below now also exist as `[SugarIndex]`
-- attributes on their owning entities (Article/Category/ArticleTag/ArticleTranslation → samples,
-- FileTranslation/UserRole/Permission → Infrastructure), so a CodeFirst dev/test database (`InitTables`)
-- gets the SAME indexes with the IDENTICAL names used here. Names match exactly, so this migration's
-- `CREATE INDEX IF NOT EXISTS` overlaps idempotently with the CodeFirst emission on a live PG database
-- (whichever ran first wins; the other is a no-op). A dev fail-fast (`SchemaGuard`) asserts the
-- correctness-critical constraints at startup, but NOT these performance indexes (their absence is a
-- latency regression, not a correctness gap — out of the guard's scope by design).
--
-- KNOWN ASYMMETRY — the two PARTIAL indexes (`ix_articles_live` / `ix_categories_live`, below, with
-- `WHERE deletedat IS NULL`) CANNOT be expressed via `[SugarIndex]` (SqlSugar attributes have no filter
-- clause). They stay MIGRATION-ONLY: they exist on live PG via this file, and are simply absent on a
-- CodeFirst dev/test database. This is deliberate and is the one place CodeFirst and this migration
-- diverge.

-- ---------------------------------------------------------------------------------------------
-- M2O foreign keys
-- ---------------------------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_articles_categoryid        ON articles (categoryid);
CREATE INDEX IF NOT EXISTS ix_categories_parentid        ON categories (parentid);

-- ---------------------------------------------------------------------------------------------
-- M2M junction FKs (both directions — Article<->Tag via article_tags)
-- ---------------------------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_article_tags_articleid     ON article_tags (articleid);
CREATE INDEX IF NOT EXISTS ix_article_tags_tagid         ON article_tags (tagid);

-- ---------------------------------------------------------------------------------------------
-- Translation lookup keys — (parent fk, locale), used by overlay read / translatable sort
-- subquery / translatable search
-- ---------------------------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_article_translations_fk_locale ON article_translations (articleid, locale);
CREATE INDEX IF NOT EXISTS ix_file_translations_fk_locale    ON file_translations (fileid, locale);

-- ---------------------------------------------------------------------------------------------
-- Identity / RBAC hot paths — effective-permission resolution on every authenticated request
-- ---------------------------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_user_roles_userid          ON user_roles (userid);
CREATE INDEX IF NOT EXISTS ix_user_roles_roleid          ON user_roles (roleid);
CREATE INDEX IF NOT EXISTS ix_permissions_roleid         ON permissions (roleid);

-- ---------------------------------------------------------------------------------------------
-- Soft-delete floor — every list/get on a soft-deletable collection carries an implicit
-- `WHERE deletedat IS NULL` (SqlSugar global query filter, Phase 9b). Partial index keeps it
-- cheap and small (only covers live rows).
-- ---------------------------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_articles_live   ON articles (id)   WHERE deletedat IS NULL;
CREATE INDEX IF NOT EXISTS ix_categories_live ON categories (id) WHERE deletedat IS NULL;
