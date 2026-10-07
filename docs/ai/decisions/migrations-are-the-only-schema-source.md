# Migrations are the only schema source

## Decision

Every table, column and index of a running application comes from a FluentMigrator migration.
`Struo.Api` issues no DDL outside migrations: `StartupMigrationGate` applies pending migrations when
`Database:MigrateOnStartup` is `true`, and otherwise refuses to start until the `migrate` command has
run. The core migrations live in `src/Struo.Infrastructure/Migrations/Core/`.

Unit-level test harnesses may still build tables with `db.CodeFirst.InitTables(...)`. App-level test
hosts (`ApiFactory`) run the migrations instead.

## Why

SqlSugar's default CodeFirst mode is a destructive diff on an existing table: it adds columns,
changes column types and drops columns that are absent from the entity. Running it at startup would
apply that diff to tables that already hold data, on every deploy. A migration is reviewed, runs once,
and is recorded in `{TablePrefix}schema_versions`.

CodeFirst is safe in a unit harness only because it builds the same structure as the core migrations.
`CoreSchemaParityTests` compares the two on SQLite, and `CoreMigrationsLiveTests` does the same on
PostgreSQL, SQL Server, MySQL and MariaDB when a connection is configured. When the two
disagree, the parity test fails and the migration is the side to trust.

## Evidence

`PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres` shows the DROP on
real PostgreSQL: an unfiltered `InitTables` over a narrowed probe entity, run against a table created
from the probe's wide entity, drops the column the narrow entity lacks. It is opt-in and needs a live
PostgreSQL connection.

On SQLite the same drop is gated behind `ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn`
(upstream `SqliteCodeFirst.ExistLogic`, `Src/Asp.NetCore2/SqlSugar/Realization/Sqlite/CodeFirst/SqliteCodeFirst.cs`,
tag 5.1.4.197). `SqlSugarClientFactory.Create` sets `MoreSettings` only for
`SqlServerCodeFirstNvarchar` (true on SQL Server), never `SqliteCodeFirstEnableDropColumn`, so a
SQLite run cannot show the drop.

## Unknowns

What SQLite does with `SqliteCodeFirstEnableDropColumn` turned on is not measured in this repository;
no test sets the flag. `CoreMigrationsLiveTests` runs only where a connection is configured, and Oracle
has no connection, so the parity claim is unmeasured there.

## Referenced from

- `AGENTS.md` ("Migrations are the only schema source")
- `tests/Struo.Tests/Query/PostgresIntegrationTests.cs`
