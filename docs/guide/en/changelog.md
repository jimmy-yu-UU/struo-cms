# Changelog

## 0.9.2 — 2026-10-13

### Fixed

- A `DateTime` column declared `[ColumnShape(ColumnShape.TimestampWithTimeZone)]` stores a UTC
  instant on PostgreSQL and SQL Server and reads back as `DateTimeKind.Utc`, whatever the PostgreSQL
  server's `TimeZone` and the API host's zone. In core these are `UserSession.CreatedAt`/`ExpiresAt`,
  `SiteSettings.UpdatedAt` and `MediaFolder.CreatedAt`/`UpdatedAt`.
- Updating a media folder leaves `MediaFolder.CreatedAt` unchanged, whether or not the API host and
  PostgreSQL share a time zone.
- On SQL Server these columns keep 100 ns precision instead of rounding to 1/300 s.
- REST and GraphQL filters on these columns with `_eq`, `_neq`, `_gt`, `_gte`, `_lt`, `_lte`, `_in` or
  `_nin` compare the value as an instant: one with `Z` or an offset at the instant it names, one
  without a zone as UTC. The result depends on neither the database server's zone nor the API host's,
  on every backend. In 0.9.1, MySQL, MariaDB and SQLite read a `Z` or offset value as the API host's
  local time, and PostgreSQL read a zone-less value in the session zone. `_contains`, `_starts_with`
  and `_ends_with` are unchanged.
- On PostgreSQL and SQL Server these columns reach API JSON as UTC strings ending in `Z`, for
  example `MediaFolder`'s `createdAt` and `updatedAt` and a fork's own `TimestampWithTimeZone`
  fields. PostgreSQL emitted the API host's offset (such as `+08:00`) and SQL Server no zone suffix;
  check clients that parse these strings.

### Upgrade notes

Only PostgreSQL needs action, and only if the session time zone (the server's `TimeZone`, or the
connection string's `Timezone` when set) was not UTC while 0.9.1 or earlier wrote the data. Every
value written then is early by that zone's offset, and 0.9.2 reads it at its true instant, so it
shows up shifted by that offset.

To find the old zone, run `SHOW timezone;` in psql connected as the API's database user to the API's
database, with `PGTZ` unset: a `timezone` set by `ALTER DATABASE` or `ALTER ROLE` makes it differ from
the server default. If the API's connection string sets `Timezone`, or the API host sets the `PGTZ`
environment variable, that value is the old zone.

Back up the database first. Then stop the old API and, before starting 0.9.2, run the correction
below once, with `'Asia/Taipei'` replaced by the old zone and the `struo_` prefix replaced by your
`Database:TablePrefix`. Data written by 0.9.2 is already correct, so run it exactly once:
running it again, or after 0.9.2 has written rows, shifts them a second time. Correct a fork's own
`TimestampWithTimeZone` columns the same way.

```sql
BEGIN;
UPDATE struo_user_sessions
   SET createdat = (createdat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC',
       expiresat = (expiresat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
UPDATE struo_site_settings
   SET updatedat = (updatedat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
UPDATE struo_media_folders
   SET createdat = (createdat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC',
       updatedat = (updatedat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
COMMIT;
```

psql prints `BEGIN`, three `UPDATE <row count>` lines and `COMMIT`.

Where the API host's zone differed from PostgreSQL's, the drift `MediaFolder.CreatedAt` has already
accumulated is not recoverable by this correction: the database does not keep the original value.
SQL Server, MySQL, MariaDB and SQLite need no action.

## 0.9.1 — 2026-10-13

::: danger Breaking
- Compose publishes every port on `127.0.0.1` by default, so only the machine running the stack
  reaches PostgreSQL, Redis, the API, the admin and MinIO. Anyone who reached the stack from another
  machine must set `STRUO_BIND_ADDRESS=0.0.0.0` (in `.env` or the shell), which publishes the ports
  on all network interfaces. That exposes the fixed dev passwords and the password-less Redis to the
  network.
:::

### Fixed

- `migrate:preview` prints the core seed's `INSERT` with the bootstrap admin's password hash
  replaced by `<redacted>`; the admin's email stays visible. `migrate` and `MigrateOnStartup` still
  write the real hash.

## 0.9.0 — 2026-10-12

::: danger Breaking
- Tables come only from FluentMigrator migrations; CodeFirst no longer creates tables at startup. The API
  refuses to start while migrations are pending. Run `migrate` (`dotnet run --project src/Struo.Api -- migrate`,
  or `dotnet Struo.Api.dll migrate` in the image) as a deployment step, or set `Database:MigrateOnStartup=true`.
- Every collection, translation sidecar and junction table needs a migration: generate one with
  `make:migration <Name> --entity <Type> --output <dir>` and apply it with `migrate`.
- A database built by v0.8.x makes startup, `migrate` and `MigrateOnStartup` refuse and ask for
  `migrate:baseline`. Generate migrations for the fork's own existing tables first, run
  `migrate:baseline` once, then use `migrate`. The full procedure is in
  [chapter 24](24-upgrading-from-0-8.md).
- `Database:MigrationsPath`, `Database:AutoSyncSchema` and `db/migrations/` are removed, and `.sql`
  scripts are not run; startup logs a warning while either key is still set. Express future schema
  changes as FluentMigrator migrations deriving `StruoMigration`. The `{prefix}schema_migrations` table
  is unused; versions are recorded in `{prefix}schema_versions`.
- Seeding runs in the core seed migration, during `migrate` or `MigrateOnStartup`. It reads
  `Auth:BootstrapAdmin:*`, `Localization` and `Rbac:PublicReadCollections`; the environment that runs
  `migrate` must have them.
- Removed public types: `DatabaseInitializer`, `MigrationRunner`, `SchemaMigration`, `SchemaGuard`
  (with its `TranslationSidecarDescriptor`), `DataSeeder`, `LanguageSeeder`, `AdminUserSeeder`,
  `RbacSeeder` (the last three survive only as helpers in the test project). Remove fork code that calls them.
- In Development, startup runs `SchemaChecker` against the entity model and aborts on any error
  (it replaces `SchemaGuard`, with broader checks). Use `migrate:check` for the findings.
- Compose services have no fixed `container_name` any more (`struo-postgres` becomes
  `<project>-postgres-1`); change scripts that use `docker exec struo-postgres`. Named volumes keep
  their names, so data carries over.
- Databases upgraded from v0.8.x need per-backend follow-ups (SQL Server: the access-token index and
  `varchar` → `nvarchar`; MySQL/MariaDB: `datetime(6)` and utf8mb4); see chapter 24.
:::

### Added

- Migration commands, each the first argument to `Struo.Api`: `migrate`, `migrate:status`,
  `migrate:preview`, `migrate:baseline`, `migrate:check`, `make:migration`. Exit codes: 0 success,
  1 failure, 2 usage error.
- Configuration keys `Database:MigrateOnStartup` (default `false`) and
  `Database:MigrationLockTimeoutSeconds` (default 60, range 1–3600).
- `migrate` takes an advisory lock so concurrent runs apply each migration once: PostgreSQL,
  MySQL/MariaDB and SQL Server. SQLite and Oracle take none; Oracle logs a warning.
- `StruoMigration` base class with `Db`, `FrameworkTable(...)`, `CreateTranslationUniqueIndex(...)` and
  the column helpers `AsJson(Db)`, `AsLongText(Db)`, `AsTimestamp(Db)` and `AsShape(shape, Db)`.
- `SchemaChecker.Check(db, dbType, entityTypes)`: fork tests can assert `HasErrors` is false.
- `ColumnShape.Timestamp`.
- Core migrations `202610080000_CreateCoreSchema` and `202610080001_SeedCoreData`; the sample Blog ships
  `202610080100_CreateBlogSchema`.
- `docker compose up -d --build` starts the whole stack: postgres, redis, `api` (port 8081) and `admin`
  (port 8080); variables `STRUO_ADMIN_PORT`, `STRUO_API_PORT`, `STRUO_ADMIN_EMAIL`,
  `STRUO_ADMIN_PASSWORD`.
- New dependency `FluentMigrator.Runner.*` 8.0.1 (Core, Postgres, SqlServer, MySql, SQLite, Oracle).
- Test connection keys `Testing:SqlServerConnection`, `Testing:MySqlConnection` and
  `Testing:MariaDbConnection`, overridden only by `STRUO_TEST_SQLSERVER_CONNECTION`,
  `STRUO_TEST_MYSQL_CONNECTION` and `STRUO_TEST_MARIADB_CONNECTION`; `STRUO_TEST_BACKEND` moves the
  whole test suite to a live backend. See chapter 22.
- `migrate` and `MigrateOnStartup` do not write the SQL they execute to the log, because the seed's
  `INSERT` carries the bootstrap admin's password hash. `migrate:preview` prints that SQL, so treat
  its output as a secret.

### Changed

- SQL Server 2025, MySQL 8.4 and MariaDB 11.8 are verified backends, alongside PostgreSQL; Oracle stays
  unverified. The versions table is in `AGENTS.md`, "Verification".
- SQL Server string columns are `nvarchar`; `migrate:check` warns (`NonUnicodeString`) about
  `varchar`/`char`/`text` columns.
- On MySQL/MariaDB a `DateTime` without a shape is `datetime(6)` (microseconds); other backends are
  unchanged.
- The API image no longer contains `db/migrations`. CI's docker job runs `migrate` before the smoke
  test and also smoke-tests the Compose stack.
- `docker compose up -d` also builds and starts the API and the admin SPA. To start only the
  dependencies for development, run `docker compose up -d postgres redis`; with MinIO,
  `docker compose --profile s3 up -d postgres redis minio createbuckets`.
- Backend packages: `AWSSDK.S3` 4.0.104.1, `NetVips.Native` 8.18.7, `Scalar.AspNetCore` 2.17.13,
  `coverlet.collector` 10.1.0.
- Frontend packages: the 11 `@tiptap/*` packages 3.31.4, `@lucide/vue` 1.49.0, `@tanstack/vue-table`
  9.2.6, `vue-i18n` 11.4.13, `vite` 8.3.2, `@types/node` 26.6.4, `jsdom` 30.1.2, `vue-tsc` 3.3.12.

### Fixed

- SQL Server: users without an access token can coexist; the `accesstoken` index is non-unique (the
  unique index allowed a single NULL). Fresh databases only; for upgraded ones see chapter 24.
- SQL Server: `aggregate[avg]` over integer fields no longer divides as integers.
- SQL Server: `aggregate[sum]` over `int` fields no longer overflows past 2^31.
- SQL Server: a cancelled request surfaces as a cancellation, not as a `SqlSugarException`.
- MySQL/MariaDB: two writes within one second stay ordered (microsecond timestamps). Fresh databases
  only.
- Uploads with a file name over 255 UTF-16 units no longer fail: the name is shortened to the column width,
  keeping the extension; neither the name cut nor the title cut splits a surrogate pair.
- A process that uses more than one database backend no longer mixes column types or row binders between
  them (`ProviderScopedReflectionCache`).

## 0.8.2 — 2026-10-01

### Changed

- Backend packages: `AWSSDK.S3` 4.0.103.4, `HotChocolate.AspNetCore` 16.6.7, `Scalar.AspNetCore`
  2.17.10.
- Frontend packages: `@lucide/vue` 1.48.0, `vite` 8.3.1, `@types/node` 26.6.3.

## 0.8.1 — 2026-10-01

### Fixed

- The admin sidebar's version label reads `version` from `frontend/package.json` (inlined by Vite at
  build time) instead of a literal.

## 0.8.0 — 2026-10-01

::: danger Breaking
- The framework's 12 tables carry a prefix set by `Database:TablePrefix`, default `struo_` (`users` →
  `struo_users`, `schema_migrations` → `struo_schema_migrations`). Sample and fork collections are not
  prefixed. On an existing database, startup creates a fresh, empty set of framework tables under the
  new names and seeds them (bootstrap admin included); the existing tables are not read. Pick one: set
  `Database:TablePrefix` to the empty string to keep the existing tables, or rename them to the
  prefixed names before starting.
- Core index and unique-constraint names follow the table name: `ix_<table>_<column>`,
  `ux_<table>_<meaning>`, for example `ux_struo_revisions_item_no`. Update any migration of yours that
  refers to a core index by name.
- Forks with `Database:MigrationsPath` set: the tracking table is prefixed too (`struo_schema_migrations`)
  and the new one is empty, so starting without acting re-runs every script in the directory, and the
  first `ALTER` against an existing table fails and aborts startup. Same remedy as above: set the prefix
  to the empty string, or rename `schema_migrations` along with the other tables.
- A migration of yours that names a core table (for example `ALTER TABLE users`) must use the prefixed
  name (`struo_users`). Renaming tables instead of recreating them keeps the existing index names; they
  do not become `ux_struo_…` on their own.
- `LanguageSeeder.SeedAsync` and `DataSeeder.SeedAsync` take a `LocalizationOptions` argument; affects only fork code that calls them directly.
- With no enabled language, `ILanguageProvider.DefaultCode()` throws instead of returning `"en"`. Unreachable in normal operation (see the `language` collection rules below).
- The admin SPA's UI-locale API changed: the `UiLocale` type moved to `@/locales`; `DEFAULT_UI_LOCALE`
  is removed; `resolveInitialUiLocale(saved, enabled, fallback)` is pure; `uiLocaleStore.set()` returns
  a boolean and refuses locales outside the enabled set; `uiLocaleStore.configure()` must run before
  mount. Forks that edited these files adjust accordingly.
- The i18n fallback locale is `AdminUi:DefaultLocale` (`zh-TW` as shipped) instead of a fixed `en`.
- An existing `struo_languages` table that already breaks a rule (zero or two defaults, `en` next to
  `EN`) refuses any write that leaves it broken; one corrective write fixes it.
:::

### Added

- `Database:TablePrefix` configuration key (env `Database__TablePrefix`), validated at startup.
- `Localization` section: seed source for the `struo_languages` table when it is first created (`Languages`, `DefaultLanguage`), validated at startup.
- `AdminUi` section: admin-UI locales offered and the default (`Locales`, `DefaultLocale`), published by `GET /api/config` as `uiLocales` / `uiDefaultLocale`; the switcher is hidden with a single locale.
- Write rules on the `language` collection: `code` unique case-insensitively, at least one enabled row, exactly one enabled default; violations answer 400.

## 0.7.0 — 2026-09-23

The template as it stood when the project went open source. Entries start with this version.
