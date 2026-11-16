# Query values are parsed to the column type, and text sets are bound

## Decision

A filter or id value reaches SQL only through `ValueConditions`
(`src/Struo.Infrastructure/Query/ValueConditions.cs`), the one file in `src/` that names
`ConditionalType.In`, `ConditionalType.NotIn` or `ConditionalType.Equal`. It parses each value to
the column's CLR type with the invariant culture and renders the parsed value; the caller's text is
never passed on. The supported column types are `Guid`, `int`, `long`, `short`, `byte`, `bool`,
`decimal`, `double`, `float`, `DateTime`, `DateTimeOffset`, enums and `string`. `_in`/`_nin` on any
other type is a 400; the comparison operators on such a type keep passing the text as a bound value.

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
  every subquery list and the two id lookups that carry an extra filter
  (`WhereInQueries.QueryWhereInFilteredAsync` and `WhereInWindowQueries`).
- `Query:MaxInValues` (default 500, at most 900) bounds the `_in`/`_nin` values of one REST request or
  one GraphQL collection query field, and `Query:MaxRelationLinks` (default 1000) bounds the ids of one
  relation in one write. The link count is checked before the write transaction opens, and a revision
  revert is exempt: its snapshot is the system's own data.
- A text set takes strings: a number or a boolean element, a number or a boolean in place of a list,
  and an empty element of a query-string list on a typed field are each a 400. A string in place of
  a list is split at its commas (`FilterValueList.Elements`).
- A JSON object is no value, alone or as a list element, for any operator: `QueryParser.ReadValue`
  throws a `QueryException` naming the field and the operator, because the value leaves it as plain
  objects and text and no later step could tell an object from a string. A query string carries
  strings only.
- A null value is a 400 for every operator but `_null`/`_nnull`, and the message points to those
  two. A list given to an operator other than `_in`/`_nin` is a 400. GraphQL skips an operator key
  whose value is null before it reaches `ValueConditions`.
- A text column is a `string` property or a column stored as JSON (`ValueTarget.StoredAsJson`, read
  from the ORM's column info). `_contains`, `_starts_with` and `_ends_with` on any other column are
  a 400 that names the field and the operator and lists the operators the column takes, date-time
  columns included. `MetadataScanner.IsTextColumn` (a `string` property, a JSON-column interface or
  `IsJson`) gives the same answer from the entity type, and `Searchable = true` on a column that is
  not text fails the scan with a `MetadataException`; `TextColumnAgreementTests` pins that both
  sides agree for every field of the core and fixture collections.
- Every `Equal` on a value compared as text (a `string`, a JSON column, or a type outside the parsed
  set such as `DateOnly`, `TimeOnly` and `TimeSpan`) is a bound equality, the way a set member is,
  so the word `null` is never a null test on any column. `_eq`, the translation sidecar's locale
  condition (`FilterTranslator.TranslationSubquery`, `TranslationStore`) and the id lookups all go
  through `ValueConditions`; `ValueConditionsConventionTests` fails when another file names
  `ConditionalType.Equal`, as it does for `In` and `NotIn`.
- `ValueTarget.Of` is the one way to describe a column (database name, CLR type, UTC-instant
  binding, stored-as-JSON), so a request filter, an id lookup and the translation store answer "is
  it text" the same way.
- `Searchable = true` on a Repeater sub-field fails the scan too: a sub-field has no column and is
  never searched.
- A `POST /query` body key of the wrong JSON kind (`search`, `limit`, `offset`, an element of `sort`
  or `fields`, also inside `deep`, or a body that is not an object) is a `QueryException` naming the
  key; reading it with `GetString` or `TryGetInt32` would throw `InvalidOperationException`, which
  answers 500. A number with a fraction or beyond the 32-bit range is read as a missing
  `limit`/`offset`, as in the query string.
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
- An `Equal` whose `FieldValue` is the text `null` renders `IS NULL`; one whose `FieldValue` is the
  text `[null]` rewrites `FieldValue` in place, so a second render of the same model differs. A list
  request renders twice (count, then page). The name check renders a copy for that reason. Inside a
  subquery SqlSugar renders the null-test form without the `AND` that joins it to the condition
  before it, so the statement is invalid.
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
- The whole `QueryValueInjection` namespace passes on SQLite, PostgreSQL 17, MySQL 8.4, MariaDB 11.8
  and SQL Server 2022, including every supported type's set, comparison and bad-value cases, the
  booleans as `1`/`0` sets and the refusal of a real name clash.
- The whole `Struo.Tests` project on SQL Server 2022 (CU27): 3711 passed, 225 skipped, 2 failed. The
  two failures are the duplicate language code cases that differ only in letter case, which answer
  `409` under a case-insensitive collation and do not touch a filter. The run was inside an SDK
  container on the container network: from the host, the connection to a throwaway SQL Server
  container timed out at the pre-login handshake on every port and client tried.

Measured 2026-11-13 on main before the change, with scratch probes (not committed) on SQLite 3.53.3,
PostgreSQL 17.11, MySQL 8.4.13, MariaDB 11.8.9 and SQL Server 2022 (16.00.4305), through REST,
`POST /query` and GraphQL:

- `_eq` with `null` rendered `IS NULL`, and `[null]` rendered `= 'null'` the first time and
  `IS NULL` on every later render of the same list. Count and page of one request therefore
  differed: `meta.total` 1 and no row, on all five backends and all three entrances. `_neq` was
  never special.
- `filter[title][_eq]=null&locale=en` on a translatable field, `_eq: "null"` in a `deep` filter and
  a `locale` equal to an existing language code `null` answered 500 on all five backends: the
  rendered subquery was `WHERE Locale = @p Title is null`.
- `_contains`, `_starts_with` and `_ends_with` on integer, boolean, decimal, floating point, `Guid`,
  date-time, enum, `DateOnly` and `TimeOnly` columns answered 500 (`42883`) on PostgreSQL only; the
  other four backends answered 200 with results that differed per backend (SQL Server matched 0 rows
  for `_starts_with "2026"` on a `datetime` column). On a column stored as JSON they ran everywhere.
  A `Searchable` field that is not text made every `search=` request answer 500 on PostgreSQL.
- A JSON object as `_in` was compared as raw text: `{"a":1}` matched a row whose code was that text,
  and `{"a":1,"b":2}` was split at its comma and matched nothing. A null value of `_eq`/`_neq` on a
  typed column compared with the type's default (`= 0`, `= False`) and threw `ArgumentNullException`
  (500) on a `Guid`. A list given to `_eq` on a text column was joined with commas.

Measured 2026-11-16 with a scratch probe (not committed) on a text column through `_contains`,
`_starts_with` and `_ends_with` on SQLite, PostgreSQL 17.11, MySQL 8.4.13, MariaDB 11.8.9 and SQL
Server 2022 (16.0.4305): nothing escapes a value. `%` and `_` are wildcards on all five (a value of
`%` matches every row, `100%` matches `100x`). `[` starts a character class on SQL Server only, so
`[ab]` matches every row containing `a` or `b` there and only the stored text `x[ab]y` elsewhere. A
backslash is an escape character on PostgreSQL, MySQL and MariaDB and a literal on SQLite and SQL
Server; on PostgreSQL an `_ends_with` value of one backslash is refused with `22025` (a 500).

## Unknowns

- Whether a `_contains`, `_starts_with` or `_ends_with` value should have `%`, `_`, `[` and the
  backslash escaped is not decided.
- On SQL Server the injection was not reproduced before the fix, and nothing is measured for the cost
  of the group forms, collation equality of the `=` group against the inline list, or the 2100
  parameter limit with `MaxInValues` raised towards 900 plus the other conditions of a request.
- A fork with a text primary key sends text ids through the group form. A relation array of 1000 text
  ids is an OR chain of 1000, which SQLite refuses; lower `Query:MaxRelationLinks` there.
- An enum column is read as its number: SqlSugar's CodeFirst creates an integer column for one, and the
  enum tests pass on five backends with that. A fork that stores enums as text needs its own filter
  handling.
- Oracle has no instance here.

## Referenced from

- `src/Struo.Infrastructure/Query/ValueConditions.cs`
- `AGENTS.md`
- `docs/ai/architecture.md`
