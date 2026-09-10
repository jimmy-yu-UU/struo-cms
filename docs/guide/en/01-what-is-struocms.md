# 1. What StruoCMS Is

Every time you build a content management system from scratch, you redo the same infrastructure:
users and permissions, file storage, multilingual content, REST and GraphQL APIs. StruoCMS is a
headless CMS template you fork directly, built on .NET 10 and SqlSugar, with PostgreSQL as the
verified database. The admin UI is a Vue 3 single-page application.

After you fork it, you declare content collections in your own project. StruoCMS turns them into
database tables, APIs, and admin screens, so that infrastructure work is not yours to redo.

## What it includes

- **Collection engine**: one C# entity declaration drives the table schema, REST endpoints, GraphQL
  schema, query DSL, and the admin form screens. Change it in one place and all five interfaces
  update.
- **REST API**: every response uses the same envelope. Success is `{success, data, meta?}`; failure
  is `{success:false, error:{code, message, details?}}`. Callers don't have to guess the shape.
- **GraphQL API**: the schema is generated from the same collection metadata as REST, so both APIs
  expose the same data shape.
- **Query DSL**: every filter, sort, and relation path is validated against metadata before it
  becomes SQL. A misspelled field name is rejected before the query is assembled.
- **Authentication**: cookie and bearer token are both supported, and passwords are always hashed
  with Argon2id. OpenID Connect single sign-on is a core feature too, for connecting to an existing
  identity system, but it is off by default.
- **Role-based permissions**: read, write, and delete are granted per collection.
- **Files and media**: choose local disk or an S3-compatible storage backend. Images are converted
  on the fly as the file is downloaded, so you don't run a separate transcoding service.
- **Revisions and soft delete**: both are core features, and both are toggled per collection — the
  collections that need them turn them on; it is not forced on site-wide.
- **Multilingual content**: a field can hold a separate translation per language, so different
  language versions of the same record don't interfere with each other.
- **Site settings and branding**: one settings record covers the whole site. A super-admin edits it
  directly in the admin UI, with no config file changes or redeploy.
- **Admin SPA**: a Vue 3 single-page application that brings all of the above into one interface.

## What you build yourself

The content model starts with you. A freshly installed StruoCMS has zero collections — Article,
Tag, and Category are all things you declare yourself. After forking, put your collection classes
in the assembly named by `Struo:ContentAssemblies`. StruoCMS scans that assembly together with the
API host project's own assembly and turns your classes into tables, REST endpoints, and GraphQL
schema automatically.

Three things stay on you:

- Write the database migrations for your own collections.
- Put your business logic in your own services or collections.
- Deploy the whole project to your own environment.

The core handles the framework layer only.

`samples/Struo.Sample.Blog` is an optional demo project. It uses collections like Article and
Category to show how to define your own content model with the tools the core provides. The API
host never references it, and you can delete it once you've learned from it.

Deleting it is not just deleting the folder: it is also wired into the solution file and a test
project, so removing the folder alone breaks the solution-level build. The full removal steps are
covered in a later chapter dedicated to the sample project.

## What a fresh install looks like

A StruoCMS install with no collections added yet looks like this:

- The database has exactly eleven framework tables. If you set `Database:MigrationsPath`, startup
  creates a twelfth table, `schema_migrations`, to track which migrations have run.
- The admin sidebar has no content groups, only System. For a super-admin, System holds Language,
  Role, and User — nothing else, because the framework collections File, MediaFolder, Permission,
  and UserRole all declare `Hidden = true`. That is a display-layer flag, not a permission.
- Dashboard always shows. Media Library and Settings appear only when your permissions allow.

This is the correct state, not a broken install. The core deliberately holds no content of its own;
it waits for your collections to fill it in.

## Supported databases

`Database:DbType` accepts five values: PostgreSQL, MySql, SqlServer, Sqlite, Oracle.

| Database | Status |
|---|---|
| PostgreSQL | only verified runtime target |
| Sqlite | test suite only |
| MySql, SqlServer, Oracle | type mappings exist, unverified |

Some ordering and literal-conversion logic in queries handles PostgreSQL and SQLite behavior
specifically. Anything other than PostgreSQL is unsupported today — verify it yourself before you
rely on it in production.

## What's next

To see how the internals are split and where the core/sample boundary sits, read
[Chapter 2: Architecture](02-architecture.md). To skip the theory and get the API and admin SPA
running, read [Chapter 3: Getting Started](03-getting-started.md).
