# Changelog

## 0.11.0

::: danger Breaking
- `Errors` and `Warnings` on `SchemaCheckReport` are methods: `GetErrors()` and `GetWarnings()`. Code that
  reads them as properties must call the methods.
- `IRelationExpander.ExpandAsync` and `RelationExpander.ExpandAsync` take one `EntityAccessors` in place of
  the three delegates, right after `deep`. Its members are `ProjectTarget`, `ParentId` and `ReadProp`. A
  fork that implements `IRelationExpander` must change its `ExpandAsync` signature to accept
  `EntityAccessors`; a fork that calls `ExpandAsync` directly must pass `new EntityAccessors(...)`.
- `GraphQlQueryBuilder.BuildQuery` takes a `GraphQlQueryArgs` (filter, sort, paging, search, `deep`,
  facets, aggregate), followed by `collection` and `relationTarget`. Code that calls it directly must
  build a `GraphQlQueryArgs` first.
- `SchemaTypeMapper.TagItemInputName` is a constant, not a method. Change `TagItemInputName()` to
  `TagItemInputName`.
- The template no longer ships a bootstrap admin password. `Auth:BootstrapAdmin:Password` is `""` in
  `appsettings.json` and `STRUO_ADMIN_PASSWORD` defaults to empty in `docker-compose.yml`. When the
  password is empty, the run that creates the admin generates a random password of at least 24
  characters and logs it once as a Warning. A configured password that fails `Auth:Password` or equals `admin` in any letter case
  is refused in every environment: `migrate`, `migrate:preview` and a `Database:MigrateOnStartup` start fail
  with an error naming `Auth__BootstrapAdmin__Password`, even on a database that is already seeded, until
  the value is fixed or removed. The Production-only default-password warning is gone. The Playwright suites read the password from
  `E2E_PASSWORD`, which is required. A fork that relied on `admin@admin.com` / `admin` must set a
  password or read the generated one from the `migrate` output.
- An enabled `Oidc` section whose `Authority` has a path segment of `common`, `organizations` or
  `consumers` is refused at startup unless `Oidc:AllowedTenantId` or `Oidc:AllowedEmailDomains` is set.
  The error names both keys. A fork with a multi-tenant authority and no pin must set one of them or
  switch to a single-tenant authority.
- `GET /api/items/{collection}/{id}/revisions` returns 404 for an unknown item id; it returned an empty
  array. The revisions of a trashed item (list and single revision, REST and GraphQL) need delete
  permission on the collection; a caller without it gets 404 on REST and an empty list or `null` on
  GraphQL. `IItemUseCases.ListRevisionsAsync` and `IGraphQlDataSource.ListRevisionsAsync` return a
  nullable list, where `null` means the item is unknown or hidden. Code that reads the result must handle
  `null`, and a fork's own implementation must return `null` for a hidden item, or it will not hide
  trashed items.
- `/openapi/v1.json` and `/scalar` are mapped in Development only, and the session cookie is always
  `Secure` in every environment except Development; both were gated on Production alone, so a
  Staging-like environment (Staging, UAT, ...) exposed the API spec and sent cookies over plain HTTP.
  The new `OpenApi:Enabled` and `Auth:Cookie:RequireSecure` keys override each default. A fork that
  serves OpenAPI outside Development must set `OpenApi__Enabled=true`, and one that runs over HTTP
  outside Development must set `Auth__Cookie__RequireSecure=false`.
- Every GraphQL mutation requires a signed-in caller. An anonymous mutation is refused with
  `UNAUTHORIZED` even when the `public` role holds a write grant on the collection; queries are
  unchanged. This matches the REST write endpoints, which already require sign-in. A client that wrote
  through GraphQL without a session cookie or bearer token must sign in first.
- User emails are stored trimmed and lowercase, on every write path: `POST /api/users`, create and update
  of the `user` collection through the items API and GraphQL, accounts created by OIDC sign-in, and the
  bootstrap admin. An email that matches another user's in any letter case is refused with `409`
  `CONFLICT`. Sign-in still matches in any letter case, so rows stored with mixed case keep working, and
  no existing row is rewritten. A client that expects the casing it sent to come back now receives
  lowercase. Before upgrading, an operator can list pre-existing case-duplicates with
  `SELECT lower(email), count(*) FROM struo_users GROUP BY lower(email) HAVING count(*) > 1;` (adjust the
  table name to your `Database:TablePrefix`) and merge or delete the extra rows.
- `ILoginAttemptThrottle` has `TryReserveAsync` in place of `CheckAsync` and `RecordFailureAsync`;
  `ResetAsync` is unchanged. `TryReserveAsync` counts the attempt and reports whether it may proceed,
  and the caller invokes it before verifying the password. A fork that implements the interface must
  provide it, and a fork that calls the old methods must call it instead.
- Every login attempt counts against `RateLimiting:LoginAccount:PermitLimit`, not only failed ones, and
  with `Redis:ConnectionString` set a login fails with a 5xx error while Redis is unreachable.
- `ILanguageProvider` is asynchronous: `Enabled()`, `DefaultCode()` and `IsEnabled(code)` are replaced by
  `GetEnabledAsync()`, `DefaultCodeAsync()` and `IsEnabledAsync(code)`, and `CanonicalizeAsync(code)`
  returns the configured spelling of an enabled language, or `null`. A fork that implements the
  interface must change those signatures and add `CanonicalizeAsync`, and a fork that calls the old
  methods must call the async ones.
- A locale key in a translations payload and a `?locale=` value are stored and queried in the language
  row's configured spelling (`ZH-tw` becomes `zh-TW`). No existing row is rewritten. On PostgreSQL and
  SQLite, where locale comparison is case-sensitive (and on Oracle, which is case-sensitive by default and
  is documented as unverified), a database that already holds translation rows for one item under two
  spellings of the same locale keeps both; an operator can find them with
  `SELECT <foreign key column>, lower(locale), count(*) FROM <translation table> GROUP BY <foreign key column>, lower(locale) HAVING count(*) > 1;`
  (substitute the translation table's foreign key column) and delete the extra row. A row with a single
  spelling that is not exactly a configured language code (`zh-tw` where the language is `zh-TW`) is no
  longer found on those backends, and the next update of its item inserts a second row beside it. After
  removing duplicates as above, find such rows with
  `SELECT DISTINCT locale FROM <translation table> WHERE locale NOT IN (SELECT code FROM <language table>);`
  and fix each one with
  `UPDATE <translation table> SET locale = '<configured code>' WHERE locale = '<stored spelling>';`
  The framework's own tables carry the `Database:TablePrefix` (default `struo_`); a table of your own
  keeps the name you gave it.
- Three new `Query` keys bound the cost of one request. `MaxSortFields` (default 5) caps the fields of a
  top-level sort and of each nested list's `sort`, `MaxDeepRelations` (default 20) caps the relation
  nodes in a `deep` tree, and `MaxExpandRows` (default 1000) caps the rows one relation fetch may read.
  `MaxFilterConditions` is now one budget shared by the root filter and every nested `deep` filter, where
  each nested filter used to count on its own. A request over any of these limits is refused with 400;
  a relation fetch over `MaxExpandRows` is refused, never truncated. `IItemRepository.QueryWhereInFilteredAsync` and
  `QueryEntityWhereInAsync` take an optional `int? maxRows` before the cancellation token, so a fork
  that implements the interface must add it, and a call that passed the token positionally must name it
  (`ct: ct`). `QueryValidator.Validate` takes an optional `FilterBudget`.
  `DeepExpansionCoordinator.ExpandAsync` is replaced by `Prepare`, which validates the `deep` tree and
  takes a `FilterBudget?`, and `ExpandPreparedAsync`, which expands the prepared tree; a caller that used
  `ExpandAsync` calls both. `ItemService` validates the `deep` tree before the search lookup or the root
  query, so an over-cap `deep` on a `GET` by id returns 400 even when the id does not exist (it returned
  404 before). There is no cap on `offset`.
- The media library's trash and purge of a file (`DELETE /api/files/{id}`, with `?purge=true`) apply the
  relation rules every other collection's delete applies. A file still referenced through an
  `OnDelete.Restrict` relation returns `409 CONFLICT` on both, where it was trashed or purged. A purge sets
  `OnDelete.SetNull` references to null and removes many-to-many junction rows, where it left them
  pointing at a missing file. The generic items API and GraphQL delete of a file go through the same path.
- Image transform parameters are normalized, and the transform is bounded. A requested `width` or
  `height` snaps up to the next value of `Struo:Files:ImageTransform:SizeSteps` (default 64, 128, 256,
  320, 480, 640, 768, 1024, 1280, 1600, 1920, 2560, 3840, 4096), so `width=100` returns a 128-wide
  image. With `fit=cover` only the width snaps and the height follows the requested ratio, rounded to a multiple
  of 8 pixels. `fit` is
  `inside` or `cover`; any other value is `400`. A client-supplied `quality` rounds to a multiple of 10 within 10 to 100.
  Only sources in `TransformableContentTypes` (JPEG, PNG, WebP, AVIF, GIF) are transformed; an SVG with
  `?width=` returns the original bytes, as does a source over `MaxSourcePixels` (50 000 000) or whose
  header cannot be read, and an AVIF grid image is never transformed. At most `MaxConcurrentTransforms` (2) transforms run at once, and a request
  that waits longer than `QueueTimeoutSeconds` (10) gets `503` `IMAGE_TRANSFORM_BUSY`. The variant cache
  is capped by `CacheMaxBytes` (2 GB) with least-recently-used eviction. A client that needs an exact
  pixel size must request a size on the list or configure `SizeSteps`, and one that passed another
  `fit` value must change it.
:::

### Added

- `[CmsCollection(Name = "...")]` pins a collection's name. The name is the key stored in permission
  grants, revisions and other rows, so a fork that sets it before it holds data can rename the C# class
  without orphaning them (a translation sidecar's foreign key property must still be `{ClassName}Id`). It
  must match `^[a-z][A-Za-z0-9]*$` and be unique; a clash fails startup with a `MetadataException` naming
  both types. Without `Name` the collection keeps the camel-cased class name.

- Editors can switch a file between `published` and `draft` in the media library's file dialog, and the
  items API refuses a file `status` other than those two with `400`. `Struo:Files:DefaultStatus`
  (`published` or `draft`, default `published`) sets the status of a new upload; any other value fails
  startup. A published file stays readable by anyone who has its id. Chapter 15 describes the model.
  A file row whose stored `status` is neither value is refused with `400` on any update that sends
  `status`, which includes saving its title or alt text in the admin UI; send a valid `status`
  through `PUT /api/items/file/{id}` or correct the column directly to repair such a row.

### Security

- On MySQL and MariaDB, a language code could inject SQL through a sort on a translatable field. A
  language written with a `"Code"` JSON key instead of `code` skipped the code-format check, and the
  sort embedded the locale in the ORDER BY text as a quote-doubled string literal, which those
  backends do not treat as safe. Sorting is now built from typed SqlSugar expressions and binds the
  locale as a SQL parameter, so no SQL text is assembled for any sort. The code-format rule
  (`[A-Za-z0-9_-]`, 1 to 35 characters) now runs on the stored language row inside the write
  transaction, whatever the JSON key casing.

- A password or OIDC login that arrived with a valid session cookie signed the new user into that
  cookie's existing session, so a cookie planted in a victim's browser became the victim's session. A
  sign-in now always issues a fresh session, and the session the request presented is revoked once the
  sign-in succeeds. A failed login leaves it untouched.

- A login attempt is now counted against the account before its password is verified, so parallel
  guesses can no longer all read the same count and slip past `RateLimiting:LoginAccount:PermitLimit`.
  With `Redis:ConnectionString` set the count is a Redis atomic counter shared by every instance. A new `RateLimiting:LoginConcurrency`
  section (`Enabled` true, `PermitLimit` 4, `QueueLimit` 32) caps how many logins verify a password at
  once in a process; the rest get 429 with `Retry-After`. Anyone who knows an account's email can keep it
  locked out of password sign-in until the window ends; SSO sign-in is unaffected.

### Fixed

- A `Query:DefaultLimit` larger than `Query:MaxLimit` fails startup with an error naming both keys; the
  host used to start and serve default pages larger than `MaxLimit`. A `Struo:ContentAssemblies` entry that
  cannot be loaded still fails startup with a `MetadataException`, which now carries the original load
  failure as its `InnerException`.

- The enabled-language list loads with an asynchronous query, and a failed load is not kept for the rest
  of the request: the next access queries again.

- `/health/live` and `/health/ready` no longer resolve the caller's permissions, so a database failure in
  the role store cannot make the liveness probe return 500 and restart healthy instances.

- A Production start with an empty `Redis:ConnectionString` logs one warning: sessions and login
  throttling are then kept in process memory, so only a single replica works and a restart signs every
  user out. Replacing a role's permission grants joins a transaction the caller already opened instead of
  committing its own. Chapter 16 states that an API access token survives a password change.

- A write that hits a database unique key (a duplicate role name, for example) returns `409 CONFLICT` with the
  message `A record with the same unique value already exists.` on REST and GraphQL, on every supported
  backend; it returned a 500. The message never names the index or column.

- Purging an item sets the foreign key to null on every row that references it, and that statement now quotes
  the column name. A relation field whose column name is a reserved word (`order`, `user`, `group`) made the
  purge fail with a SQL syntax error, and a mixed-case custom column name did not match on PostgreSQL. The
  optimistic-lock update and the trash and restore guards already filtered on the `id`, `version` and
  `deletedat` columns, so they are unaffected; they now use the same typed filter.

- Deleting a file through the generic items API (`DELETE /api/items/file/{id}`) or the GraphQL `deleteFile`
  mutation does what the media library does. Trashing clears the brand logo when it was that file; purging
  also deletes the translations, the logo reference and the stored blob. A purge no longer leaves the blob
  behind, and a storage failure while deleting the blob is logged as a warning with the storage key instead
  of being ignored.

### Upgrade notes

The Redis login throttle counts under new keys, so each account's login-attempt count starts from
zero at upgrade. During a rolling deploy, instances still on 0.10 and instances on 0.11 count under
different keys, so an account can receive up to twice its limit in attempts until every instance runs
0.11.

The language-code format rule checks every row of the language collection on each write to it,
including a delete. A malformed code that is already stored blocks every write to the collection
until it is corrected, and with two or more malformed rows the API can neither fix nor delete them.
Check the stored language rows for codes outside `[A-Za-z0-9_-]{1,35}` and correct or delete the
malformed rows directly in the database before upgrading.

If OIDC is enabled and `Oidc:Authority` has a path segment `common`, `organizations` or `consumers`
(or the consumers tenant GUID), set `Oidc:AllowedTenantId` or `Oidc:AllowedEmailDomains` before
upgrading, or the host refuses to start.

If your `.env` was copied from an earlier `.env.example`, it sets `STRUO_ADMIN_PASSWORD=admin`. Clear
it, or set a value that meets `Auth:Password`, before upgrading: the compose `api` service runs
`Database:MigrateOnStartup` and refuses to start with that value, even on a database that is already
seeded.

## 0.10.0 — 2026-10-14

::: danger Breaking
- REST responses carry every `DateTime` as a UTC instant ending in `Z`, such as
  `2026-10-14T02:00:00Z`. They used to be zone-less for most columns. A client that read those
  zone-less strings as local time now reads a different time, so check any code that parses these
  values. Revision snapshots taken in 0.10.0 and later carry `Z` too, over REST and GraphQL.
- A date-only value written through the API, such as `2026-10-14`, is UTC midnight and comes back as
  `2026-10-14T00:00:00Z`, so a browser west of UTC shows the previous day.
:::

### Fixed

- Date-time fields in the admin show the browser-local time. A value entered as 10:00 in UTC+8 used
  to show 02:00 after a reload.
- A date-time written through REST or GraphQL with an offset, such as `2026-10-14T10:00:00+08:00`,
  is stored as that instant in a plain `DateTime` column, and in a zone-aware column on MySQL,
  MariaDB and SQLite. It was stored as the API host's local time.

### Upgrade notes

Values written through the API with a non-`Z` offset by 0.9.4 and earlier, on a host outside UTC,
were stored as the host's local time in a plain `DateTime` column, and in a zone-aware column on
MySQL, MariaDB and SQLite. Those rows cannot be told apart from correct ones, so they need a manual
correction if they matter. Zone-aware columns on PostgreSQL and SQL Server have been stored
correctly since 0.9.2.

Values written by the admin were always stored correctly. Revision snapshots taken by 0.9.4 and
earlier keep their zone-less strings.

## 0.9.4 — 2026-10-14

### Security

- `System.Security.Cryptography.Pkcs` moves to 10.0.13. `Microsoft.Data.SqlClient` (through
  `SqlSugarCore` and `FluentMigrator.Runner.SqlServer`) and `Oracle.ManagedDataAccess.Core` pulled in
  9.0.0, affected by GHSA-846f-f764-76mx (High) and GHSA-2fmv-rv2c-59x7 (Moderate), both .NET
  denial-of-service advisories. NuGet audit fails the restore of 0.9.3 and earlier on them.

## 0.9.3 — 2026-10-13

### Fixed

- A plain `DateTime` column (one without `TimestampWithTimeZone`, such as `CreatedAt`, `UpdatedAt`,
  `DeletedAt` and a fork's own date fields) filtered with `_eq`, `_neq`, `_gt`, `_gte`, `_lt`, `_lte`,
  `_in` or `_nin` compares a value with `Z` or an offset at the instant it names, and a value without a
  zone as UTC. Before 0.9.3 such a value was read as the API host's local time, so on a host outside
  UTC the result was off by the host's offset; queries like this now return different rows there,
  which is the fix.
- On SQL Server, `min` and `max` over a `TimestampWithTimeZone` column no longer throw
  `InvalidCastException`.
- On PostgreSQL, `min` and `max` over these columns return a UTC instant (JSON ending in `Z`)
  instead of one carrying the API host's offset.

### Security

All of these are transitive dependencies of build tooling and do not reach the built application:

- `source-map-js` in the frontend and the docs site moves to 1.2.2 (GHSA-68fv-2mgg-jv7q).
- `vue` in the docs site moves to 3.5.43, which brings a fixed `@vue/server-renderer`
  (GHSA-g2v6-rqmx-r4w6).
- `postcss-selector-parser` in the frontend moves to 7.1.6 (GHSA-rj75-hqrm-r3gf).
  `@tailwindcss/typography` pins it to 6.0.10 and 6.x has no fix, so an override in
  `frontend/pnpm-workspace.yaml` takes it across the major.

### Upgrade notes

No data correction is needed: the timestamps the CMS writes itself (audit fields, revisions,
sessions and the like) were always right, and only how filters and aggregates read them changed.

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
