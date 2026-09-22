# ColumnTypeMap picks per-backend column literals verified only on PostgreSQL and SQLite

## Decision

`ColumnTypeMap.For` resolves a dialect-neutral `ColumnShape` to a concrete column type literal per
backend:

- `ColumnShape.LongText` → PostgreSQL/SQLite `text`, MySQL `longtext`, SqlServer `nvarchar(max)`,
  Oracle `clob`.
- `ColumnShape.TimestampWithTimeZone` → PostgreSQL/SQLite `timestamptz`, MySQL `datetime(6)`,
  SqlServer `datetimeoffset`, Oracle `timestamp with time zone`.

Three of these are deliberate, non-obvious choices rather than the first vendor synonym that comes to
mind: MySQL uses `longtext`, not `text`; MySQL uses `datetime(6)`, not `timestamp`; SqlServer uses
`nvarchar(max)`. PostgreSQL is this repository's verified runtime target and SQLite is the test
target (`AGENTS.md`). The MySQL/SqlServer/Oracle literals are chosen to be syntactically valid so that
CodeFirst table creation succeeds on those backends; they are not claimed to be verified against a
live instance of any of the three.

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

Both `nvarchar(max)` (SqlServer) and `datetime(6)` (MySQL) are already parenthesised literals.
Whether SqlSugar appends a further length suffix to a `DbColumnInfo.DataType` that already contains
parentheses — which would emit malformed DDL such as `nvarchar(max)(4000)` — is the question this
decision turns on: if the answer were yes, these two literals would need a different resolution. The
answer, established by reading the upstream source below, is no.

## Evidence

Read directly from the upstream SqlSugar source at `https://github.com/DotNetNext/SqlSugar` (the
`donet5/SqlSugar` slug this package is still sometimes referenced by is a 301 redirect to the same
repository, confirmed by reading the HTTP response), tag `5.1.4.197` — the closest published git tag
to the version pinned in `Directory.Packages.props`, which pins `SqlSugarCore` `5.1.4.220` and is not
itself tagged upstream. The tag series stops at `5.1.4.197`, with a dozen-plus untagged stable NuGet
releases sitting strictly between it and the pin (`.212`/`.213` only ever shipped as prerelease
builds). The endpoints were bounded, not every commit in between: the relevant methods below were
byte-identical between the `5.1.4.197` tag and `master` when diffed, so the divergence risk is low but
not zero. This is a source-reading conclusion, not a result verified against a live SQL Server or
MySQL instance.

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

Whether the parenthesised-literal reasoning in Evidence holds against a live SQL Server or MySQL
instance is unverified — it rests on reading the source, not on running it.

Whether that reasoning holds unchanged across the dozen-plus untagged `SqlSugarCore` releases between
the closest published tag and the pinned version is unverified; Evidence bounds the risk as low but
not zero.

The Oracle mapping (`clob` for `LongText`, `timestamp with time zone` for `TimestampWithTimeZone`) is
unverified against a live Oracle instance and was not part of the source-reading investigation at all
(that investigation covers only the already-parenthesised SqlServer/MySQL literals).

## Referenced from

- `src/Struo.Infrastructure/Persistence/ColumnTypeMap.cs`
- `AGENTS.md` ("Hard constraints")
- `docs/ai/conventions.md` ("Citing code from docs and comments")
- `docs/ai/conventions.md` ("Column type mapping")
