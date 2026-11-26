# Options are validated before the startup migration

## Decision

`StruoCmsStartupChecks.RunStruoStartupChecksAsync` runs the framework's startup validator
(`IStartupValidator.Validate()`) as its first step, before anything opens a database connection.
Every options type registered with `ValidateOnStart()` is validated there, so an invalid setting
stops the host before `Database:MigrateOnStartup` applies a migration.

The command-line commands (`migrate` and the others) do not run the startup validator. Each
validates the options it reads, when it reads them.

## Why

A host that migrates the database and then refuses to start leaves an operator with a changed
database and no running API, and with an error about a setting that has nothing to do with the
database.

`ValidateOnStart()` registers one startup validator for all validated options; the host runs it when
it starts, which is after the checks in `RunStruoStartupChecksAsync`. Calling it first moves every
registration at once, including one a fork adds.

A command validates only what it reads because it is run with less configuration than the host: a
`migrate` step in a pipeline has the database settings and may have no cookie, OIDC or rate-limit
section at all.

## Evidence

Measured on 2026-11-26 at the commit before the change, with a host built the way a fork's
`Program.cs` builds it (`ForkHost`), a fresh SQLite file and `Database:MigrateOnStartup=true`. With
each of these settings the host failed to start, and the database file already held the migrated
tables:

| Setting | Value |
|---|---|
| `RateLimiting:Login:PermitLimit` | `0` |
| `RateLimiting:LoginConcurrency:PermitLimit` | `0` |
| `Query:MaxLimit` | `0` |
| `Api:MaxJsonBodyBytes` | `0` |

`Auth:Password:MinLength=0` and a cookie name with a space already failed before the migration.
`StartupValidationOrderTests` pins all six: the start fails with an error that names the setting,
and the database has no table.

The fifth external review (2026-11-25) measured the same order on PostgreSQL 17 with an invalid
rate-limit value: the core migrations were applied and the host then did not listen.

`migrate` with `RateLimiting:Login:PermitLimit=0` on SQLite: exit code 0, every migration applied
and the bootstrap admin created.

## Unknowns

- The order was measured on SQLite, and by the external review on PostgreSQL; the check runs before
  any connection is opened, so no other backend was run.
- A validator a fork registers with `ValidateOnStart()` runs before the migration as well. One that
  reads the database was not measured.

## Referenced from

- `src/Struo.Api/Hosting/StruoCmsStartupChecks.cs`
