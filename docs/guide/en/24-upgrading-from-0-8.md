# 24. Joining a Database Built by v0.8.x

This chapter joins a database that v0.8.x built with CodeFirst at startup to the migrations, so you
can manage it with `migrate` as [Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md)
describes.

Such a database has the framework tables but no version table. `migrate:baseline` exists for exactly
this case: it records every known migration as applied without running it. The steps below were
walked through on PostgreSQL, SQL Server, MySQL and MariaDB.

## The steps, in order

1. **Back up the database.** No command reverses a migration.
2. **Merge the newer core and build.** Keep `Database:TablePrefix` exactly as it was; with a
   different prefix, `migrate` creates a second, empty set of framework tables under the new names.
3. **Remove `Database:MigrationsPath` and `Database:AutoSyncSchema`** from every appsettings file
   and environment. They have no effect, and the API logs a warning at startup for each one still
   present:

   ```
   [17:42:58 WRN] The configuration key Database:MigrationsPath is not used: migrations are code in the scanned assemblies and are applied with the 'migrate' command.
   …
   ```

   Nothing runs `.sql` scripts. The changes they already made to the database stay; make sure the
   entity classes describe them.
4. **Before baselining, write migrations for your fork's own existing tables.** Run `make:migration`
   once for every collection, translation sidecar and junction whose table already exists
   (`Acme.Content` stands for the fork's content project):

   ```
   $ dotnet run --project src/Struo.Api -- make:migration CreateNotes --entity Note --output ../../samples/Acme.Content/Migrations --namespace Acme.Content.Migrations
   Created ../../samples/Acme.Content/Migrations\202610120942_CreateNotes.cs
   ```

   Review the generated files and rebuild; if you deploy the image, rebuild it. Don't generate a
   migration for a table that doesn't exist yet: baseline would record it as applied and the table
   would never be created. Add those after step 5. A relative `--output` resolves against
   `src/Struo.Api`, and the directory must already exist; see
   [Writing a migration](21-schema-and-upgrades.md#writing-a-migration) in Chapter 21.
5. **Run `migrate:baseline`, once.** Until then, both `migrate` and API startup refuse, with a
   message telling you to run `migrate:baseline`. Baseline takes the migration lock, writes every
   version — core, the sample if enabled, and your fork — in one transaction, and logs the SQL
   FluentMigrator runs:

   ```
   $ dotnet run --project src/Struo.Api -- migrate:baseline
   …
   Recorded 202610080000  CreateCoreSchema
   Recorded 202610080001  SeedCoreData
   Recorded 202610080100  CreateBlogSchema
   Recorded 202610120942  CreateNotes
   4 migration(s) recorded as applied.
   ```

   The seed is recorded, not run, so existing users, roles and languages are untouched. Baseline only
   adds the version table, so it can run while v0.8.x instances are still serving. It refuses, exiting
   `1`, when the database already has records or the framework tables don't exist.
6. **Run `migrate`.** Migrations added after the baseline are applied here; otherwise it prints
   `Nothing to migrate.`.
7. **Run `migrate:check`.** PostgreSQL, MySQL and MariaDB should show `0 error(s), 0 warning(s)`.
   SQL Server warns once per `varchar` string column:

   ```
   $ dotnet run --project src/Struo.Api -- migrate:check
   WARNING article_tags.note: database has non-Unicode varchar (length 255); characters outside the collation's code page are lost, use nvarchar
   …
   0 error(s), 30 warning(s)
   ```

   The number of warnings depends on your tables. Any error means an entity and its table disagree —
   usually a column the entity declares that the table doesn't have, or a column a `.sql` script
   changed that the entity doesn't reflect. Fix it before starting in Development, where the startup
   check refuses on errors.
8. **Do the follow-ups for your backend in the next section.** `migrate:check` cannot see these:
   SQL Server's access-token index, MySQL/MariaDB timestamp precision, and the database character
   set. On SQL Server, finish them before starting the API: Chinese or emoji text stored before the
   conversion becomes `?` and cannot be recovered.
9. **Start the API.**

## Per-backend follow-ups

Stop the API before running these statements. Names below assume the default `struo_` prefix;
change them if yours differs.

### SQL Server: the access-token index (required)

v0.8.x created a unique index on `struo_users.accesstoken`. A unique index on SQL Server allows only
one NULL, so creating a second user without an access token fails. Replace it with a plain index:

```sql
DROP INDEX ux_struo_users_accesstoken ON struo_users;
CREATE INDEX ix_struo_users_accesstoken ON struo_users (accesstoken);
```

On PostgreSQL and MySQL/MariaDB, NULLs don't collide, so this step is optional there. PostgreSQL's
`DROP INDEX` takes no `ON struo_users`; the rest is the same.

### SQL Server: `varchar` to `nvarchar`

v0.8.x created string columns as `varchar`, and characters outside the collation's code page (for example
Chinese and emoji under a Latin1 collation) are stored as `?`. If you store such text, convert before storing it: data already turned into
`?` is not recovered by the conversion.

An indexed column cannot be altered in place, so each index is dropped, the column altered, and the
index recreated as it was. The query below changes nothing; it prints those three groups of
statements for core, sample and fork tables alike, keeping length and nullability, and each index's
name, uniqueness and column order:

```sql
WITH cols AS (
    SELECT c.object_id, c.column_id, c.name, c.max_length, c.is_nullable
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE ty.name = 'varchar' AND t.is_ms_shipped = 0
),
idx AS (
    SELECT DISTINCT i.object_id, i.index_id, i.name, i.is_unique
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN cols ON cols.object_id = ic.object_id AND cols.column_id = ic.column_id
    WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0
)
SELECT stmt FROM (
    SELECT 1 AS step, OBJECT_NAME(object_id) AS tbl, name AS item,
           'DROP INDEX ' + QUOTENAME(name) + ' ON ' + QUOTENAME(OBJECT_NAME(object_id)) + ';' AS stmt
    FROM idx
    UNION ALL
    SELECT 2, OBJECT_NAME(object_id), name,
           'ALTER TABLE ' + QUOTENAME(OBJECT_NAME(object_id)) + ' ALTER COLUMN ' + QUOTENAME(name)
           + ' nvarchar(' + CASE WHEN max_length = -1 OR max_length > 4000 THEN 'max'
                                 ELSE CAST(max_length AS varchar(4)) END + ')'
           + CASE WHEN is_nullable = 1 THEN ' NULL;' ELSE ' NOT NULL;' END
    FROM cols
    UNION ALL
    SELECT 3, OBJECT_NAME(object_id), name,
           'CREATE ' + CASE WHEN is_unique = 1 THEN 'UNIQUE ' ELSE '' END + 'INDEX ' + QUOTENAME(name)
           + ' ON ' + QUOTENAME(OBJECT_NAME(object_id)) + ' ('
           + (SELECT STRING_AGG(QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END, ', ')
                     WITHIN GROUP (ORDER BY ic.key_ordinal)
              FROM sys.index_columns ic
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = idx.object_id AND ic.index_id = idx.index_id AND ic.is_included_column = 0)
           + ');'
    FROM idx
) s
ORDER BY step, tbl, item;
```

Do the access-token index first. Review the printed statements and run them in one go between
`SET XACT_ABORT ON; BEGIN TRANSACTION;` and `COMMIT;`. Afterwards `migrate:check` should show
`0 error(s), 0 warning(s)`.

### MySQL/MariaDB: `datetime` to `datetime(6)`

Most timestamp columns v0.8.x created are whole-second `datetime`; a new database uses `datetime(6)`.
Rows written within the same second cannot be told apart by the default sort (`createdAt`, newest
first). Change every `datetime` column in core, sample and fork tables to `datetime(6)`; columns that
are already `datetime(6)` and the version table's `appliedon` stay as they are. This query prints the
`ALTER TABLE` statements, one per table, keeping nullability:

```sql
SET SESSION group_concat_max_len = 1000000;
SELECT CONCAT('ALTER TABLE `', TABLE_NAME, '` ',
              GROUP_CONCAT(CONCAT('MODIFY `', COLUMN_NAME, '` datetime(6) ',
                                  IF(IS_NULLABLE = 'YES', 'NULL', 'NOT NULL'))
                           ORDER BY ORDINAL_POSITION SEPARATOR ', '),
              ';') AS stmt
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND COLUMN_TYPE = 'datetime'
  AND TABLE_NAME <> 'struo_schema_versions'
GROUP BY TABLE_NAME
ORDER BY TABLE_NAME;
```

`MODIFY` drops any default the statement does not restate. None of these core or sample columns has
a default; if one of yours does, add it back to the printed statement. Don't write this as a fork
migration: a deployment that baselines later records it as applied without ever running it.

### MySQL/MariaDB: character set to utf8mb4

A utf8mb3 or latin1 database in strict mode rejects 4-byte characters such as emoji, and the API
answers `INTERNAL_SERVER_ERROR`. Change the database default first, so tables created by later
migrations inherit it; then print a conversion statement for every table not yet utf8mb4, review, and
run them. These statements were run on MySQL 8.4 and MariaDB 11.8:

```sql
ALTER DATABASE `<your database>` CHARACTER SET utf8mb4;

SELECT CONCAT('ALTER TABLE `', TABLE_NAME, '` CONVERT TO CHARACTER SET utf8mb4;') AS stmt
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_TYPE = 'BASE TABLE'
  AND TABLE_COLLATION NOT LIKE 'utf8mb4%'
ORDER BY TABLE_NAME;
```

## Leftovers

- A deployment that used `MigrationsPath` keeps `struo_schema_migrations`. Nothing reads it and
  `migrate:check` does not mention it. Drop it once the entities reflect what its scripts did, or keep
  it as a record.
- A `db/migrations/` directory in your fork can be deleted.
- v0.8.x named unique group indexes and translation-sidecar indexes `Index_{table}_{columns}_Unique`
  (lower case on PostgreSQL); a new database names them `ux_…`. `migrate:check` matches by columns,
  not names; before a later migration drops or renames one of these indexes by name, look up its
  actual name.

## If step 4 was skipped

Skip step 4 and the baseline does not record the fork's migrations. Nothing shows it at first:
`migrate:check` is clean and the API starts, because the check compares entities with tables, not
with migrations. Once you add the migration, `migrate:status` lists it as `Pending`, the API refuses
to start, and `migrate` fails on the table that already exists:

```
$ dotnet run --project src/Struo.Api -- migrate
…
migrate failed: Migration run failed; lowest unrecorded version: 202610120942. 42P07: relation "notes" already exists
```

As long as no migration has been applied since the baseline, clear the version table, add the
migrations, run `migrate:baseline` again, and confirm with `migrate:status`, `migrate` and
`migrate:check`:

```sql
DELETE FROM struo_schema_versions;
```

## What's next

Once joined, day-to-day schema management and core upgrades are back in
[Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md); what each version changed is
in the [changelog](changelog.md).
