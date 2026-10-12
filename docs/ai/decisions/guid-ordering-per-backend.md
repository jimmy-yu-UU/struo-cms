# Guid ids sort in each database's own order

## Decision

The default item order is `CreatedAt DESC, Id ASC`, and a client sort gets `Id ASC` appended
(`OrderByExpressionBuilder`). `Id ASC` is the order the database itself gives the id column. The
product does not re-sort ids in .NET, and tests that check the tie-break compare against the
database's own `ORDER BY id`, not against `Guid.CompareTo`.

## Why

The tie-break exists for stable paging: two queries over the same rows must return them in the same
order, so `Skip`/`Take` never repeats or drops a row. Any total order on the id gives that.

The databases do not agree on that order:

- SQL Server stores a `Guid` as `uniqueidentifier` and compares its last 6 bytes first, so its order
  differs from .NET's `Guid.CompareTo`, and UUIDv7 ids are not time-ordered there.
- PostgreSQL stores it as `uuid` and compares the bytes as stored. MySQL and MariaDB store it as
  lowercase text in `varchar(36)`; SQLite stores lowercase text in a column declared
  `uniqueidentifier`. On these four the order equals the order of the canonical string, which is
  also .NET's `Guid.CompareTo` order.

## Evidence

- `DefaultOrderingTests.Equal_createdAt_ties_break_on_id_ascending` seeds rows with equal
  `CreatedAt` and asserts the repository returns them in the database's `ORDER BY id` order, twice in
  a row, on the backend `STRUO_TEST_BACKEND` selects.
- `DefaultOrderingTests.Explicit_sort_gets_id_tiebreak_for_stable_pagination` asserts two pages of a
  sort on a non-unique column neither overlap nor repeat a row.
- The storage types above are what SqlSugar's CodeFirst creates for a `Guid` primary key on each
  backend, read back from the column catalog. On SQL Server, 200 mixed v4/v7 ids sorted by the
  database did not match their .NET order; on the other four they did.

## Unknowns

Oracle is not covered. A fork that maps `Guid` to a binary column on MySQL or MariaDB gets the byte
order of that column, which this record does not measure.

## Referenced from

- `tests/Struo.Tests/Query/DefaultOrderingTests.cs`
