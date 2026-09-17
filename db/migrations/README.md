# Schema migrations

## 1. What this directory is

Reviewed, forward-only `*.sql` scripts that evolve an **existing** database's schema — `ALTER TABLE`,
data backfills, and similar changes to tables that are already there. **The template ships zero files
here.** Whatever `NNN-*.sql` scripts eventually live in this directory belong to a specific fork, not to
StruoCMS core: core creates its own tables via CodeFirst (§2 below) and never needs a bootstrap script
of its own. See [chapter 21](../../docs/guide/en/21-schema-and-upgrades.md) of the guide for schema
management and startup order, and [chapter 20](../../docs/guide/en/20-deployment.md) for the
full deployment picture, including configuration and backups.

## 2. Three layers, three responsibilities

Schema management is split into three layers by **responsibility**, not by environment:

| Responsibility | Executor | Environment | Backend | Default |
|---|---|---|---|---|
| Create tables that **do not exist** | CodeFirst (filtered `InitTables`) | All environments | All five | Always on |
| **Alter existing** tables (automatic diff) | Full CodeFirst sync | Development only | All five | `AutoSyncSchema=false` — must be explicitly turned on |
| **Alter existing** tables (reviewed) | `MigrationRunner` + `.sql` scripts in this directory | All environments | All five | `MigrationsPath` empty = off |

What this means in practice:

- **Table creation happens everywhere, automatically.** In any environment, on any of the five
  configured backends, whenever an entity type's table does not yet exist, it gets created before
  anything else runs. `DataSeeder` then seeds initial data into whichever tables were newly created
  during that startup — this is the one part of schema management that is uniform across every
  environment.
- **An existing table is never touched automatically.** The only way an already-existing table gets
  altered without a reviewed script is `AutoSyncSchema=true`, and that setting only takes effect in
  Development — it is ignored (with a logged warning) anywhere else. Outside Development, the
  only way to alter an existing table is a script in this directory, applied by `MigrationRunner`.
- **This runs identically on all five backends**, not just PostgreSQL. Table creation, `AutoSyncSchema`,
  and `MigrationRunner` all execute regardless of `Database:DbType`. PostgreSQL remains the only backend
  this repository verifies live; the other four are expected to work because this repo maps every
  dialect-specific column shape it needs to a per-backend type literal itself (`ColumnTypeMap.cs`,
  applied by the `SqlSugarClientFactory` entity-service hook) rather than emitting one hardcoded literal
  — but that mapping is not part of this repo's live verification for MySQL/SqlServer/Oracle.
- **Execution order is fixed, and it matters.** At startup: a table-name snapshot, then
  `CreateMissingTables`, then optional `SyncSchema` (Development only), then `MigrationRunner`, then
  dev-only `SchemaGuard`, then `DataSeeder`'s seeding. `MigrationRunner` runs **after** CodeFirst table
  creation, so the tables its `ALTER` scripts target already exist by the time it runs, and **before**
  `DataSeeder` seeds, so seed data lands on the final structure rather than a partially-migrated one.

## 3. When you need to write a migration

The governing rule, stated plainly because it overrides the tempting shortcut of just editing an
entity class:

> Any structural change that touches existing data must go through a migration. `AutoSyncSchema` is
> intended only for fast schema iteration in Development, on a schema that does not yet hold any real
> data.

The reason a migration is required so often is structural, not incidental: CodeFirst's automatic sync
computes a **structural diff** between the entity classes and the live table — it knows the target
shape, but it has no idea what you *intended*. A column rename and a "drop one column, add another" are
indistinguishable to a diff. That ambiguity is why renames, type narrowing, new `NOT NULL`/`UNIQUE`
constraints against populated tables, and column splits/merges are all real-data hazards, not edge
cases. Chapter 15 of the guide carries the full hazard-by-hazard table (what `AutoSyncSchema` would
actually do in each case, and the safe alternative); the short version is: if the table might already
hold rows you care about, write a script here instead of relying on the diff.

## 4. File naming and rules

```
NNN-short-kebab-description.sql
```

- **`NNN`** — zero-padded, a **single contiguous series**. The numeric prefix is the apply order
  (ordinal filename sort), so it must be monotonic with no gaps. Since the template ships no scripts, a
  fork's first migration is `001-...`.
- **`short-kebab-description`** — one logical change per file.
- Each file starts with a header comment: date, author, and a one-line statement of intent.
- Tracking is by **filename only** — there is no checksum or content hash. A filename already recorded
  as applied is never re-run, even if its on-disk content is edited afterward. **Never modify a file
  that may already be applied anywhere** (including in someone else's deployment); ship a new file
  instead.

## 5. Portability guidance

**Write standard SQL wherever possible, to preserve the option of switching database engines later.**

Prefer:

- `ALTER TABLE … ADD COLUMN` / `DROP COLUMN` / `RENAME COLUMN`
- `CREATE INDEX` / `CREATE UNIQUE INDEX`
- `UPDATE` / `INSERT` / `DELETE` for data backfills
- Standard type names: `varchar(n)`, `integer`, `bigint`, `boolean`, `timestamp`, `numeric(p,s)`

Avoid, with a portable alternative:

| Avoid | Why | Instead |
|---|---|---|
| PostgreSQL-specific types (`jsonb`, `uuid`, `timestamptz`, `serial`) | Don't exist on the other four backends | Standard types; let the ORM handle the application-level mapping |
| `DO $$ … $$` | PL/pgSQL, PostgreSQL-only | Split into multiple plain statements |
| `IF NOT EXISTS` on `ALTER` / `CREATE INDEX` | Not supported on SQL Server | **Not needed** — the tracking table already guarantees each file runs at most once (see below) |
| `::` cast syntax | PostgreSQL-only | `CAST(x AS type)` |
| Dialect-specific functions (`now()` vs `GETDATE()` vs `SYSDATE`) | Differ per engine | Pass the value from the application layer, or accept the coupling deliberately in your own fork |
| `RETURNING` | Non-standard | A separate query |

**A note on timestamps:** the bare `timestamp` in the *prefer* list above is the standard-SQL type
name for "a point in time," not a retraction of this repo's time-zone-aware convention. A new framework
table's instant-storing columns should still be time-zone-aware, storing UTC (see
`docs/ai/task-playbooks.md`, Playbook 4). The portable way to satisfy that convention is not a
PostgreSQL-only `timestamptz` literal in the script: if the column is also modeled as an entity
property, mark it `[ColumnShape(ColumnShape.TimestampWithTimeZone)]`
(`src/Struo.Infrastructure/Persistence/ColumnShape.cs`) so CodeFirst resolves the matching literal per
backend, and a freshly created table ends up with the same column this migration is adding to an
existing one. If you are hand-writing the DDL directly instead — a column not backed by any entity
property CodeFirst would ever create — `ColumnTypeMap.cs` is where those per-backend literals
(`timestamptz` on PostgreSQL, `datetime(6)` on MySQL, `datetimeoffset` on SQL Server, `timestamp with
time zone` on Oracle) are centralized; copy the one for your backend rather than assuming PostgreSQL's.

**Check the target column's actual current type rather than assuming one — this repository's own
framework tables are not uniform.** Most `AuditableEntity` `createdat`/`updatedat` columns are bare
`timestamp`, but `media_folders.createdat`/`updatedat` are already time-zone-aware (both carry
`[ColumnShape(ColumnShape.TimestampWithTimeZone)]` — `src/Struo.Infrastructure/Files/MediaFolder.cs`),
and so are `site_settings.updatedat` and the runner's own tracking column `schema_migrations.appliedat`
(`src/Struo.Infrastructure/Persistence/SchemaMigration.cs`). Read the entity declaration in `src/`, or
the live schema, before writing the `ALTER`. The existing bare-`timestamp` columns have deliberately
**not** been retroactively converted: re-anchoring already-stored values against a session time zone is
a silent data shift.

**Idempotency is not required — portability takes priority over it.** The `schema_migrations` tracking
table already guarantees each filename runs at most once, so a script never needs to protect itself
against being re-run. Given that guarantee, `IF NOT EXISTS` and friends buy nothing while costing real
portability — they are among the least portable constructs in the table above (SQL Server has no
equivalent syntax at all). Write the plain, non-defensive form of the statement. (If you are carrying
forward scripts written against this repository's older guidance, which did ask for idempotent scripts,
they still apply correctly — the guards are simply redundant now.)

Other rules:

- One logical change per file (§4).
- Prefer separate files for structural changes vs. data changes, since DDL transaction semantics differ
  by engine (§6).
- Forward-only — there is no automatic `down`. A rollback is a new, compensating forward script.

## 6. Known limits

1. **DDL rollback does not work on MySQL or Oracle.** The runner wraps each file's execution together
   with its tracking-row insert in one transaction, but MySQL and Oracle both commit DDL implicitly —
   a failure partway through a script on those backends leaves whatever DDL already ran in place; the
   transaction cannot roll it back. Back up before running structural changes on these backends and do
   it during a maintenance window.
2. **No advisory lock.** If multiple replicas start concurrently against `Database:MigrationsPath`
   pointing at the same directory, more than one process may attempt the same pending file at the same
   time. Deploy schema changes with a single replica first (or as a separate one-off job) rather than
   relying on N replicas racing each other. The same caveat applies to CodeFirst table creation.
3. **No checksum, no down-migration, no dry-run.** This runner's job is "apply ALTER scripts and record
   what ran" — nothing more. If you need checksums, reversible migrations, or a dry-run mode, use a
   dedicated tool (DbUp, Flyway, Liquibase) instead; leaving `Database:MigrationsPath` empty disables
   this mechanism entirely so it does not conflict with one.
4. **`Database:MigrationsPath` must be an absolute path in Production.** The runner passes the
   configured value straight to a directory-exists check with no content-root resolution of its own. A
   relative value resolves against the **process's current working directory** at launch, which is not
   guaranteed to be the application's own folder — confirmed live: a relative path that exists only
   relative to the repository root, not the process's actual working directory, throws
   `DirectoryNotFoundException` and the process exits with code 1. Configure an absolute path for any
   deployment where the launcher might not `cd` into the application's own directory first.

## 7. Upgrading core across a fork

Because the template ships zero scripts and table creation is create-only (it never touches a table
that already exists), **a change to StruoCMS core's own schema cannot automatically reach an existing
fork's deployment**. This is the direct consequence of two things holding at once: core carries no
vendor SQL, and existing tables are never silently altered.

The practical path:

- Core schema changes are announced in release notes — which table changed, which column, and to what
  type.
- Each fork writes its own `ALTER` script(s), for the backend it actually runs, in its own
  `db/migrations/`, based on that announcement.
- As a diagnostic aid, you can run `AutoSyncSchema=true` in Development against a **copy** of your
  production schema to see what CodeFirst's diff would change — but treat that only as a hint about
  what to write by hand. Do not treat the diff's output as the production execution plan; §3's hazard
  list (destructive renames, silent truncation, and the rest) applies to that diff exactly as it does
  anywhere else.
