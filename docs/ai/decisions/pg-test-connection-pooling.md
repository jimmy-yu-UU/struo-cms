# PostgreSQL test suite disables Npgsql connection pooling

## Decision

`PostgresIntegrationTests` disables Npgsql connection pooling via `PgTestConnectionString.DisablePooling`,
so each test gets its own physical connection instead of reusing one from the process-wide pool. An
explicit `Pooling=` setting already present in the caller's connection string is left alone and wins.

## Why

Reuse of a pooled physical connection across a connection-close boundary — whether that boundary falls
between two tests or between two commands inside one test — produces `NpgsqlException: Exception while
reading from stream` → `IOException` → `SocketException` carrying the Windows `WSA_OPERATION_ABORTED`
text ("the I/O operation has been aborted because of either a thread exit or an application request").

If pooling is re-enabled, that abort returns: the suite goes red on one test, not stably the same one
from run to run. The expected casualty is `Resolved_connection_disables_pooling`
(`tests/Struo.Tests/Query/PostgresIntegrationTests.cs`), which asserts the resolved connection string
carries `Pooling=false`.

Taking the pool out is test isolation aimed at the one variable proven to control the failure — the same
category as giving each test its own database. It is not a retry, a swallowed exception, or a relaxed
assertion, and it costs the suite nothing it exists to check: every test still runs real DDL and DML
against a real PostgreSQL, so the type-mapping, column-semantics and concurrency divergences this suite
exists to catch are all still exercised. It does cost the repo its only local reproduction of the abort —
the way back to one is setting `Pooling=true` yourself in the connection string.

Production cannot reach the second-pool amplifier described in Evidence: it binds one connection string,
never calls `DbMaintenance.CreateDatabase()`, and never constructs an `NpgsqlConnection` directly, so it
holds exactly one pool. That bounds the amplifier to the test harness; it does not by itself explain away
the underlying phenomenon, which the single-pool bare host still shows.

## Evidence

Measured 2026-08-04: the failing test passes 5/5 when it is the only test in the process, so its own logic
is not the cause.

Measured 2026-08-04: one run went red on the first test executed, with the pool still empty, so a
preceding test is not required — reuse inside a single test can trigger the abort too.

Measured 2026-08-04: a warm-up connection opened first does not help, ruling out a cold-start effect.

Measured 2026-08-04: PostgreSQL logs no error for these runs (`log_min_messages=warning` would show one)
and the server stays nowhere near `max_connections`, so the abort is raised locally, not by the server.

Measured 2026-08-04: with pooling off the suite is green 5/5; with pooling on it went red in 8 of 9 runs.

Measured 2026-08-04: the same abort and stack trace reproduced at the merge-base `74c5dcc` in 5 of 6 runs,
and which test goes red is not stable — ruling out a branch-specific cause.

Measured 2026-08-05: the same repository code (`SqlSugarItemRepository.CreateGenericAsync`) on a pooled
connection aborted 4 times in 46 runs of a minimal xUnit project — the positive control, close to 9% of
runs.

Measured 2026-08-05: a plain console process across five threading models aborted 0 times in 150 runs.

Measured 2026-08-05: `Struo.Api` booted as Production against real PostgreSQL with pooling on served
22,023 item-API requests with 0 aborts.

Measured 2026-08-05: adding a second Npgsql pool to the bare probe — a second connection string differing
only in `Database=`, the shape `PostgresIntegrationTests`' repo/raw-client builders create by calling
`DbMaintenance.CreateDatabase()` — took its failure rate from 2 of 15 to 15 of 15.

Measured 2026-08-05: an extra pooled open/close on the same database, with no second pool, went 0 of 12.

Measured 2026-08-05: a swallowed server-side error on the same database went 1 of 12.

Measured 2026-08-05: a plain connection to the maintenance database that throws nothing went 11 of 12 —
isolating the second pool, not the extra connection and not the swallowed error, as the amplifier.

Measured 2026-08-05: with the second-pool amplifier set aside, the single-pool bare xUnit host still
failed close to 10% of runs.

Evidence note: the source states the bare host's failure rate two ways — close to 9% where it introduces
the second-pool amplifier (the 4-of-46 positive-control run above) and close to 10% where it confirms the
amplifier is isolated from production — without giving a run count for the second figure or reconciling
the two.

Measured 2026-08-05: three separate renderings of "a thread that exits" outside xUnit stayed green.

## Unknowns

What aborts the socket is inferred from the Windows error code — a thread-exit I/O cancellation, with
xUnit's worker threads the plausible source — and is unproven. The three outside-xUnit renderings that
stayed green (see Evidence) weaken that hypothesis slightly; they do not settle it, because they are a
reconstruction of xUnit's threading, not xUnit's own.

Production has never been observed to hit this abort. That is a non-observation, not proof of safety —
the mechanism is still unknown, so nothing here rules the abort out for a different threading model, load
shape, or Windows build than the ones measured.

Why only one test goes red is itself unexplained: on the run where the abort followed the very first
test, the test executed immediately after it passed, and nothing measured here accounts for that
asymmetry.

## Referenced from

- `tests/Struo.Tests/Support/PgTestConnectionString.cs`
- `AGENTS.md` ("Verification")
