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
>
> **Update (2026-07-22 rebaseline):** the baseline described below as aspirational is now realized.
> `db/migrations/001-core-baseline.sql` exists, reproducing the **core-framework** `InitTables` output
> only (the 9 `FrameworkEntityTypes` tables) — not the whole application. It was machine-generated via
> `InitTables(FrameworkEntityTypes.All)` + `pg_dump --schema-only`, then hardened to idempotent. The 13
> historical sample-entangled scripts that previously lived in this folder were superseded and removed.
> Sample/business schema (the Blog demo, and any downstream fork's own collections) is intentionally out
> of scope for this template's core folder — see [`db/migrations/README.md`](../db/migrations/README.md).

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
  001-core-baseline.sql            -- core-framework schema matching InitTables(FrameworkEntityTypes.All)
  002-add-users-token-columns.sql
  003-article-add-hero-image.sql
  ...
```

- Zero-padded, contiguous, single-series numeric prefix + `-` + short kebab-case description. The prefix
  is the apply order (ordinal filename sort), so it must be monotonic and gap-free.
- One logical change per file. Forward-only (no automatic `down`); write a compensating script if a
  rollback is needed.
- Each file starts with a comment: date, author, ticket/PR, and a one-line intent.
- Idempotency where practical (`IF NOT EXISTS`, guarded `ALTER`), so a re-run is safe.

See [`db/migrations/README.md`](../db/migrations/README.md) for the authoritative, current convention.

## Baseline

`001-core-baseline.sql` reproduces what `InitTables` generates for the **core framework** entity set
(`FrameworkEntityTypes.All`) on PostgreSQL — it does not cover the whole application. It was generated
from a fresh scratch database running `InitTables(FrameworkEntityTypes.All)`, dumped with
`pg_dump --schema-only`, reviewed, and hardened to idempotent. Sample/business schema (this template's
Blog demo, or a downstream fork's own collections) is **not** part of the core baseline — it lives in
its own `NNN-…` migrations, added by whoever owns that schema, starting after the core baseline. From the
baseline forward, every entity change that alters the core schema gets a numbered follow-up script — and
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
