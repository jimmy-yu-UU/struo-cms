# Query values are parsed to the column type, and text sets are bound

## Decision

A filter or id value reaches SQL only through `ValueConditions`
(`src/Struo.Infrastructure/Query/ValueConditions.cs`), the one file in `src/` that names
`ConditionalType.In`, `NotIn`, `Equal`, `Like`, `LikeLeft` or `LikeRight`. It parses each value to
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
  from the ORM's column info: a multi-value interface, a structured type on `Hidden`, `IsJson`).
  `_contains`, `_starts_with` and `_ends_with` on any other column are a 400 that names the field
  and the operator and lists the operators the column takes, date-time columns included.
  `MetadataScanner.IsTextColumn` (a `string` property, a multi-value interface, a structured type or
  `IsJson`) gives the same answer from the entity type, and `Searchable = true` on a column that is
  not text fails the scan with a `MetadataException`. `Searchable = true` together with
  `Hidden = true` fails it too, whatever the property type: a hidden field is dropped from the
  search whitelist and from the admin filter list, so the declaration does nothing; a field on the
  `Hidden` interface without the flag joins the whitelist and follows the text rule.
  `TextColumnAgreementTests` pins that both sides agree for every field of the core and fixture
  collections, the `Hidden` structured ones included.
- Every `Equal` on a text column (a `string` or a column stored as JSON) is a bound equality, the
  way a set member is, so the word `null` is never a null test there. On a column of a type outside
  the parsed set that is not text (`DateOnly`, `TimeOnly`, `TimeSpan`) the value goes into the
  condition as written, except that `_eq` with `null` or `[null]` is a 400 pointing to
  `_null`/`_nnull`: the ORM reads exactly those two texts (case-sensitive) of an `Equal` as a null
  test and cannot be told otherwise. No `[CmsField]` property can have these types (startup rejects
  them), so only a many-to-one foreign-key property reaches this path.
  `_eq`, the translation sidecar's locale condition (`FilterTranslator.TranslationSubquery`,
  `TranslationStore`) and the id lookups all go through `ValueConditions`.
  `ValueConditionsConventionTests` reads the text of `src/` and fails on another file that names
  `ConditionalType.In`, `NotIn`, `Equal`, `Like`, `LikeLeft` or `LikeRight` (also with the
  `SqlSugar.` or `global::` qualifier or a verbatim `@`), imports or aliases the enum, sets
  `ConditionalType = default`, or builds a `ConditionalModel` initializer, typed or target-typed,
  without a `ConditionalType`. Two files that set `CustomConditionalFunc` are exempt from the
  initializer rule, because the ORM reads that function before the condition type. The scan cannot
  see a cast from a number, a condition type chosen through reflection or `Enum.Parse`, a condition
  type assigned in a separate statement, a model filled in after `new ConditionalModel()`, or braces
  inside verbatim, raw or interpolated strings or comments of an initializer body. An anonymous
  `new { }` and a generic `where T : new()` constraint are not read as models.
- `ValueTarget.Of` is the one way to describe a column (database name, CLR type, UTC-instant
  binding, stored-as-JSON), so a request filter, an id lookup and the translation store answer "is
  it text" the same way.
- The value of `_contains`, `_starts_with` and `_ends_with` is matched as plain text on SQLite,
  PostgreSQL, MySQL, MariaDB and SQL Server, on a `string` column and on a column stored as JSON,
  and so is the term of the built-in database `search`, which reaches the same code.
  `ValueConditions.Like` escapes the characters a LIKE pattern reads, by
  `ValueTarget.DbType`, and the ORM builds the condition from the escaped value:

  | Backend | Value | Reaches the statement as |
  |---|---|---|
  | PostgreSQL, MySQL, MariaDB | `\`, `%`, `_` get a backslash | the ORM's LIKE, value bound |
  | SQL Server | `[`, `%`, `_` become `[[]`, `[%]`, `[_]` | the ORM's LIKE, value bound |
  | SQLite | no `%` and no `_` | the ORM's LIKE, value bound |
  | SQLite | has `%` or `_` | a typed subquery, below |
  | Oracle | as written | the ORM's LIKE; not measured |

  The ORM's `Utilities.EscapeLikeValue` is not used: it never escapes the backslash, so a backslash
  before a wildcard leaves a live wildcard on PostgreSQL, MySQL and MariaDB, and on SQL Server it
  escapes the output of its own previous character. A `ConditionalModel` renders no ESCAPE clause
  and has no member to set one, and SQLite has no default escape character, so a pre-escaped value
  cannot work there. A typed `Contains`, `StartsWith` or `EndsWith` on the column does render
  `escape '\'`, but SqlSugar 5.1.4.221's SQLite method does so only when the value holds the two
  characters backslash and `%`, or backslash and `_`. `LiteralLikeSubquery`
  (`src/Struo.Infrastructure/Query/LiteralLikeSubquery.cs`) therefore builds that expression only
  for a value with `%` or `_`, escaped with backslashes, which always holds such a pair; a value
  with neither takes the plain path, because a blanket escape would turn a lone backslash into a
  wrong match. The expression runs as `key IN (SELECT key FROM entity WHERE ...)`, wrapped by
  `SubQueryConditional`, which renames the subquery's parameters; the entity's global soft-delete
  filter is cleared inside it, because the outer query applies its own. The key is
  `EntityDescriptor.IdProperty`, so a translation sidecar or junction collection works the same way.
  A column that is stored as JSON and is not a `string` property uses `ToString()`, which the ORM
  renders as `CAST(column AS TEXT)`. A value with `%` or `_` on a `ValueTarget` that carries no
  entity is an `InvalidOperationException`, never a wildcard match. `LikeLiteralConditionTests` pins
  the behaviour on whichever backend runs it: 16 values by 3 operators on a text and a JSON column
  against an ordinal oracle read from the stored text, which a change to the ORM's SQLite method
  fails; `LikeLiteralApiTests` and `LikeLiteralTranslatorTests` pin every entrance and group.
  Escaping does not change case or accent behaviour: SQLite folds ASCII case on both of its paths,
  PostgreSQL is case-sensitive, and MySQL, MariaDB and SQL Server fold case under their default
  collations. An empty value matches every row that is not null.
- `Searchable = true` on a Repeater sub-field fails the scan too: a sub-field has no column and is
  never searched.
- A `[CmsField]` on a property with `[SugarColumn(IsIgnore = true)]` has no column and carries
  `FieldMetadata.Ignored`. `QueryValidator`, `FacetPath` and `RelationPath` leave it out of the
  filter, sort, facet, aggregate, relation-leaf and junction-leaf allowlists, and `SearchableFields`
  skips it; field selection still accepts it. `Searchable = true` or `Sortable = true` on one fails
  the scan, and the GraphQL filter input type has no entry for it.
- A `POST /query` body is read as follows. The body is an object, and `filter` and `deep` (and each
  relation entry inside `deep`) are objects, `sort` and `fields` arrays of strings, `search` a
  string, `limit` and `offset` numbers, the same inside `deep`; anything else is a `QueryException`
  naming the key and, inside `deep`, the relation path. A key that is ignored would return
  unfiltered rows to a caller who believes the result is filtered, and reading a wrong kind with
  `GetString` or `TryGetInt32` would throw `InvalidOperationException`, which answers 500. For the
  same reason a key the parser does not read is a 400: the body accepts `fields`, `filter`, `sort`,
  `limit`, `offset`, `search`, `deep`, `facets` and `aggregate`, a `deep` relation entry (at any
  depth) accepts `fields`, `filter`, `sort`, `limit`, `offset` and `deep`. Keys are matched as
  written, case-sensitively, and a key with a `null` value is unknown too. One list per level in
  `QueryParser` decides the unknown key and fills the message, which repeats at most 64 characters
  of the key, cut between characters. Field and relation names are validated elsewhere, and an
  unknown aggregate operation or filter operator is a 400 there. JSON `null` for a known key means
  the key is not given; a `null` relation entry in `deep` is the same as `{}`. An element of
  `_and`/`_or` that is not an object is a 400 naming the group. A number with a fraction or beyond
  the 32-bit range is read as a missing `limit`/`offset`, as in the query string. A repeated key
  takes the last value.
- Text that cannot be read as text is a 400. System.Text.Json throws `InvalidOperationException`
  from a property name or a string value that holds an escaped lone surrogate, so `ParseEnvelope`
  walks the whole body once, before it reads any name or string, and answers with a `QueryException`
  that does not repeat the text. The walk covers every property name and every string value at any
  depth: body keys, filter field and operator names, `deep` relation names, aggregate operations,
  and the strings of `search`, `fields`, `sort`, `facets`, filter values and `_in` elements. It is
  the only place that catches that exception. A query string and GraphQL do not go through it.
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

Measured 2026-11-16 with a scratch probe (not committed) on a text column and a JSON column through
`_contains`, `_starts_with` and `_ends_with` on SQLite, PostgreSQL 17, MySQL 8.4, MariaDB 11.8 and
SQL Server 2022 (default collation `SQL_Latin1_General_CP1_CI_AS`): a value the ORM passes on as it
is, `%` and `_` are wildcards on all five (a value of `%` matches every row, `100%` matches
`100x`). `[` starts a character class on SQL Server only. A backslash is an escape character on
PostgreSQL, MySQL and MariaDB and a literal on SQLite and SQL Server; on PostgreSQL an
`_ends_with` value of one backslash is refused with `22025`. The probe ran 16 values (`%`, `_`,
`[ab]`, `[`, a backslash, `d` and a backslash, a backslash and `%`, `^`, `'`, two quotes, the empty
string, one CJK character, 200 letters, `a_c`, `100%`, two backslashes) by 3 operators by 2 columns,
96 cases per backend. Its second column was a string-typed twin of the JSON column's table, so it
read the stored text; with the escapes above every case was a literal match on all five backends.
A JSON property that is not a `string` is not part of that probe. `LikeLiteralConditionTests` pins
it: `List<string>`, a class, an array and a dictionary match as plain text, which on SQLite the ORM
renders as a cast to text. The same class runs the 16-value matrix against the stored text on the
backend that executes it and passed on SQLite, PostgreSQL 17, MySQL 8.4, MariaDB 11.8 and SQL
Server 2022.

Measured 2026-11-16 with a scratch probe (not committed) on a real table with `DateOnly`, `TimeOnly`
and `TimeSpan` columns, through `_eq` with the value passed on as written, on SQLite, PostgreSQL
17.11, MySQL 8.4.13, MariaDB 11.8.9 and SQL Server 2022 (16.0.4305):

| Backend | Date, time and duration values | `NULL` as a date |
|---|---|---|
| SQLite | the time and the duration match; the date text finds no row | no row |
| PostgreSQL | every value fails with `42883` | `42883` |
| MySQL | all three match | `Incorrect DATE value` |
| MariaDB | all three match | no row |
| SQL Server | all three match | `Conversion failed` |

PostgreSQL names `date = text` and `time = text` in the `42883` message. The words `null` and
`[null]` were not sent on these backends: the ORM reads exactly those two texts of an `Equal` as a
null test, so they are refused before the statement.

## Unknowns

- A `DateOnly`, `TimeOnly` or `TimeSpan` column is reachable only as a many-to-one foreign-key
  property, because startup rejects those types on a `[CmsField]` property. Filter values for such a
  column are passed on as written, and on PostgreSQL every `_eq` on one fails with `42883`.
- Not measured for the plain-text match: MySQL with `NO_BACKSLASH_ESCAPES` in `sql_mode` (the
  backslash escape would not apply), SQL Server databases with a case-sensitive or accent-sensitive
  collation, and Oracle, which has no instance here and takes the ORM's LIKE with the value as
  written. A SqlSugar upgrade can change the SQLite method the typed path depends on.
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
- `src/Struo.Infrastructure/Query/LiteralLikeSubquery.cs`
- `AGENTS.md`
- `docs/ai/architecture.md`
