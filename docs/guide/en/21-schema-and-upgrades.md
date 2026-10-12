# 21. Schema Management and Upgrades

Where tables come from, how to change them, and how a change to core's own schema reaches your fork
— this chapter covers all three. If your database was built by v0.8.x CodeFirst, first join it to
the migrations with [Chapter 24: Joining a Database Built by v0.8.x](24-upgrading-from-0-8.md).

## Migrations are the only schema source

Every table, column and index comes from a FluentMigrator migration. The API never creates or alters
a table itself. Applied migrations are recorded in the version table `{prefix}schema_versions`, where
the prefix is `Database:TablePrefix` (default `struo_`).

Migrations live in three places:

- **Core**: `src/Struo.Infrastructure/Migrations/Core/`. `CreateCoreSchema` creates the framework
  tables; `SeedCoreData` seeds the languages, roles and permissions, and the bootstrap admin.
- **The sample**: `CreateBlogSchema` in `samples/Struo.Sample.Blog/Migrations/`, scanned only when
  the sample is listed in `Struo:ContentAssemblies`.
- **Your fork**: in your own content project. That project must be listed in
  `Struo:ContentAssemblies` and reference `Struo.Infrastructure`.

The commands scan `Struo.Infrastructure` plus every assembly listed in `Struo:ContentAssemblies` and
run migrations in version order. Seeding is a migration too, so it happens only through `migrate` or
`MigrateOnStartup`.

## The six commands

Each command is the first argument to `Struo.Api`:

| Command | What it does |
|---|---|
| `migrate` | Applies every pending migration |
| `migrate:status` | Lists the state of every migration |
| `migrate:preview` | Prints the SQL of pending migrations, runs nothing |
| `migrate:check` | Compares the database with the entities, read-only |
| `migrate:baseline` | Records every migration as applied, runs nothing |
| `make:migration` | Writes a new migration file |

In the repository, use `dotnet run --project src/Struo.Api -- <command>`. That applies the `http`
launch profile, so the environment is Development and your `appsettings.Development.json` is read.
A published build uses `dotnet Struo.Api.dll <command>`; that is the API image's entrypoint, so
`docker run --rm … <image> <command>` works too.

The commands read the same configuration as the API: appsettings plus `Section__Key` environment
variables, and configuration keys may also follow the command, e.g. `--Database:ConnectionString=…`.
Exit code `0` is success, `1` failure, `2` a usage error.

`migrate` first prints FluentMigrator's log line for every step, then one `Applied <version>  <name>`
line per migration it applied; with nothing pending it prints only `Nothing to migrate.`.
`migrate:status` prints one line per migration:

```
$ dotnet run --project src/Struo.Api -- migrate:status
202610080000   Applied   2026-10-12 09:57:49Z  CreateCoreSchema
202610080001   Applied   2026-10-12 09:57:49Z  SeedCoreData
202610080100   Applied   2026-10-12 09:57:49Z  CreateBlogSchema
3 applied, 0 pending, 0 orphaned
```

`Pending` means not applied yet. `Orphaned` means the version table has a record but no scanned
assembly holds that migration, for example after the sample is removed from
`Struo:ContentAssemblies`. Orphaned records do not block startup.

`migrate:preview` prints the SQL the pending migrations would run and writes nothing; its last line
is `-- <n> migration(s) pending`.

> On a new database the preview includes `SeedCoreData`'s `INSERT` with the bootstrap admin's email
> and password hash. Treat that output as a secret; don't paste it into an issue or a public CI log.

`migrate:check` compares the live tables with the schema the entities declare. It takes no lock and
writes nothing; a clean run prints only `0 error(s), 0 warning(s)`. Errors are a missing table or
column, a nullability, type-category or string-length difference, and a missing required unique
index; any error makes it exit `1`. Warnings are a column the database has but no entity declares,
and a non-Unicode string column on SQL Server.

`make:migration` is covered in "Writing a migration" below, `migrate:baseline` in Chapter 24.

## What startup checks

Before the API serves, `StartupMigrationGate` looks at the database. `Database:MigrateOnStartup`
defaults to `false` in every environment, and then the API refuses to start while any migration is
pending, exiting with code `1`:

```
[17:57:36 FTL] StruoCMS host terminated unexpectedly
System.InvalidOperationException: 3 migration(s) pending, the lowest is 202610080000; run the 'migrate' command, or set Database:MigrateOnStartup to true.
…
```

So a deployment runs `migrate` as a step of its own, before the new API version goes live. Set to
`true`, the API takes the migration lock, applies pending migrations, and then starts serving; the
local Compose stack runs this way.

When the framework tables exist but there is no version table, both settings refuse to start, and
the message says to run `migrate:baseline` once — see [Chapter 24](24-upgrading-from-0-8.md).

In Development, startup also runs the same comparison as `migrate:check` and refuses to start on any
error. Production skips that step, so run `migrate:check` yourself.

## Version table, lock and transactions

The version table `{prefix}schema_versions` has three columns, `version`, `appliedon` and
`description`, with a unique index on `version`. Whether a migration has been applied is decided by
its version number alone.

`migrate`, `migrate:baseline` and `MigrateOnStartup` first take an advisory lock named after the
table prefix: `pg_try_advisory_lock` on PostgreSQL, `GET_LOCK` on MySQL/MariaDB, `sp_getapplock` on
SQL Server. When several replicas start at once, one applies; the others wait for it to finish and
then find nothing pending. The wait is capped by `Database:MigrationLockTimeoutSeconds` (default 60,
range 1 to 3600), after which the command fails. SQLite and Oracle take no lock; Oracle logs a
warning, and running `migrate` concurrently there is unsupported.

Each migration runs in its own transaction. On failure that migration rolls back, the message names
the lowest unapplied version, and migrations already applied stay applied. MySQL, MariaDB and Oracle
commit DDL implicitly, so a failed migration there can be left half-done. Migrations only move
forward and there is no rollback command, so back up the database before deploying.

## Writing a migration

`make:migration` writes a migration file. It reads entity metadata only, never touches the database,
and never overwrites an existing file:

```
make:migration <Name> [--entity <Type>] --output <dir> [--namespace <Ns>]
```

- The version is the UTC time to the minute (`yyyyMMddHHmm`), moved forward a minute when a file in
  the directory already has it.
- `--entity` takes the type name of a collection, translation sidecar or junction and writes a
  `Create.Table` built from its metadata; without it you get an empty skeleton.
- The `--output` directory must already exist. A relative path resolves against the process's
  working directory, which under `dotnet run --project src/Struo.Api` is `src/Struo.Api` — so write
  `../../<your project>/Migrations`, or an absolute path.

For the sample's `Tag` it writes this (later columns and `Down()` omitted):

```csharp
[Migration(202610120958, "CreateTags")]
public sealed class CreateTags : StruoMigration
{
    public override void Up()
    {
        Create.Table("tags")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
…
```

Rules for writing one:

- Derive from `StruoMigration` and use FluentMigrator's portable calls (`Create.Table`,
  `Alter.Table`, `Create.Index`, `Insert.IntoTable`). For the column types that differ per backend use
  `AsTimestamp(Db)`, `AsJson(Db)`, `AsLongText(Db)`, or `AsShape(ColumnShape.<shape>, Db)`.
- Name a framework table with `FrameworkTable("users")`, which applies the table prefix; your own
  tables carry no prefix.
- Branch only where a backend really needs its own form, and branch on a constant:
  `if (Db == StruoDbType.MySql)`. Don't use FluentMigrator's `IfDatabase("…")`: it takes a free
  string, a misspelt one silently does nothing, and the migration is still recorded as applied.
- Write no SQL strings and reference no entity classes: a migration is a snapshot of the schema as it
  was when you wrote it.
- Never edit a migration that may already be applied somewhere; add another.

An automatic diff cannot see intent, so a change that touches existing data takes several steps:

1. **Renaming a column**: write a `Rename.Column` migration, then change the entity; "drop one
   column, add another" loses the data.
2. **Removing a property**: once you're sure the column is unused, drop it with a `Delete.Column`
   migration.
3. **Narrowing a type**: add the new column, backfill and verify, then switch over and drop the old
   one; narrowing in place fails or truncates.
4. **Adding `NOT NULL` to a table with rows**: add the column nullable, backfill, then add `NOT NULL`.
5. **Adding a unique index over duplicate values**: deduplicate first, then create the index.
6. **Splitting or merging columns**: always a migration that moves the data.

Afterwards, read the SQL with `migrate:preview`, then run `migrate` and `migrate:check` against a
disposable database of the backend you actually use; DDL that SQLite accepts may not be accepted
elsewhere. Your fork's own tests can call `SchemaChecker.Check` and assert there are no errors.

## The four raw-SQL exceptions in core

The invariant that all database access goes through SqlSugar (see
[Chapter 2: Architecture](02-architecture.md)) has four deliberate exceptions, none of which touches
request input:

- **The relation-filter subquery wrapper**: four fixed forms assembled around SQL that SqlSugar
  generated.
- **`OrderByExpressionBuilder`'s `ORDER BY` text**: three sort forms — column, relation path and
  translated field.
- **The migration lock, `MigrationLockSql`**: one parameterised acquire and release statement per
  backend.
- **`IndexCatalog`'s index queries**: the read-only catalog queries `migrate:check` uses to read
  indexes; Oracle has none, so the index check is skipped with a warning.

## Upgrading core

Core's schema changes ship as core migrations inside `Struo.Infrastructure`, so they reach your fork
when you merge a newer core; the [changelog](changelog.md) names every new core migration. After the
merge, `migrate:status` lists them as `Pending`. Read their SQL with `migrate:preview`, run `migrate`
on a backed-up database, then run `migrate:check`.

## Backend differences

- **MySQL/MariaDB**: migrations set no character set, so tables take the database default. Create the
  database with `CHARACTER SET utf8mb4`; tooling such as SqlSugar's `CreateDatabase` can create
  utf8mb3, which cannot store emoji. Run with the server's default strict `sql_mode`; non-strict mode
  silently truncates or replaces values.
- **SQL Server**: `migrate:check` warns about `varchar` columns; characters outside the collation's
  code page are stored as `?`.
- **Oracle**: type-mapped only, never verified. No lock, and `migrate:check` skips the type, length and
  index checks with warnings.
- **Unit tests that build tables with CodeFirst** get unique group indexes named
  `Index_{table}_{columns}_Unique`. Keep that within 63 characters: MySQL/MariaDB reject a name over
  64, and PostgreSQL truncates one over 63.

## What's next

That covers schema management; the next step is what test layers this system has and what CI runs,
in [Chapter 22: Testing and CI](22-testing.md).
