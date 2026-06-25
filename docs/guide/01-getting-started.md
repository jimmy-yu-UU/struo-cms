# Getting Started with StruoCMS

Phase 0 foundation — local development setup and verification.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0 or later | `dotnet --version` to confirm |
| PostgreSQL | 14+ | Required at runtime; not needed for running tests |

Tests use an in-memory SQLite temp file and require no external services.

---

## Solution Layout

```
struo-cms/
├── src/
│   ├── Struo.Domain/           # Core domain types, interfaces (IAuditable, ports)
│   ├── Struo.Application/      # Application-layer abstractions (ICurrentUserAccessor, etc.)
│   ├── Struo.Infrastructure/   # SqlSugar wiring, health checks, DatabaseInitializer, DI extension
│   └── Struo.Api/              # ASP.NET Core host — controllers, Scalar, Serilog, Program.cs
├── samples/
│   └── Struo.Sample.Blog/      # Minimal Blog sample (Article entity, ICurrentUserAccessor stub)
└── tests/
    └── Struo.Tests/            # xUnit unit + integration tests (SQLite, no external services)
```

Dependency direction: `Api` → `Infrastructure` → `Application` → `Domain`.
`samples/Struo.Sample.Blog` is referenced by `Struo.Api` for the Phase 0 sample entity only.

---

## Configuration

Two keys are required at runtime:

| Key | Description | Default (appsettings.json) |
|---|---|---|
| `Database:DbType` | One of `PostgreSQL`, `MySql`, `SqlServer`, `Sqlite`, `Oracle` | `PostgreSQL` |
| `Database:ConnectionString` | ADO.NET connection string for the chosen engine | placeholder — must be replaced |

**Never commit real connection strings.** Use .NET user-secrets for local development:

```bash
cd src/Struo.Api
dotnet user-secrets init        # only needed once per checkout
dotnet user-secrets set "Database:DbType" "PostgreSQL"
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=struo;Username=YOUR_USER;Password=YOUR_PASSWORD"
```

User-secrets are stored outside the repo at `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` (Windows) or `~/.microsoft/usersecrets/` (Linux/macOS) and are never tracked by git.

For CI or container environments, supply the keys as environment variables (double-underscore path separator):

```bash
Database__DbType=PostgreSQL
Database__ConnectionString="Host=db;Port=5432;Database=struo;Username=YOUR_USER;Password=YOUR_PASSWORD"
```

---

## Running the API

```bash
dotnet run --project src/Struo.Api
```

The host binds to the URLs printed at startup (typically `http://localhost:5000` / `https://localhost:5001`). Check the console for the actual ports.

### Endpoints

| URL | Method | Notes |
|---|---|---|
| `/api/ping` | GET | Returns `{ "message": "pong" }` — always available |
| `/health/live` | GET | Liveness — 200 means the process is up |
| `/health/ready` | GET | Readiness — 200 means the database is reachable |
| `/scalar` | GET | Interactive API explorer — **Development only** (see below) |
| `/openapi/v1.json` | GET | OpenAPI spec — **Development only** (see below) |

### Scalar / OpenAPI (non-production only)

Scalar (Mars theme, axios HTTP client) and the OpenAPI spec are mapped only when
`ASPNETCORE_ENVIRONMENT` is **not** `Production`. In Production both routes return 404.
This is intentional — Phase 6 will revisit once authentication/authorization lands.

---

## Development-only: InitTables

On startup, when `ASPNETCORE_ENVIRONMENT=Development`, the API calls
`DatabaseInitializer.InitializeDevelopmentSchema(...)` which issues SqlSugar's
`CodeFirst.InitTables` for the registered entity types (currently `Article`).
This creates or updates table schemas to match the current entity definitions.

**Outside Development this call is refused at the code level** — `DatabaseInitializer`
throws `InvalidOperationException` if `IHostEnvironment.IsDevelopment()` is false.
`Program.cs` wraps the call in `if (app.Environment.IsDevelopment())` so the guard
is never even reached in other environments.

Production schema changes must go through reviewed migration scripts.

---

## Log Files

Serilog writes structured logs to rolling daily files:

```
src/Struo.Api/logs/struo-<date>.log
```

The sink is configured via `appsettings.json` under the `Serilog` section.
The console also receives request logs via `UseSerilogRequestLogging()`.
Log level defaults: `Information` globally, `Warning` for `Microsoft.AspNetCore`.

---

## Running Tests

```bash
dotnet test
```

The test suite (`tests/Struo.Tests`) uses:

- **xUnit** as the test runner
- **FluentAssertions** for assertions
- **SQLite temp file** as the database — a unique temporary file path is generated per test run via `Path.GetTempFileName()`, so tests are fully isolated and require no external services or Postgres instance

All tests should pass with 0 failures.

To run with verbose output:

```bash
dotnet test --logger "console;verbosity=detailed"
```

To run a specific test class:

```bash
dotnet test --filter "FullyQualifiedName~DatabaseInitializerTests"
```

---

## Security Notes

- **Never** commit `appsettings.Development.json` with real credentials (the file contains only log-level overrides).
- The `Database:ConnectionString` in `appsettings.json` contains `REPLACE_ME` placeholders — this is intentional and safe to commit.
- All user input entering the system goes through SqlSugar's ORM layer; there is no raw SQL path.
