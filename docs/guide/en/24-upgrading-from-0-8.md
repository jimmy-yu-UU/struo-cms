# 24. Joining a Database Built by v0.8.x

This chapter joins a database that v0.8.x built with CodeFirst at startup to the migrations, so you
can manage it with `migrate` as [Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md)
describes.

Such a database has the framework tables but no version table. `migrate:baseline` exists for exactly
this case: it records the migrations that match the v0.8 schema as applied without running them.
The steps below were walked through on PostgreSQL, SQL Server, MySQL and MariaDB.

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
   once for every collection, translation sidecar and junction whose table already exists. The run
   below comes from a rehearsal whose content project sat at `samples/Acme.Content`; point
   `--output` at your own content project's migrations folder, and `--namespace` at its namespace:

   ```
   $ dotnet run --project src/Struo.Api -- make:migration CreateNotes --entity Note --output ../../samples/Acme.Content/Migrations --namespace Acme.Content.Migrations
   Created ../../samples/Acme.Content/Migrations\202610120942_CreateNotes.cs
   ```

   Review the generated files and rebuild; if you deploy the image, rebuild it. Don't generate a
   migration for a table that doesn't exist yet: baseline would record it as applied and the table
   would never be created. Add those after step 5. A relative `--output` resolves against
   `src/Struo.Api`, and the directory must already exist; see
   [Writing a migration](21-schema-and-upgrades.md#writing-a-migration) in Chapter 21.

   A fork with more than one database built before migrations baselines all of them before it adds
   any fork migration beyond the ones step 4 writes for existing tables. Baseline records every fork
   migration present at that moment as applied, without running it, on whichever database it runs
   against, so a migration added after one database is baselined is skipped on any database
   baselined later. Before baselining such a database, confirm its schema already contains what
   those migrations create.
5. **Run `migrate:baseline`, once.** Until then, both `migrate` and API startup refuse, with a
   message telling you to run `migrate:baseline`. Baseline takes the migration lock, writes the
   versions in one transaction — core up to and including `SeedCoreData`, the sample if enabled, and
   your fork — and logs the SQL FluentMigrator runs:

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
6. **MySQL and MariaDB only: convert the database and the framework tables to utf8mb4.** `migrate`
   refuses a database whose default character set, or the default of a framework table, is not
   utf8mb4, so this comes first; the section "MySQL/MariaDB: character set to utf8mb4" below has
   the statements. On PostgreSQL and SQL Server, go on to step 7.
7. **Run `migrate`.** The core migrations newer than `SeedCoreData` are applied here, such as
   `AddLanguageCodeUniqueIndex`, `CreateRoleCapabilities`, `DropRoleCapabilities` and
   `ConvertStringColumnsToUtf8Mb4`, and so are any migrations added after the baseline; with none
   pending it prints `Nothing to migrate.`. If two language codes are ones the database treats as
   equal, `AddLanguageCodeUniqueIndex` stops the run; the [changelog](changelog.md) says how to find
   and remove them. On MySQL and MariaDB, `ConvertStringColumnsToUtf8Mb4` stops the run when two
   values in a unique column are equal under the utf8mb4 collation; the [changelog](changelog.md)
   says how to find and remove them, and the section on the character set below says which
   columns it leaves alone. If you skipped step 6, `migrate` stops before it applies anything and
   prints what to run; the section on the character set below shows the message.
8. **Run `migrate:check`.** PostgreSQL should show `0 error(s), 0 warning(s)`. MySQL and MariaDB do
   too when every string column is utf8mb4, and otherwise warn once per column that is not. SQL
   Server warns once per `varchar` string column:

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
9. **Do the follow-ups for your backend in the next section.** `migrate:check` cannot see SQL
   Server's access-token index or MySQL/MariaDB timestamp precision; on MySQL and MariaDB it warns
   about string columns that are not utf8mb4, which step 6 converts. On SQL Server, finish the
   follow-ups before starting the API: Chinese or emoji text stored before the conversion becomes
   `?` and cannot be recovered.
10. **Start the API.**

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

v0.8.x created string columns as `varchar`, and characters outside the collation's code page (for
example Chinese and emoji under a Latin1 collation) are stored as `?`. If you store such text,
convert before storing it: data already turned into `?` is not recovered by the conversion.

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

A string column that is not utf8mb4 rejects 4-byte characters such as emoji, and the API answers
`INTERNAL_SERVER_ERROR`; `migrate:check` warns once for each such column. A column declared
without a character set takes the default of its table, so `migrate` converts the core string
columns only where the database and the framework tables default to utf8mb4.

Before it applies anything, `migrate` and a `Database:MigrateOnStartup` start read the default
character set and collation of the database and the default collation of every framework table that
exists. If the database or one of those tables does not default to utf8mb4, nothing is applied or
changed, `migrate` exits `1`, the API does not start, and the message names what it found and the
statements to run. A database whose default is utf8mb3 or latin1 is refused, for example one
created without `CHARACTER SET utf8mb4` on a server whose default is latin1. This is `migrate` on a
utf8mb3 database right after step 5, on MySQL 8.4:

```
migrate failed: migrations on MySQL and MariaDB need utf8mb4 as the default character set of the database and the framework tables, and the items below do not have it, so nothing was applied or changed.
Database `u3` has the default character set utf8mb3.
Table `struo_file_translations` has the default collation utf8mb3_general_ci.
Table `struo_files` has the default collation utf8mb3_general_ci.
Table `struo_languages` has the default collation utf8mb3_general_ci.
Table `struo_media_folders` has the default collation utf8mb3_general_ci.
Table `struo_permissions` has the default collation utf8mb3_general_ci.
Table `struo_revisions` has the default collation utf8mb3_general_ci.
Table `struo_roles` has the default collation utf8mb3_general_ci.
Table `struo_schema_versions` has the default collation utf8mb3_general_ci.
Table `struo_site_settings` has the default collation utf8mb3_general_ci.
Table `struo_user_roles` has the default collation utf8mb3_general_ci.
Table `struo_user_sessions` has the default collation utf8mb3_general_ci.
Table `struo_users` has the default collation utf8mb3_general_ci.
Run these statements, then run 'migrate' again:
ALTER DATABASE `u3` CHARACTER SET utf8mb4;
ALTER TABLE `struo_file_translations` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_files` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_languages` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_media_folders` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_permissions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_revisions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_roles` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_schema_versions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_site_settings` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_user_roles` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_user_sessions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_users` CONVERT TO CHARACTER SET utf8mb4;
Convert tables of your own that do not default to utf8mb4 with ALTER TABLE ... CONVERT TO CHARACTER SET utf8mb4;
'migrate:check' lists their columns.
```

Which statements to run depends on the default of the database itself:

| The database default is | The statements to run |
|---|---|
| not utf8mb4 | `ALTER DATABASE`, then one `ALTER TABLE` per table |
| utf8mb4 (any collation) | one `ALTER TABLE` per table, with the database collation |

Run the statements the message prints, or print the `ALTER TABLE` statements with the query below.
When the database default is not utf8mb4 (the message says so), run `ALTER DATABASE` before the
statements of the message and before the query: the query reads the collation of the database,
which until then is not a utf8mb4 collation, and `COLLATE latin1_swedish_ci`, for example, is
rejected after `CONVERT TO CHARACTER SET utf8mb4`. When the database already is utf8mb4, skip that
statement. The statements of the message end in the collation of the database when the database is
utf8mb4 and carry no `COLLATE` when the database itself is not; the statements of the query always
end in it. After `ALTER DATABASE` that collation is the default of utf8mb4, so either way a
converted table and a table created later have the same collation, and comparing columns of the
two does not raise `Illegal mix of collations`. The query also lists your fork's own tables, which
the message does not, and a table that holds a column outside utf8mb4 although its own default is
utf8mb4. These steps work on MySQL 8.4 and MariaDB 11.8 against a utf8mb3 database, a latin1
database, and a `utf8mb4_unicode_ci` database whose tables are utf8mb3:

```sql
ALTER DATABASE `<your database>` CHARACTER SET utf8mb4;
```

```sql
SELECT CONCAT('ALTER TABLE `', t.TABLE_NAME, '` CONVERT TO CHARACTER SET utf8mb4 COLLATE ',
              s.DEFAULT_COLLATION_NAME, ';') AS stmt
FROM information_schema.TABLES t
JOIN information_schema.SCHEMATA s ON s.SCHEMA_NAME = t.TABLE_SCHEMA
WHERE t.TABLE_SCHEMA = DATABASE()
  AND t.TABLE_TYPE = 'BASE TABLE'
  AND (t.TABLE_COLLATION NOT LIKE 'utf8mb4\_%'
       OR EXISTS (SELECT 1 FROM information_schema.COLUMNS c
                  WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA
                    AND c.TABLE_NAME = t.TABLE_NAME
                    AND c.CHARACTER_SET_NAME IS NOT NULL
                    AND c.CHARACTER_SET_NAME <> 'utf8mb4'))
ORDER BY t.TABLE_NAME;
```

`CONVERT TO` converts every text column of the table, keeps its indexes and nullability, and sets
the default character set and collation of the table. The text of a utf8mb3 column and of a latin1
column reads the same afterwards. A column declared on purpose with another character set such as
`ascii` is converted too; review the generated statement for such a table before you run it.

`CONVERT TO` also changes column types: a `TEXT` column becomes `MEDIUMTEXT` (on MySQL 8.4 and
MariaDB 11.8, for latin1 and utf8mb3 columns alike), and afterwards `migrate:check` has no finding
for that column. The core tables have no `TEXT` column; a table of your own can have them, for
example a column that a migration declares with `AsString` for 8193 to 65535 characters or with
`AsFixedLengthString` for 256 to 65535 characters.

After the statements have run, the query returns no rows, `migrate` runs, and `migrate:check`
reports no character-set warning.

#### Comparison after the conversion

The collation of a converted column depends on the path:

| Path | Collation of the converted column |
|---|---|
| `CONVERT TO` without `COLLATE` | the default of the character set |
| `CONVERT TO` with `COLLATE <c>` | `<c>` |
| a column `migrate` re-declares | the default collation of its table |

The default collation of the character set is `utf8mb4_0900_ai_ci` on MySQL 8.4 and
`utf8mb4_uca1400_ai_ci` on MariaDB 11.8, whatever the database collation is. The tables below give
the measured result of five conversions with these two defaults:

- A: MySQL 8.4, `utf8mb3_general_ci` to `utf8mb4_0900_ai_ci`.
- B: MariaDB 11.8, `utf8mb3_general_ci` to `utf8mb4_uca1400_ai_ci`.
- C: MariaDB 11.8, `utf8mb3_uca1400_ai_ci` to `utf8mb4_uca1400_ai_ci`.
- D: MySQL 8.4, `latin1_swedish_ci` to `utf8mb4_0900_ai_ci`.
- E: MariaDB 11.8, `latin1_swedish_ci` to `utf8mb4_uca1400_ai_ci`.

Each cell is "before the conversion → after it", and "yes" means the two values are equal; the last
row asks whether `a` sorts before `_`. The first table is A, B and C:

| Pair of values | A | B | C |
|---|---|---|---|
| `a` and `a` plus a trailing space | yes → no | yes → yes | yes → yes |
| sharp s and `ss` | no → yes | no → yes | yes → yes |
| sharp s and `s` | yes → no | yes → no | no → no |
| full-width `Ａ` and `A` | no → yes | no → yes | yes → yes |
| `a` and `a` plus U+200B | no → yes | no → yes | yes → yes |
| `a` sorts before `_` | yes → no | yes → no | no → no |

The second table is D and E. A dash means that latin1 cannot store the pair, so there is nothing to
convert:

| Pair of values | D | E |
|---|---|---|
| `a` and `a` plus a trailing space | yes → no | yes → yes |
| sharp s and `ss` | no → yes | no → yes |
| sharp s and `s` | no → no | no → no |
| full-width `Ａ` and `A` | — | — |
| `a` and `a` plus U+200B | — | — |
| `a` sorts before `_` | yes → no | yes → no |

A starting collation that is in neither table, or a `COLLATE` other than the two defaults above, is
not covered. Find the pairs of values that become equal with the duplicate-value query in the
[changelog](changelog.md) entry on MySQL and MariaDB string columns, under the collation the column
will have.

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
