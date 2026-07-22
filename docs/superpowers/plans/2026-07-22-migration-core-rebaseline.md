# Migration Core Rebaseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 13 sample-entangled historical migration scripts with a single reviewed **core-only bootstrap baseline** (`001-core-baseline.sql`) that lets a downstream production deploy create the entire core framework schema from migrations alone — no dev-only `InitTables` dependency — while the sample Blog schema stays out of the core migration path entirely.

**Architecture:** StruoCMS is a *reusable CMS template*: only core framework functionality ships; business collections are added by downstream forks. The schema source-of-truth is the entity classes + SqlSugar attributes (`InitTables` in dev). Production never runs `InitTables`, so it needs a reviewed SQL baseline. We generate that baseline by running `InitTables(FrameworkEntityTypes.All)` against a scratch PostgreSQL DB, `pg_dump --schema-only`, then make it idempotent and review it — guaranteeing zero drift from the entities. All sample (Blog) DDL is deleted from `db/migrations/` (dev `InitTables` still builds sample tables from sample entity attributes; downstream forks delete the sample and write their own migrations).

**Tech Stack:** .NET 10 / C# · SqlSugarCore · PostgreSQL (runtime) + SQLite (tests) · `MigrationRunner` (in-house, PG-only, no-op on SQLite) · xUnit + AwesomeAssertions.

## Global Constraints

- All DB access via SqlSugar ORM; **the sole vendor-SQL exception is versioned `db/migrations/*.sql` scripts** (this plan's product). — CLAUDE.md §17.4
- Migration scripts must be **idempotent** (`CREATE TABLE IF NOT EXISTS`, `CREATE [UNIQUE] INDEX IF NOT EXISTS`) and forward-only. — `db/migrations/README.md`
- Timestamp columns use **`timestamptz`**, never bare `timestamp`. Store UTC. — DB-7
- `InitTables` is Development-only; `MigrationRunner` is PG-only and a hard no-op on SQLite/other. — `DatabaseInitializer`, `MigrationRunner`
- Package versions are NEVER hand-authored. — CLAUDE.md §17.5
- No prod DB exists yet → migrations may be freely rebaselined (confirmed with user 2026-07-22).
- **BL-5 decision (resolved by this plan):** dev keeps `Database:MigrationsPath` empty (dev = `InitTables`); the baseline is a **production bootstrap artifact**. Recorded, no dev auto-migrate.
- **SEC-11 decision (2026-07-22):** accept current SVG mitigation (attachment disposition + `<img>`); no code change — recorded in the task list only, out of this plan's code scope.
- Process: implementation by **Sonnet 5** subagents; adversarial review by **Fable 5** subagents (per user instruction + [[workflow-model-and-cost-prefs]]). Live-PG generation/gate steps (Tasks 1, 6) are orchestrator-driven because they need iterative live-DB interaction; their SQL artifacts are still Fable-reviewed.

## Core entity set (the baseline's exact contents)

`FrameworkEntityTypes.All` (src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs) → 9 tables:

| Entity | Table |
|--------|-------|
| Localization.Language | `languages` |
| Files.File | `files` |
| Files.FileTranslation | `file_translations` |
| Identity.User | `users` |
| Identity.Role | `roles` |
| Identity.Permission | `permissions` |
| Identity.UserRole | `user_roles` |
| Revisions.Revision | `revisions` |
| Settings.SiteSettings | `site_settings` |

Core has **no M2M junctions** and **no partial soft-delete indexes** (soft-delete is on the sample `articles`/`categories` only). So a `pg_dump` of a core-only `InitTables` build is complete. The runner also creates its own `schema_migrations` tracking table at runtime (not part of the baseline).

## File structure

- **Create:** `db/migrations/001-core-baseline.sql` — the reviewed core bootstrap baseline (generated + hand-hardened).
- **Delete:** `db/migrations/001-…013-….sql` (all 13 existing scripts). — sample-entangled historical patches, superseded.
- **Modify:** `db/migrations/README.md` — rewrite for the baseline-first model.
- **Modify:** `docs/migration-strategy.md` — replace the aspirational baseline section with the realized baseline.
- **Modify:** `tests/Struo.Tests/Persistence/IndexParityTests.cs` — retarget to core entities + baseline parity; drop sample-entity coupling.
- **Create:** `tests/Struo.Tests/Persistence/CoreBaselineParityTests.cs` — assert the baseline file covers every core table.
- **Modify:** `src/Struo.Api/appsettings.json` — refresh the `MigrationsPath` comment for the baseline model.
- **Modify:** `docs/audit-2026-07-21-remediation-tasklist.md` — record BL-5 / DB-20 resolution + the rebaseline; note DB-16/17/18 reframing.

---

### Task 1: Generate & harden the core baseline SQL (orchestrator + live PG)

**Files:**
- Create: `db/migrations/001-core-baseline.sql`
- Reference: `src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs`, `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
- Temp harness: `tests/Struo.Tests/Persistence/CoreBaselineGenTests.cs` (throwaway; deleted at end of task)

**Interfaces:**
- Consumes: `FrameworkEntityTypes.All`, `SqlSugarClientFactory.Create(DatabaseOptions, ICurrentUserAccessor)`.
- Produces: `db/migrations/001-core-baseline.sql` (idempotent core DDL, reviewed).

- [ ] **Step 1: Confirm scratch PG + pg_dump available**

Run:
```bash
psql --version && pg_dump --version
psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS struo_baseline_gen; CREATE DATABASE struo_baseline_gen;"
```
Expected: versions print; database recreated. (Use the local dev PG credentials; adjust host/user as the dev environment requires.)

- [ ] **Step 2: Write a throwaway generation test that builds the core-only schema on the scratch PG**

Create `tests/Struo.Tests/Persistence/CoreBaselineGenTests.cs`:
```csharp
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

// THROWAWAY — deleted at the end of the rebaseline (Task 1 Step 6). Builds the core-only schema
// on a scratch PG so pg_dump can capture the reviewed baseline. Skipped unless the env var is set.
public sealed class CoreBaselineGenTests
{
    [Fact]
    public void Build_core_only_schema_on_scratch_pg()
    {
        var conn = Environment.GetEnvironmentVariable("STRUO_BASELINE_GEN_PG");
        if (string.IsNullOrWhiteSpace(conn)) return; // no-op unless explicitly generating

        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = conn },
            new TestCurrentUserAccessor(Guid.Empty));

        client.CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());
    }
}
```

- [ ] **Step 3: Run the generator against the scratch PG**

Run:
```bash
STRUO_BASELINE_GEN_PG="Host=localhost;Port=5432;Database=struo_baseline_gen;Username=postgres;Password=postgres" \
  dotnet test tests/Struo.Tests --filter FullyQualifiedName~CoreBaselineGenTests
```
Expected: PASS. Then verify exactly the 9 core tables exist:
```bash
psql -h localhost -U postgres -d struo_baseline_gen -c "\dt"
```
Expected: `languages, files, file_translations, users, roles, permissions, user_roles, revisions, site_settings` (and nothing sample-related).

- [ ] **Step 4: Dump the schema**

Run:
```bash
pg_dump -h localhost -U postgres -d struo_baseline_gen \
  --schema-only --no-owner --no-privileges --no-comments \
  > /tmp/core-baseline.raw.sql
```
Expected: file contains `CREATE TABLE`/`CREATE INDEX`/`CREATE UNIQUE INDEX` for the 9 tables, `timestamptz` for temporal columns, `uuid` PKs.

- [ ] **Step 5: Harden into the reviewed baseline**

Produce `db/migrations/001-core-baseline.sql` from the raw dump:
- Strip pg_dump preamble noise (`SET`, `SELECT pg_catalog.set_config`, `\connect`, `ALTER TABLE … OWNER`).
- `CREATE TABLE` → `CREATE TABLE IF NOT EXISTS`.
- `CREATE INDEX` → `CREATE INDEX IF NOT EXISTS`; `CREATE UNIQUE INDEX` → `CREATE UNIQUE INDEX IF NOT EXISTS`.
- Keep inline `PRIMARY KEY`/`NOT NULL`/`DEFAULT` exactly as dumped.
- Prepend the standard header:
```sql
-- db/migrations/001-core-baseline.sql
-- Date: 2026-07-22
-- Author: Audit 2026-07-21 Batch 5 — migration core rebaseline
-- Ticket: docs/audit-2026-07-21-remediation-tasklist.md — BL-5 / rebaseline
--
-- CORE BOOTSTRAP BASELINE. Creates the complete core-framework schema (the 9 FrameworkEntityTypes
-- tables: languages/files/file_translations/users/roles/permissions/user_roles/revisions/site_settings)
-- plus their PKs, unique constraints, and indexes — machine-generated from InitTables(FrameworkEntityTypes.All)
-- via pg_dump, then made idempotent. This is the PRODUCTION bootstrap path: a downstream deploy applies
-- it to an EMPTY database via MigrationRunner (Database:MigrationsPath) with NO dependency on the
-- Development-only InitTables. Idempotent (IF NOT EXISTS throughout) so it is a safe no-op in dev, where
-- InitTables has already created these tables, and safe to re-run.
--
-- Sample/business schema (Blog: articles/tags/categories/…) is intentionally NOT here — the sample is a
-- demo built by InitTables in dev; a downstream fork deletes the sample and adds its own NNN-… migrations.
```
Save it, then re-verify idempotency against a *second* fresh DB:
```bash
psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS struo_baseline_verify; CREATE DATABASE struo_baseline_verify;"
psql -h localhost -U postgres -d struo_baseline_verify -f db/migrations/001-core-baseline.sql
psql -h localhost -U postgres -d struo_baseline_verify -f db/migrations/001-core-baseline.sql   # 2nd run
psql -h localhost -U postgres -d struo_baseline_verify -c "\dt"
```
Expected: both runs succeed with no error; `\dt` lists exactly the 9 core tables. (Full app smoke is Task 6.)

- [ ] **Step 6: Delete the throwaway generator and commit the baseline**

```bash
rm tests/Struo.Tests/Persistence/CoreBaselineGenTests.cs
git add db/migrations/001-core-baseline.sql
git commit -m "feat(migrations): add core bootstrap baseline (rebaseline, prod self-bootstrap)"
```
> NOTE: This commit still coexists with the old 001-013 files (same numeric prefix `001-`). That is fine within one commit; Task 2 removes the old files in the very next commit. The old and new `001-` names differ in suffix so the filesystem holds both.

---

### Task 2: Delete the 13 sample-entangled historical migrations

**Files:**
- Delete: `db/migrations/001-widen-content-bearing-text-columns.sql` … `013-identity-unique-constraints.sql` (all 13 originals).

- [ ] **Step 1: Confirm nothing in source code references the old filenames**

Run:
```bash
grep -rn "widen-content-bearing\|retroactive-add-version\|article-multivalue\|article-structured\|article-files-column\|article-repeater\|soft-delete-columns\|revisions-table\|hot-path-indexes\|revisions-unique-number\|translation-unique-locale\|site-settings-table\|identity-unique-constraints" --include=*.cs --include=*.md . | grep -v "docs/superpowers/plans/2026-07-22-migration-core-rebaseline.md"
```
Expected: only doc references (README.md, migration-strategy.md, ROADMAP.md, prior plans) — no `.cs` runtime/test coupling (MigrationRunnerTests uses invented filenames like `001-a.sql`). Record any hits for Tasks 3/4.

- [ ] **Step 2: Remove the old files**

```bash
git rm db/migrations/001-widen-content-bearing-text-columns.sql \
       db/migrations/002-retroactive-add-version-columns.sql \
       db/migrations/003-article-multivalue-columns.sql \
       db/migrations/004-article-structured-columns.sql \
       db/migrations/005-article-files-column.sql \
       db/migrations/006-article-repeater-column.sql \
       db/migrations/007-soft-delete-columns.sql \
       db/migrations/008-revisions-table.sql \
       db/migrations/009-hot-path-indexes.sql \
       db/migrations/010-revisions-unique-number.sql \
       db/migrations/011-translation-unique-locale.sql \
       db/migrations/012-site-settings-table.sql \
       db/migrations/013-identity-unique-constraints.sql
ls db/migrations/
```
Expected: only `001-core-baseline.sql` and `README.md` remain.

- [ ] **Step 3: Verify backend build + tests still green (SQLite path unchanged)**

```bash
dotnet test tests/Struo.Tests --filter FullyQualifiedName~MigrationRunnerTests
```
Expected: PASS (runner tests use invented filenames, independent of the folder).

- [ ] **Step 4: Commit**

```bash
git add -A db/migrations/
git commit -m "refactor(migrations): remove sample-entangled historical scripts (superseded by core baseline)"
```

---

### Task 3: Retarget IndexParityTests to core + add baseline coverage test

**Files:**
- Modify: `tests/Struo.Tests/Persistence/IndexParityTests.cs`
- Create: `tests/Struo.Tests/Persistence/CoreBaselineParityTests.cs`

**Interfaces:**
- Consumes: `FrameworkEntityTypes.All`, `SqlSugarClientFactory.Create`, `SqliteTestDatabase`.
- Produces: two passing test classes proving (a) core entity `[SugarIndex]` attributes still emit their indexes under `InitTables`, and (b) `001-core-baseline.sql` mentions every core table.

- [ ] **Step 1: Rewrite IndexParityTests to core-only entities**

Replace the file body so `IndexedEntities` and `MappedIndexNames` use only core entities/indexes. The old test coupled to sample `Article`/`Category`/`ArticleTag`/`ArticleTranslation` and their `ix_articles_*`/`ix_article_tags_*`/`ix_article_translations_*` names — those move out with the sample. Keep the core-mapped indexes:
```csharp
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Core index parity: the plain btree indexes declared as [SugarIndex] on core framework entities are
/// emitted by CodeFirst (InitTables) in dev/test AND captured in db/migrations/001-core-baseline.sql for
/// production. Sample (Blog) index parity is the sample's own concern and no longer asserted here.
/// </summary>
public sealed class IndexParityTests
{
    private static (SqliteTestDatabase, ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        return (db, client);
    }

    private static readonly Type[] CoreIndexedEntities =
    [
        typeof(FileTranslation), typeof(UserRole), typeof(Permission),
    ];

    public static TheoryData<string> CoreMappedIndexNames() =>
    [
        "ix_file_translations_fk_locale",
        "ix_user_roles_userid",
        "ix_user_roles_roleid",
        "ix_permissions_roleid",
    ];

    private static List<string> IndexNames(ISqlSugarClient client) =>
        client.Ado.SqlQuery<string>("SELECT name FROM sqlite_master WHERE type='index'");

    [Theory]
    [MemberData(nameof(CoreMappedIndexNames))]
    public void InitTables_emits_each_core_mapped_index(string indexName)
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(CoreIndexedEntities);
            IndexNames(client).Should().Contain(indexName);
        }
    }

    [Fact]
    public void InitTables_is_idempotent_for_core_indexes()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(CoreIndexedEntities);
            var act = () => client.CodeFirst.InitTables(CoreIndexedEntities);
            act.Should().NotThrow();
            IndexNames(client).Should().Contain("ix_user_roles_userid");
        }
    }
}
```
> VERIFY DURING IMPLEMENTATION: confirm the exact `[SugarIndex]` names on `FileTranslation`/`UserRole`/`Permission` match the four names above (grep `SugarIndex` in those files). If a name differs, use the actual attribute name — the test must reflect reality, never be weakened to pass.

- [ ] **Step 2: Run it, expect PASS**

```bash
dotnet test tests/Struo.Tests --filter FullyQualifiedName~IndexParityTests
```
Expected: PASS. If a name mismatch fails it, fix the test to the real attribute name (Step 1 note), not the assertion strength.

- [ ] **Step 3: Write CoreBaselineParityTests — the baseline covers every core table**

Create `tests/Struo.Tests/Persistence/CoreBaselineParityTests.cs`:
```csharp
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Guards the rebaseline invariant: db/migrations/001-core-baseline.sql must create a table for every
/// core FrameworkEntityTypes table. Catches an entity added to the framework without a matching baseline
/// update (the prod-bootstrap path would otherwise silently miss it).
/// </summary>
public sealed class CoreBaselineParityTests
{
    private static readonly string[] CoreTables =
    [
        "languages", "files", "file_translations", "users", "roles",
        "permissions", "user_roles", "revisions", "site_settings",
    ];

    private static string BaselinePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "migrations")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repo's db/migrations directory must be locatable from the test host");
        return Path.Combine(dir!.FullName, "db", "migrations", "001-core-baseline.sql");
    }

    [Fact]
    public void Baseline_creates_every_core_table()
    {
        var sql = File.ReadAllText(BaselinePath());
        foreach (var table in CoreTables)
            sql.Should().Contain($"CREATE TABLE IF NOT EXISTS public.{table}",
                $"the core baseline must create the '{table}' table");
    }
}
```
> VERIFY: the pg_dump may qualify tables as `public.<table>` or bare `<table>`. Match Task 1's actual output — adjust the asserted substring to what the hardened baseline literally contains. Never relax to a substring so loose it can't fail.

- [ ] **Step 4: Run it, expect PASS**

```bash
dotnet test tests/Struo.Tests --filter FullyQualifiedName~CoreBaselineParityTests
```
Expected: PASS.

- [ ] **Step 5: Full backend suite green**

```bash
dotnet test
```
Expected: all green (818-baseline minus any sample-index theory cases removed; net count noted in the run).

- [ ] **Step 6: Commit**

```bash
git add tests/Struo.Tests/Persistence/IndexParityTests.cs tests/Struo.Tests/Persistence/CoreBaselineParityTests.cs
git commit -m "test(migrations): retarget index parity to core + assert baseline table coverage"
```

---

### Task 4: Rewrite migration docs for the baseline-first model

**Files:**
- Modify: `db/migrations/README.md`
- Modify: `docs/migration-strategy.md`

- [ ] **Step 1: Rewrite `db/migrations/README.md`**

Replace the "single contiguous series 001–013 / renumber mapping / 010 hazard" content with the baseline-first model. Required points (write as prose, no placeholders):
- The folder now holds **`001-core-baseline.sql`** = the complete core-framework schema, machine-generated from `InitTables(FrameworkEntityTypes.All)` via `pg_dump`, hardened to idempotent.
- **Dev:** `InitTables` builds core + sample from entity classes; `Database:MigrationsPath` stays **empty** (BL-5). The baseline is a no-op here.
- **Production:** apply `001-core-baseline.sql` to an empty DB via the runner (set `MigrationsPath`) — no `InitTables` needed. This is the self-bootstrap path.
- **Test:** SQLite → runner is a hard no-op.
- **Downstream forks:** delete the sample project, then add their own `NNN-…` scripts for their own collections; the baseline gives them the core starting point.
- **Numbering:** next script is `002-…`; keep the header convention (date/author/ticket/intent), idempotent, forward-only, `timestamptz` for temporal columns.
- Drop the obsolete DB-6 renumber-mapping table and the `010` InitTables-ordering-hazard section (the sample migrations that caused it are gone; the redundant-dev-index wart is eliminated because dev and prod now share InitTables/dump index names).

- [ ] **Step 2: Update `docs/migration-strategy.md`**

Change the "Baseline" section from aspirational to realized: `001-core-baseline.sql` now exists and reproduces the **core** `InitTables` output (not the whole app). Update the folder-convention example to the single-series `NNN-` names actually used, and note the sample/business split (business migrations live downstream, not in this template's core folder). Keep the "manual, versioned, reviewed" decision and the DbUp graduation note intact.

- [ ] **Step 3: Commit**

```bash
git add db/migrations/README.md docs/migration-strategy.md
git commit -m "docs(migrations): rewrite for core-baseline-first model"
```

---

### Task 5: Refresh appsettings comment + resolve BL-5 in config docs

**Files:**
- Modify: `src/Struo.Api/appsettings.json` (the `// MigrationsPath` comment)

- [ ] **Step 1: Update the MigrationsPath comment**

Change the `"// MigrationsPath"` comment value to describe the baseline model precisely:
```json
"// MigrationsPath": "Directory of reviewed *.sql migrations (PostgreSQL only). Empty/absent = disabled — the default. DEV keeps this empty: InitTables builds the schema. PRODUCTION sets it to the deployed migrations dir so 001-core-baseline.sql bootstraps the core schema on an empty DB (no InitTables). See db/migrations/README.md.",
"MigrationsPath": ""
```
Keep `"MigrationsPath": ""` unchanged (dev stays on InitTables — BL-5).

- [ ] **Step 2: Verify the JSON parses (build)**

```bash
dotnet build src/Struo.Api
```
Expected: build succeeds (comment is a sibling JSON key, already the established pattern in this file).

- [ ] **Step 3: Commit**

```bash
git add src/Struo.Api/appsettings.json
git commit -m "docs(config): clarify MigrationsPath is prod-only baseline bootstrap (BL-5)"
```

---

### Task 6: Live PG gate — prove the baseline self-bootstraps core to an empty prod DB

**Files:** none (verification only).

- [ ] **Step 1: Create a fresh empty DB and apply ONLY the baseline (no InitTables)**

```bash
psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS struo_prod_sim; CREATE DATABASE struo_prod_sim;"
psql -h localhost -U postgres -d struo_prod_sim -f db/migrations/001-core-baseline.sql
psql -h localhost -U postgres -d struo_prod_sim -c "\dt"
psql -h localhost -U postgres -d struo_prod_sim -c "\di"
```
Expected: 9 core tables present; unique indexes present for `users.email`, `users.accesstoken`, `roles.name`, `permissions(roleid,collection)`, `user_roles(userid,roleid)`, `revisions(collectionname,itemid,revisionnumber)`, translation `(fk,locale)` pairs.

- [ ] **Step 2: Boot the API in a PRODUCTION-like mode against that DB with the runner on and InitTables off**

Run the API with `ASPNETCORE_ENVIRONMENT=Production`, `Database:ConnectionString` → `struo_prod_sim`, `Database:MigrationsPath=db/migrations`, launched from repo root, `RateLimiting__Login__Enabled=false`, a `BootstrapAdmin` set. (Production disables `InitTables` by design — this proves the baseline alone suffices.) Confirm startup succeeds (no `SchemaGuard`/Npgsql error) and `schema_migrations` now records `001-core-baseline.sql`.

- [ ] **Step 3: Core smoke against the baseline-only schema**

Exercise the core paths (via HTTP or a scripted client): admin login → `/me`; create a `user`; upload a `file` + serve `/api/files/{id}/content`; create an item that captures a `revision`; `PUT /api/settings/branding` (site_settings upsert); list `languages`. Each must succeed with no schema error.
Expected: all green — this is the proof that a downstream prod can run on migrations alone. Capture evidence (status codes + a row count or two) for the task-list record.

- [ ] **Step 4: Idempotent re-run**

Restart the API (runner re-runs) → `001-core-baseline.sql` is skipped (already in `schema_migrations`); no error. Optionally re-apply the file by hand (Task 1 Step 5 already proved raw idempotency).
Expected: clean restart, no duplicate-object errors.

---

### Task 7: Record decisions & rebaseline in the audit task list

**Files:**
- Modify: `docs/audit-2026-07-21-remediation-tasklist.md`

- [ ] **Step 1: Update Batch 5 + decisions**

- Mark **BL-5** resolved: rebaselined to a core-only bootstrap baseline; dev stays InitTables (MigrationsPath empty); baseline = prod bootstrap. Cross-reference this plan.
- Mark **SEC-11** resolved: accept current SVG mitigation, no code change (record rationale).
- Mark **DB-20** resolved/absorbed: README + migration-strategy rewritten; old renumber/hazard drift removed.
- Note **DB-16 / DB-17 / DB-18** reframing: DB-16 core index recommendations (lower(email) functional, files.contenttype text_pattern_ops, partial live indexes) are now decisions for a follow-up `002-…` migration or entity attributes — NOT auto-carried; DB-17/DB-18 (projection-only fetches) remain code-level LOWs unaffected by the rebaseline.
- Add a "Batch 5 — migration rebaseline" execution record (commits, live PG gate evidence).

- [ ] **Step 2: Commit**

```bash
git add docs/audit-2026-07-21-remediation-tasklist.md
git commit -m "docs(audit): record migration rebaseline + BL-5/SEC-11/DB-20 resolutions"
```

---

## Self-Review

**Spec coverage:** core-only baseline (Task 1) ✓; sample DDL removed (Task 2) ✓; tests retargeted + baseline parity guard (Task 3) ✓; docs rewritten (Tasks 4–5) ✓; self-bootstrap proven on live PG (Task 6) ✓; decisions recorded (Task 7) ✓. BL-5 resolved (Global Constraints + Tasks 5/7); SEC-11 recorded (Task 7).

**Placeholder scan:** generated-artifact steps (Task 1 Step 5, Task 6) are procedures with exact commands + review criteria, not "TODO" — acceptable for a machine-generated file whose bytes can't be pre-written. Test code blocks (Task 3) carry explicit "verify the real attribute/table name; never weaken the assertion" notes.

**Type/name consistency:** `FrameworkEntityTypes.All`, `SqlSugarClientFactory.Create`, `SqliteTestDatabase`, `TestCurrentUserAccessor`, `DatabaseOptions`/`StruoDbType` all used as they exist in the codebase; the 9 core table names are consistent across Tasks 1/3/6.

## Out of scope (remaining Batch 5 LOW — separate follow-up)

This plan is the migration-architecture slice only. The rest of Batch 5 LOW — BL-2, BL-4, SEC-9, SEC-12, AUTH-2, DB-16/17/18/19/21, FE-20/21/22/23/24/25/26/27/28/29/30, TEST-7/8/9, ARC-8 (deferred) — is a separate plan to run after this lands.
