# One process-wide SqlSugarScope serves every request

## Decision

`ISqlSugarClient` is registered as a singleton. `SqlSugarClientFactory.Create` builds one
`SqlSugarScope` at first resolution, with the soft-delete filter, `AuditAop` and `CancellationAop`
attached in the configure action. This is SqlSugar's documented pattern for `SqlSugarScope`.

Database work therefore follows two rules, also stated in `AGENTS.md`:

- It runs in async methods. SqlSugar picks a scope's inner client from the call stack: with an async
  frame it uses an `AsyncLocal`, without one a `ThreadLocal`. A sync lambda in `Task.Run`, a timer
  callback or a sync job gets a client that stays on the pool thread and is reused by unrelated work.
- `CopyNew()` is only for work that needs no configure action. The copy has no audit stamping, no SQL
  Server cancellation translation, and shares the parent's soft-delete filter provider.
  `DbReadinessCheck` uses it for its connection ping.

Further constraints:

- A touch of the scope from a flow that later forks children (the host's start-up flow, a
  `BackgroundService`) hands those children one shared client. HTTP requests do not inherit it.
  Start such work from its own flow, or give it a `CopyNew()`.
- An `ICurrentUserAccessor` a fork registers must not be scoped and must read the user on each call,
  because `AuditAop` captures it when the scope is built. A scoped registration stops startup.
- Per-request mutation of the client (`db.Aop.X =`, `db.QueryFilter.Add`, `Ado.CommandTimeOut`) only
  reaches the inner client of the current flow.

## Why

SqlSugar stores a scope's inner client in two process-wide static dictionaries
(`CallContextAsync<SqlSugarClient>` and `CallContextThread<SqlSugarClient>`), keyed by the scope
instance. Nothing removes an entry, not idle time, not a GC and not disposing the host. A scope built
per request therefore adds one key per request:

| Request shape | Retained per request |
|---|---|
| Ordinary REST or GraphQL request | 115 to 160 B |
| `/health/ready` with a sync `Task.Run` ping | about 2.5 KB (a whole inner client on a pool thread) |

61 000 mixed in-process requests retained 43 MB, which stayed after a full GC and after the host was
disposed. SqlSugar's documentation says to use `SqlSugarScope` as a singleton for this reason. A
singleton holds one key for the life of the process, and the retention test asserts that no request
adds another.

A singleton scope also keeps the per-flow isolation that parallel GraphQL root fields rely on: each
logical async flow still gets its own inner client, connection and transaction. The configure action
runs for every inner client, so the filter and the audit and cancellation hooks apply to all of them.
`AuditAop` reads the current user on each call, from the singleton `ICurrentUserAccessor`, so no scoped
state is captured.

## Evidence

In-process SQLite requests, sequential, 3 000 per row (microseconds, then KB allocated):

| Request | Per-request scope | Singleton scope |
|---|---|---|
| Admin list | 2 147 / 653 | 2 071 / 633 |
| GraphQL, 3 root fields | 3 060 / 1 250 | 3 034 / 1 228 |
| `/health/ready` | 163 / 63 | 151 / 82 |

The singleton costs the same as the per-request scope within about 3%. Each scope member access still
builds a `StackTrace`, about 15 to 20% of an in-process SQLite request; only a plain `SqlSugarClient`
removes it, and that gives up the parallel-resolver safety above.

Concurrency probes on PostgreSQL, SQL Server, MySQL, MariaDB and SQLite, run against the singleton:

- 1 600 four-root GraphQL requests (8 at a time, with a DataLoader): all returned the exact data.
- 120 rolled-back transactions concurrent with other writers: no leaked row, no lost acknowledged
  write, each request saw its own uncommitted row.
- 200 writes by four users: every row stamped with its own user.
- Aborted requests cancelled their own query while concurrent requests kept running.

The two shared-connection failure modes were reproduced on purpose to confirm the probes can fail: a
plain client per request with parallel resolvers failed all 320 PostgreSQL requests, and one plain
client for the whole process failed 1 537 of 1 600 SQLite requests.

On SQL Server, MySQL and MariaDB, concurrent article creation produces deadlock victims. That is
database behaviour and is the same with a per-request scope.

### Method

A throwaway copy of the repository with an environment switch between the shapes, outside the
worktree and not committed. Retention is `GC.GetTotalMemory(true)` after three forced full GCs, plus
the key counts of the two static dictionaries read by reflection. Machine: AMD Ryzen 9 9900X, Windows
11, .NET 10, SqlSugarCore 5.1.4.221, HotChocolate 16.6.8, Release build.

The regression tests are `SqlSugarScopeRetentionTests` (key counts after 2 000 requests, readiness
included) and `SqlSugarSingletonScopeApiTests` (concurrent GraphQL, rollbacks and audit stamping).

## Unknowns

- Nothing enforces the "touch the scope inside the async callee" convention. A flow that touches it
  before forking children shares one client; no request path does today.
- The cost of resolving the current user on each `AuditAop` call from the HTTP context is not
  measured separately; it is inside the request timings above.
- Concurrent article creation deadlocks on SQL Server, MySQL and MariaDB under load. It is a pre-existing database
  behaviour that this decision does not address.

## Referenced from

- `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
