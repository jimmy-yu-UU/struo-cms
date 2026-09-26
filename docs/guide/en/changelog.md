# Changelog

## 0.8.0

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
:::

### Added

- `Database:TablePrefix` configuration key (env `Database__TablePrefix`), validated at startup.
- `Localization` section: seed source for the `struo_languages` table when it is first created (`Languages`, `DefaultLanguage`), validated at startup.
- `AdminUi` section: admin-UI locales offered and the default (`Locales`, `DefaultLocale`), published by `GET /api/config` as `uiLocales` / `uiDefaultLocale`; the switcher is hidden with a single locale.
- Write rules on the `language` collection: `code` unique case-insensitively, at least one enabled row, exactly one enabled default; violations answer 400.

## 0.7.0 — 2026-09-23

The template as it stood when the project went open source. Entries start with this version.
