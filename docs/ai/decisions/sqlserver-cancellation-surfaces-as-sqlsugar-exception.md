# Cancellation surfaces as OperationCanceledException on SQL Server

## Decision

A cancelled `CancellationToken` surfaces as `OperationCanceledException` on every backend. On SQL
Server, `CancellationAop` (registered by `SqlSugarClientFactory` on every inner client of the
`SqlSugarScope`) sets SqlSugar's `Aop.OnError`: when a command fails while the token on the client's
`Ado` is cancelled, it throws `OperationCanceledException` carrying that token, with the SqlSugar
error as the inner exception. Other backends do not register the hook.

Covered: every Ado command a factory-built client runs — queryables, insertables, updateables,
deleteables and the `Ado` query methods — because SqlSugar invokes `OnError` from the catch block of
each of them. That includes `SqlSugarItemRepository`'s and `FileService`'s ct-taking entry points
and every other service that reaches the database through the injected `ISqlSugarClient`.

Not covered:

- `Ado.BeginTranAsync` and an explicit `Ado.Open`/`OpenAsync`. They open the connection outside the
  catch that invokes `OnError`, so a cancelled token there still surfaces as `SqlSugarException`.
  `TransactionRunner`, `SqlSugarPermissionGrantStore` and `SqlSugarSiteSettingsStore` begin
  transactions this way.
- The raw clients `MigrationHost` and `MigrationLock` build with `new SqlSugarClient`; they take no
  request token.

## Why

SqlSugar opens the connection in `AdoProvider.CheckConnectionAsync`, which catches every exception
from `DbConnection.OpenAsync(token)` and rethrows a new `SqlSugarException` with the original message
and no inner exception. On SQL Server, `SqlConnection.OpenAsync` with a cancelled token throws
`TaskCanceledException` inside that catch, so callers see `SqlSugarException` ("Connection open error
. A task was canceled"). A caller that tells cancellation apart from failure by catching
`OperationCanceledException` then sees a client abort as a database failure.

`OnError` is the one SqlSugar extension point that runs for every command and can replace the
exception it rethrows. The condition reads the token state rather than the exception, because the
wrapper keeps only the message text.

SqlSugar keeps the last token on the client's `Ado` until another call sets one. A later call on
the same client without its own token runs under that stale token: an async call fails on it anyway,
and the hook only changes the exception type. A synchronous call does not pass the token down, so
an unrelated failure of a synchronous call on a client whose last token was cancelled is reported as
cancellation. That only happens inside a scope whose request was already cancelled.

## Evidence

Measured 2026-10-12: `SqlSugarItemRepositoryTests.GetByIdAsync_honors_cancellation`,
`CreateAsync_honors_cancellation` and `FileServiceCancellationTests.GetAsync_honors_cancellation`
failed on SQL Server with `SqlSugarException` thrown from `AdoProvider.CheckConnectionAsync`, and
passed on SQLite, PostgreSQL, MySQL and MariaDB. With the hook registered they pass on all five.

The `CheckConnectionAsync`, `OnError` and `SqlSugarException` behaviour above was read from the
decompiled SqlSugarCore 5.1.4.221 assembly.

## Unknowns

A token cancelled while a SQL Server command is already executing is translated by the same hook,
but no test cancels mid-command, so the exception `SqlClient` raises there is not measured. Oracle is
not covered.

## Referenced from

- `src/Struo.Infrastructure/Persistence/CancellationAop.cs`
