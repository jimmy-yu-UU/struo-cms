# ColumnTypeMap picks one column literal per backend for each ColumnShape

## Decision

`ColumnTypeMap.For` resolves a dialect-neutral `ColumnShape` to a concrete column type literal per
backend:

- `ColumnShape.LongText` → PostgreSQL/SQLite `text`, MySQL `longtext`, SqlServer `nvarchar(max)`,
  Oracle `clob`.
- `ColumnShape.TimestampWithTimeZone` → PostgreSQL/SQLite `timestamptz`, MySQL `datetime(6)`,
  SqlServer `datetimeoffset`, Oracle `timestamp with time zone`.
- `ColumnShape.Timestamp` (zone-less) → MySQL `datetime(6)`, which MariaDB shares. It has no
  literal for any other backend.

`ColumnShape.Timestamp` changes the column on MySQL and MariaDB only, and needs no attribute. On a
MySQL client, the `EntityService` hook in `SqlSugarClientFactory` gives it to every
`DateTime`/`DateTime?` property that has neither `[ColumnShape]` nor its own
`[SugarColumn(ColumnDataType = ...)]`. An explicit `[ColumnShape(ColumnShape.Timestamp)]` is
accepted on every backend and behaves the same way. The migrations write those columns with
`AsTimestamp(Db)`, which `AsShape(ColumnShape.Timestamp, Db)` delegates to: `datetime(6)` on MySQL
and FluentMigrator's `AsDateTime()` on every other backend. Those backends keep SqlSugar's default
`DateTime` mapping in CodeFirst and `AsDateTime()` in the migrations, so nothing calls
`ColumnTypeMap.For(ColumnShape.Timestamp, ...)` there. `DateTimeOffset` keeps SqlSugar's own mapping
everywhere. Because both timestamp shapes resolve to `datetime(6)` on MySQL, `EntitySchemaReader`
takes a zone-less column's category from the property (`SqlSugarClientFactory.IsTimestampShaped`),
not from the literal.

Three of these are deliberate, non-obvious choices rather than the first vendor synonym that comes to
mind: MySQL uses `longtext`, not `text`; MySQL uses `datetime(6)`, not `timestamp`; SqlServer uses
`nvarchar(max)`. PostgreSQL, SQL Server, MySQL and MariaDB are verified backends and SQLite is the
test target (`AGENTS.md`). On the four verified backends the `LongText`, `TimestampWithTimeZone` and
`Timestamp` literals are read back from live instances (Evidence). The Oracle literals are chosen to
be syntactically valid so that CodeFirst table creation succeeds there; they are not verified against
a live instance.

`ColumnTypeMap.For` is not the only site in `src/` that writes a vendor type literal directly:
`SqlSugarClientFactory.ApplySqliteIdentityColumnRewrite` sets `DataType` to `"INTEGER"` on a SQLite
identity primary key.

## Why

MySQL's `TEXT` caps at 65,535 bytes — not unbounded, unlike every other mapping here. A full item
`Revision.Snapshot` or a realistic RichText/Markdown/Json body can exceed that: error 1406 in strict
mode, silent truncation otherwise. `LONGTEXT` (up to 4 GiB) is the conventional MySQL choice for
unbounded text, which is why `LongText` maps to `longtext`, not `text`, on MySQL.

MySQL's `TIMESTAMP` is range-limited to 1970-2038; `DATETIME(6)` storing UTC is the conventional
choice for an instant with no such ceiling, which is why `TimestampWithTimeZone` maps to
`datetime(6)`, not `timestamp`, on MySQL.

MySQL's and MariaDB's plain `DATETIME` keeps whole seconds. Two writes within one second then store
the same `UpdatedAt`, and the audit timestamp cannot order them. `Timestamp` maps to `datetime(6)`
there for that reason. The other backends have no such gap that a test shows, so they keep the types
SqlSugar's CodeFirst and FluentMigrator's `AsDateTime()` create. SQL Server's `datetime` has a
precision of about 3 ms.

Both `nvarchar(max)` (SqlServer) and `datetime(6)` (MySQL) are already parenthesised literals.
Whether SqlSugar appends a further length suffix to a `DbColumnInfo.DataType` that already contains
parentheses — which would emit malformed DDL such as `nvarchar(max)(4000)` — is the question this
decision turns on: if the answer were yes, these two literals would need a different resolution. The
answer, established by reading the upstream source below, is no.

## Evidence

Zone-less timestamp types, as SqlSugar's `GetColumnInfosByTableName` reports them (type / `Length` /
`DecimalDigits`), measured on 2026-10-08 against PostgreSQL 13.2, SQL Server 2025, MySQL 8.4.11,
MariaDB 11.8.9 and SQLite. FluentMigrator `AsDateTime()` and SqlSugar CodeFirst `DateTime`/`DateTime?`
with no shape reported the same type on every backend. Outside MySQL and MariaDB these are the types
the backends keep:

| Backend | `AsDateTime()` and CodeFirst `DateTime` | `datetime(6)` |
|---|---|---|
| PostgreSQL | `timestamp` / 0 / 0 | — |
| SQL Server | `datetime` / 23 / 3 | — |
| MySQL | `datetime` / 0 / 0 | `datetime` / 6 / 0 |
| MariaDB | `datetime` / 0 / 0 | `datetime` / 6 / 0 |
| SQLite | `DATETIME` (FluentMigrator), `datetime` (CodeFirst) / 0 / 0 | — |

On Oracle, SqlSugar's default type name for `DateTime` (`Ado.DbBind.GetDbTypeName("DateTime")`) is
`date`, and FluentMigrator's `OracleTypeMap` gives `AsDateTime()` as `TIMESTAMP(4)`; both were read
in code, not measured on a live instance. `StruoMigrationTypeTests.Shapes_resolve_to_the_live_backend_literals`
pins the `AsTimestamp(Db)` column per live backend, `ColumnTypeExtensionsTests` pins that it equals
`AsDateTime()` outside MySQL, and `CoreMigrationsLiveTests` compares the migrated and CodeFirst core
schemas, `DecimalDigits` and `Length` included, on each of them.

The rest of this section covers the parenthesised `LongText` and `TimestampWithTimeZone` literals.

Read directly from the upstream SqlSugar source at `https://github.com/DotNetNext/SqlSugar` (the
`donet5/SqlSugar` slug this package is still sometimes referenced by is a 301 redirect to the same
repository, confirmed by reading the HTTP response), tag `5.1.4.197` — the closest published git tag
to the version pinned in `Directory.Packages.props`, which pins `SqlSugarCore` `5.1.4.221` and is not
itself tagged upstream. The tag series stops at `5.1.4.197`, with a dozen-plus untagged stable NuGet
releases sitting strictly between it and the pin (`.212`/`.213` only ever shipped as prerelease
builds). The endpoints were bounded, not every commit in between: the relevant methods below were
byte-identical between the `5.1.4.197` tag and `master` when diffed, so the divergence risk is low but
not zero. For the pinned version, the live suite confirms the CREATE TABLE path: on SQL Server 2025
`ShapedColumnAssertions.ShouldHaveShape` reads `nvarchar(max)` back as `nvarchar` with `Length` -1,
and on MySQL 8.4.11 and MariaDB 11.8.9 it reads `datetime(6)` back as `datetime` with `Length` 6, for
columns CodeFirst builds in a fresh unit database (`ContentColumnMappingTests`, `ColumnTypeMapTests`).

Both `SqlServerDbMaintenance` and `MySqlDbMaintenance` resolve the length suffix through a
`GetSize(DbColumnInfo item)` method that reads only `item.Length`/`item.DecimalDigits` — it never
inspects `item.DataType` for existing parentheses. The shared base implementation
(`Src/Asp.NetCore2/SqlSugar/Abstract/DbMaintenanceProvider/Methods.cs`, used as-is by SqlServer)
returns a `null` size whenever `Length == 0 && DecimalDigits == 0`:
`"else if (item.Length > 0 && item.DecimalDigits == 0) { dataSize = ... }"` — none of its branches
match when both are zero, so `dataSize` stays `null`.
`MySqlDbMaintenance.GetSize`
(`Src/Asp.NetCore2/SqlSugar/Realization/MySql/DbMaintenance/MySqlDbMaintenance.cs`) has the same
zero/zero fallthrough. A `null` `dataSize` is substituted as an empty string by `string.Format`, so
the composed column clause (e.g. SqlServer's `CreateTableColumn = "{0} {1}{2} {3} {4} {5}"`, where
`{1}` is `DataType` and `{2}` is `dataSize`) renders as just `nvarchar(max)` with no trailing suffix.
This holds for both CREATE TABLE (`CodeFirstProvider.NoExistLogic` → `EntityColumnToDbColumn` →
`DbMaintenance.CreateTable` → `GetCreateTableSql` → `GetSize`) and ALTER TABLE ADD COLUMN
(`CodeFirstProvider.ExistLogic` → the same `EntityColumnToDbColumn` → `DbMaintenance.AddColumn` →
`GetAddColumnSql` → `GetSize`) — both paths share the identical helper, so there is no divergence
between table creation and column addition here.

`item.Length` reaches `GetSize` as `0` because: (1) `DbColumnInfo.Length` defaults to `0` (a plain C#
`int`); (2) the `EntityService` hook in `SqlSugarClientFactory.cs` sets only `column.DataType` for a
`ColumnShapeAttribute`-marked property and never touches `column.Length`; (3)
`CodeFirstProvider.Execute`'s own default-length injection
(`"if (item.PropertyInfo.PropertyType == UtilConstants.StringType && item.DataType.IsNullOrEmpty() &&
item.Length == 0) { item.Length = DefultLength; }"`) is guarded on `DataType.IsNullOrEmpty()`, which
is false once `ColumnTypeMap.For` has already assigned a literal — the `EntityService` hook that
assigns it runs earlier in the same pipeline, at
`Src/Asp.NetCore2/SqlSugar/Abstract/EntityMaintenance/EntityMaintenance.cs:424-430`. That call site is
lexically inside the private `SetColumns` method (`:307`), which `GetEntityInfoNoCache` (`:58`) calls
at `:89` while building the `EntityInfo` that `CodeFirstProvider.Execute` subsequently injects the
default length into — so the hook's assignment is already in place by the time the guard is checked;
and (4) `EntityColumnToDbColumn` copies `Length` straight through (`"Length = item.Length,"`) without
ever re-deriving it from the `DataType` string.

A third length-touching mechanism exists and is also a no-op for these two literals: both providers
run a `ConvertCreateColumnInfo(DbColumnInfo x)` pre-pass before `GetSize` — not only in the CREATE
path (SqlServer's `CreateTable` calls it per column at
`Realization/SqlServer/DbMaintenance/SqlServerDbMaintenance.cs:721`; MySQL's `GetCreateTableSql` does
the same at `Realization/MySql/DbMaintenance/MySqlDbMaintenance.cs:537`) but also in the modify path
(SqlServer's `UpdateColumn` calls it at `SqlServerDbMaintenance.cs:498`; MySQL's `UpdateColumn` at
`MySqlDbMaintenance.cs:612`) — both providers' method is defined once, at
`SqlServerDbMaintenance.cs:756-771` and `MySqlDbMaintenance.cs:792-808` respectively, and reused by
every caller. SqlServer's version only rewrites `DataType` when it case-insensitively equals
`"nvarchar"` or `"varchar"` and `Length < 1` — `"nvarchar(max)".EqualCase("nvarchar")` is `false`, so
the already-parenthesised literal never matches that branch. MySQL's version checks the same two names
plus a `{"longtext", "date"}` array — `"datetime(6)"` matches neither, so it also passes through
untouched. Both are no-ops here for the same underlying reason `GetSize` is: the condition that would
trigger a rewrite never holds for a value that already contains its own parentheses.

The `LongText` conclusion above is not limited to properties carrying `ColumnShapeAttribute`: a
multi-value `[CmsField]` property (a JSON column) resolves to the identical
`ColumnTypeMap.For(ColumnShape.LongText, dbType)` call inside the same `EntityService` hook's
JSON-column branch, so `"nvarchar(max)"` reaches SQL Server through this route too. The reasoning
holds there for the same reason: that branch sets `column.IsJson = true` and `column.DataType` only,
never `column.Length`, and `CodeFirstProvider.EntityColumnToDbColumn` does not copy `IsJson` into the
resulting `DbColumnInfo` at all — it is not one of the fields its object initializer sets — so nothing
about the JSON route changes the zero-length conclusion. A fork relying on JSON columns on SQL Server
is covered by the same no-op finding as the shaped properties above.

## Unknowns

On Oracle the two paths create different zone-less timestamp types: `DATE` (whole seconds) from
CodeFirst and `TIMESTAMP(4)` from the migrations. Neither was measured on a live instance.

A `DateTime` primary key skips the `EntityService` hook, so CodeFirst keeps SqlSugar's default type for
it, while `make:migration` emits `AsTimestamp(Db)`. On MySQL that is `datetime` against
`datetime(6)`. No entity has a `DateTime` primary key.

The ALTER TABLE ADD COLUMN and `UpdateColumn` paths in Evidence rest on reading the source at tag
`5.1.4.197`, not the pinned version; the live suite exercises only CREATE TABLE.

The Oracle mapping (`clob` for `LongText`, `timestamp with time zone` for `TimestampWithTimeZone`) is
unverified against a live Oracle instance and was not part of the source-reading investigation at all
(that investigation covers only the already-parenthesised SqlServer/MySQL literals).

## Referenced from

- `src/Struo.Infrastructure/Persistence/ColumnTypeMap.cs`
- `AGENTS.md` ("Hard constraints")
- `docs/ai/conventions.md` ("Citing code from docs and comments")
- `docs/ai/conventions.md` ("Column type mapping")
- `AGENTS.md` ("Verification")
