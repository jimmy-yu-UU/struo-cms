# StruoCMS

StruoCMS is a headless CMS template you fork directly, built on .NET 10 and SqlSugar, with
PostgreSQL as the verified database and a Vue 3 single-page application as the admin UI. After you
fork it, you declare content collections in your own project, and StruoCMS turns them into database
tables, APIs, and admin screens.

The content model starts with you: a fresh install has zero content collections, and Article, Tag,
and Category are all yours to declare. `samples/Struo.Sample.Blog` is an optional demo project that
shows how to define your own collections with the tools the core provides; you can delete it once
you've learned from it — the removal steps are in the sample chapter.

[![CI](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml/badge.svg)](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml)

## What it includes

- **Collection engine**: declare a C# entity with a few attributes, and StruoCMS derives the
  database table schema, REST endpoints, GraphQL schema, query DSL, and the admin form screens from
  that one declaration.
- **REST API and GraphQL API**: both generated from the same collection metadata; every REST
  response uses the same envelope.
- **Authentication and role-based permissions**: cookie- and bearer-token authentication, Argon2id
  password hashing, and OpenID Connect single sign-on, off by default; read, write, and delete are
  granted per collection.
- **Files and media**: a local-disk or S3-compatible storage backend, with on-the-fly image
  transforms as files are downloaded.
- **Revisions and soft delete**: both core features, both toggled per collection.
- **Multilingual content, site settings, and branding**: a field can hold a separate translation per
  language; one settings record covers the whole site, edited directly in the admin UI by a
  super-admin.
- **Admin SPA**: a Vue 3 single-page application that brings all of the above into one interface.

## Quick start

Prerequisites: .NET SDK 10.0.x, Node.js 24.x, pnpm 10.x, Docker (with Compose).

The API and the admin SPA can also run as containers — the repo has a Dockerfile for each; the
full steps are in the deployment chapter.

```bash
# 1. Start PostgreSQL and Redis
docker compose up -d

# 2. Configure the API (gitignored local settings file)
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json

# 3. Run the API
dotnet run --project src/Struo.Api
# listens on http://localhost:5221

# 4. In a second terminal, run the admin SPA
cd frontend
pnpm install
pnpm dev
# listens on http://localhost:5173, proxies /api to :5221
```

Open `http://localhost:5173` and log in with the default account: `admin@admin.com` / `admin`. The
account is seeded only when the `users` table is first created; to use a different one, set
`Auth__BootstrapAdmin__Email` / `Auth__BootstrapAdmin__Password` before the first start.

Starting in Production while the password is still the default logs a warning naming
`Auth__BootstrapAdmin__Password` instead of blocking startup — change it before you go live. The
full steps, including the PostgreSQL/Redis port overrides and health checks, are in
[Chapter 3: Getting Started](docs/guide/en/03-getting-started.md).

## Architecture

The backend has four projects (`Struo.Domain`, `Struo.Application`, `Struo.Infrastructure`,
`Struo.Api`) with dependencies running in one direction only; alongside them sit the standalone
`frontend/` workspace and the `schema/` contract snapshots.
[Chapter 2: Architecture](docs/guide/en/02-architecture.md) has the full picture.

## Documentation

The full manual lives under `docs/`, in English and 繁體中文, chapter-for-chapter; start at
[Chapter 1: What StruoCMS Is](docs/guide/en/01-what-is-struocms.md). If you develop this project
with an AI coding agent, read [`AGENTS.md`](AGENTS.md) first. The documentation site is its own
project; install and run it with:

```bash
pnpm -C docs install
pnpm -C docs dev
```

## License

StruoCMS is licensed under the [MIT License](LICENSE). Third-party components bundled with the
image-transform feature are listed with their own license terms in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

---

[繁體中文說明](README.zh-TW.md)
