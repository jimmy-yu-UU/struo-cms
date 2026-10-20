# SqlSugarScope adds about 70 microseconds per awaited call and retains memory per request

## Decision

Pending maintainer decision. `SqlSugarClientFactory.Create` returns a `SqlSugarScope` built per
request, with the soft-delete filter, `AuditAop` and `CancellationAop` attached in the configure
action. That shape is unchanged. The measurements below say it is not free: on SQLite it is above the
5% bar, on PostgreSQL it sits at the bar, and it leaves a few hundred bytes of static state behind per
request. No optimisation is chosen here.

## Why

A scope builds no inner client at construction (`Create` costs 90 ns). `SqlSugarScope.GetContext`
picks the inner client on every member access:

- It builds a `StackTrace` with file info and scans up to 35 frames. If a frame is an async state
  machine, the client lives in `CallContextAsync` (an `AsyncLocal`); otherwise in a `ThreadLocal`.
- A client created inside an awaited `async` callee is discarded when that callee returns. Every
  awaited repository call that is the first to touch the scope in its flow builds a new inner client
  and re-runs the configure action. `Dispose` from the request flow builds one more, to dispose it.
- Child flows inherit the client their parent already created, so a parent that touched the scope
  hands all its children one shared connection.

So the cost is per inner client, and a request creates several: one per awaited call, plus one at
`Dispose`. Measured with 3 awaited `GetByIdAsync` calls and `Dispose`, 3 inner clients are created
during the calls and a fourth at `Dispose`.

| Request of 3 awaited reads + Dispose | Plain client | Scope | Difference |
|---|---|---|---|
| SQLite | 352 us / 155 KB | 552 us / 238 KB | +200 us (36% of the scope request) |
| PostgreSQL | 3764 us / 163 KB | 3987 us / 336 KB | +223 us (5.6%) |

A request with one awaited read shows +75 us on SQLite and +148 us on PostgreSQL (noisy). That is
about 70 us and 30 KB per awaited call. A real request touches the scope from more flows than these
three reads (permission resolution, session stores, language lookup), and also spends time outside
the database, so the share on a live host falls between the two columns above.

Retained memory is separate from time. SqlSugar keeps one entry per scope in a static dictionary and
never removes it. Over 10 000 request-shaped scopes the dictionary grew by 10 000 keys and the heap by
about 145 B per request, on both backends. A plain client retained nothing. That is about 145 MB per
million requests. The readiness shape (`DbReadinessCheck`: a synchronous lambda in `Task.Run`, which
takes the `ThreadLocal` path) retained about 2.4 KB per call.

Levers, none chosen:

| Lever | Effect | Constraints |
|---|---|---|
| Keep the per-request scope | The measured cost above | None |
| One singleton `SqlSugarScope` (SqlSugar's intended pattern) | One dictionary key; no per-request config and no `Dispose` client. Measured 3-read request: SQLite 525 us against 552, PostgreSQL within noise. Per-call inner clients and per-access `StackTrace` remain | `AuditAop` captures `ICurrentUserAccessor` at configure time, so it would have to resolve the user per call; a touch from the host's start-up flow would be inherited by every request; the DI lifetime of `ISqlSugarClient` changes |
| Plain `SqlSugarClient` | No scope overhead | Loses the per-flow isolation the factory comment relies on for parallel GraphQL root fields |

The filter and `CancellationAop` attach to the client, not to a request, so the singleton shape
leaves them unchanged.

## Evidence

### Method

A throwaway BenchmarkDotNet 0.15.8 project outside the repository, Release build, referencing
`Struo.Infrastructure` and `Struo.Sample.Blog` by project path. It is not committed.

- Read: `SqlSugarItemRepository.GetByIdAsync("category", id)` on a repository built per call over the
  client under test. Request shapes are `async` methods that await it 1 or 3 times, then `Dispose`.
- Plain path: `new SqlSugarClient(config)` on the scope's `ConnectionConfig`, with the same filter and
  `AuditAop`. `CancellationAop` is a no-op outside SQL Server.
- Flows: N `Task.Run(async () => await read)` flows on one scope, against N separate plain clients.
- Inner clients were counted with a scope built from the same config and a counting configure action.
  Retained memory is `GC.GetTotalMemory(true)` after 10 000 iterations, minus a warmed baseline.
- SQLite: a temporary file. PostgreSQL: `web-struo-cms-test-db` on a local server, read-only
  queries, Npgsql pooling on.
- 5 warm-up and 20 measured iterations, `MemoryDiagnoser`.

Machine: AMD Ryzen 9 9900X (24 logical cores), Windows 11, .NET 10.0.13, SqlSugarCore 5.1.4.221.

### Numbers

| Measurement | SQLite | PostgreSQL |
|---|---|---|
| 1 awaited read + `Dispose`, plain / scope | 126 / 201 us | 1268 / 1417 us |
| 3 awaited reads + `Dispose`, plain / scope | 352 / 552 us | 3764 / 3987 us |
| 3 awaited reads, singleton scope | 525 us / 219 KB | 4068 us / 286 KB |
| 1 flow, scope / plain clients | 208 / 164 us | 1326 / 1299 us |
| 4 flows, scope / plain clients | 325 / 279 us | 2409 / 2326 us |
| 16 flows, scope / plain clients | 498 / 442 us | 7702 / 7534 us |
| Retained per request-shaped scope | 144 B | 145 B |
| Retained per readiness-shaped scope | 2406 B | 2403 B |

The scope's extra time stays nearly flat from 1 to 16 flows on SQLite (44 to 56 us) and its extra
allocation grows by about 10 KB per flow. The PostgreSQL differences are inside noise. The `Create`-only figure is 93 ns against 920 ns for a plain client, and the cost lands at
the first member access.

## Unknowns

- SQL Server, MySQL and MariaDB were not measured. The cost sits in SqlSugar's client creation, so it
  should not depend on the provider, but the share depends on the query time.
- The singleton figures come from a bench that supplies a fixed current user. The cost of resolving
  the user per call from the HTTP context is not measured.
- Today no request path touches the scope in a parent flow and then runs database work concurrently:
  every touch in the pipeline sits inside an awaited `async` callee. The guarantee is a convention. A
  non-async `Task`-returning store method called before `next()` would make parallel GraphQL root
  fields share one connection.

## Referenced from

- `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
