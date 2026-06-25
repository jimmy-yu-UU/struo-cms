# StruoCMS — Phase 0 (Foundation / 地基) Design

> Date: 2026-06-25
> Source of truth: the StruoCMS master spec (§0–§18). This document covers **Phase 0 only**.
> Each phase runs its own brainstorm → write-plan (TDD) → execute-plan → verify cycle (master spec §17.1).

## 1. Goal & scope

Stand up the Clean Architecture skeleton and the cross-cutting foundations every later phase depends on, with one minimal sample entity proving the pipeline end to end.

**In scope (master spec §18, Phase 0):**
- Clean Architecture four layers + `samples` + `tests`
- DI composition root
- SqlSugar with switchable `DbType` (default PostgreSQL; tests use SQLite in-memory)
- Serilog (local rolling-file sink, config-driven)
- Health checks: `/health/live` + `/health/ready`
- Dev-only `InitTables`
- `IAuditable` + audit-field auto-fill (SqlSugar AOP)
- One minimal sample entity
- Scalar API docs (Mars theme, axios client) — *added requirement*
- Developer usage documentation scaffold — *added requirement*

**Out of scope (deferred to later phases):** metadata attributes & scanner (Phase 1); generic CRUD & query DSL (Phase 2); relations (Phase 3); i18n (Phase 4); files (Phase 5); auth/session/SSO/RBAC/Redis (Phase 6); Vue SPA (Phase 7); GraphQL (Phase 8); soft delete/revisions/hooks (Phase 9).

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| API style | **Controllers** (project-wide, master spec §1 "choose one") |
| API docs/test UI | **Scalar** (`Scalar.AspNetCore`), **Mars** theme, **axios** default request client |
| Developer docs | Complete developer-facing usage docs are a deliverable; `docs/guide/` grows each phase |
| Postgres verification | User provides a connection string at verify time; never committed |
| Sample scope | **One minimal entity**; the full Blog domain grows phase-by-phase |
| InitTables gating | **`IsDevelopment()` only** — no appsettings switch; defensive guard throws if reached outside Development |

## 3. Repository layout

```
struo-cms/
├─ StruoCMS.sln
├─ CLAUDE.md                      # condensed §1 stack + §2 dependency rules + §17 execution rules
├─ Directory.Build.props          # net10.0, Nullable enable, LangVersion latest, warnings-as-errors
├─ Directory.Packages.props       # Central Package Management — all versions here, added via `dotnet add package` (latest)
├─ global.json                    # pin SDK 10.x
├─ .gitignore  .editorconfig
├─ docs/
│   ├─ superpowers/specs/2026-06-25-phase0-foundation-design.md
│   └─ guide/                     # developer usage docs (Phase 0: setup/run/config)
├─ src/
│   ├─ Struo.Domain/             # zero external deps
│   ├─ Struo.Application/        # → Domain only
│   ├─ Struo.Infrastructure/     # → Application + Domain (+ SqlSugarCore, Serilog)
│   └─ Struo.Api/                # → Application + Infrastructure (Controllers, Scalar, host)
├─ samples/
│   └─ Struo.Sample.Blog/        # → Domain + SqlSugarCore (the one minimal entity)
└─ tests/
    └─ Struo.Tests/              # xUnit + SQLite in-memory
```

**Dependency rule (master spec §2), enforced by project references:** Domain → (nothing); Application → Domain; Infrastructure → Application + Domain; Api → Application + Infrastructure. Framework projects never reference business sample projects.

## 4. Components

### 4.1 Struo.Domain (pure)
- **`IAuditable`** — pure interface: `DateTime CreatedAt`, `string? CreatedBy`, `DateTime UpdatedAt`, `string? UpdatedBy`. No SqlSugar attributes; SqlSugar maps these columns by naming convention.
- Domain exceptions namespace placeholder (grows later).
- `CmsEntityBase` is **deferred** — a base class carrying persistence attributes cannot live in pure Domain. `IAuditable` alone satisfies Phase 0.

### 4.2 Struo.Application (ports + options)
- **`ICurrentUserAccessor`** port — `string? GetCurrentUserId()`. Seam for Phase 6 auth.
- **`DatabaseOptions`** — `DbType` (enum), `ConnectionString`. Bound from config.
- `AddStruoApplication(...)` DI extension (minimal in Phase 0).

### 4.3 Struo.Infrastructure (implementations)
- **SqlSugar registration** — `ISqlSugarClient` scoped; maps `DatabaseOptions.DbType` → `SqlSugar.DbType` for PostgreSQL / MySQL / SqlServer / Sqlite / Oracle.
- **Audit AOP** — SqlSugar `DataExecuting` hook: on insert set `CreatedAt`/`UpdatedAt = now`, `CreatedBy`/`UpdatedBy = currentUser`; on update set `UpdatedAt`/`UpdatedBy`. Applies only to entities implementing `IAuditable`. User id from `ICurrentUserAccessor`.
- **`StubCurrentUserAccessor`** — returns `"system"`. Replaced in Phase 6.
- **Database initializer** — `InitTables` for an explicit entity-type list; runs only when `IsDevelopment()`; internal guard throws if invoked otherwise.
- **`DbReadinessCheck`** — `IHealthCheck` performing a lightweight SqlSugar ping.
- `AddStruoInfrastructure(IConfiguration)` DI extension.

### 4.4 Struo.Api (composition root)
- Controllers + camelCase JSON.
- .NET 10 built-in OpenAPI (`AddOpenApi`) + **Scalar** at `/scalar` (Mars theme, axios).
- Serilog host integration + `UseSerilogRequestLogging()`.
- Health endpoints `/health/live` (liveness) and `/health/ready` (readiness → `DbReadinessCheck`).
- Dev-only `InitTables` invocation during startup.
- Graceful shutdown; config via env/appsettings; no in-process state.
- One demo controller: `GET /api/ping` → version/status summary (so Scalar renders a real endpoint; generic CRUD is Phase 2).

### 4.5 samples/Struo.Sample.Blog
- One minimal entity (e.g. `Article`) with `[SugarColumn(IsPrimaryKey, IsIdentity)] long Id`, a couple of scalar fields, and `: IAuditable`. Carries SqlSugar persistence attributes (the deliberate §2 compromise). No `[Cms*]` attributes yet (Phase 1).

## 5. Configuration

`appsettings.json` (committed, placeholders):
- `Database:DbType` = `PostgreSQL`
- `Database:ConnectionString` = placeholder
- `Serilog` section (rolling file sink)

Real Postgres connection string → user-secrets / `appsettings.Development.json` (gitignored) / environment variables. **No `InitTables` flag** — gating is `IsDevelopment()`.

## 6. Data flow (insert path, illustrative)

1. Caller inserts an `IAuditable` entity via `ISqlSugarClient`.
2. SqlSugar `DataExecuting` AOP detects `IAuditable`, reads user id from `ICurrentUserAccessor`, stamps audit fields.
3. Row persisted; on update only `UpdatedAt`/`UpdatedBy` are restamped.

## 7. Error handling

- Startup fails fast on: missing/invalid `DbType`, `InitTables` reached outside Development (throws).
- Unreachable DB is reported via `/health/ready` (not a process crash).
- Serilog captures unhandled exceptions and request logs.
- Unified response/error envelope is Phase 9; Phase 0 uses framework defaults.

## 8. Testing (TDD — failing tests first; master spec §17.2)

xUnit + SQLite in-memory (kept-alive open connection so schema persists):
1. **Audit AOP** stamps `CreatedAt`/`CreatedBy` on insert and `UpdatedAt`/`UpdatedBy` on update.
2. **InitTables** creates the sample table on SQLite.
3. **DbType mapping** resolves each enum value to the correct `SqlSugar.DbType`.
4. **Readiness/liveness** integration test (`WebApplicationFactory`): `/health/ready` → 200 with reachable DB; `/health/live` → 200.
5. **Negative**: with `ASPNETCORE_ENVIRONMENT=Production`, the initializer does not create tables (guard holds).

## 9. Verification gate (evidence required)

- `dotnet build` clean (warnings-as-errors) and `dotnet test` green.
- App boots; `/health/live` & `/health/ready` → 200; `/scalar` renders with Mars theme and axios.
- **Connects to the user-provided Postgres**: readiness 200 against it, and dev `InitTables` creates the sample table there.
- Serilog log file written.
- Audit fields auto-filled (tests + one live insert).
- Production environment → `InitTables` does not run (negative test).
- `docs/guide/` contains Phase 0 setup/run/config documentation.

## 10. Package & environment rules (master spec §15)

- All NuGet packages added via `dotnet add package` at latest stable; versions centralized in `Directory.Packages.props`. No versions written from memory.
- All DB access through SqlSugar ORM; zero vendor-specific SQL.
- `InitTables` only in Development.
