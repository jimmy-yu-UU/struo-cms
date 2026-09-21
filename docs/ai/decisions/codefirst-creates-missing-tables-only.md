# Startup CodeFirst only creates tables that do not exist

## Decision

`DatabaseInitializer.CreateMissingTables` filters `entityTypes` to the ones whose table name is not
in the startup snapshot of existing tables, and hands only that filtered list to SqlSugar's
`CodeFirst.InitTables`. It runs in every environment and on every backend, called from `Program.cs`
right after `IEntityTypeCollector.CollectForInitTables()` produces the full entity list.

`Program.cs` calls `DatabaseInitializer.SyncSchema` only when `Database:AutoSyncSchema` is enabled,
handing it that same full, unfiltered entity list. `SyncSchema` itself requires
`IHostEnvironment.IsDevelopment()` to be true; in any other environment it logs a warning and returns
`false` without touching the database.

Evolving an existing table outside Development goes through `db/migrations` scripts applied by
`MigrationRunner`, which runs in every environment and on every backend and is disabled only when
`Database:MigrationsPath` is left empty.

## Why

An unfiltered `InitTables` call over an existing table is destructive: SqlSugar's default CodeFirst
mode adds columns, changes column types, and drops columns that are absent from the entity. If
`CreateMissingTables` stopped filtering — handing the full entity list to `InitTables` the way
`SyncSchema` does — every startup, in every environment, would run that destructive diff against
tables that already hold data. If `SyncSchema` ran outside Development, the same destructive diff
would run in Production on every deploy instead of only through a reviewed migration script.

`PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres` is the test that
proves the DROP happens: on real PostgreSQL, an unfiltered `InitTables` call over a narrowed probe
entity, run against a table `InitTables` already created from the probe's wide entity, drops the
column that is absent from the narrow entity.

The SQLite-only suite that runs on every CI job cannot show this. On SQLite the same drop is gated
behind `ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn`, which
`SqlSugarClientFactory.Create` never sets, so
`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite` observes the removed
column staying in place — a property of this repository's configuration, not of the SQLite engine or
SqlSugar's SQLite dialect. `docs/guide/en/21-schema-and-upgrades.md`, "Where schema sync is
dangerous", item 7, describes what turning that flag on would do: an ordinary column gets dropped
silently, while a column tied to a primary key, an index, a `CHECK`, or a foreign key makes
`ALTER TABLE` fail and startup abort.

## Evidence

Measured 2026-08-03 (SqlSugarCore 5.1.4.215): on real PostgreSQL, an unfiltered `InitTables` call
over `DestructiveInitProbeNarrow`, run after `InitTables` created the table from
`DestructiveInitProbeWide`, drops the `Doomed` column
(`PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres`, opt-in,
requires a live PostgreSQL connection).

Measured 2026-08-03 (SqlSugarCore 5.1.4.215): on SQLite, the same probe pair keeps the `Doomed`
column (`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite`, runs on
every CI job).

Both tests re-assert their result on every run against whatever `SqlSugarCore` version
`Directory.Packages.props` currently pins (5.1.4.220 on this branch), not only against the version
the 2026-08-03 measurement used.

Upstream source (tag 5.1.4.197): `SqliteCodeFirst.ExistLogic` in
`Src/Asp.NetCore2/SqlSugar/Realization/Sqlite/CodeFirst/SqliteCodeFirst.cs:10,50-58` implements DROP
COLUMN behind `ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn`.

`grep -rn "MoreSettings" src/ tests/`: `SqlSugarClientFactory.Create` builds its `ConnectionConfig`
with `ConnectionString`, `DbType`, `IsAutoCloseConnection`, and `ConfigureExternalServices` only —
`MoreSettings` is never assigned anywhere in `src/` or `tests/`.

## Unknowns

The current comments state no unmeasured fact and no open question. Every claim in `## Why` and
`## Evidence` is either one of the two measurements themselves or a code fact confirmed by reading
`SqlSugarClientFactory.Create`, `DatabaseInitializer`, or `Program.cs` directly. The nearest thing to
a repository-configuration property rather than an engine property — that SQLite keeps the column
only because `SqlSugarClientFactory` never sets the drop-column flag — is recorded as a measured
fact in `## Why`, not as an unknown.

## Referenced from

- `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs`
- `tests/Struo.Tests/Persistence/DatabaseInitializerTests.cs`
- `tests/Struo.Tests/Query/PostgresIntegrationTests.cs`
- `AGENTS.md` ("CodeFirst creates; migrations evolve")
