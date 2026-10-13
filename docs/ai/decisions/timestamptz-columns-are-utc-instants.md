# Zone-aware columns hold UTC instants, through a SqlSugar data converter

## Decision

`SqlSugarClientFactory.ApplyColumnShape` attaches `UtcInstantConverter` (an `ISugarDataConverter`)
to every `DateTime`/`DateTime?` property declared `[ColumnShape(ColumnShape.TimestampWithTimeZone)]`,
on PostgreSQL and SQL Server only. The converter binds the write as `DbType.DateTimeOffset`, with a
`Local` value converted to UTC and an `Unspecified` value taken as UTC, and returns every read as
`DateTimeKind.Utc`. MySQL, MariaDB and SQLite get no converter. The factory sets no `Timezone` on the
connection.

Item-API and GraphQL filters on such a property never reach the converter: SqlSugar builds a
`ConditionalModel` parameter from its value alone. `ConditionalModelTranslator.ToUtcInstantModel`
therefore parses an `_eq`, `_neq`, `_gt`, `_gte`, `_lt`, `_lte`, `_in` or `_nin` value as an instant (a
trailing `Z` or offset names it, a value without one is UTC; a query-string `_in`/`_nin` string is
split on commas first, as SqlSugar splits it) and emits it as `datetimeoffset` at `+00:00` on
PostgreSQL and SQL Server, and as the UTC wall clock in Microsoft.Data.Sqlite's text form
(`yyyy-MM-dd HH:mm:ss.FFFFFFF`) on the other backends. SqlSugar renders `IN`/`NOT IN` as inline
literals, so on SQLite the literal must match the stored text exactly. The text operators
(`_contains`, `_starts_with`, `_ends_with`) keep the general path and compare the column's text form.

## Why

SqlSugar binds every `DateTime` parameter as `DbType.DateTime`, and its PostgreSQL provider forces
Npgsql's legacy timestamp behavior. Npgsql 5 sends that as `timestamp` (no zone), ignoring `Kind`,
and PostgreSQL casts it to `timestamptz` in the session `TimeZone`. On read, Npgsql 5 returns a
`timestamptz` as `Kind=Local` in the host zone. So without the converter:

- the stored instant is off by the session zone's offset whenever the server zone is not UTC;
- a value read back is the host-local wall clock, which code treating ticks as UTC (the repository's
  convention) reads as off by the host offset;
- a full-row `Updateable(entity)` writes that `Local` value back as wall-clock digits, so
  `MediaFolder.CreatedAt` moves by (host offset − session offset) on every generic-item update.

SQL Server's provider binds the same `DbType.DateTime` as legacy `datetime`, which rounds to 1/300 s
before the value reaches `datetimeoffset(7)`. Binding `DateTimeOffset` removes that rounding.

MySQL `datetime(6)` and SQLite text never consult a zone: the wall-clock digits go in and come out
unchanged, so the UTC values the product writes stay UTC there.

Alternatives not taken:

- `Timezone=UTC` on the Npgsql connection makes the stored instant right but leaves reads at
  `Kind=Local`, so the host-offset error and the `CreatedAt` drift remain.
- `SqlParameterDbType = DbType.DateTimeOffset` without a converter fixes entity binds and `Where`,
  but `SetColumns(s => new T { … })` still binds `timestamp`, and reads stay `Kind=Local`.
- `DateTimeOffset` properties would change `IAuditable.CreatedAt` on `AuditableEntity`, the
  `IUserSessionStore` contract, the JSON shape and the MySQL/SQLite mapping.
- An Npgsql 6+ upgrade alone keeps the legacy behavior SqlSugar forces; turning that off changes
  every plain `timestamp` column and throws on non-UTC `Kind`.

## Evidence

Probe on SqlSugarCore 5.1.4.221, Npgsql 5.0.18, PostgreSQL 17, host UTC+8, 2026-10-13: with server
`TimeZone` UTC the stored instant was right and the read was `+8h, Kind=Local`; with
`Asia/Taipei` every row was stored 8 h early and read back as the UTC digits. A full-row update
round trip moved the value +8 h per update on the UTC server. With the converter, `Insertable`,
`Updateable(entity)`, `UpdateColumns`, `SetColumns(s => new T { … })`, `SetColumns(s => s.A == v)`
and `Where(s => s.A < v)` all bound `TimestampTz` and read `Kind=Utc`.

The 57 tests in `UtcInstantColumnTests`, `MediaFolderTests`, `SqlSugarUserSessionStoreTests`,
`SiteSettingsStoreTests`, `UtcInstantJsonTests` and `ConditionalModelTranslatorTests` pin this on the
backend `STRUO_TEST_BACKEND` selects (host UTC+8, 2026-10-13). Without the converter and
`ToUtcInstantModel` they failed 10 on SQLite, 8 on MySQL 8.4 and MariaDB 11.8, 20 on SQL Server 2025,
and 22 on PostgreSQL 17 (server `TimeZone` UTC and `Asia/Taipei`) and PostgreSQL 13.2. With both, all
57 pass on each of those seven configurations. The whole suite then passes on SQLite, MySQL, MariaDB,
SQL Server and PostgreSQL 13.2.

The filter failures follow SqlSugar's parsing: a `Z` or offset string parses to host-local time, a
zone-less one is cast in the PostgreSQL session zone, SQL Server's legacy `datetime` rounding breaks
`_eq`, and a query-string `_in` arrives as one comma-separated string.

The data-correction statements in the 0.9.2 changelog were run on PostgreSQL 17 with server
`TimeZone` `Asia/Taipei`, against rows written by 0.9.1 code into the three core tables. Before the
statements the converter read every value 8 h early; after them, every value read back as the
instant 0.9.1 meant to write, also with the server switched back to UTC.

## Unknowns

The converter runs only where SqlSugar maps a parameter or a reader column through the entity's
column info. Measured on this suite's backends:

- Covered: `Insertable` of one entity and of a list, `Updateable(entity)`, `SetColumns` member-init
  and `==` forms, lambda `Where`, and entity reads (`UtcInstantColumnTests`).
- Not covered: aggregates. `AggregateQueries` selects into `AggregateRow`, so min/max of such a column
  come back `Kind=Local` (the right instant) on PostgreSQL and throw `InvalidCastException` on SQL
  Server, where `AggregateValueNormalizer` meets a `DateTimeOffset`. The SQL Server throw does not
  depend on the converter. Any other `Select` projection into a non-entity type reads the same way.
- Not covered: `SetColumns(s => s.A, value)` (the column-plus-value overload) still binds `timestamp`
  on PostgreSQL. SqlSugar's bulk-copy and `Fastest` paths are unverified. Nothing in `src/` uses
  either on these columns.
- Oracle has no instance here, so the converter is not attached there and Oracle's behavior is
  unverified.

## Referenced from

- `src/Struo.Infrastructure/Persistence/UtcInstantConverter.cs`
- `AGENTS.md`, "Verification"
