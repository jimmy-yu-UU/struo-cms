# Query values are parsed to the column type, and text sets are bound

## Decision

A filter or id value reaches SQL only through `ValueConditions`
(`src/Struo.Infrastructure/Query/ValueConditions.cs`), the one file in `src/` that names
`ConditionalType.In` or `ConditionalType.NotIn`. It parses each value to the column's CLR type with
the invariant culture and renders the parsed value; the caller's text is never passed on. The
supported column types are `Guid`, `int`, `long`, `short`, `byte`, `bool`, `decimal`, `double`,
`float`, `DateTime`, `DateTimeOffset`, enums and `string`. `_in`/`_nin` on any other type is a 400;
the comparison operators on such a type keep passing the text as a bound value.

- A set on a number, boolean, `Guid`, date or enum column stays `IN`/`NOT IN` with the rendered
  values (SqlSugar writes them into the statement as literals). A boolean set renders `1`/`0`.
- A set on a text column is a `ConditionalCollections`: one `Equal` per distinct value joined by OR
  for `_in`, one `NoEqual` per value joined by AND for `_nin`. Each value is bound through
  `FieldValueConvertFunc`; `FieldValue` holds a fixed word. Every entry is a new instance and the
  first entry's connector is `And`.
- An empty `_in` matches no row (a group of `IS NULL` and `IS NOT NULL` on the column, never an empty
  group) and an empty `_nin` adds no condition. A null element or a nested list is a 400. A text
  `_nin` inside an `_or` group is a 400; a text `_in` there adds its equalities to the group.
- `ConditionalTree` is not used.
- `ParameterNames.EnsureDistinct` renders a condition list once, on a copy and without running it
  (`ToSql()`), when the list names a column and a column made of it plus trailing digits. The list is
  refused only when the rendered parameters repeat a name. The check sits at the root condition list,
  every subquery list and the id lookup that carries an extra filter.
- `Query:MaxInValues` (default 500, at most 900) bounds the `_in`/`_nin` values of one REST request or
  one GraphQL collection query field, and `Query:MaxRelationLinks` (default 1000) bounds the ids of one
  relation in one write. The link count is checked before the write transaction opens.
- A text set takes text elements only: a number, a boolean or an object element, a scalar in place of a
  list, and an empty element of a query-string list on a typed field are each a 400.
- The id lookups (`WhereInQueries`, `ManyToManySync`, `TranslationStore`, `PurgeOps`) take the column's
  CLR type from the entity and refuse a value that is not of it with an `InvalidOperationException`.
  A relation array is parsed to the target key type in `ItemWriteSideSync` before it gets there.

## Why

SqlSugar renders the values of `In`/`NotIn` into the SQL text. A number is written as it is, any
other value is quoted with only the single quote doubled. A number column therefore takes whatever
text the caller wrote, and on MySQL and MariaDB a backslash turns the closing quote of a text value
into an escaped one. Parsing first removes the first problem for every typed column. Text cannot be
rendered safely, so it is bound.

The group form is the only typed way to bind a text set. `ConditionalTree` was rejected: its parameter
names clash with a group's on the same column, and it takes its connector from its inner first entry.
SqlSugar behaviours the group form has to work around:

- A group's first entry carries the connector between the group and the condition before it.
- An `Equal` whose `FieldValue` is the text `null` renders `IS NULL`; one whose `FieldValue` is the text
  `[null]` rewrites `FieldValue` in place, so a second render of the same model differs. A list request
  renders twice (count, then page). The name check renders a copy for that reason.
- One instance listed twice is numbered by its first position, which drops the closing parenthesis and
  repeats a parameter name.
- An empty group renders nothing: the condition disappears, or the `AND` after it is left dangling.
- A parameter is named after its column plus a position number with nothing between them, so a column
  and a column made of it plus digits can share a name.

The name check is two steps because the clash depends on SqlSugar's numbering, which can change. A
name comparison (a column and the same name plus digits) only decides whether to render. The rendered
parameter list then decides: a pair such as `code` and `code1` that never lands on one name is a valid
query. A guard test fails if SqlSugar stops repeating a name for a forced clash.

`MaxInValues` stops at 900 because a text list of that size is one OR chain of that depth, and SQLite
refuses an expression tree deeper than 1000; SQL Server allows 2100 parameters in a command.

## Evidence

Measured 2026-11-10 with scratch tests (not committed) on SQLite, PostgreSQL 17, MySQL 8.4 and
MariaDB 11.8, a table of 20,000 rows with an index on the column:

- The group form of `_in` still uses the index. With 500 values: SQLite 22.7 ms, PostgreSQL 38.4 ms,
  MySQL 29.8 ms, MariaDB 29.3 ms, against 1.2, 15.6, 5.9 and 5.7 ms for the inline list.
- The group form of `_nin` scans the table. With 500 values: 229.8, 141.0, 282.4 and 161.8 ms, against
  15.3, 331.5, 12.6 and 12.9 ms for the inline list.
- SqlSugar builds a group's SQL in quadratic time: about 10 ms for 500 values, 35 ms for 1000, 0.7 to
  0.8 s for 5000 and 2.4 to 3.1 s for 10,000.
- SQLite refused an OR chain of 1000 (997 was fine, and 998 failed with two other conditions in the
  statement). PostgreSQL refused 66,000 values with its 65,535 parameter limit and ran 10,000.
  MySQL and MariaDB ran 10,000.
- With an `_or` group of `code1 = k1` and a text `_in` of 15 values on `code`, PostgreSQL returned one
  row (id 2) where two matched (ids 1 and 2), and the other three backends refused the statement.

Measured 2026-11-11 with the committed tests under `tests/Struo.Tests/Security/QueryValueInjection`:

- A condition list with `code1` at position 1 and `code` at position 11, and one with a ten-value group
  on `code` beside `code1`, both fail on SQLite with `Must add values for the following parameters`.
- The whole `QueryValueInjection` namespace passes on SQLite, PostgreSQL 17, MySQL 8.4 and MariaDB
  11.8, including every supported type's set, comparison and bad-value cases, and the booleans as
  `1`/`0` sets.

## Unknowns

- SQL Server was not run. Throwaway SQL Server 2022 and 2025 containers accepted connections inside the
  container, but the host connection timed out at the pre-login handshake with three connection
  strings, so nothing is measured there: the boolean `1`/`0` set, collation equality of the `=` group,
  the 2100 parameter limit with `MaxInValues` plus the other conditions of a request, and whether the
  name clash is refused or silently wrong.
- A fork with a text primary key sends text ids through the group form. A relation array of 1000 text
  ids is an OR chain of 1000, which SQLite refuses; lower `Query:MaxRelationLinks` there.
- An enum column is read as its number: SqlSugar's CodeFirst creates an integer column for one, and the
  enum tests pass on four backends with that. A fork that stores enums as text needs its own filter
  handling.
- Oracle has no instance here.

## Referenced from

- `src/Struo.Infrastructure/Query/ValueConditions.cs`
- `AGENTS.md`
- `docs/ai/architecture.md`
