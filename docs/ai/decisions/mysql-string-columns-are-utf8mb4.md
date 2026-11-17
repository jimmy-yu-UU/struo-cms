# MySQL and MariaDB string columns are utf8mb4

## Decision

On MySQL and MariaDB, `AsString(n)` and `AsFixedLengthString(n)` render types without a character
set, so a column takes the default of its table, which for a new table is the database's, utf8mb4 on
a database created as the manual says. `migrate` refuses a database or a core table whose default is
not utf8mb4 (below). The
migration runner's MySQL type map is `Utf8Mb4MySqlTypeMap`
(`src/Struo.Infrastructure/Migrations/Utf8Mb4MySqlTypeMap.cs`), a subclass of FluentMigrator's
`MySql8TypeMap` that restates every key of `DbType.String` and `DbType.StringFixedLength`.
`MigrationDialect.Register` installs it for the `MySql` dialect, so every runner built through it is
covered; `MigrationHostOptions.ConfigureRunnerServices` runs afterwards and can put the framework's
map back, which the tests do to build a database the way the previous release did.

The core migration `ConvertStringColumnsToUtf8Mb4` (`202611170000`) re-declares the 21 core string
columns and the version table's `description` with `Alter.Column(...).AsString(n)`, restating length
and nullability. It produces no expression on any other backend. `Down` does nothing.

On MySQL and MariaDB, `MigrationHost.ApplyAsync` (the path of `migrate` and of
`Database:MigrateOnStartup`) refuses before it applies anything. When migrations are pending it
reads the database's default character set and collation and the default collation of every core
table that exists, the framework tables under `TablePrefix` and the version table, and refuses when
one is not utf8mb4. A core table that does not exist yet is skipped, a new empty database is judged
on its default alone, a database whose `SCHEMATA` row is not visible is judged on its tables alone,
and a fork's tables are not part of it (maintainer ruling). Only the catalog read sits inside the
`try` that reports "Could not inspect the database"; the decision is made outside it. The refusal is
a `MigrationFailedException`: the CLI prints `migrate failed: <message>` and exits 1, and startup
terminates the host. Nothing is applied, no version row is written and no table or column changes.
The message opens with one clause: migrations on MySQL and MariaDB need utf8mb4 as the default
character set of the database and the framework tables, the items below do not have it, so nothing
was applied or changed. It then names the database and its default character set when that offends,
each offending table with its default collation, and gives the statements. When the database itself
offends: `ALTER DATABASE ... CHARACTER SET utf8mb4` and one
`ALTER TABLE ... CONVERT TO CHARACTER SET utf8mb4` per listed table, without `COLLATE` (both end at
the character set's default). When only tables offend in a utf8mb4 database: one
`ALTER TABLE ... CONVERT TO CHARACTER SET utf8mb4 COLLATE <database default collation>` per listed
table, so the converted tables and the tables later migrations create share one collation. When the
`SCHEMATA` row is not visible, or its default collation is null, the table statements carry no
`COLLATE`. The last two lines give the statement for a fork's own tables that do not default to
utf8mb4, `ALTER TABLE ... CONVERT TO CHARACTER SET utf8mb4` with the same `COLLATE` suffix as the
table statements (none when the database itself offends), and say that `migrate:check` lists their
columns. `Utf8Mb4Defaults` decides and builds the message; `DefaultCharacterSetCatalog` only reads.
The `migrate:check` warning quotes the table name with the same helper and gives
`ALTER TABLE <table> CONVERT TO CHARACTER SET utf8mb4`, followed by `COLLATE` and the database
collation when the database is utf8mb4 with a collation other than the default of utf8mb4; it names
no collation of another character set, which the server rejects after `utf8mb4`.
`migrate:status`, `migrate:preview`, `migrate:check`,
`migrate:baseline` and `make:migration` do not run it.

`migrate:check` reads each entity table's column character sets and collations with one typed
query on `information_schema.COLUMNS` (`CharacterSetCatalog`) and warns
(`FindingKind.NonUtf8Mb4String`) for a column an entity declares as String or LongText whose
character set is not utf8mb4. The warning never fails the check.
A fork's own tables are not converted by `migrate`: the warning names them and chapter 24 gives the
statement.

The two reads of `migrate` are typed queries of the same form on `information_schema.SCHEMATA`
(`DEFAULT_CHARACTER_SET_NAME` and `DEFAULT_COLLATION_NAME` of the connected schema) and
`information_schema.TABLES` (`TABLE_NAME` and `TABLE_COLLATION` of the tables of the connected
schema). Together with the `COLUMNS` read that makes three.

The `AS("information_schema.<TABLE>")` call is the only string in each query; the schema and the
table name are bound parameters. The maintainer approved exactly this form; it is not a raw SQL
statement and no other string overload is approved.

## Why

Both servers store a national type (`NVARCHAR`, `NCHAR`) as utf8mb3 whatever the database default
is, and the framework map renders `AsString` as a national type: 21 core columns, the version
table's `description`, and every `AsString` column of a fork. A supplementary-plane character, an
emoji for example, can then neither be stored (`Incorrect string value`, an `INTERNAL_SERVER_ERROR`)
nor compared with the column (`Illegal mix of collations`).

The alternatives that were measured and not taken:

- A helper such as `AsStringU(n, Db)` that calls `AsCustom("varchar(n)")` on MySQL: every migration
  of every fork would have to call it, and `MigrationGenerator` and its golden files would change.
- `AsString(n, collation)`: the column stays utf8mb3 and the server refuses any utf8mb4 collation;
  it also writes `COLLATE` into the DDL of every backend.

Replacing the map changes the one place that decides, so a fork's existing `AsString` calls need no
edit.

## Evidence

MySQL 8.4.13 and MariaDB 11.8.9, databases created `CHARACTER SET utf8mb4`.

DDL of one column, `AsString(n)`, framework map against the replacement:

| Size | Framework map | Replacement |
|---|---|---|
| none, 1 to 8192 | `NVARCHAR(n)` (255 when none) | `VARCHAR(n)` (255 when none) |
| 8193 to 65535 | `TEXT CHARACTER SET utf8` | `TEXT` |
| 65536 to 16777215 | `MEDIUMTEXT CHARACTER SET utf8` | `MEDIUMTEXT` |
| above 16777215 and `int.MaxValue` | `LONGTEXT CHARACTER SET utf8` | `LONGTEXT` |

`AsFixedLengthString(n)`: `NCHAR(n)` for none and up to 255, then the same `TEXT` family as above;
the replacement renders `CHAR(n)` and the same `TEXT` family without a character set. The framework
map has five templates (keys -1, 8192 or 255, 65535, 16777215, `int.MaxValue`) per text type;
overwriting only some keys leaves `NVARCHAR` for the others, so the subclass restates all ten and
`Utf8Mb4TypeMapTests` compares the key set with the framework's by reflection. The DDL of SQLite,
PostgreSQL and SQL Server is unchanged: `Utf8Mb4TypeMapTests` pins one statement per backend.

Column state after applying the core set to a database created as utf8mb4:

| Core migrations applied with | utf8mb3 columns | utf8mb4 columns |
|---|---|---|
| framework map | 22 (21 core, `struo_schema_versions.description`) | 36 |
| replacement map | 0 | 58 |

The version table's `description` is `NVARCHAR(1024)` under the framework map; a migration can alter
it on both servers.

Conversion of an existing column with `Alter.Column(name).OnTable(t).AsString(n)` under the
replacement map renders `ALTER TABLE ... MODIFY COLUMN ... VARCHAR(n) NOT NULL` or `... NULL`:

- A primary key on a string column, unique indexes (single and composite) and nullability are kept
  when the statement restates nullability; data is identical before and after (ASCII, CJK, a BMP
  symbol, a trailing space).
- A default the statement does not restate is dropped. No core string column has a default.
- A conversion is not transactional. A unique collision stops the run at the first `MODIFY` that
  collides; the columns converted before it stay converted, the migration is not recorded, and a
  rerun re-declares the converted columns without change. On MySQL,
  `Duplicate entry 'strasse' for key '<prefix>roles.ux_<prefix>roles_name'` is the text `migrate`
  prints for the values `ss` and the German sharp s in a unique column; `Utf8Mb4CollisionLiveTests`
  runs it.
- The pair `ss` and the sharp s is different under `utf8mb3_general_ci` and `latin1_swedish_ci`
  (MySQL and MariaDB) and equal under `utf8mb3_uca1400_ai_ci` (the MariaDB 11.8 default for
  utf8mb3); a MariaDB table that is `utf8mb3_general_ci` converts with a collision like MySQL does
  (below).

Comparison semantics of a string column, from `=` on hex-built values:

| Pair | MySQL utf8mb3 | MySQL utf8mb4 | MariaDB utf8mb3 (uca1400) | MariaDB utf8mb4 |
|---|---|---|---|---|
| `a` and `a` plus a trailing space | equal | different | equal | equal |
| sharp s and `ss` | different | equal | equal | equal |
| sharp s and `s` | equal | different | different | different |
| `a` and `A`, `e` and `e` with acute | equal | equal | equal | equal |
| full-width `A` and `A` | different | equal | equal | equal |
| `a` and `a` plus U+200B | different | equal | equal | equal |

MySQL compares with `utf8mb3_general_ci` (PAD SPACE) before the conversion and `utf8mb4_0900_ai_ci`
(NO PAD) after it: a unique index treats a trailing space as a difference, and the sort order of
punctuation differs. A MariaDB column that is already in the `uca1400_ai_ci` family stays in it: no
row compares differently and the sort order is the same. A MariaDB `utf8mb3_general_ci` column does
not (next table).

Five transitions, measured with hex-built values. Each cell is "equal before, equal after"; the
last row asks whether `a` sorts before `_`:

| Pair | A | B | C |
|---|---|---|---|
| `a` and `a` plus a trailing space | yes, no | yes, yes | yes, yes |
| sharp s and `ss` | no, yes | no, yes | yes, yes |
| sharp s and `s` | yes, no | yes, no | no, no |
| full-width `A` and `A` | no, yes | no, yes | yes, yes |
| `a` and `a` plus U+200B | no, yes | no, yes | yes, yes |
| `a` sorts before `_` | yes, no | yes, no | no, no |

| Pair | D | E |
|---|---|---|
| `a` and `a` plus a trailing space | yes, no | yes, yes |
| sharp s and `ss` | no, yes | no, yes |
| sharp s and `s` | no, no | no, no |
| full-width `A` and `A` | - | - |
| `a` and `a` plus U+200B | - | - |
| `a` sorts before `_` | yes, no | yes, no |

A is MySQL 8.4 `utf8mb3_general_ci` to `utf8mb4_0900_ai_ci`; B is MariaDB 11.8
`utf8mb3_general_ci` to `utf8mb4_uca1400_ai_ci`; C is MariaDB 11.8 `utf8mb3_uca1400_ai_ci` to
`utf8mb4_uca1400_ai_ci`; D is MySQL 8.4 `latin1_swedish_ci` to `utf8mb4_0900_ai_ci`; E is MariaDB
11.8 `latin1_swedish_ci` to `utf8mb4_uca1400_ai_ci`. The trailing-space row changes on MySQL only
(A and D), not on MariaDB (B and E). In B, a MariaDB `utf8mb3_general_ci` table with a unique column
holding `ss` and the sharp s stops `CONVERT TO` with `Duplicate entry 'ss' for key 'ux_t_name'` and
keeps its two rows, like A.

Starting from latin1 (D and E), measured on both servers: a latin1 column cannot store the
full-width letter or U+200B (error 1366, `Incorrect string value`), which is why those rows have a
dash. A latin1 unique column holding `ss` and the sharp s stops `CONVERT TO` with
`Duplicate entry` naming the sharp s and keeps its two rows, as in A and B. Any other starting
collation is covered by the duplicate-value query of the changelog. An `ascii`, a `latin1` and a
utf8mb3 column of one table are all utf8mb4 after `CONVERT TO CHARACTER SET utf8mb4`, with the new
default collation of the table (`utf8mb4_0900_ai_ci` on MySQL, `utf8mb4_uca1400_ai_ci` on
MariaDB), so a column declared on purpose as `ascii` is converted too.

Collation per path, measured in a `utf8mb4_unicode_ci` database whose tables are utf8mb3:

| Path | MySQL 8.4 | MariaDB 11.8 |
|---|---|---|
| `CONVERT TO`, no `COLLATE` | `utf8mb4_0900_ai_ci` | `utf8mb4_uca1400_ai_ci` |
| `CONVERT TO ... COLLATE utf8mb4_unicode_ci` | `utf8mb4_unicode_ci` | `utf8mb4_unicode_ci` |
| `MODIFY COLUMN` in a unicode_ci or bin table | the table's | the table's |

`CONVERT TO` without `COLLATE` gives the character set's default whatever the database collation,
so a table converted that way sits beside tables created later with the database's collation, and
comparing their columns raises `Illegal mix of collations`. The statements of the refusal (when the
database is utf8mb4) and of the chapter 24 query end in `COLLATE <database default collation>`.
The query run before `ALTER DATABASE` on a latin1 database prints
`... CONVERT TO CHARACTER SET utf8mb4 COLLATE latin1_swedish_ci`, which fails on both servers with
error 1253 (`COLLATION 'latin1_swedish_ci' is not valid for CHARACTER SET 'utf8mb4'`); run after
`ALTER DATABASE ... CHARACTER SET utf8mb4` it prints `COLLATE utf8mb4_0900_ai_ci` (MySQL) or
`COLLATE utf8mb4_uca1400_ai_ci` (MariaDB) and the statement succeeds, which is why the chapter runs
`ALTER DATABASE` before the message's statements and before the query. A `TEXT` column of a latin1
or utf8mb3 table becomes `MEDIUMTEXT` under `CONVERT TO` on both servers; the core tables have no
`TEXT` column; a fork's `AsString(8193..65535)` or `AsFixedLengthString(256..65535)` column is one,
and `migrate:check` reports nothing for it afterwards (`Utf8Mb4TextWideningLiveTests`).

Catalog read: `DbMaintenance.GetColumnInfosByTableName` returns the same `DbColumnInfo` for a
utf8mb3 and a utf8mb4 column, so it cannot show the difference. The typed query returns
`COLUMN_NAME` and `CHARACTER_SET_NAME` of every column on both servers. The table collation shows
nothing on a database whose tables are utf8mb4 but whose string columns are utf8mb3, which is why
`migrate:check` reads the columns and `migrate` reads the table defaults: a column takes the
default of its table.

Index key length: the longest core key is `struo_revisions` (two 255-character columns and a
bigint), 2048 bytes under utf8mb4; MySQL's limit is 3072 and all core indexes are created on both
servers.

Table default character set: `MODIFY COLUMN ... VARCHAR(n)` without a character set takes the
table's default. Measured on both servers, with two tables holding `NVARCHAR` columns, one with
default latin1 and one with default utf8mb3, in a utf8mb4 database:

| Table default | ASCII-only column after `MODIFY` | Column holding CJK after `MODIFY` |
|---|---|---|
| latin1 | becomes latin1 | fails with 1366; column stays utf8mb3 |
| utf8mb3 | stays utf8mb3 | stays utf8mb3, text unchanged |

A migration that did this would convert nothing in a utf8mb3 database and would stop partway in a
latin1 database, because the run is not transactional, which is why `migrate` refuses such a
database before it applies anything. A new `AsString` column in a latin1 database is latin1 for the
same reason.

`information_schema.TABLES` has `TABLE_COLLATION` and no character set. Measured on MySQL 8.4.13 and
MariaDB 11.8.9, tables created with each default:

| Table default | MySQL `TABLE_COLLATION` | MariaDB `TABLE_COLLATION` |
|---|---|---|
| none, in a utf8mb4 database | `utf8mb4_0900_ai_ci` | `utf8mb4_uca1400_ai_ci` |
| utf8mb4 | `utf8mb4_0900_ai_ci` | `utf8mb4_uca1400_ai_ci` |
| utf8mb3 | `utf8mb3_general_ci` | `utf8mb3_uca1400_ai_ci` |
| utf8 | n/a | `utf8mb3_uca1400_ai_ci` |
| latin1 | `latin1_swedish_ci` | `latin1_swedish_ci` |

Tables created with an explicit collation report it as given: `utf8mb4_general_ci`,
`utf8mb4_unicode_ci` and `utf8mb4_bin` on both servers, and on MariaDB `utf8mb4_uca1400_as_cs`,
`utf8mb4_uca1400_nopad_ai_ci` and `utf8mb4_nopad_bin`. An ascii, binary and utf16 table reports
`ascii_general_ci`, `binary` and `utf16_general_ci` (MariaDB `utf16_uca1400_ai_ci`).

Every collation name starts with the name of its character set up to the first underscore, and
`binary` has none, so the rule is the text before the first underscore (the whole name when there is
none). `information_schema.SCHEMATA` returns `DEFAULT_CHARACTER_SET_NAME` directly: `utf8mb4` on
both servers for a utf8mb4 database.

Refusal and the written sequence of chapter 24, run through the CLI on MySQL 8.4.13 and MariaDB
11.8.9 against three databases each: default utf8mb3, default latin1, and a database altered to
`utf8mb4 COLLATE utf8mb4_unicode_ci` whose tables are utf8mb3. The fixtures hold the core migrations
up to `SeedCoreData` applied with the framework type map and the version table removed, with CJK
text in `struo_roles.name` and `description` and in `struo_revisions.CollectionName`. After
`migrate:baseline`, `migrate` exits 1. The message lists the database only when its default is not
utf8mb4 and `ALTER DATABASE` with it; the sequence runs `ALTER DATABASE` only then, then the query
of the chapter, which prints one `ALTER TABLE ... COLLATE <database collation>` per table (11 or 12
tables; the version table exists after the baseline and is listed when it is not utf8mb4). After the
statements every table's default collation equals the database's, `migrate` applies the four pending
migrations, `migrate:check` prints `0 error(s), 0 warning(s)`, no column is outside utf8mb4, no
column's collation differs from the database's, and the stored bytes of the CJK text are identical
before and after. A refusal on an empty latin1 database lists the database only; with
`Database:MigrateOnStartup` the host terminates with the same message.

`CONVERT TO CHARACTER SET utf8mb4` of a table that holds the values `ss` and the German sharp s in a
unique column fails on MySQL 8.4 with `Duplicate entry` naming the value and the index and leaves
the table unchanged; a MariaDB `utf8mb3_uca1400_ai_ci` table stores no such pair, because it
compares them as equal, and a `utf8mb3_general_ci` one converts like MySQL (transition B above).

Equivalence: a database created by the migrations with the replacement map, a database built with
the framework map and then migrated, and a database built with the replacement map up to the release
before the conversion and then migrated have identical `information_schema` rows for tables
(collation), columns (type, nullability, default, character set, collation, key) and indexes, on
both servers: 149 rows, 0 differing lines. On the already-utf8mb4 database the conversion changes no
row and no InnoDB table id.

Rebuild: `ALTER TABLE ... MODIFY COLUMN ... CHARACTER SET utf8mb4` of a plain column is accepted
with `ALGORITHM=INPLACE, LOCK=NONE` on both servers and leaves the InnoDB table id unchanged. The
migration changes the InnoDB table id of the seven core tables that have an indexed string column
(`struo_users`, `struo_roles`, `struo_languages`, `struo_file_translations`, `struo_permissions`,
`struo_revisions`, `struo_user_sessions`) on MySQL, so it rebuilds them, and of none on MariaDB.

Unique collision on MySQL: with the two values `ss` and the German sharp s in `struo_roles.name`,
the run stops at `struo_roles` with
`Duplicate entry '<value>' for key 'struo_roles.ux_struo_roles_name'`; the server names whichever of
the two values it checks second. `struo_users` is converted before it, the tables after it are not,
and the migration is not recorded. After one value is deleted the rerun succeeds and no column is
outside utf8mb4.

## Tests

- `Utf8Mb4TypeMapTests`: the generated DDL of the type map per size class and backend, and the key
  set.
- `ConvertStringColumnsToUtf8Mb4Tests`: no expression off MySQL, 22 alterations on MySQL, each
  restating a column the creating migration declares.
- `Utf8Mb4ColumnsTests`: the pure warning function, its message, and the backend gate: only MySQL
  reads the character sets.
- `Utf8Mb4DefaultsTests`: the decision (database default by table default, case of names, skipped
  tables, missing rows, a non-default table prefix), the message (real names, only what offends,
  both statements, the collation of the database, the whole opening line of each shape, the closing
  lines (the fork statement with the collation suffix of the table statements), name order,
  quoting), the character set derived from each measured collation, and the gate: the catalog is
  read on MySQL only, for every `StruoDbType` value.
- `MigrationDefaultsRefusalTests`: the apply path asks the check after it has found pending
  migrations and before the runner creates a table; a refusal leaves no table and no version table;
  an unreadable catalog is a `MigrationFailedException` and a fault in the decision is not; nothing
  pending asks nothing; on a backend other than MySQL and MariaDB the default check applies the
  migrations (skipped on those two).
- `Utf8Mb4DefaultsLiveTests` (MySQL and MariaDB when configured): a latin1 or utf8mb3 database
  without tables, and with core tables built with the framework type map, is refused and stays
  byte-identical in structure, character sets, data and applied versions; the statements of the
  message make `migrate` succeed and the column character sets and collations equal a fresh
  database's, with the same column structure; a `utf8mb4_unicode_ci` database whose tables are
  utf8mb3 gets statements ending in that collation and every table and column ends in it; the
  refusal leaves the tables' default collations as they were; a utf8mb4 database migrates;
  `MigrateOnStartup` surfaces the refusal.
- `Utf8Mb4TextWideningLiveTests` (MySQL and MariaDB when configured): `TEXT` becomes `MEDIUMTEXT`
  and the schema check has no finding afterwards.
- `Utf8Mb4LiveTests` and `Utf8Mb4CollisionLiveTests` (MySQL and MariaDB when configured): a fresh
  database, the upgrade (including each column's collation), the supplementary-plane round trip, the
  warning and the collision.

## Unknowns

- Row-size effect (65535 bytes) of 4 bytes per character on a wide `VARCHAR`; MariaDB's maximum key
  length; other server versions than the two above.
- Whether a rebuild on MySQL blocks concurrent writes, and how long it takes on a large table.
- A tool that builds its own FluentMigrator runner outside `MigrationHost` gets the framework map.
- A fork's own `AsString` columns in an existing database are converted only by the fork. The mixed
  state (core converted, fork tables not) was measured for joins and `IN` subqueries, which work;
  GraphQL and the string operators were not measured in it.
- A database built by v0.8.x CodeFirst was not measured; the fixtures hold the core migrations up
  to `SeedCoreData` applied with the framework type map and the version table removed.
- Oracle is not covered.

## Referenced from

- `src/Struo.Infrastructure/Migrations/Utf8Mb4MySqlTypeMap.cs`
- `src/Struo.Infrastructure/Migrations/Schema/CharacterSetCatalog.cs`
- `src/Struo.Infrastructure/Migrations/Schema/DefaultCharacterSetCatalog.cs`
- `src/Struo.Infrastructure/Migrations/Schema/Utf8Mb4Defaults.cs`
- `src/Struo.Infrastructure/Migrations/Core/202611170000_ConvertStringColumnsToUtf8Mb4.cs`
- `AGENTS.md` ("Invariants")