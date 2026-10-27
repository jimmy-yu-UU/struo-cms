# Changelog

## 0.14.0

::: danger Breaking
- `Program.cs` no longer holds the host composition. Service registration, the rate limiters, the
  request pipeline and the startup checks moved into `Struo.Api` behind two calls,
  `builder.AddStruoCms()` and `app.UseStruoCmsAsync()`. `Program.cs` is now a short file that your
  fork owns: upstream does not change the composition inside it, and the file itself is yours to edit.

  If your fork edited the old `Program.cs`, take the new one from this release and move your own
  registrations after `AddStruoCms()`; a registration made there wins over the core default. Add
  configuration sources before the call. The core registers `ISearchProvider`, `IItemChangeNotifier`,
  `IHtmlSanitizer`, `IPasswordHasher` and `IFileAccessPolicy` with `TryAdd`, so a registration made
  before `AddStruoCms()` is kept for those too.
  Before:

  ```csharp
  builder.Services.AddStruoInfrastructure();
  builder.Services.AddStruoMetadata(builder.Configuration, typeof(Program).Assembly);
  builder.Services.AddStruoData();
  // ... the rest of the registrations, the rate limiters, then:
  builder.Services.AddScoped<ISearchProvider, MySearchProvider>();
  var app = builder.Build();
  app.UseStruoForwardedHeaders();
  // ... the rest of the pipeline, the migration gate, the migration-CLI branch
  await app.RunAsync();
  ```

  After:

  ```csharp
  builder.AddStruoCms();
  builder.Services.AddScoped<ISearchProvider, MySearchProvider>();

  var app = builder.Build();
  if (await app.TryRunStruoCommandAsync(cli)) return;
  await app.UseStruoCmsAsync();
  await app.RunAsync();
  ```
- `IHtmlSanitizer`, `IPasswordHasher` and `IFileAccessPolicy` are registered with `TryAdd`. A fork
  that registered its own implementation before `AddStruoCms()` now keeps it; before, the core
  default won over a registration made that early.
:::

### Added

- `GraphQl:MaxExecutionDepth` (12), `GraphQl:MaxFieldCost` (150) and `GraphQl:MaxTypeCost` (150) set
  the GraphQL depth and cost limits. Each must be greater than 0, or startup fails with a message
  naming the key. The defaults are unchanged.
- `ConfigureGraphQl` on the `AddStruoCms()` callback gives a fork HotChocolate's builder to add its
  own types and root fields. A name that clashes with the generated schema fails startup with a
  `GraphQL name collision` message. See [Chapter 18](18-extension-points.md).
- `Struo:Files:Backend=custom` lets a fork supply its own `IFileStorage`, registered after `AddStruoCms()`. The
  core builds no storage in that mode and skips the Local and S3 validation; startup fails with a message
  naming the key when no `IFileStorage` is registered. A fork may also replace the `local` or `s3` storage,
  since the core now registers its default with `TryAdd`.
- `IFileStorage.Describe()` has a default (the type name) and is implemented by the local and S3 backends.
  `files:sweep` prints it after `storage:`: the root path for local, the bucket and endpoint host for S3,
  never a credential. See [Chapter 18](18-extension-points.md).
- `StruoDomainException` (in `Struo.Domain`) is a base class for a fork's own client-facing errors: a code, a
  400 to 499 status and a message. REST and GraphQL return them unchanged. A status outside 400 to 499
  throws when the exception is constructed. See [Chapter 12](12-rest-conventions.md#your-own-error-codes).
- A pending migration older than the highest applied one (a branch merged late) is flagged: `migrate` and a
  startup that applies migrations log a warning, `migrate:status` marks the row `(out of order)`, and a
  startup with `MigrateOnStartup` off logs the warning before it refuses on the pending migration. The
  migration is still applied; exit codes are unchanged. See [Chapter 21](21-schema-and-upgrades.md).
- `Auth:Cookie:Name` sets the session cookie's name (default `struo.session`); the CSRF guard and session
  rotation follow it. Startup fails, naming the key, for a name that is not a valid cookie name, a `__Secure-`
  or `__Host-` name on a cookie that is not always `Secure`, or a `__Host-` name on a cookie with a `Domain` or
  a `Path` other than `/`. See [Chapter 4](04-configuration.md) and [Chapter 25](25-after-forking.md).

### Changed

- The `AuthSchemes.SessionCookieName` constant is replaced by `SessionCookieName.Default` and the
  `SessionCookieName` service. A fork that edited the constant sets `Auth:Cookie:Name` instead; code that read
  the constant resolves `SessionCookieName` and reads its `Value`.

## 0.13.1 — 2026-10-27

### Changed

- A SQL Server database needs `READ_COMMITTED_SNAPSHOT` on. Run
  `ALTER DATABASE [your_database] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE` once, with
  no other connection open to the database; Azure SQL Database has it on by default. When it is off,
  startup logs one warning that names the database and the statement to run. Startup still
  succeeds. See [Chapter 20](20-deployment.md#sql-server-turn-on-read-committed-snapshot).

### Fixed

- Concurrent creates of an item with translations failed with a deadlock on MySQL, MariaDB and SQL
  Server, and the API answered 500. A create no longer deletes translation rows before inserting
  them, which was the cause on MySQL and MariaDB; on SQL Server the cause was the default locking
  isolation, covered by the setting above.
- Concurrent updates of different items of a collection with translations failed with a deadlock on
  SQL Server, and occasionally on MariaDB. An update now replaces each stored translation row in
  place, or inserts a row for a new locale, instead of deleting and re-inserting it, which removes the
  MariaDB case; on SQL Server the updates succeed with `READ_COMMITTED_SNAPSHOT` on.

### Security

- The admin image runs `apk upgrade` in its runtime stage, so each build picks up Alpine package
  fixes published after the `nginx-unprivileged` base image was built (brotli-libs CVE-2026-111497,
  curl/libcurl CVE-2026-92392 at the time of this release). The container still runs as uid 101.

## 0.13.0 — 2026-10-22

::: danger Breaking
- The `Oidc` section of `appsettings.json` has a new shape: shared `Oidc:ReturnUrlDefault`, plus one
  entry per identity provider under `Oidc:Providers:{id}` (id: lowercase letters, digits, hyphens).
  The per-provider keys are `Enabled`, `DisplayName`, `Icon`, `Order`, `Authority`, `ClientId`,
  `ClientSecret`, `CallbackPath`, `Scopes`, `Prompt`, `RequireEmailVerified`, `AllowedTenantId` and
  `AllowedEmailDomains`. Move your settings under a provider id, and rename the secret's environment
  variable the same way (`Oidc__ClientSecret` becomes `Oidc__Providers__microsoft__ClientSecret`;
  the user-secrets or `appsettings.Development.json` key `Oidc:ClientSecret` becomes
  `Oidc:Providers:microsoft:ClientSecret`).
  The old flat keys directly under `Oidc` are ignored; a startup warning lists the ones found.
  Before:

  ```json
  "Oidc": {
    "Enabled": true,
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "ClientId": "<client-id>",
    "AllowedTenantId": "<tenant-id>"
  }
  ```

  After:

  ```json
  "Oidc": {
    "Providers": {
      "microsoft": {
        "Enabled": true,
        "DisplayName": "Microsoft 365",
        "Icon": "microsoft",
        "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "ClientId": "<client-id>",
        "CallbackPath": "/signin-oidc",
        "AllowedTenantId": "<tenant-id>"
      }
    }
  }
  ```
- Code-level renames for forks that compile against them: `appConfigStore.oidcEnabled` becomes
  `oidcProviders`; `AuthSchemes.Oidc` becomes `AuthSchemes.OidcFor(id)`, so scheme names are
  `oidc-{id}`; the flat properties of `OidcOptions` move to `OidcProviderOptions` under
  `OidcOptions.Providers`; `OidcWiring.ResolveLocalPrincipalAsync` takes a `providerId` argument.
- `GET /api/auth/login/oidc` is replaced by `GET /api/auth/login/oidc/{provider}`, which returns `404`
  for an unknown or disabled provider.
- `GET /api/config` no longer returns `oidcEnabled`; it returns `oidcProviders`, an array of
  `{ id, displayName, icon }` in button order.
- The default callback path is per provider: `/signin-oidc-{id}` instead of `/signin-oidc`. The shipped
  `microsoft` entry sets `CallbackPath` to `/signin-oidc`, so a registered Entra redirect URI keeps
  working, and the shipped `google` entry uses `/signin-oidc-google`. A provider entry without
  `CallbackPath` uses `/signin-oidc-{id}`; register that redirect URI at the identity provider, or set
  `CallbackPath` to the path already registered. Each provider needs its own path. Each provider also
  gets its own sign-out paths, `/signout-callback-oidc-{id}` and `/signout-oidc-{id}`; register the
  sign-out callback again if you use it. A reverse proxy in front of the API must forward these paths;
  the admin image's nginx and the Vite dev server already do.
- An enabled provider whose `Authority` is Google (`https://accounts.google.com`) must set a non-empty
  `Oidc:Providers:{id}:AllowedEmailDomains`, or startup fails with an error naming that key. Google has
  no tenant to pin, and without a domain any Google account could sign in and get a new account.
:::

### Added

- Several sign-in providers at once. The login page shows one button per enabled provider, ordered by
  `Order`, then id, and each button starts that provider's own sign-in.
- A Google preset: a disabled `google` entry ships in `appsettings.json`, with
  `Authority` `https://accounts.google.com` and `RequireEmailVerified` on.
- An SSO icon registry in `frontend/src/lib/ssoIcons.ts`. A provider's `Icon` token selects the mark
  (`microsoft` and `google` are built in, anything else shows a key icon), and a fork adds its own by
  editing that file.
- After signing in, the admin returns to the page that was requested. Opening a deep link while signed
  out, or having the session end mid-work, now lands on that page once you sign in again (password or an
  external provider); only a same-origin path is honoured, anything else falls back to the dashboard.

### Fixed

- Some admin UI text stayed in English when the interface language was Chinese. The Yes/No and
  item-count values in list columns, the related-items messages, the upload-failed message, the
  rich-text colour menu (button, swatches, custom colour and clear), the screen-reader "Close" label
  on dialogs and drawers, the mobile sidebar's screen-reader title and description, and the generic
  "Request failed" message shown when the server returns no readable error now follow the chosen
  language. The browser tab title reads "StruoCMS" before the site name loads.
- A failed logout request left the user stranded in the admin. Logout now always ends on the login
  page, clears the per-user data held in the browser, and the login page says so when the server
  could not confirm the sign-out; a `401` (no session left to end) counts as success.
- An expired session could trap an open edit form behind its unsaved-changes prompt. When the
  server rejects a request because the session ended, the admin now goes straight to the login page
  with a "session expired" notice, dropping unsaved edits instead of asking about them, and returns
  to the same page after sign-in. A wrong password or a signed-out visit behaves as before.
- The media details dialog could save one file's title, alt text and status onto another: switching
  files quickly let a slow earlier load overwrite the form, and Save then targeted the file the
  dialog pointed at. The form now clears while the next file loads, Save stays disabled until that
  file is loaded, and the update always targets the loaded file; a version-conflict refresh that
  finishes after a switch no longer touches the new file.
- The effective-permissions panel, the file picker, the multi-file field and the related-items list
  no longer show an earlier, slower response over a newer one, and a stale failure no longer clears
  the loading state or reports an error.
- The rich-text editor rewrote external images to a local path. Any `<img>` whose address merely
  contained `/files/…/content` was tagged as a managed file and saved as `/api/files/…/content`, which
  broke the image. Only this site's own file URL with a valid file id (optionally with a query string,
  which is kept) is now treated as a managed file; every other image is saved as typed, on load and on
  save.
- The admin image now serves every file outside `/assets/` with `Cache-Control: no-cache`, so
  `theme-init.js`, `favicon.svg` and similar files are revalidated after an upgrade instead of
  staying stale in the browser. The hashed files under `/assets/` stay cached immutably.
- Opening `/collections/user/new` or `/collections/file/new` directly now goes back to that
  collection's list. Users are created with the create-user dialog and files come from upload, so
  the generic new-item form never applied to them.

### Security

- `GET /api/auth/login/oidc/{provider}` no longer accepts a `returnUrl` containing control characters.
  Browsers strip tab, carriage return and line feed from a URL, so `/<tab>/evil.example` passed the
  in-site check and then redirected off-site; such a value now falls back to `Oidc:ReturnUrlDefault`.
  The admin's own `?redirect=` check refuses the same characters.
- The frontend test tooling is on `vitest` 4.1.12, which fixes GHSA-qxg6-89mg-rqv8 and
  GHSA-xp26-3454-mjgp in `@vitest/mocker`. In `docs/`, five more dev-server advisories in the
  `vite` 5 that `vitepress` 1.6.4 pins are accepted and listed in `docs/pnpm-workspace.yaml`: no
  `vite` 5 release fixes them, and the built site is unaffected.

## 0.12.0 — 2026-10-21

::: danger Breaking
- The `AdminUi` section of `appsettings.json` is removed, with `AdminUiOptions` and its startup
  validation, and `GET /api/config` no longer returns `uiLocales` or `uiDefaultLocale`. The admin UI
  offers every message catalog registered in `frontend/src/locales` and defaults to `en` instead of
  `zh-TW`. To keep Chinese as the default, set `VITE_DEFAULT_UI_LOCALE=zh-TW` in `frontend/.env` (dev
  server) or a committed `frontend/.env.production` (image build) and rebuild the SPA; a language a
  user picked in the switcher still wins. Remove `AdminUi__*` environment variables and `AdminUi` keys
  from your configuration; the host ignores them.
- `uiLocaleStore.configure(enabled, default)` is now `uiLocaleStore.init()`, and `appConfigStore` no
  longer has `uiLocales` or `uiDefaultLocale`. The i18n fallback locale is the build's default catalog.
- The admin image is built on `nginxinc/nginx-unprivileged`, runs nginx as a non-root user and listens
  on container port `8080` instead of `80`. Compose maps `${STRUO_ADMIN_PORT:-8080}:8080`, so the host
  port is unchanged; a fork's own manifest, `docker run --publish` command or reverse-proxy upstream
  must point at container port `8080`.
- `IFileStorage` has a new member, `IAsyncEnumerable<StoredBlob> ListAsync(CancellationToken)`; a
  fork's own storage implementation must add it to compile. It returns every stored blob as
  `StoredBlob(Key, LastModifiedUtc)`: the key as passed to `SaveAsync`, and the last-modified time in
  UTC, or `DateTime.MaxValue` when the backend cannot report one (an age-based sweep then keeps the
  blob). Stream it page by page instead of materialising it; `files:sweep` reads it once or twice per
  run.
- `Struo:Files:ImageTransform:MaxConcurrentTransforms` must be at least `2`; `1` stops startup with an
  error naming the key. Anonymous requests hold at most `MaxConcurrentTransforms - 1` slots, so an
  authenticated request (session cookie or bearer token) always has one left; an anonymous request
  past that cap waits `QueueTimeoutSeconds`, then gets `503` `IMAGE_TRANSFORM_BUSY`. With the default
  of `2`, anonymous cold transforms run one at a time; a fork that serves public images and wants more
  of them in parallel raises the setting.
- `POST /api/items/user` and the GraphQL `createUser` mutation return `400` `BAD_USER_INPUT`: creating
  a user needs the password hashing and password policy that only `POST /api/users` applies. Create
  users there, or as a super-admin from the "New" dialog on the admin UI's user list.
- `ISqlSugarClient` is a singleton, one `SqlSugarScope` for the whole process instead of one per
  request, so memory no longer grows with every request (about 150 bytes each, and about 2.5 KB per
  `/health/ready` call). Each async flow still gets its own connection and transaction, but database
  work must run in async methods (no DB access from a sync lambda in `Task.Run`, a timer callback or a
  sync job), and `CopyNew()` skips audit stamping and SQL Server cancellation translation. A fork that
  overrides the registration as scoped, or relies on the client being disposed at the end of a
  request, behaves differently. A fork's own `ICurrentUserAccessor` must not be scoped (startup fails
  otherwise) and must read the user on each call.
:::

### Added

- `files:sweep` removes orphaned blobs from the local or S3 store. Without `--apply` it is a dry run
  that lists them; it never touches a blob younger than `--min-age-hours` (default 24) and treats a
  blob referenced by a trashed `files` row as in use. It first prints its storage and database targets
  (never credentials), and `--apply` refuses with exit code `3` when more than 50% of the eligible
  blobs are orphans, unless `--force` is given. See
  [Removing orphaned blobs](20-deployment.md#removing-orphaned-blobs).
- [Chapter 25: After Forking](25-after-forking.md) lists what a fork changes first: the license
  line, `UserSecretsId`, the Sonar variables, the sample, the admin-UI language, the `__Host-` cookie
  prefix, the CSP and forwarded-header settings. A root `SECURITY.md` describes private vulnerability
  reporting for this template; a fork replaces it with its own.
- The `docker` CI job scans the API and admin images with Trivy and fails on any HIGH or CRITICAL
  vulnerability that has an upstream fix. Results appear in Code scanning on the public repository.

### Changed

- When a page expands exactly one parent through a one-to-many relation with a nested `limit`, the
  nested `sort`, `offset` and `limit` run in SQL with the nested `filter`, and `Query:MaxExpandRows`
  does not apply: a parent with more children than the cap returns just the requested window.
  Ordering there follows the database, as in a list query (its null placement and text collation;
  newest child first when there is no nested `sort`). A nested `sort` over several parents, or without
  a `limit`, still sorts in memory with nulls first, so one parent and several can order differently.
  Many-to-many relations are unchanged. The new `IItemRepository.QueryWhereInWindowAsync` returns
  `null` by default, which keeps an implementation without it on the capped in-memory path.
- The API image's `HEALTHCHECK` and the Compose `api` health check read the port from
  `ASPNETCORE_HTTP_PORTS` (8080 when unset; the first entry of a `;`-separated list), so the probe
  keeps working when a fork changes the listening port.

### Fixed

- A collection whose generated GraphQL names collide with another collection's or a built-in name
  (`article` with `articleList`, `category` with `categories`, a collection named `revision`) stops
  startup with a `MetadataException` in the cause chain naming the collection(s) and the clashing
  name(s), instead of a duplicate-type error from the GraphQL schema build.
- A JPEG's stored `width` and `height` follow its Exif orientation: orientations 5 to 8 (a 90 degree
  turn) swap them, so a portrait photo that a phone saved sideways reports the size it is displayed
  at. This applies to JPEG uploads from now on; existing rows keep their stored dimensions, and WebP
  and AVIF files still report their coded size.
- External (OIDC) sign-in works under `pnpm dev`. The Vite dev server forwards `/signin-oidc` and
  `/signout-callback-oidc` to the API and keeps the browser's `Host` on every proxied path, so the
  provider's `redirect_uri` is `http://localhost:5173/signin-oidc` instead of `:5221`. Register that
  URI (and the sign-out callback) with your provider for development.

### Security

- `Directory.Packages.props` no longer suppresses GHSA-2m69-gcr7-jv3q; `SQLitePCLRaw` is 2.1.12 and
  `dotnet list package --vulnerable` reports nothing.

## 0.11.0 — 2026-10-20

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
- The `FileService` constructor takes one `FileServiceCollaborators` (`Repository`, `Languages`,
  `CurrentUser`) in place of the three parameters after `storage`, followed by the optional `notifier`,
  `logger` and `references` (an `ItemPurgePipeline`). Dependency injection resolves it; a fork that
  constructs `FileService` by hand must pass `new FileServiceCollaborators(...)`.
- `SchemaTypeMapper.TagItemInputName` is a constant, not a method. Change `TagItemInputName()` to
  `TagItemInputName`.
- The template no longer ships a bootstrap admin password. `Auth:BootstrapAdmin:Password` is `""` in
  `appsettings.json` and `STRUO_ADMIN_PASSWORD` defaults to empty in `docker-compose.yml`. When the
  password is empty, the run that creates the admin generates a random password of at least 24
  characters and logs it once as a Warning. A configured password that fails `Auth:Password` or equals
  `admin` in any letter case is refused in
  every environment: `migrate`, `migrate:preview` and a `Database:MigrateOnStartup` start fail with an
  error naming `Auth__BootstrapAdmin__Password`, even on a database that is already seeded, until the
  value is fixed or removed. The Production-only default-password warning is gone. The Playwright suites
  read the password from `E2E_PASSWORD`, which is required. A fork that relied on
  `admin@admin.com` / `admin` must set a password or read the generated one from the `migrate` output.
- An enabled `Oidc` section whose `Authority` has a path segment of `common`, `organizations` or
  `consumers`, or is the consumers tenant GUID, is refused at startup unless `Oidc:AllowedTenantId` or
  `Oidc:AllowedEmailDomains` is set. The error names both keys. A fork with a multi-tenant authority and no pin must set one of them or
  switch to a single-tenant authority.
- OIDC sign-in now sends `prompt=select_account` by default, so an existing OIDC deployment shows the
  provider's account picker at every sign-in. Set `Oidc:Prompt` to an empty string to keep the previous
  behavior (see Added).
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
- Every login attempt counts against `RateLimiting:LoginAccount:PermitLimit`, not only failed ones (a
  successful sign-in still resets the count), and with `Redis:ConnectionString` set a login fails with a
  5xx error while Redis is unreachable.
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
  There is no cap on `offset`.
  `MaxFilterConditions` is now one budget shared by the root filter and every nested `deep` filter, where
  each nested filter used to count on its own. A request over any of these limits is refused with 400;
  a relation fetch over `MaxExpandRows` is refused, never truncated.
  `IItemRepository.QueryWhereInFilteredAsync` and `QueryEntityWhereInAsync` take an optional `int? maxRows` before the cancellation token, so a fork
  that implements the interface must add it, and a call that passed the token positionally must name it
  (`ct: ct`). `QueryValidator.Validate` takes an optional `FilterBudget`.
  `DeepExpansionCoordinator.ExpandAsync` is replaced by `Prepare`, which validates the `deep` tree and
  takes a `FilterBudget?`, and `ExpandPreparedAsync`, which expands the prepared tree; a caller that used
  `ExpandAsync` calls both. `ItemService` validates the `deep` tree before the search lookup or the root
  query, so an over-cap `deep` on a `GET` by id returns 400 even when the id does not exist (it returned
  404 before).
- The media library's trash and purge of a file (`DELETE /api/files/{id}`, with `?purge=true`) apply the
  relation rules every other collection's delete applies. A file still referenced through an
  `OnDelete.Restrict` relation returns `409 CONFLICT` on both, where it was trashed or purged. A purge sets
  `OnDelete.SetNull` references to null and removes many-to-many junction rows, where it left them
  pointing at a missing file. The generic items API and GraphQL delete of a file go through the same path
  (see Fixed).
- Image transform parameters are normalized, and the transform is bounded. A requested `width` or
  `height` snaps up to the next value of `Struo:Files:ImageTransform:SizeSteps` (default 64, 128, 256,
  320, 480, 640, 768, 1024, 1280, 1600, 1920, 2560, 3840, 4096), so `width=100` returns a 128-wide
  image. With `fit=cover` only the width snaps and the height follows the requested ratio, rounded to a
  multiple of 8 pixels. `fit` is `inside` or `cover`; any other value is `400`. A client-supplied
  `quality` rounds to a multiple of 10 within 10 to 100. Only sources in `TransformableContentTypes`
  (JPEG, PNG, WebP, AVIF, GIF) are transformed. An SVG with `?width=` returns the original bytes, as does
  a source over `MaxSourcePixels` (50 000 000) or whose header cannot be read. An AVIF grid or overlay
  image is never transformed. The size libvips reads is checked against the same limit before decoding.
  At most `MaxConcurrentTransforms` (2) transforms run at once, and a request
  that waits longer than `QueueTimeoutSeconds` (10) gets `503` `IMAGE_TRANSFORM_BUSY`. The variant cache
  is capped by `CacheMaxBytes` (2 GB) with least-recently-used eviction. A client that needs an exact
  pixel size must request a size on the list or configure `SizeSteps`, and one that passed another
  `fit` value must change it.
- `IImageTransformer.Transform` takes a `long maxSourcePixels` after the request and returns a nullable
  `ImageTransformResult?`, where `null` means the image the decoder would produce is over that limit. A
  fork that implements the interface must add the parameter and honour the limit, and a fork that calls
  it must pass the limit and handle `null` (serve the original bytes).
- The API host loads native libvips at startup (`NetVips.BlockUntrusted` is set during service
  registration), even with image transforms disabled. A host without a usable libvips fails to start.
- `Query:DefaultLimit` larger than `Query:MaxLimit` fails startup with an error naming both keys; the host
  used to start and serve default pages larger than `MaxLimit`.
- The admin image's Content-Security-Policy blocks images and API calls to any origin other than its own
  until `CSP_IMG_EXTRA` / `CSP_CONNECT_EXTRA` list them. A fork that serves files from a CDN or presigned
  bucket URLs (`Struo:Files:PresignedRedirect`), sets an absolute external `Branding:LogoUrl`, or points
  `VITE_API_BASE_URL` at another origin loses those images or requests on upgrade until it sets them.
:::

### Added

- `ForwardedHeaders:Enabled` (default `false`), `ForwardedHeaders:KnownProxies` and
  `ForwardedHeaders:KnownNetworks` make the API honour `X-Forwarded-For`, `X-Forwarded-Proto` and
  `X-Forwarded-Host` from the listed proxies, ahead of authentication, session rotation and rate
  limiting. A malformed address or CIDR range, or an enabled section with both lists empty, fails startup
  naming the key. A deployment behind a reverse proxy that wants the real client address (the per-IP login
  limiter) or an `https` OIDC redirect URI sets it; with it off, nothing changes. Chapters 4 and 20
  describe it.

- `[CmsCollection(Name = "...")]` pins a collection's name. The name is the key stored in permission
  grants, revisions and other rows, so a fork that sets it before it holds data can rename the C# class
  without orphaning them (a translation class's foreign key property must still be `{ClassName}Id`). It
  must match `^[a-z][A-Za-z0-9]*$` and be unique; a clash fails startup with a `MetadataException` naming
  both types. Without `Name` the collection keeps the camel-cased class name.

- Editors can switch a file between `published` and `draft` in the media library's file dialog, and the
  items API refuses a file `status` other than those two with `400`. `Struo:Files:DefaultStatus`
  (`published` or `draft`, default `published`) sets the status of a new upload; any other value fails
  startup. A published file stays readable by anyone who has its id. Chapter 15 describes the model.
  A file row whose stored `status` is neither value is refused with `400` on any update that sends
  `status`, which includes saving its title or alt text in the admin UI; send a valid `status`
  through `PUT /api/items/file/{id}` or correct the column directly to repair such a row.

- `Oidc:Prompt` (default `select_account`) sets the `prompt` parameter of the OIDC authorize request, so
  the provider always shows its account picker. The value is one or more of `select_account`, `login`,
  `consent` and `none`, separated by spaces, and an empty value sends no prompt (the previous silent
  sign-in). An invalid value fails startup naming `Oidc:Prompt`. Chapters 4 and 16 describe it.

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
  With `Redis:ConnectionString` set the count is a Redis atomic counter shared by every instance. A new
  `RateLimiting:LoginConcurrency` section (`Enabled` true, `PermitLimit` 4, `QueueLimit` 32) caps how
  many logins verify a password at once in a process; the rest get 429 with `Retry-After`. Anyone who knows an account's email can keep it
  locked out of password sign-in until the window ends; SSO sign-in is unaffected.

- The admin image sends a `Content-Security-Policy` (scripts from the image only, no framing, no plugins) and a
  `Permissions-Policy` that turns off camera, microphone, geolocation, payment and USB. Proxied `/api/*`
  responses, and the two OIDC callback paths, get `default-src 'none'; frame-ancestors 'none'; sandbox`.
  The `CSP_IMG_EXTRA` and `CSP_CONNECT_EXTRA` container variables (empty by default) add origins to
  `img-src` and `connect-src`; a fork whose images or API calls go to another origin must set them (see
  Breaking). The theme bootstrap is the file `theme-init.js`, not an inline script in `index.html`.

### Fixed

- OIDC sign-in works behind the shipped admin nginx. The image proxies `/signin-oidc` and
  `/signout-callback-oidc` to the API like `/api/`, and forwards the client's `Host` header with its port
  as `Host` and `X-Forwarded-Host` (it sent the host name without the port), so the redirect URI the API
  builds matches the one registered with the provider. A custom `Oidc:CallbackPath` outside `/api/` needs
  its own `location` block in `frontend/nginx/default.conf.template`.

- A failed or refused external sign-in (a provider error, a declined consent, a tenant, domain, email or
  inactive-account refusal) redirects the browser to `/login?error=<code>`, where the login page shows a
  localized message and then removes the parameter. The browser used to be left on a raw `401` JSON
  response. The codes are `external_login_failed`, `external_login_denied` and
  `external_login_<NoEmail|EmailNotVerified|DomainNotAllowed|TenantNotAllowed|Inactive>`; exception text
  never reaches the URL. Chapter 16 lists them.

- A `Struo:ContentAssemblies` entry that cannot be loaded still fails startup with a `MetadataException`,
  which now carries the original load failure as its `InnerException`.

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
  purge fail with a SQL syntax error, and a mixed-case custom column name did not match on PostgreSQL.

- Deleting a file through the generic items API (`DELETE /api/items/file/{id}`) or the GraphQL `deleteFile`
  mutation does what the media library does. Trashing clears the brand logo when it was that file; purging
  also deletes the translations, the logo reference and the stored blob. A purge no longer leaves the blob
  behind, and a storage failure while deleting the blob is logged as a warning with the storage key instead
  of being ignored.

- A JPEG that carries more than 64 KB of EXIF or ICC metadata ahead of its frame header (typical for phone
  photos) gets its width and height on upload, and the image-transform endpoint resizes it instead of serving
  the original. The reader follows the JPEG segment lengths up to the frame header; a truncated file, or one
  whose segment length runs past the end, still reports no dimensions.

- An upload whose database write fails (an outage, a cancelled request) removes the file content it had
  already stored and returns the original error. A failure while removing that content is logged as a
  warning with the storage key; it never replaces the upload's error.

### Upgrade notes

The Redis login throttle counts under the new `login-attempts:` key prefix, so each account's
login-attempt count starts from zero at upgrade; the old `login-throttle:` entries expire on their own.
During a rolling deploy, instances still on 0.10 and instances on 0.11 count under
different keys, so an account can receive up to twice its limit in attempts until every instance runs
0.11.

The language-code format rule checks every row of the language collection on each write to it,
including a delete. A malformed code that is already stored blocks every write to the collection
until it is corrected, and with two or more malformed rows the API can neither fix nor delete them.
Check the stored language rows for codes outside `[A-Za-z0-9_-]{1,35}` and correct or delete the
malformed rows directly in the database before upgrading.

Image variants already in the cache were keyed on parameters that were not normalized, and requests now
produce different keys, so those variants are never read again. They still count toward `CacheMaxBytes`,
and the first capped write may sweep them. Deleting the `CachePath` directory reclaims the space at once;
with `CacheMaxBytes` set to `0` (no cap) they stay until you delete them.

Install libvips on every host before upgrading, because the host now needs it to start (see Breaking).

If OIDC is enabled and `Oidc:Authority` has a path segment `common`, `organizations` or `consumers`
(or the consumers tenant GUID), set `Oidc:AllowedTenantId` or `Oidc:AllowedEmailDomains` before
upgrading, or the host refuses to start.

If your `.env` was copied from an earlier `.env.example`, it sets `STRUO_ADMIN_PASSWORD=admin`. Clear
it, or set a value that meets `Auth:Password`, before upgrading: the compose `api` service runs
`Database:MigrateOnStartup` and refuses to start with that value, even on a database that is already
seeded.

A fork that replaced `frontend/nginx/default.conf.template` must carry over the CSP and
`Permissions-Policy` headers, the `/signin-oidc` and `/signout-callback-oidc` locations, and `$http_host`
for `Host` and `X-Forwarded-Host`.

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
