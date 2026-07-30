# StruoCMS

A reusable, headless CMS **template**: fork it, define your own content collections, ship your own
product. It is not a finished CMS product and ships no business content models of its own.

[![CI](https://github.com/stu640978/struo-cms/actions/workflows/ci.yml/badge.svg)](https://github.com/stu640978/struo-cms/actions/workflows/ci.yml)

## What you get

- A metadata-driven collections engine — declare a plain C# entity with attributes, and StruoCMS
  derives its database table, REST endpoints, GraphQL schema, query-DSL surface and admin-UI form
  from that one declaration.
- Identity and role-based access control (RBAC): per-collection read/write/delete permissions,
  Argon2id password hashing, cookie- and bearer-token authentication, optional OpenID Connect SSO.
- Files and media, with a local-disk or S3-compatible storage backend and on-the-fly image
  transforms.
- Revisions and soft delete, both opt-in per collection.
- Internationalization (per-locale content), site settings/branding, a query DSL, a REST API, a
  GraphQL API, and the Vue 3 admin SPA that drives all of the above.

## What you do not get

No business content models. There is no "Article", "Product", or any other domain collection shipped
in the core — a default install has zero content collections. `samples/Struo.Sample.Blog` is an
optional, detachable demo (Article/Tag/Category) that shows *how* to define collections using the same
primitives your own fork would use; it is not referenced by the API host by default and is meant to be
deleted once you've used it to learn the patterns.

## Quick start

Prerequisites: .NET SDK 10.0.x, Node.js 24.x, pnpm 10.x, Docker (with Compose).

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

Open `http://localhost:5173` and log in with the seeded bootstrap admin: `admin@admin.com` / `admin`
(seeded only the first time the `users` table is created — not re-created or reset on later boots, even
against an emptied table; override it via `Auth:BootstrapAdmin:Email`/`Auth:BootstrapAdmin:Password`
before that first boot). A production start still using the default password logs a startup warning but
does not refuse to start — change it before going to production. Full detail, including MinIO's optional
`s3` Compose profile and port-override variables, is in
[chapter 2](docs/guide/en/02-getting-started.md).

## Architecture

Four backend projects in a strict, one-directional dependency chain, plus a separate frontend
workspace:

```
Struo.Domain  <──  Struo.Application  <──  Struo.Infrastructure  <──  Struo.Api
   (nothing)         (→ Domain)           (→ Application, Domain)   (→ Application, Infrastructure)

frontend/            Vue 3 admin SPA (separate pnpm workspace), talks to Struo.Api over REST/GraphQL
```

- `Struo.Domain` — domain types; no project or package references at all.
- `Struo.Application` — application-layer abstractions, options, query/security contracts.
- `Struo.Infrastructure` — SqlSugar wiring, identity, files, health checks, DI extensions.
- `Struo.Api` — the ASP.NET Core host: controllers, GraphQL, Scalar, Serilog, `Program.cs`.
- `schema/` — committed core-collection wire-shape snapshot (`core-collections.json`) the schema
  contract gate checks both stacks against — see `schema/README.md`.

Framework code never references `samples/*` — only `tests/Struo.Tests` does, which is what makes
`samples/Struo.Sample.Blog` truly optional and deletable.

## Documentation

The full manual lives under `docs/`, in English and 繁體中文 (zh-TW), chapter-for-chapter:

| # | Chapter |
|---|---|
| 1 | [Introduction & Architecture](docs/guide/en/01-introduction-and-architecture.md) |
| 2 | [Getting Started](docs/guide/en/02-getting-started.md) |
| 3 | [Configuration Reference](docs/guide/en/03-configuration-reference.md) |
| 4 | [Defining a Collection](docs/guide/en/04-defining-a-collection.md) |
| 5 | [Field Types & Interfaces](docs/guide/en/05-field-types.md) |
| 6 | [Internationalization](docs/guide/en/06-internationalization.md) |
| 7 | [Relations](docs/guide/en/07-relations.md) |
| 8 | [Query DSL](docs/guide/en/08-query-dsl.md) |
| 9 | [REST API](docs/guide/en/09-rest-api.md) |
| 10 | [GraphQL API](docs/guide/en/10-graphql-api.md) |
| 11 | [Files, Media & Image Transforms](docs/guide/en/11-files-and-media.md) |
| 12 | [Authentication, SSO & RBAC](docs/guide/en/12-auth-and-rbac.md) |
| 13 | [Revisions & Soft Delete](docs/guide/en/13-revisions-and-soft-delete.md) |
| 14 | [Admin SPA Customization](docs/guide/en/14-admin-spa-customization.md) |
| 15 | [Deployment, Operations & Testing](docs/guide/en/15-deployment-operations-testing.md) |
| 16 | [Sample Walkthrough](docs/guide/en/16-sample-walkthrough.md) |

zh-TW readers, start from [`docs/README.md`](docs/README.md) for the translated index. AI coding
agents working in this repository should read [`AGENTS.md`](AGENTS.md).

## License

StruoCMS is licensed under the [MIT License](LICENSE). Third-party components bundled with the
image-transform feature are listed with their own license terms in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

---

[繁體中文說明](README.zh-TW.md)
