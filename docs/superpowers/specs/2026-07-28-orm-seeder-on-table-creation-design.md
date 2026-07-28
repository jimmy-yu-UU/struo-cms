# Design — ORM Seeder coupled to table creation (all environments)

**Date:** 2026-07-28
**Status:** Approved for planning
**Topic:** Make initial-data seeding an ORM-Seeder concern that runs in every environment, triggered strictly by table creation.

---

## 0. Problem / Motivation

The stated concern was "startup runs SQL scripts to insert data; that should be an ORM Seeder that only runs at the initialization phase." Investigation showed the premise is inverted:

- **Schema (DDL)** already comes from reviewed `db/migrations/*.sql` via `MigrationRunner` — verified **pure DDL, zero `INSERT`** (`001`–`003`).
- **Seed data** already comes from **ORM seeders** (`LanguageSeeder`, `AdminUserSeeder`, `RbacSeeder`).

The real gap: those seeders are wrapped in `if (app.Environment.IsDevelopment())` in `Program.cs`, so **production never seeds initial data**. A fresh production database therefore has schema but **no languages, no admin user, no admin/public roles → nobody can log in**.

Additionally, the existing seeder guards key off **row emptiness** (`if (AnyAsync()) return;`). Under that rule, if an operator empties a table the seeder would **re-insert** on next startup — which contradicts the desired "trust existing tables, keep prod stable" behavior.

## 1. Goal

Initial-data seeding is an ORM concern that runs in **all environments**, but a seeder fires **only when its target table is created during this startup**. Once a table exists, its data is trusted and never re-seeded.

## 2. Core Principle (authoritative)

> **A seeder is triggered only at the moment its table is created.** No table-creation action ⇒ no seeder. If the corresponding table already exists in the DB (regardless of row contents), its seeder does **not** run, and the existing data is assumed correct — this preserves production stability.

Table creation happens via:
- **Development:** SqlSugar `CodeFirst.InitTables` (creates missing tables; additively adds missing columns to existing tables).
- **Production / all envs:** `MigrationRunner` applying reviewed `*.sql` (e.g. `001-core-baseline.sql` creates all core tables on a fresh DB).

## 3. Design

### 3.1 Snapshot-before / seed-after mechanism

In `Program.cs`, inside the existing startup scope:

1. **Before any schema step**, snapshot the set of existing table names into `existingBefore` via `db.DbMaintenance.GetTableInfoList(false)` (no cache), lower-cased for case-insensitive comparison.
2. Run schema creation as today: dev `InitTables`, then `MigrationRunner.ApplyAsync` (all envs), then dev-only `SchemaGuard`.
3. **After** schema creation, call the new unified `DataSeeder.SeedAsync(...)` in **all environments** (remove the `if (IsDevelopment())` gate around seeding).

### 3.2 `DataSeeder` orchestrator (new)

New file `src/Struo.Infrastructure/Persistence/DataSeeder.cs`. Single entry point that owns seeding order and the table-creation gate:

```
public static async Task SeedAsync(
    ISqlSugarClient db,
    ISet<string> existingBefore,   // table names present BEFORE schema step (lower-cased)
    IConfiguration config,
    IPasswordHasher hasher,
    IHostEnvironment env,
    ILogger logger,
    CancellationToken ct = default)
```

Each seeder declares its **trigger table**. The orchestrator runs a seeder only when its trigger table was **just created** this run, i.e. `!existingBefore.Contains(triggerTable)`:

| Seeder | Trigger table | Notes |
|--------|---------------|-------|
| `LanguageSeeder` | `languages` | seeds `en` (default) + `zh-TW` |
| `AdminUserSeeder` | `users` | seeds bootstrap admin from config |
| `RbacSeeder` | `roles` | seeds `admin`/`public` roles, public read grants, and binds bootstrap admin → `admin` role (only if the admin user exists) |

Trigger-table names are matched against the actual DB table names (SqlSugar entity mapping is lower-case; `languages`, `users`, `roles`, `permissions`, `user_roles`). The orchestrator logs, per seeder, whether it fired or was skipped (and why: "table pre-existed").

Ordering stays `Language → AdminUser → Rbac` so RBAC can bind the just-seeded admin.

### 3.3 Inner guards (belt-and-suspenders)

The existing `if (AnyAsync()) return;` guards inside each seeder are **kept**. Under the new model the orchestrator's table-creation gate is the primary control; the row guards are a secondary safety net (e.g. two concurrent hosts racing on a fresh DB). They no longer drive the "should I seed" decision.

### 3.4 Bootstrap admin — default credentials in config

- `appsettings.json` (committed) ships a **default bootstrap admin** so a fresh install always has a login:
  - `Auth:BootstrapAdmin:Email` = `admin@admin.com`
  - `Auth:BootstrapAdmin:Password` = `admin`
  - Rationale: email login likely enforces email format, so the account uses `admin@admin.com`; password is `admin`. Operators override via env (`Auth__BootstrapAdmin__Email`, `Auth__BootstrapAdmin__Password`) or `appsettings.Production.json`.
- Because a default always exists, the "missing credentials → lockout" edge is eliminated: when `users` is first created, an admin is always seeded.
- **Default-password safety net:** at startup, if `env.IsProduction()` **and** the effective bootstrap password still equals the shipped default `admin`, log a prominent `WARNING` ("Bootstrap admin is using the default password 'admin'; change it immediately."). **Non-blocking** — startup proceeds. This check runs regardless of whether seeding fired this run, so it also nags existing prod installs left on the default.

### 3.5 `Program.cs` changes summary

- Add `existingBefore` snapshot before the schema steps.
- Keep dev `InitTables`, all-env `MigrationRunner`, dev `SchemaGuard` as-is.
- Replace the three inline seeder calls (currently dev-only) with a single all-env `DataSeeder.SeedAsync(...)` call passing `existingBefore`.
- Add the Production default-password warning (may live inside `DataSeeder` or `AdminUserSeeder`; place in `DataSeeder` so `Program.cs` stays thin).

### 3.6 Doc-comment correction (no behavior change)

`Program.cs` line ~171 and `DatabaseInitializer` say "InitTables can only add tables". SqlSugar `InitTables` is additive for **columns** too. Tighten the comment to "creates missing tables and additively adds missing columns; it does not perform destructive schema changes." Comment-only.

## 4. Non-Goals (YAGNI)

- **No changes to the SQL migrations** — they remain pure DDL.
- **No CodeFirst/`InitTables` in production** — prod schema stays migration-driven.
- **No forced destructive schema sync in dev.** SqlSugar cannot safely drop/rename/retype; forcing "full sync" is a false promise and would hide required prod migrations, causing dev/prod drift. Dev keeps `InitTables` additive behavior; structural changes require a reviewed migration, with `SchemaGuard` catching drift.
- **No seeding on/off config flag** — seeding is always attempted; the table-creation gate is the control.
- **No change to default seed languages** (`en` + `zh-TW`); downstream forks adjust.
- **No forced-password-change-on-first-login flow** — a startup WARNING is the chosen safety mechanism.

## 5. Testing

Unit (SQLite, xUnit) + one live PostgreSQL pass.

**Unit / integration:**
1. **Fires on creation:** fresh DB (trigger table absent from `existingBefore`) → seeder inserts expected rows.
2. **Skips when pre-existing (key regression):** trigger table present in `existingBefore` → seeder does **not** run, **even when the table was manually emptied** (proves the gate is table-existence, not row-emptiness).
3. **Idempotent across restarts:** run `SeedAsync` twice; second run (tables now pre-exist) inserts nothing → no duplicates.
4. **Seeds run outside Development:** invoke seeding path with a Production `IHostEnvironment` and confirm rows are seeded (proves the dev-only gate is gone).
5. **Bootstrap admin default:** with default config, fresh `users` → admin `admin@admin.com` seeded and bound to `admin` role.
6. **Default-password warning:** Production env + default password → WARNING logged; non-default password → no warning; startup never throws in either case.
7. **RBAC binding:** admin user present → user-role row created; public read grants created for configured collections.

**Live PostgreSQL (Production-like):** point `Database:MigrationsPath` at `db/migrations`, run against a disposable fresh DB with `ASPNETCORE_ENVIRONMENT=Production`:
- Schema built from `001-core-baseline.sql`; `languages`, `roles`, `permissions`, `user_roles`, admin `users` row all populated by seeders.
- Restart against the now-populated DB → no re-seeding, no duplicate rows, no errors.

## 6. Files touched

- **New:** `src/Struo.Infrastructure/Persistence/DataSeeder.cs` (orchestrator + trigger-table gate + prod default-password warning).
- **Edit:** `src/Struo.Api/Program.cs` (snapshot `existingBefore`; replace dev-only inline seeders with all-env `DataSeeder.SeedAsync`; tighten InitTables comment).
- **Edit:** `src/Struo.Api/appsettings.json` (default `BootstrapAdmin` = `admin@admin.com` / `admin`; doc comment noting env override + prod-change guidance).
- **Edit (comment only):** `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs` (InitTables capability wording).
- **New tests:** `tests/Struo.Tests/Persistence/DataSeederTests.cs` (cases 1–7). Existing `LanguageSeederTests`/`AdminUserSeederTests`/`RbacSeeder`-related tests remain valid.

## 7. Risks / Notes

- **`GetTableInfoList` cost:** one metadata query at startup; negligible.
- **Table-name matching:** must match SqlSugar's actual (lower-cased) table names, not entity class names. Verify against `EntityTypeCollector` / mapping during implementation.
- **Partial-seed-then-crash:** if a table is created but its seeder crashes mid-run, next startup sees the table as existing and will **not** retry (per the "trust existing tables" principle). Acceptable per design; operator remediation = drop the affected table (or DB) and restart. Documented, not automated.
- **Concurrent hosts on first boot** (multi-replica cold start): two hosts may both see the table as freshly created and race to seed; the inner row guards + unique constraints (`languages.code`, `roles.name`, `users.email`) make duplicate inserts fail rather than duplicate. Acceptable for the initial-bootstrap window.
