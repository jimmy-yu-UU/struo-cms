# SQL Server needs READ_COMMITTED_SNAPSHOT on

## Decision

A SQL Server database that runs StruoCMS must have `READ_COMMITTED_SNAPSHOT` on. The application does
not change the setting. At startup, after the migration step, `SqlServerIsolationCheck` reads
`is_read_committed_snapshot_on` for the current database and logs one Warning when it is off. The
warning names the setting, says that concurrent writes can deadlock, and gives the `ALTER DATABASE`
statement to run. The check runs only on SQL Server and never fails startup. Test databases the suite
creates on SQL Server have the setting turned on right after creation
(`LiveDatabaseDdl.EnableReadCommittedSnapshot`).

The startup check's single `SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name =
DB_NAME()` is a maintainer-approved raw-SQL exception to "all database access through SqlSugar":
SqlSugar has no typed surface for the catalog flag. The query is read-only, has no parameters and
takes no input.

## Why

Under the default locking READ COMMITTED, a read inside a transaction takes shared locks on every row
it scans. The revision snapshot of a create or update re-reads the item's translation rows
(`RevisionSnapshotBuilder` → `TranslationStore.LoadTranslationsAsync`). On a small sidecar table the
plan is a clustered index scan, so the read waits on the uncommitted translation rows of every
concurrent write, and those writes wait on each other: deadlock victims (error 1205). The same
exposure exists for any other in-transaction read, including a fork's own code.

With `READ_COMMITTED_SNAPSHOT` on, readers use row versions and never block on writers, which is the
behaviour PostgreSQL's MVCC gives and the code already relies on there.

## Evidence

Investigation 2026-10-22 on SQL Server 2025 with 16 parallel creates over 5 rounds:

- Without the setting, 73 to 74 of 80 creates failed with 1205 at the translation read; concurrent
  updates of different items failed 14 of 16.
- Skipping the translation read removed the failures, and `ALTER DATABASE … SET
  READ_COMMITTED_SNAPSHOT ON` with no code change removed them too (16 of 16 created, no deadlock
  report, three runs).
- With 20 000 pre-seeded rows the plan became a seek and the deadlock disappeared, so without the
  setting the failure depends on the plan: young or small tables hit it first.
- `ConcurrentCreateDeadlockTests` is green on SQL Server, MySQL, MariaDB, PostgreSQL and SQLite with
  the setting on.

## Why the application does not run the ALTER

`ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON` needs exclusive access to the database: it waits for
every other connection to finish, or, with `WITH ROLLBACK IMMEDIATE`, rolls their transactions back.
An application that issues it at startup would either hang while a second instance is running or kill
that instance's in-flight work, and it needs a permission the application login should not have.
The deployment owner runs it once, deliberately. Azure SQL Database has the setting on by default.

## Tests

`SqlServerIsolationCheckTests` covers a database with the setting off (one Warning naming the
setting, the deadlock risk and the statement), on (no log), and every other backend (no query run).

## Unknowns

Oracle is not covered. Whether SQL Server's lock-escalation or plan choices under other table sizes
reintroduce a deadlock with the setting on is not measured beyond the 16-way create burst above.

## Referenced from

- `src/Struo.Infrastructure/Persistence/SqlServerIsolationCheck.cs`
- `tests/Struo.Tests/Persistence/SqlServerIsolationCheckTests.cs`
- `tests/Struo.Tests/Support/LiveDatabaseDdl.cs`
- `AGENTS.md` ("Invariants" and "Verification")
- `docs/guide/en/20-deployment.md`
