# MySQL and MariaDB string columns are utf8mb4

## Decision

On MySQL and MariaDB, `AsString(n)` and `AsFixedLengthString(n)` render types without a character
set, so a column takes the database's default, utf8mb4 on a database created as the manual says. The
migration runner's MySQL type map is `Utf8Mb4MySqlTypeMap`
(`src/Struo.Infrastructure/Migrations/Utf8Mb4MySqlTypeMap.cs`), a subclass of FluentMigrator's
`MySql8TypeMap` that restates every key of `DbType.String` and `DbType.StringFixedLength`.
`MigrationDialect.Register` installs it for the `MySql` dialect, so every runner built through it is
covered; `MigrationHostOptions.ConfigureRunnerServices` runs afterwards and can put the framework's
map back, which the tests do to build a database the way the previous release did.

The core migration `ConvertStringColumnsToUtf8Mb4` (`202611170000`) re-declares the 21 core string
columns and the version table's `description` with `Alter.Column(...).AsString(n)`, restating length
and nullability. It produces no expression on any other backend. `Down` does nothing.

`migrate:check` reads each entity table's column character sets with one typed query on
`information_schema.COLUMNS` (`CharacterSetCatalog`) and warns (`FindingKind.NonUtf8Mb4String`) for
a column an entity declares as String or LongText whose character set is not utf8mb4. The warning
never fails the check. A fork's own tables are not converted by `migrate`: the warning names them
and chapter 24 gives the statement.

The `AS("information_schema.COLUMNS")` call is the only string in the query; the schema and the
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
- Under the old character set the same server version compared the pair as different (MySQL) or
  equal (MariaDB), which is why MariaDB converts without a collision.

Comparison semantics of a string column, from `=` on hex-built values:

| Pair | MySQL utf8mb3 | MySQL utf8mb4 | MariaDB utf8mb3 | MariaDB utf8mb4 |
|---|---|---|---|---|
| `a` and `a` plus a trailing space | equal | different | equal | equal |
| sharp s and `ss` | different | equal | equal | equal |
| sharp s and `s` | equal | different | different | different |
| `a` and `A`, `e` and `e` with acute | equal | equal | equal | equal |
| full-width `A` and `A` | different | equal | equal | equal |
| `a` and `a` plus U+200B | different | equal | equal | equal |

MySQL compares with `utf8mb3_general_ci` (PAD SPACE) before the conversion and `utf8mb4_0900_ai_ci`
(NO PAD) after it: a unique index treats a trailing space as a difference, and the sort order of
punctuation differs. MariaDB stays in the `uca1400_ai_ci` family: no row compares differently and
the sort order is the same.

Catalog read: `DbMaintenance.GetColumnInfosByTableName` returns the same `DbColumnInfo` for a
utf8mb3 and a utf8mb4 column, so it cannot show the difference. The typed query returns
`COLUMN_NAME` and `CHARACTER_SET_NAME` of every column on both servers. Filtering `TABLE_COLLATION`
of `information_schema.TABLES` finds nothing on a database whose tables are utf8mb4 but whose string
columns are utf8mb3.

Index key length: the longest core key is `struo_revisions` (two 255-character columns and a
bigint), 2048 bytes under utf8mb4; MySQL's limit is 3072 and all core indexes are created on both
servers.

Table default character set: `MODIFY COLUMN ... VARCHAR(n)` without a character set takes the
table's default, so the migration converts a column only where the table's default is utf8mb4. In a
database created as utf8mb3, whose tables therefore default to utf8mb3, the migration is recorded,
58 columns stay utf8mb3 on both servers and `migrate:check` prints 23 warnings (the 21 string
columns and the two long-text columns `revisions.snapshot` and `site_settings.brandname`). After
`ALTER DATABASE ... CHARACTER SET utf8mb4` and `ALTER TABLE ... CONVERT TO CHARACTER SET utf8mb4` on
the 12 tables that have such a column, no column is outside utf8mb4 and `migrate:check` prints
`0 error(s), 0 warning(s)`; chapter 24 prints those statements from `information_schema.COLUMNS` and
they were run on both servers.

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
- `Utf8Mb4ColumnsTests`: the pure warning function.
- `Utf8Mb4LiveTests` and `Utf8Mb4CollisionLiveTests` (MySQL and MariaDB when configured): a fresh
  database, the upgrade, the supplementary-plane round trip, the warning and the collision.

## Unknowns

- Row-size effect (65535 bytes) of 4 bytes per character on a wide `VARCHAR`; MariaDB's maximum key
  length; other server versions than the two above.
- Whether a rebuild on MySQL blocks concurrent writes, and how long it takes on a large table.
- A tool that builds its own FluentMigrator runner outside `MigrationHost` gets the framework map.
- A fork's own `AsString` columns in an existing database are converted only by the fork. The mixed
  state (core converted, fork tables not) was measured for joins and `IN` subqueries, which work;
  GraphQL and the string operators were not measured in it.
- Oracle is not covered.

## Referenced from

- `src/Struo.Infrastructure/Migrations/Utf8Mb4MySqlTypeMap.cs`
- `src/Struo.Infrastructure/Migrations/Schema/CharacterSetCatalog.cs`
- `src/Struo.Infrastructure/Migrations/Core/202611170000_ConvertStringColumnsToUtf8Mb4.cs`
- `AGENTS.md` ("Invariants")