# SqlSugarScope costs 50 to 100 microseconds per request, and the factory keeps it

## Decision

`SqlSugarClientFactory.Create` keeps returning a `SqlSugarScope`, built per request, with the
soft-delete filter, `AuditAop` and `CancellationAop` attached in the configure action. Nothing is
optimised. The measured overhead sits inside SqlSugar's own inner-client creation, which this
repository's code does not control. The only lever is a plain `SqlSugarClient`, and that brings back
the races the scope exists to prevent.

## Why

`Create` itself is cheap: 0.09 microseconds and 880 bytes, because the scope builds no inner client
until the first query. The cost is paid at that first query, where SqlSugar builds an inner client and
re-runs the configure action.

Measured against a plain `SqlSugarClient` on the same `ConnectionConfig`, with the same filter and
audit attachments, one single-item read through `SqlSugarItemRepository.GetByIdAsync` costs more:

| Backend | Plain client | Scope | Difference | Share of the scope request |
|---|---|---|---|---|
| SQLite | 126 us / 54.7 KB | 176 us / 75.1 KB | +50 us / +20 KB | 28% |
| PostgreSQL | 1259 us / 57.2 KB | 1324 us / 76.9 KB | +65 us / +20 KB | 4.9% (inside noise) |

Per request the scope adds about 50 to 100 microseconds and 20 KB. On PostgreSQL the confidence
intervals of the two rows overlap, so the share is an upper estimate. On SQLite the share is above
5%, but a read from a local file is the cheapest read a request can contain. The repository call is
also a lower bound of a request: Kestrel, authentication, serialization and the response body are
not in it. At 50 to 100 microseconds, the scope stays under 5% of any request that takes more than
about 1 to 2 milliseconds.

Additional async flows in one scope cost about 60 KB each, the same as a separate plain client, and
about 35 microseconds each on SQLite against 19 for a plain client per flow. The rows are in the
numbers below.

Replacing the scope with a plain client would save the difference and lose the guarantee in the
factory comment: sibling GraphQL resolvers on separate threads each get their own inner client.
`AuditAop` reads `ICurrentUserAccessor` per inner client, so any replacement would have to attach it
to every client it hands out.

## Evidence

### Method

A throwaway BenchmarkDotNet 0.15.8 project outside the repository, a Release build, referencing
`Struo.Infrastructure` and `Struo.Sample.Blog` by project path. It is not committed.

- Entity: the sample `Category`. Read: `GetByIdAsync("category", id)` on a repository built per call
  over the client under test, the single-item read a request makes.
- SQLite: a temporary file, one seeded row. PostgreSQL: the `web-struo-cms-test-db` database on a
  local server, read-only queries, Npgsql pooling on, which is the production default.
- Every process-wide cache (entity info, row binders, JIT) is warmed before measuring.
- Scope path: `SqlSugarClientFactory.Create`. Plain path: `new SqlSugarClient(config)` on the
  scope's own `ConnectionConfig`, plus the soft-delete filter and `AuditAop`. `CancellationAop` is
  a no-op outside SQL Server.
- Flows: N parallel `Task.Run` flows, each running the same read, on one scope, against N separate
  plain clients, which is the lower bound a scope can reach.
- 5 warm-up and 20 measured iterations, `MemoryDiagnoser`.

Machine: AMD Ryzen 9 9900X (24 logical cores), Windows 11, .NET 10.0.13, SqlSugarCore 5.1.4.221.

### Numbers

| Measurement | SQLite | PostgreSQL |
|---|---|---|
| `Create` only, scope | 93 ns / 872 B | 89 ns / 880 B |
| `Create` only, plain | 920 ns / 1.7 KB | 875 ns / 1.8 KB |
| Create + first query, scope | 219 us / 71.6 KB | 1351 us / 74.0 KB |
| Create + first query, plain | 116 us / 53.5 KB | 1211 us / 55.8 KB |
| Query on an already-used scope | 145 us / 66.8 KB | 1277 us / 69.3 KB |
| 1 flow, scope / plain clients | 222 / 163 us | 1277 / 1226 us |
| 4 flows, scope / plain clients | 376 / 283 us | 2274 / 2285 us |
| 16 flows, scope / plain clients | 739 / 445 us | not measured / 7304 us |

The 16-flow scope row on PostgreSQL has no figure. Under BenchmarkDotNet that case threw
`ObjectDisposedException` from Npgsql. Outside it, a loop of 16 parallel flows on a fresh scope per
round hung once in 155 rounds and once in 380 rounds, with pooling on. 4 flows ran 1500 rounds
clean, 16 plain clients ran 1500 rounds clean, and 16 flows on a scope ran 400 rounds clean with
pooling off. The cause is not established here.

## Unknowns

- The 16-flow PostgreSQL hang above. It is separate from the cost question and needs its own look
  before anything relies on 16 concurrent flows on one scope.
- A scope whose request flow has already used it synchronously hands that same inner client to child
  tasks, because the flow's `AsyncLocal` value is inherited. Parallel children then share one
  connection and fail with "Connection already open". A scope untouched by the parent gave every child
  its own inner client.
- SQL Server, MySQL and MariaDB were not measured. The per-request cost is inside SqlSugar's client
  creation, which does not depend on the provider, but the share does depend on the query time.

## Referenced from

- `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
