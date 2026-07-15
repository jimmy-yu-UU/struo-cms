# StruoCMS — Schema Migration Strategy

> **Status:** strategy of record (2026-07-06). Audit finding **D3**.
>
> **Update (DB-6, 2026-07-15):** a lightweight in-house runner now exists — no third-party framework
> was added. `MigrationRunner` (`Struo.Infrastructure/Persistence`) applies the reviewed `*.sql`
> scripts in order on PostgreSQL, tracking applied filenames in a `schema_migrations` table. It is
> config-driven (`Database:MigrationsPath`, disabled by default) and a no-op on non-PostgreSQL
> backends. Numbering was also unified to a single contiguous `NNN-description.sql` series (the
> `0001__…` examples below are historical). See
> [`db/migrations/README.md`](../db/migrations/README.md) for the current convention and runbook.

## The problem

`DatabaseInitializer` runs `InitTables` (SqlSugar CodeFirst) **only in Development** — it *creates*
tables from entity classes but cannot **alter** or **drop** existing columns. There is therefore no
supported path for evolving the schema of a **production** database after the first deploy.

## Decision: manual, but versioned and reviewed (not a framework — yet)

Keeping `InitTables` out of production is correct: an auto-diff tool applied to a live database can
drop/rename columns and lose data. We keep production schema changes **manual and explicit**. The gap
we *are* closing is that manual changes today are ad-hoc — unversioned, unreviewed, unrepeatable, and
prone to environment drift. So:

1. **Every production schema change is a checked-in SQL script.** No ad-hoc `ALTER` typed straight
   into a prod console.
2. **Scripts are versioned and ordered.** See the folder convention below.
3. **Scripts are applied manually** (by a human, during a maintenance window) — the operator stays in
   control; nothing auto-runs against production.
4. **Scripts are reviewed** like any other code change (PR).

This preserves the manual safety you want while making changes auditable, repeatable across
environments (staging → prod), and diff-able.

## Folder & naming convention

```
db/migrations/
  0001__baseline.sql              -- full schema matching the current InitTables output
  0002__add_users_token_columns.sql
  0003__article_add_hero_image.sql
  ...
```

- Zero-padded sequence prefix + `__` + short snake_case description.
- One logical change per file. Forward-only (no automatic `down`); write a compensating script if a
  rollback is needed.
- Each file starts with a comment: date, author, ticket/PR, and a one-line intent.
- Idempotency where practical (`IF NOT EXISTS`, guarded `ALTER`), so a re-run is safe.

## Baseline

`0001__baseline.sql` should reproduce what `InitTables` currently generates for the target provider
(PostgreSQL). Generate it once from a fresh dev database (e.g. `pg_dump --schema-only`), review it, and
commit. From then on, every entity change that alters the schema gets a numbered follow-up script — and
the entity change and its migration script land in the **same PR**.

## Applying to production (manual runbook)

1. Back up the database.
2. In a maintenance window, apply the new script(s) in order with a reviewed SQL client.
3. Deploy the matching application build.
4. Record which scripts have been applied (a simple `schema_migrations(version, applied_at)` table you
   insert into by hand, or an ops log).

## When to graduate to tooling

Adopt **DbUp** (runs checked-in SQL scripts in order, idempotently, tracking a `SchemaVersions` table)
when any of these become true:

- More than one production/staging environment to keep in sync.
- Applying scripts by hand becomes error-prone or frequent.
- You want a repeatable, automated (but still SQL-you-wrote, not auto-diffed) apply step.

DbUp keeps you writing the exact SQL — it just removes the human-tracking and ordering errors. **Avoid
auto-diff migrators** (or CodeFirst diff) against production for the data-loss reason above.
FluentMigrator is an option if you later want C#-authored, up/down migrations, but it is heavier than
needed today.

## Not in scope now

- No migration framework installed.
- No automated production apply step.
- `InitTables` remains Development-only for fast local iteration.
