# ORM Seeder Coupled to Table Creation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make initial-data seeding an all-environment ORM concern that fires a seeder only when its target table is created during this startup; pre-existing tables are trusted and never re-seeded.

**Architecture:** A new `DataSeeder` orchestrator snapshots table names before schema creation, then after schema creation runs each existing seeder (`LanguageSeeder`/`AdminUserSeeder`/`RbacSeeder`) only when its trigger table was created this run (`present now && absent before`). `Program.cs` removes the dev-only gate and calls `DataSeeder` in all environments. A committed default bootstrap admin (`admin@admin.com`/`admin`) guarantees a login; a Production startup still on the default password logs a non-blocking WARNING.

**Tech Stack:** .NET 10 / C#, SqlSugarCore (`DbMaintenance.GetTableInfoList`), xUnit + AwesomeAssertions, SQLite (tests) / PostgreSQL (runtime).

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL in application code (spec §4; CLAUDE.md §17.4).
- Core lives only in `src/Struo.*`; never reference `samples/*` (CLAUDE.md §2).
- Table-name matching uses the actual lower-cased SqlSugar table names: `languages`, `users`, `roles`, `permissions`, `user_roles` (verified from `[SugarTable(...)]`).
- Seeder trigger = **table creation**, never row-emptiness (spec §2). Pre-existing tables (even if manually emptied) are skipped.
- Seeding runs in **all environments** (remove the `if (IsDevelopment())` gate).
- Default bootstrap admin verbatim: Email `admin@admin.com`, Password `admin`. Overridable via env `Auth__BootstrapAdmin__Email` / `Auth__BootstrapAdmin__Password`.
- Do **not** modify `db/migrations/*.sql`, `appsettings.Development.json`, or introduce CodeFirst in production (spec §4 Non-Goals).

## File Structure

- **Create:** `src/Struo.Infrastructure/Persistence/DataSeeder.cs` — table-name snapshot helper + orchestrator with the creation gate + prod default-password warning.
- **Create:** `tests/Struo.Tests/Persistence/DataSeederTests.cs` — gate behavior, idempotency, prod-seeds, warning.
- **Modify:** `src/Struo.Api/Program.cs` — snapshot before schema; replace dev-only inline seeders with all-env `DataSeeder.SeedAsync`; tighten InitTables comment.
- **Modify:** `src/Struo.Api/appsettings.json` — default `BootstrapAdmin` = `admin@admin.com` / `admin` + doc comment.
- **Modify (comment only):** `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs` — InitTables capability wording.

---

### Task 1: `DataSeeder` orchestrator with table-creation gate

**Files:**
- Create: `src/Struo.Infrastructure/Persistence/DataSeeder.cs`
- Test: `tests/Struo.Tests/Persistence/DataSeederTests.cs`

**Interfaces:**
- Consumes (existing signatures): `LanguageSeeder.SeedAsync(ISqlSugarClient)`, `AdminUserSeeder.SeedAsync(ISqlSugarClient, IPasswordHasher, string?, string?)`, `RbacSeeder.SeedAsync(ISqlSugarClient, string?, IEnumerable<string>, CancellationToken)`.
- Produces:
  - `DataSeeder.GetTableNames(ISqlSugarClient db) : ISet<string>` — lower-cased ordinal set of current table names.
  - `DataSeeder.SeedAsync(ISqlSugarClient db, ISet<string> existingBefore, IPasswordHasher hasher, string? bootstrapAdminEmail, string? bootstrapAdminPassword, IReadOnlyList<string> publicReadCollections, bool isProduction, ILogger logger, CancellationToken ct = default) : Task`
  - Constants: `DataSeeder.LanguagesTable = "languages"`, `UsersTable = "users"`, `RolesTable = "roles"`, `DefaultAdminPassword = "admin"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Persistence/DataSeederTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class DataSeederTests
{
    private static ISqlSugarClient NewDb(SqliteTestDatabase db)
    {
        var c = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = db.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (column.IsPrimarykey || column.IsIgnore) return;
                    if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
                        column.IsNullable = true;
                }
            }
        });
        c.CodeFirst.InitTables(typeof(Language), typeof(User), typeof(Role), typeof(Permission), typeof(UserRole));
        return c;
    }

    private static readonly IPasswordHasher Hasher = new Argon2idPasswordHasher();
    private static readonly string[] NoCollections = [];

    [Fact]
    public async Task Fires_seeder_when_trigger_table_created_this_run()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        // Tables exist now; existingBefore is empty => every trigger table was "just created".
        var existingBefore = new HashSet<string>(StringComparer.Ordinal);

        await DataSeeder.SeedAsync(db, existingBefore, Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);

        (await db.Queryable<Language>().CountAsync()).Should().Be(2);
        (await db.Queryable<User>().CountAsync()).Should().Be(1);
        (await db.Queryable<Role>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Skips_seeder_when_trigger_table_preexisted_even_if_empty()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        // Trigger tables were present BEFORE this run => skip, despite being empty.
        var existingBefore = DataSeeder.GetTableNames(db);

        await DataSeeder.SeedAsync(db, existingBefore, Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);

        (await db.Queryable<Language>().CountAsync()).Should().Be(0);
        (await db.Queryable<User>().CountAsync()).Should().Be(0);
        (await db.Queryable<Role>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Second_run_seeds_nothing_no_duplicates()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);

        await DataSeeder.SeedAsync(db, new HashSet<string>(StringComparer.Ordinal), Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);
        // Second boot: tables now pre-exist.
        await DataSeeder.SeedAsync(db, DataSeeder.GetTableNames(db), Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);

        (await db.Queryable<Language>().CountAsync()).Should().Be(2);
        (await db.Queryable<User>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Seeds_in_production_environment()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);

        await DataSeeder.SeedAsync(db, new HashSet<string>(StringComparer.Ordinal), Hasher,
            "admin@admin.com", "s3cret-not-default", NoCollections, isProduction: true, NullLogger.Instance);

        (await db.Queryable<User>().CountAsync()).Should().Be(1); // not gated out by environment
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DataSeederTests"`
Expected: FAIL — `DataSeeder` does not exist (compile error).

- [ ] **Step 3: Write the minimal implementation**

Create `src/Struo.Infrastructure/Persistence/DataSeeder.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Unified initial-data seeding for every environment. A seeder fires ONLY when its target table was
/// created during this startup (present now, absent from <c>existingBefore</c>). Pre-existing tables —
/// even if manually emptied — are trusted and never re-seeded, preserving production data.
/// </summary>
public static class DataSeeder
{
    internal const string LanguagesTable = "languages";
    internal const string UsersTable = "users";
    internal const string RolesTable = "roles";
    internal const string DefaultAdminPassword = "admin";

    /// <summary>Lower-cased ordinal set of current DB table names. Reused for the before-snapshot.</summary>
    public static ISet<string> GetTableNames(ISqlSugarClient db) =>
        db.DbMaintenance.GetTableInfoList(false)
            .Select(t => t.Name.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    public static async Task SeedAsync(
        ISqlSugarClient db,
        ISet<string> existingBefore,
        IPasswordHasher hasher,
        string? bootstrapAdminEmail,
        string? bootstrapAdminPassword,
        IReadOnlyList<string> publicReadCollections,
        bool isProduction,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existingAfter = GetTableNames(db);
        bool JustCreated(string table) =>
            existingAfter.Contains(table) && !existingBefore.Contains(table);

        if (JustCreated(LanguagesTable))
            await LanguageSeeder.SeedAsync(db);
        else
            logger.LogInformation("DataSeeder: skip LanguageSeeder — '{Table}' pre-existed.", LanguagesTable);

        if (JustCreated(UsersTable))
            await AdminUserSeeder.SeedAsync(db, hasher, bootstrapAdminEmail, bootstrapAdminPassword);
        else
            logger.LogInformation("DataSeeder: skip AdminUserSeeder — '{Table}' pre-existed.", UsersTable);

        if (JustCreated(RolesTable))
            await RbacSeeder.SeedAsync(db, bootstrapAdminEmail, publicReadCollections, ct);
        else
            logger.LogInformation("DataSeeder: skip RbacSeeder — '{Table}' pre-existed.", RolesTable);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DataSeederTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Persistence/DataSeeder.cs tests/Struo.Tests/Persistence/DataSeederTests.cs
git commit -m "feat(seed): DataSeeder orchestrator gated on table creation"
```

---

### Task 2: Production default-password warning

**Files:**
- Modify: `src/Struo.Infrastructure/Persistence/DataSeeder.cs`
- Test: `tests/Struo.Tests/Persistence/DataSeederTests.cs`

**Interfaces:**
- Consumes: `DataSeeder.DefaultAdminPassword` (Task 1).
- Produces: `DataSeeder.WarnIfDefaultAdminPasswordInProduction(bool isProduction, string? password, ILogger logger) : void`, called at the end of `SeedAsync`.

- [ ] **Step 1: Write the failing tests**

First add a capturing logger helper. Create `tests/Struo.Tests/Support/ListLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Struo.Tests.Support;

/// <summary>Minimal ILogger capturing (level, message) pairs for assertions.</summary>
public sealed class ListLogger : ILogger
{
    public readonly List<(LogLevel Level, string Message)> Entries = [];
    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
```

Append to `DataSeederTests.cs`:

```csharp
    [Fact]
    public void Warns_when_production_uses_default_password()
    {
        var logger = new ListLogger();
        DataSeeder.WarnIfDefaultAdminPasswordInProduction(isProduction: true, "admin", logger);
        logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public void No_warning_when_production_password_is_non_default()
    {
        var logger = new ListLogger();
        DataSeeder.WarnIfDefaultAdminPasswordInProduction(isProduction: true, "s3cret-not-default", logger);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void No_warning_outside_production_even_with_default_password()
    {
        var logger = new ListLogger();
        DataSeeder.WarnIfDefaultAdminPasswordInProduction(isProduction: false, "admin", logger);
        logger.Entries.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DataSeederTests"`
Expected: FAIL — `WarnIfDefaultAdminPasswordInProduction` not defined (compile error).

- [ ] **Step 3: Write the minimal implementation**

In `src/Struo.Infrastructure/Persistence/DataSeeder.cs`, add the method and call it at the end of `SeedAsync` (immediately after the `RbacSeeder` block, before the method closes):

```csharp
        WarnIfDefaultAdminPasswordInProduction(isProduction, bootstrapAdminPassword, logger);
    }

    internal static void WarnIfDefaultAdminPasswordInProduction(
        bool isProduction, string? password, ILogger logger)
    {
        if (isProduction && string.Equals(password, DefaultAdminPassword, StringComparison.Ordinal))
            logger.LogWarning(
                "Bootstrap admin is using the default password '{Default}'. Change it immediately via Auth__BootstrapAdmin__Password.",
                DefaultAdminPassword);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DataSeederTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Persistence/DataSeeder.cs tests/Struo.Tests/Persistence/DataSeederTests.cs tests/Struo.Tests/Support/ListLogger.cs
git commit -m "feat(seed): warn on default admin password in production"
```

---

### Task 3: Wire into startup + default admin config

**Files:**
- Modify: `src/Struo.Api/Program.cs:166-213`
- Modify: `src/Struo.Api/appsettings.json:44-48`
- Modify: `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs` (comment) and `src/Struo.Api/Program.cs` InitTables comment

**Interfaces:**
- Consumes: `DataSeeder.GetTableNames`, `DataSeeder.SeedAsync` (Tasks 1–2); `IPasswordHasher` (`Struo.Application.Security`).

- [ ] **Step 1: Set the default bootstrap admin in `appsettings.json`**

Replace the `Auth` block (`src/Struo.Api/appsettings.json` lines 44-49) with:

```json
  "Auth": {
    "// BootstrapAdmin": "Seeded ONLY when the users table is first created. Ships a default admin/admin so a fresh install always has a login. OVERRIDE in production via env Auth__BootstrapAdmin__Email / Auth__BootstrapAdmin__Password (or appsettings.Production.json). A production start still on the default password logs a WARNING.",
    "BootstrapAdmin": {
      "Email": "admin@admin.com",
      "Password": "admin"
    }
  },
```

Do **not** touch `appsettings.Development.json`.

- [ ] **Step 2: Rewire the startup scope in `Program.cs`**

In `src/Struo.Api/Program.cs`, the startup scope currently spans lines ~166-213. Replace its body so that (a) a table snapshot is taken before schema creation and (b) the dev-only seeding block is replaced by one all-environment `DataSeeder.SeedAsync` call. The final block reads:

```csharp
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        // Snapshot existing tables BEFORE any schema step, so seeders can fire only for tables
        // created during THIS startup (table-creation is the sole seeding trigger).
        var existingBefore = DataSeeder.GetTableNames(db);

        // Development-only: SqlSugar CodeFirst creates missing tables and additively adds missing
        // columns to existing tables. It performs no destructive schema changes and never runs in
        // production (structural changes go through reviewed migration scripts).
        if (app.Environment.IsDevelopment())
        {
            var entityTypes = scope.ServiceProvider
                .GetRequiredService<Struo.Application.Metadata.IEntityTypeCollector>()
                .CollectForInitTables();
            DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment, entityTypes.ToArray());
        }

        // Reviewed *.sql schema migrations. Config-driven (Database:MigrationsPath), all environments,
        // hard no-op on non-PostgreSQL. Runs AFTER InitTables and BEFORE seeding.
        var migrationsPath =
            builder.Configuration.GetSection(Struo.Application.Configuration.DatabaseOptions.SectionName)["MigrationsPath"];
        if (!string.IsNullOrWhiteSpace(migrationsPath))
        {
            var migrationLogger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>().CreateLogger("Struo.MigrationRunner");
            await MigrationRunner.ApplyAsync(db, migrationsPath, migrationLogger);
        }

        // Dev fail-fast (DB-5): assert correctness-critical constraints exist after schema creation.
        if (app.Environment.IsDevelopment())
        {
            await SchemaGuard.AssertCriticalConstraintsAsync(db, default);
        }

        // Unified initial-data seeding — ALL environments. Each seeder fires only when its trigger
        // table was created this run (see DataSeeder); pre-existing tables are left untouched.
        var seedLogger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>().CreateLogger("Struo.DataSeeder");
        var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
        await DataSeeder.SeedAsync(
            db,
            existingBefore,
            hasher,
            builder.Configuration["Auth:BootstrapAdmin:Email"],
            builder.Configuration["Auth:BootstrapAdmin:Password"],
            builder.Configuration.GetSection("Rbac:PublicReadCollections").Get<string[]>() ?? [],
            app.Environment.IsProduction(),
            seedLogger);
    }
```

Note: `DataSeeder` is in namespace `Struo.Infrastructure.Persistence`, already imported at the top of `Program.cs` (`using Struo.Infrastructure.Persistence;`).

- [ ] **Step 3: Tighten the `DatabaseInitializer` comment (no behavior change)**

In `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs`, update the XML/inline comment describing `InitTables` to state it "creates missing tables and additively adds missing columns; it performs no destructive schema changes," matching the corrected `Program.cs` comment. Adjust wording only — do not change any code.

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet build && dotnet test tests/Struo.Tests/Struo.Tests.csproj`
Expected: PASS. The `ApiFactory`-based tests boot the host (Development) through the new seeding path; confirm no regressions in seeding-adjacent suites (`AdminUserSeederTests`, `SchemaGuardTests`, `ConfigEndpointTests`, `LoginFlowTests`).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Program.cs src/Struo.Api/appsettings.json src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs
git commit -m "feat(seed): run seeders in all environments; default bootstrap admin"
```

---

### Task 4: Live PostgreSQL verification (Production-like)

**Files:** none (manual verification gate; produces evidence, no code).

**Interfaces:** Consumes the wired startup from Task 3.

- [ ] **Step 1: Prepare a disposable fresh PostgreSQL database**

Create an empty database (name it disposably, e.g. `struo_seed_verify`). Configure a Production-like run: set `Database:ConnectionString` to it, `Database:MigrationsPath` to the repo's `db/migrations`, and `ASPNETCORE_ENVIRONMENT=Production`. Provide `Auth__BootstrapAdmin__Email` / `Auth__BootstrapAdmin__Password` via env only if overriding the default.

- [ ] **Step 2: First boot — fresh DB**

Start the API. Expected in logs: `MigrationRunner` applies `001-core-baseline.sql` (+`002`,`003`); `DataSeeder` fires all three seeders (no "pre-existed" skips). If using the default password, a WARNING about the default admin password appears.
Verify rows:

```sql
SELECT count(*) FROM languages;    -- expect 2 (en, zh-TW)
SELECT count(*) FROM roles;        -- expect 2 (admin, public)
SELECT count(*) FROM users;        -- expect 1 (admin@admin.com)
SELECT count(*) FROM user_roles;   -- expect 1 (admin bound to admin role)
```

Then confirm login works with `admin@admin.com` / `admin` (or the overridden credentials).

- [ ] **Step 3: Second boot — populated DB**

Restart the API against the same (now-populated) database. Expected: `DataSeeder` logs three "pre-existed" skips; the counts above are unchanged (no duplicates); startup succeeds. If Production + default password, the WARNING still appears (nags existing installs).

- [ ] **Step 4: Record evidence and drop the disposable DB**

Capture the log lines and row counts from Steps 2–3 as the verification evidence, then drop `struo_seed_verify`.

---

## Self-Review

**Spec coverage:**
- §2 creation-gate principle → Task 1 (`JustCreated`), regression proven by `Skips_seeder_when_trigger_table_preexisted_even_if_empty`.
- §3.1 snapshot-before → Task 3 Step 2 (`existingBefore` before schema steps).
- §3.2 orchestrator + trigger tables → Task 1.
- §3.3 inner guards kept → existing seeders untouched; belt-and-suspenders remain.
- §3.4 default admin + prod warning → Task 2 + Task 3 Step 1.
- §3.5 Program.cs wiring, remove dev gate → Task 3 Step 2.
- §3.6 comment correction → Task 3 Step 3.
- §4 Non-Goals → no SQL/appsettings.Development/CodeFirst-prod changes anywhere in the plan.
- §5 testing (7 unit cases + live PG) → Tasks 1, 2, 4.
- §6 files touched → matches File Structure.

**Placeholder scan:** none — every code/test step contains full content.

**Type consistency:** `GetTableNames`, `SeedAsync`, `WarnIfDefaultAdminPasswordInProduction`, and the four table-name constants are used identically across Tasks 1–3. Seeder call signatures match the verified existing definitions (`LanguageSeeder.SeedAsync(db)`, `AdminUserSeeder.SeedAsync(db, hasher, email, password)`, `RbacSeeder.SeedAsync(db, email, collections, ct)`).
