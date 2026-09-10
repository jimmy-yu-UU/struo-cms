# StruoCMS

StruoCMS is a headless CMS template you fork directly, built on .NET 10 and SqlSugar, with
PostgreSQL as the verified database and a Vue 3 single-page application as the admin UI. After you
fork it, you declare content collections in your own project, and StruoCMS turns them into database
tables, APIs, and admin screens. The core ships no business content models — a default install has
zero content collections. `samples/Struo.Sample.Blog` is an optional, detachable demo that shows how
to define collections with the same primitives, and you delete it once you've learned from it.

[![CI](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml/badge.svg)](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml)

## What it includes

- **Collection engine**: declare a plain C# entity with attributes, and StruoCMS derives the
  database table schema, REST endpoints, GraphQL schema, query DSL, and the admin form screens from
  that one declaration.
- **REST API and GraphQL API**: both generated from the same collection metadata; every REST
  response uses the same envelope.
- **Identity and role-based access control (RBAC)**: cookie- and bearer-token authentication,
  Argon2id password hashing, optional OpenID Connect SSO; read, write, and delete permissions are
  granted per collection.
- **Files and media**: a local-disk or S3-compatible storage backend, with on-the-fly image
  transforms as files are downloaded.
- **Revisions and soft delete**: both core features, both toggled per collection.
- **Multilingual content, site settings and branding**, and the Vue 3 admin SPA that brings all of
  the above into one interface.

## Quick start

Prerequisites: .NET SDK 10.0.x, Node.js 24.x, pnpm 10.x, Docker (with Compose). To run the API and
admin SPA as container images instead, both Dockerfiles are provided; the full steps are covered in
a later chapter dedicated to deployment.

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

Open `http://localhost:5173` and log in with the seeded bootstrap admin: `admin@admin.com` /
`admin` (seeded only the first time the `users` table is created; override it via
`Auth:BootstrapAdmin:Email`/`Auth:BootstrapAdmin:Password` before that first boot). A production
start still using the default password logs a startup warning but does not refuse to start — change
it before going to production. Full detail, including PostgreSQL/Redis port overrides and the health
check, is in [Chapter 3: Getting Started](docs/guide/en/03-getting-started.md).

## Architecture

Four backend projects (`Struo.Domain`, `Struo.Application`, `Struo.Infrastructure`, `Struo.Api`) sit
in a strict, one-directional dependency chain, plus a separate `frontend/` workspace and the
`schema/` contract snapshots; the full picture is in
[Chapter 2: Architecture](docs/guide/en/02-architecture.md).

## Documentation

The full manual lives under `docs/`, in English and 繁體中文, chapter-for-chapter; start at
[Chapter 1: What StruoCMS Is](docs/guide/en/01-what-is-struocms.md). AI coding agents working in
this repository should read [`AGENTS.md`](AGENTS.md). The documentation site is its own project;
install and run it with:

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
