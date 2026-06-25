# StruoCMS Phase 0 (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the StruoCMS Clean Architecture skeleton with SqlSugar (switchable DbType), audit-field auto-fill, Serilog file logging, health checks, dev-only table creation, and Scalar API docs — proven end-to-end by one minimal sample entity.

**Architecture:** Four-layer Clean Architecture (Domain → Application → Infrastructure → Api) plus `samples` and `tests`. Dependencies point inward only. The framework layers never reference business sample projects. SqlSugar is the sole DB access path; no vendor-specific SQL.

**Tech Stack:** .NET 10, C# latest, ASP.NET Core Controllers, SqlSugarCore, Serilog, Scalar.AspNetCore, xUnit + FluentAssertions, SQLite (tests) / PostgreSQL (default runtime).

## Global Constraints

Copied verbatim from the master spec / design — every task implicitly includes these:

- **Dependency rule (§2):** Domain references nothing; Application → Domain only; Infrastructure → Application + Domain; Api → Application + Infrastructure. Framework projects never reference `samples/*`.
- **Domain purity (§2):** `Struo.Domain` has zero external package references. Persistence attributes live only on business/sample entities.
- **DB-agnostic (§15):** All DB access goes through SqlSugar ORM. Zero vendor-specific SQL/functions/types. `DbType` is config-driven.
- **InitTables (§15):** Runs only when `IsDevelopment()`. A guard throws if reached in any other environment.
- **Packages (§15):** Add every package via `dotnet add package <name>` at latest stable. Never write a version number from memory. Versions are centralized in `Directory.Packages.props` (Central Package Management).
- **JSON (§1):** Outbound JSON is camelCase.
- **TDD (§17.2):** Failing test first; the verification gate requires evidence.
- **API style (§1):** ASP.NET Core Controllers, project-wide.
- **API docs:** Scalar, **Mars** theme, **axios** default client.

---

## File Structure

**Created by this plan:**

| File | Responsibility |
|---|---|
| `StruoCMS.sln` | Solution aggregator |
| `Directory.Build.props` | Shared MSBuild props (net10.0, nullable, langversion, warnings-as-errors) |
| `Directory.Packages.props` | Central Package Management — all package versions |
| `global.json` | Pin SDK major version 10 |
| `.editorconfig` | Formatting/style |
| `CLAUDE.md` | Condensed §1 + §2 + §17 for future sessions |
| `src/Struo.Domain/Auditing/IAuditable.cs` | Pure audit-field contract |
| `src/Struo.Application/Abstractions/ICurrentUserAccessor.cs` | Current-user port (Phase 6 seam) |
| `src/Struo.Application/Configuration/StruoDbType.cs` | Framework DB-type enum |
| `src/Struo.Application/Configuration/DatabaseOptions.cs` | Bound DB options |
| `src/Struo.Infrastructure/Persistence/DbTypeMapper.cs` | `StruoDbType` → `SqlSugar.DbType` |
| `src/Struo.Infrastructure/Persistence/AuditAop.cs` | SqlSugar AOP audit stamping |
| `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs` | Builds configured `ISqlSugarClient` |
| `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs` | Dev-only `InitTables` with guard |
| `src/Struo.Infrastructure/Identity/StubCurrentUserAccessor.cs` | Returns `"system"` |
| `src/Struo.Infrastructure/Health/DbReadinessCheck.cs` | Readiness `IHealthCheck` |
| `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` | `AddStruoInfrastructure` |
| `src/Struo.Api/Program.cs` | Composition root + host wiring |
| `src/Struo.Api/Controllers/PingController.cs` | Demo endpoint for Scalar |
| `src/Struo.Api/appsettings.json` | Committed config (placeholders) |
| `samples/Struo.Sample.Blog/Article.cs` | The one minimal sample entity |
| `tests/Struo.Tests/Support/SqliteTestDatabase.cs` | Temp-file SQLite fixture |
| `tests/Struo.Tests/Support/TestCurrentUserAccessor.cs` | Test user port |
| `tests/Struo.Tests/Support/FakeHostEnvironment.cs` | `IHostEnvironment` stub |
| `tests/Struo.Tests/Support/ApiFactory.cs` | `WebApplicationFactory<Program>` over SQLite |
| `tests/Struo.Tests/Persistence/DbTypeMapperTests.cs` | Mapping tests |
| `tests/Struo.Tests/Persistence/AuditAopTests.cs` | Audit stamping tests |
| `tests/Struo.Tests/Persistence/DatabaseInitializerTests.cs` | InitTables + dev-guard tests |
| `tests/Struo.Tests/Health/DbReadinessCheckTests.cs` | Readiness unit tests |
| `tests/Struo.Tests/DependencyInjection/InfrastructureRegistrationTests.cs` | DI registration test |
| `tests/Struo.Tests/Api/EndpointSmokeTests.cs` | Integration tests for endpoints |
| `docs/guide/01-getting-started.md` | Developer setup/run/config guide |

> **SQLite test note:** Tests use a **temp-file** SQLite database (`SqliteTestDatabase`) rather than `:memory:`. This is the concrete realization of the spec's "SQLite test DB" and avoids provider keep-alive coupling between SqlSugar's SQLite provider and `Microsoft.Data.Sqlite`. Each test gets a unique file, deleted on dispose.

---

### Task 1: Solution scaffold, shared MSBuild props, project references

**Files:**
- Create: `StruoCMS.sln`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `CLAUDE.md`
- Create: the six `.csproj` projects under `src/`, `samples/`, `tests/`

**Interfaces:**
- Consumes: nothing
- Produces: a buildable empty solution with the dependency graph wired; project names `Struo.Domain`, `Struo.Application`, `Struo.Infrastructure`, `Struo.Api`, `Struo.Sample.Blog`, `Struo.Tests`.

- [ ] **Step 1: Create solution and projects**

```bash
cd /d/dotnet/struo-cms
dotnet new sln -n StruoCMS
dotnet new classlib -n Struo.Domain -o src/Struo.Domain
dotnet new classlib -n Struo.Application -o src/Struo.Application
dotnet new classlib -n Struo.Infrastructure -o src/Struo.Infrastructure
dotnet new webapi -n Struo.Api -o src/Struo.Api --use-controllers
dotnet new classlib -n Struo.Sample.Blog -o samples/Struo.Sample.Blog
dotnet new xunit -n Struo.Tests -o tests/Struo.Tests
dotnet sln add src/Struo.Domain src/Struo.Application src/Struo.Infrastructure src/Struo.Api samples/Struo.Sample.Blog tests/Struo.Tests
```

- [ ] **Step 2: Delete template noise**

Remove the webapi template's sample weather files and the classlib `Class1.cs` placeholders:

```bash
rm -f src/Struo.Api/Controllers/WeatherForecastController.cs src/Struo.Api/WeatherForecast.cs
rm -f src/Struo.Domain/Class1.cs src/Struo.Application/Class1.cs src/Struo.Infrastructure/Class1.cs samples/Struo.Sample.Blog/Class1.cs
```

- [ ] **Step 3: Wire project references (enforces §2 dependency rule)**

```bash
dotnet add src/Struo.Application reference src/Struo.Domain
dotnet add src/Struo.Infrastructure reference src/Struo.Application src/Struo.Domain
dotnet add src/Struo.Api reference src/Struo.Application src/Struo.Infrastructure
dotnet add samples/Struo.Sample.Blog reference src/Struo.Domain
dotnet add src/Struo.Api reference samples/Struo.Sample.Blog
dotnet add tests/Struo.Tests reference src/Struo.Domain src/Struo.Application src/Struo.Infrastructure src/Struo.Api samples/Struo.Sample.Blog
```

> Note: `Struo.Api` references `samples/Struo.Sample.Blog` because the composition root needs the sample entity type for dev `InitTables`. This is the only framework→sample reference and it lives at the composition root, not in framework libraries.

- [ ] **Step 4: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

Then remove the now-duplicate `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>` lines from each generated `.csproj` (they are inherited).

- [ ] **Step 5: Create `Directory.Packages.props` (empty registry — populated by later `dotnet add package`)**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create `global.json`**

```json
{
  "sdk": {
    "version": "10.0.0",
    "rollForward": "latestMinor"
  }
}
```

- [ ] **Step 7: Create `.editorconfig`** (minimal)

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
charset = utf-8-bom
dotnet_sort_system_directives_first = true
csharp_using_directive_placement = outside_namespace:warning
```

- [ ] **Step 8: Create `CLAUDE.md`** (condensed master-spec rules)

```markdown
# StruoCMS — Working Rules (condensed)

## Stack (§1)
.NET 10 / C# latest · SqlSugarCore · multi-DB (default PostgreSQL, tests SQLite) ·
ASP.NET Core **Controllers** · Serilog · Scalar (Mars theme, axios) · Redis (Phase 6) ·
Vue 3 + PrimeVue + TipTap (Phase 7). Outbound JSON = camelCase.

## Dependency rule (§2)
Domain → nothing · Application → Domain · Infrastructure → Application+Domain ·
Api → Application+Infrastructure. Framework code never references `samples/*`.
Domain stays free of external packages; persistence attributes live on entities only.

## Execution rules (§17)
1. Phase-by-phase: brainstorm → write-plan (TDD) → execute → verify. Specs in
   `docs/superpowers/specs/`, plans in `docs/superpowers/plans/`.
2. TDD: failing test first; acceptance = verification gate with evidence.
3. YAGNI: stay inside the current phase's scope.
4. All DB access via SqlSugar ORM; zero vendor SQL. InitTables dev-only.
5. Install packages at latest via `dotnet add package`; never hardcode versions.
   Versions centralized in `Directory.Packages.props`.
6. Metadata scanned at startup and cached; no per-request reflection (Phase 1+).
7. Query DSL never leaks ORM internals; field/relation paths are whitelist-validated (Phase 2+).
8. RichText is server-side sanitized (Phase 6/7).
9. When unsure about an architecture decision, stop and ask.
```

- [ ] **Step 9: Build the empty solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "chore: scaffold Clean Architecture solution with CPM and shared props"
```

---

### Task 2: DbType mapping (first real unit, TDD)

**Files:**
- Create: `src/Struo.Application/Configuration/StruoDbType.cs`
- Create: `src/Struo.Infrastructure/Persistence/DbTypeMapper.cs`
- Test: `tests/Struo.Tests/Persistence/DbTypeMapperTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `enum StruoDbType { PostgreSQL, MySql, SqlServer, Sqlite, Oracle }`; `static SqlSugar.DbType DbTypeMapper.Map(StruoDbType)`.

- [ ] **Step 1: Install SqlSugarCore into Infrastructure (latest)**

```bash
dotnet add src/Struo.Infrastructure package SqlSugarCore
```
Verify a `<PackageVersion Include="SqlSugarCore" .../>` line was added to `Directory.Packages.props`.

- [ ] **Step 2: Create the enum**

```csharp
// src/Struo.Application/Configuration/StruoDbType.cs
namespace Struo.Application.Configuration;

public enum StruoDbType
{
    PostgreSQL,
    MySql,
    SqlServer,
    Sqlite,
    Oracle
}
```

- [ ] **Step 3: Write the failing test**

```csharp
// tests/Struo.Tests/Persistence/DbTypeMapperTests.cs
using FluentAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Persistence;

public class DbTypeMapperTests
{
    [Theory]
    [InlineData(StruoDbType.PostgreSQL, SqlSugar.DbType.PostgreSQL)]
    [InlineData(StruoDbType.MySql, SqlSugar.DbType.MySql)]
    [InlineData(StruoDbType.SqlServer, SqlSugar.DbType.SqlServer)]
    [InlineData(StruoDbType.Sqlite, SqlSugar.DbType.Sqlite)]
    [InlineData(StruoDbType.Oracle, SqlSugar.DbType.Oracle)]
    public void Map_returns_matching_SqlSugar_DbType(StruoDbType input, SqlSugar.DbType expected)
    {
        DbTypeMapper.Map(input).Should().Be(expected);
    }
}
```

- [ ] **Step 4: Install FluentAssertions into the test project**

```bash
dotnet add tests/Struo.Tests package FluentAssertions
```

- [ ] **Step 5: Run the test — verify it fails**

Run: `dotnet test --filter FullyQualifiedName~DbTypeMapperTests`
Expected: FAIL — `DbTypeMapper` does not exist (compile error).

- [ ] **Step 6: Implement the mapper**

```csharp
// src/Struo.Infrastructure/Persistence/DbTypeMapper.cs
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

public static class DbTypeMapper
{
    public static SqlSugar.DbType Map(StruoDbType dbType) => dbType switch
    {
        StruoDbType.PostgreSQL => SqlSugar.DbType.PostgreSQL,
        StruoDbType.MySql => SqlSugar.DbType.MySql,
        StruoDbType.SqlServer => SqlSugar.DbType.SqlServer,
        StruoDbType.Sqlite => SqlSugar.DbType.Sqlite,
        StruoDbType.Oracle => SqlSugar.DbType.Oracle,
        _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unsupported DbType")
    };
}
```

> If a `SqlSugar.DbType` enum member name differs in the installed version, adjust the right-hand side to match (verify via the installed package).

- [ ] **Step 7: Run the test — verify it passes**

Run: `dotnet test --filter FullyQualifiedName~DbTypeMapperTests`
Expected: PASS (5 cases).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add StruoDbType enum and SqlSugar DbType mapper"
```

---

### Task 3: Domain audit contract, ports, sample entity, SqlSugar client factory

**Files:**
- Create: `src/Struo.Domain/Auditing/IAuditable.cs`
- Create: `src/Struo.Application/Abstractions/ICurrentUserAccessor.cs`
- Create: `src/Struo.Application/Configuration/DatabaseOptions.cs`
- Create: `src/Struo.Infrastructure/Identity/StubCurrentUserAccessor.cs`
- Create: `src/Struo.Infrastructure/Persistence/AuditAop.cs`
- Create: `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
- Create: `samples/Struo.Sample.Blog/Article.cs`
- Test: `tests/Struo.Tests/Support/SqliteTestDatabase.cs`, `tests/Struo.Tests/Support/TestCurrentUserAccessor.cs`, `tests/Struo.Tests/Persistence/AuditAopTests.cs`

**Interfaces:**
- Consumes: `DbTypeMapper.Map`, `StruoDbType`
- Produces:
  - `interface IAuditable { DateTime CreatedAt; string? CreatedBy; DateTime UpdatedAt; string? UpdatedBy; }`
  - `interface ICurrentUserAccessor { string? GetCurrentUserId(); }`
  - `sealed class DatabaseOptions { const string SectionName="Database"; StruoDbType DbType; string ConnectionString; }`
  - `static ISqlSugarClient SqlSugarClientFactory.Create(DatabaseOptions, ICurrentUserAccessor)`
  - `sealed class Article : IAuditable` mapped to table `articles`

- [ ] **Step 1: Create the Domain audit contract (pure)**

```csharp
// src/Struo.Domain/Auditing/IAuditable.cs
namespace Struo.Domain.Auditing;

public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
```

- [ ] **Step 2: Create the current-user port and DatabaseOptions**

```csharp
// src/Struo.Application/Abstractions/ICurrentUserAccessor.cs
namespace Struo.Application.Abstractions;

public interface ICurrentUserAccessor
{
    string? GetCurrentUserId();
}
```

```csharp
// src/Struo.Application/Configuration/DatabaseOptions.cs
namespace Struo.Application.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public StruoDbType DbType { get; set; } = StruoDbType.PostgreSQL;
    public string ConnectionString { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create the stub current-user accessor**

```csharp
// src/Struo.Infrastructure/Identity/StubCurrentUserAccessor.cs
using Struo.Application.Abstractions;

namespace Struo.Infrastructure.Identity;

/// <summary>Phase 0 placeholder. Replaced by a real session-backed accessor in Phase 6.</summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    public string? GetCurrentUserId() => "system";
}
```

- [ ] **Step 4: Create the sample entity**

```csharp
// samples/Struo.Sample.Blog/Article.cs
using SqlSugar;
using Struo.Domain.Auditing;

namespace Struo.Sample.Blog;

[SugarTable("articles")]
public sealed class Article : IAuditable
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
```

Add SqlSugarCore to the sample project:

```bash
dotnet add samples/Struo.Sample.Blog package SqlSugarCore
```

- [ ] **Step 5: Create the audit AOP and client factory**

```csharp
// src/Struo.Infrastructure/Persistence/AuditAop.cs
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Domain.Auditing;

namespace Struo.Infrastructure.Persistence;

public static class AuditAop
{
    public static void Register(ISqlSugarClient client, ICurrentUserAccessor currentUser)
    {
        client.Aop.DataExecuting = (oldValue, entityInfo) =>
        {
            if (entityInfo.EntityValue is not IAuditable) return;

            var now = DateTime.UtcNow;
            var user = currentUser.GetCurrentUserId();

            if (entityInfo.OperationType == DataFilterType.InsertByObject)
            {
                switch (entityInfo.PropertyName)
                {
                    case nameof(IAuditable.CreatedAt):
                    case nameof(IAuditable.UpdatedAt):
                        entityInfo.SetValue(now);
                        break;
                    case nameof(IAuditable.CreatedBy):
                    case nameof(IAuditable.UpdatedBy):
                        entityInfo.SetValue(user);
                        break;
                }
            }
            else if (entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                switch (entityInfo.PropertyName)
                {
                    case nameof(IAuditable.UpdatedAt):
                        entityInfo.SetValue(now);
                        break;
                    case nameof(IAuditable.UpdatedBy):
                        entityInfo.SetValue(user);
                        break;
                }
            }
        };
    }
}
```

```csharp
// src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

public static class SqlSugarClientFactory
{
    public static ISqlSugarClient Create(DatabaseOptions options, ICurrentUserAccessor currentUser)
    {
        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = options.ConnectionString,
            DbType = DbTypeMapper.Map(options.DbType),
            IsAutoCloseConnection = true
        });

        AuditAop.Register(client, currentUser);
        return client;
    }
}
```

- [ ] **Step 6: Create test support fixtures**

```csharp
// tests/Struo.Tests/Support/SqliteTestDatabase.cs
namespace Struo.Tests.Support;

/// <summary>Unique temp-file SQLite database, deleted on dispose. Provider-agnostic.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    public string FilePath { get; } =
        Path.Combine(Path.GetTempPath(), $"struo_test_{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={FilePath}";

    public void Dispose()
    {
        if (File.Exists(FilePath))
        {
            try { File.Delete(FilePath); } catch { /* best effort */ }
        }
    }
}
```

```csharp
// tests/Struo.Tests/Support/TestCurrentUserAccessor.cs
using Struo.Application.Abstractions;

namespace Struo.Tests.Support;

public sealed class TestCurrentUserAccessor(string? userId) : ICurrentUserAccessor
{
    public string? GetCurrentUserId() => userId;
}
```

Add the SQLite ADO provider to the test project (so the temp-file DB has a driver):

```bash
dotnet add tests/Struo.Tests package Microsoft.Data.Sqlite
```

- [ ] **Step 7: Write the failing audit test**

```csharp
// tests/Struo.Tests/Persistence/AuditAopTests.cs
using FluentAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class AuditAopTests
{
    private static DatabaseOptions Options(SqliteTestDatabase db) => new()
    {
        DbType = StruoDbType.Sqlite,
        ConnectionString = db.ConnectionString
    };

    [Fact]
    public void Insert_stamps_created_and_updated_audit_fields()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor("alice"));
        client.CodeFirst.InitTables<Article>();

        var id = client.Insertable(new Article { Title = "Hello" }).ExecuteReturnBigIdentity();

        var saved = client.Queryable<Article>().InSingle(id);
        saved.CreatedBy.Should().Be("alice");
        saved.UpdatedBy.Should().Be("alice");
        saved.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        saved.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Update_restamps_only_updated_audit_fields()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor("alice"));
        client.CodeFirst.InitTables<Article>();
        var id = client.Insertable(new Article { Title = "Hello" }).ExecuteReturnBigIdentity();
        var original = client.Queryable<Article>().InSingle(id);

        var updateClient = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor("bob"));
        var toUpdate = updateClient.Queryable<Article>().InSingle(id);
        toUpdate.Title = "Changed";
        updateClient.Updateable(toUpdate).ExecuteCommand();

        var after = updateClient.Queryable<Article>().InSingle(id);
        after.CreatedBy.Should().Be("alice");           // unchanged
        after.UpdatedBy.Should().Be("bob");             // restamped
        after.CreatedAt.Should().BeCloseTo(original.CreatedAt, TimeSpan.FromSeconds(1));
    }
}
```

- [ ] **Step 8: Run the tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~AuditAopTests`
Expected: FAIL (compile errors until factory/entity exist, then assertion-level until AOP correct).

- [ ] **Step 9: Build and rerun until green**

Run: `dotnet test --filter FullyQualifiedName~AuditAopTests`
Expected: PASS (2 tests).

> If `ExecuteReturnBigIdentity` / `InSingle` differ in the installed SqlSugar version, substitute the equivalent (`ExecuteReturnIdentity`, `InSingle(id)`); verify against the installed package.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add IAuditable, current-user port, sample entity, and SqlSugar audit AOP"
```

---

### Task 4: Dev-only DatabaseInitializer with environment guard

**Files:**
- Create: `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs`
- Test: `tests/Struo.Tests/Support/FakeHostEnvironment.cs`, `tests/Struo.Tests/Persistence/DatabaseInitializerTests.cs`

**Interfaces:**
- Consumes: `SqlSugarClientFactory.Create`, `Article`, `Microsoft.Extensions.Hosting.IHostEnvironment`
- Produces: `static void DatabaseInitializer.InitializeDevelopmentSchema(ISqlSugarClient, IHostEnvironment, params Type[])`

- [ ] **Step 1: Add hosting abstractions to Infrastructure**

```bash
dotnet add src/Struo.Infrastructure package Microsoft.Extensions.Hosting.Abstractions
```

- [ ] **Step 2: Create the FakeHostEnvironment test stub**

```csharp
// tests/Struo.Tests/Support/FakeHostEnvironment.cs
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Struo.Tests.Support;

public sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Struo.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } =
        new PhysicalFileProvider(AppContext.BaseDirectory);
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
// tests/Struo.Tests/Persistence/DatabaseInitializerTests.cs
using FluentAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class DatabaseInitializerTests
{
    private static (SqliteTestDatabase, SqlSugar.ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor("system"));
        return (db, client);
    }

    [Fact]
    public void Creates_table_when_environment_is_development()
    {
        var (db, client) = NewClient();
        using (db)
        {
            DatabaseInitializer.InitializeDevelopmentSchema(
                client, new FakeHostEnvironment("Development"), typeof(Article));

            var tables = client.DbMaintenance.GetTableInfoList(false);
            tables.Any(t => t.Name.Equals("articles", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue();
        }
    }

    [Fact]
    public void Throws_when_environment_is_not_development()
    {
        var (db, client) = NewClient();
        using (db)
        {
            var act = () => DatabaseInitializer.InitializeDevelopmentSchema(
                client, new FakeHostEnvironment("Production"), typeof(Article));

            act.Should().Throw<InvalidOperationException>();
        }
    }
}
```

- [ ] **Step 4: Run the tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~DatabaseInitializerTests`
Expected: FAIL — `DatabaseInitializer` does not exist.

- [ ] **Step 5: Implement the initializer**

```csharp
// src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs
using Microsoft.Extensions.Hosting;
using SqlSugar;

namespace Struo.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static void InitializeDevelopmentSchema(
        ISqlSugarClient client, IHostEnvironment environment, params Type[] entityTypes)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "InitTables is only permitted in the Development environment. " +
                $"Current environment: '{environment.EnvironmentName}'. " +
                "Production schema changes must go through reviewed migration scripts.");
        }

        if (entityTypes.Length == 0) return;
        client.CodeFirst.InitTables(entityTypes);
    }
}
```

`IsDevelopment()` comes from `Microsoft.Extensions.Hosting` (HostingAbstractions extension). Ensure `using Microsoft.Extensions.Hosting;`.

- [ ] **Step 6: Run the tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DatabaseInitializerTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add dev-only DatabaseInitializer with environment guard"
```

---

### Task 5: Readiness health check

**Files:**
- Create: `src/Struo.Infrastructure/Health/DbReadinessCheck.cs`
- Test: `tests/Struo.Tests/Health/DbReadinessCheckTests.cs`

**Interfaces:**
- Consumes: `ISqlSugarClient`, `Microsoft.Extensions.Diagnostics.HealthChecks`
- Produces: `sealed class DbReadinessCheck(ISqlSugarClient) : IHealthCheck`

- [ ] **Step 1: Add health-check abstractions to Infrastructure**

```bash
dotnet add src/Struo.Infrastructure package Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Struo.Tests/Health/DbReadinessCheckTests.cs
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Struo.Application.Configuration;
using Struo.Infrastructure.Health;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Health;

public class DbReadinessCheckTests
{
    [Fact]
    public async Task Reports_healthy_when_database_reachable()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor("system"));
        client.CodeFirst.InitTables<Article>();

        var check = new DbReadinessCheck(client);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Reports_unhealthy_when_database_unreachable()
    {
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.Sqlite,
                ConnectionString = "Data Source=/nonexistent-dir/struo_missing.db;Mode=ReadOnly"
            },
            new TestCurrentUserAccessor("system"));

        var check = new DbReadinessCheck(client);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
```

- [ ] **Step 3: Run the tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~DbReadinessCheckTests`
Expected: FAIL — `DbReadinessCheck` does not exist.

- [ ] **Step 4: Implement the readiness check (ORM-level, zero SQL)**

```csharp
// src/Struo.Infrastructure/Health/DbReadinessCheck.cs
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SqlSugar;

namespace Struo.Infrastructure.Health;

public sealed class DbReadinessCheck(ISqlSugarClient db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var ok = await Task.Run(() => db.Ado.IsValidConnection(), cancellationToken);
            return ok
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database connection is not valid.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}
```

> `db.Ado.IsValidConnection()` is SqlSugar's connectivity probe — no raw SQL, fully portable. If an async variant `IsValidConnectionAsync()` exists in the installed version, prefer it and drop the `Task.Run` wrapper.

- [ ] **Step 5: Run the tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DbReadinessCheckTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add SqlSugar-based database readiness health check"
```

---

### Task 6: Infrastructure DI registration

**Files:**
- Create: `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/Struo.Tests/DependencyInjection/InfrastructureRegistrationTests.cs`

**Interfaces:**
- Consumes: all Infrastructure types above; `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Options`
- Produces: `static IServiceCollection AddStruoInfrastructure(this IServiceCollection, IConfiguration)` registering `ICurrentUserAccessor` (singleton stub) and `ISqlSugarClient` (scoped, configured from `DatabaseOptions`).

- [ ] **Step 1: Add DI/config/options abstractions to Infrastructure**

```bash
dotnet add src/Struo.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/Struo.Infrastructure package Microsoft.Extensions.Configuration.Abstractions
dotnet add src/Struo.Infrastructure package Microsoft.Extensions.Options.ConfigurationExtensions
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/Struo.Tests/DependencyInjection/InfrastructureRegistrationTests.cs
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Infrastructure.DependencyInjection;
using Xunit;

namespace Struo.Tests.DependencyInjection;

public class InfrastructureRegistrationTests
{
    [Fact]
    public void AddStruoInfrastructure_registers_core_services()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = "Data Source=:memory:"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddStruoInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ICurrentUserAccessor>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ISqlSugarClient>().Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Run the test — verify it fails**

Run: `dotnet test --filter FullyQualifiedName~InfrastructureRegistrationTests`
Expected: FAIL — `AddStruoInfrastructure` does not exist.

- [ ] **Step 4: Implement the registration**

```csharp
// src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStruoInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddSingleton<ICurrentUserAccessor, StubCurrentUserAccessor>();

        services.AddScoped<ISqlSugarClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var currentUser = sp.GetRequiredService<ICurrentUserAccessor>();
            return SqlSugarClientFactory.Create(options, currentUser);
        });

        return services;
    }
}
```

- [ ] **Step 5: Run the test — verify it passes**

Run: `dotnet test --filter FullyQualifiedName~InfrastructureRegistrationTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add AddStruoInfrastructure DI registration"
```

---

### Task 7: API host wiring + endpoints + integration tests

**Files:**
- Create: `src/Struo.Api/Program.cs` (replace template), `src/Struo.Api/Controllers/PingController.cs`, `src/Struo.Api/appsettings.json` (replace)
- Test: `tests/Struo.Tests/Support/ApiFactory.cs`, `tests/Struo.Tests/Api/EndpointSmokeTests.cs`

**Interfaces:**
- Consumes: `AddStruoInfrastructure`, `DbReadinessCheck`, `DatabaseInitializer`, `Article`
- Produces: HTTP endpoints `GET /api/ping`, `GET /health/live`, `GET /health/ready`; Scalar UI at `/scalar`; a `public partial class Program` for `WebApplicationFactory`.

- [ ] **Step 1: Add packages to the Api project**

```bash
dotnet add src/Struo.Api package Serilog.AspNetCore
dotnet add src/Struo.Api package Serilog.Sinks.File
dotnet add src/Struo.Api package Scalar.AspNetCore
dotnet add src/Struo.Api package Microsoft.AspNetCore.OpenApi
```
(`AddOpenApi`/`MapOpenApi` ship in `Microsoft.AspNetCore.OpenApi`.)

- [ ] **Step 2: Replace `appsettings.json`**

```json
{
  "Database": {
    "DbType": "PostgreSQL",
    "ConnectionString": "Host=localhost;Port=5432;Database=struo;Username=REPLACE_ME;Password=REPLACE_ME"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning" }
    },
    "WriteTo": [
      { "Name": "File", "Args": { "path": "logs/struo-.log", "rollingInterval": "Day", "shared": true } }
    ]
  }
}
```

> The real Postgres connection string belongs in user-secrets / `appsettings.Development.json` (gitignored) / env vars — never in this committed file.

- [ ] **Step 3: Write `Program.cs`**

```csharp
// src/Struo.Api/Program.cs
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using SqlSugar;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Health;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services));

    builder.Services
        .AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

    builder.Services.AddOpenApi();
    builder.Services.AddStruoInfrastructure(builder.Configuration);
    builder.Services.AddHealthChecks()
        .AddCheck<DbReadinessCheck>("database", tags: ["ready"]);

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    app.MapControllers();
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
        options.WithTheme(ScalarTheme.Mars)
               .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Axios));

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment, typeof(Article));
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "StruoCMS host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
```

> Verify the Scalar fluent API names (`ScalarTheme.Mars`, `ScalarTarget.JavaScript`, `ScalarClient.Axios`, `WithDefaultHttpClient`) against the installed `Scalar.AspNetCore` version; adjust if the enum/method names differ.

- [ ] **Step 4: Write `PingController.cs`**

```csharp
// src/Struo.Api/Controllers/PingController.cs
using Microsoft.AspNetCore.Mvc;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "StruoCMS",
        utc = DateTime.UtcNow
    });
}
```

- [ ] **Step 5: Write the API test factory (overrides config to SQLite)**

```csharp
// tests/Struo.Tests/Support/ApiFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Struo.Tests.Support;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteTestDatabase _db = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = _db.ConnectionString
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _db.Dispose();
    }
}
```

Add the MVC testing package:

```bash
dotnet add tests/Struo.Tests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 6: Write the failing integration tests**

```csharp
// tests/Struo.Tests/Api/EndpointSmokeTests.cs
using System.Net;
using FluentAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public class EndpointSmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Liveness_returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_returns_200_when_db_reachable()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ping_returns_ok_payload_in_camelCase()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"ok\"");
        body.Should().Contain("\"service\":\"StruoCMS\"");
    }
}
```

- [ ] **Step 7: Run the tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~EndpointSmokeTests`
Expected: FAIL until `Program.cs`/controller compile and run.

- [ ] **Step 8: Build and rerun until green**

Run: `dotnet test --filter FullyQualifiedName~EndpointSmokeTests`
Expected: PASS (3 tests).

- [ ] **Step 9: Run the full suite**

Run: `dotnet test`
Expected: PASS (all tests across all files).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: wire API host with Serilog, OpenAPI/Scalar, health endpoints, and ping"
```

---

### Task 8: Developer guide + verification gate evidence

**Files:**
- Create: `docs/guide/01-getting-started.md`

**Interfaces:**
- Consumes: the finished app
- Produces: developer-facing setup/run/config documentation; recorded verification evidence.

- [ ] **Step 1: Write the getting-started guide**

Create `docs/guide/01-getting-started.md` with: prerequisites (.NET 10 SDK; Postgres for runtime), solution layout, how to configure `Database:DbType`/`Database:ConnectionString` via user-secrets, how to run (`dotnet run --project src/Struo.Api`), endpoint URLs (`/scalar`, `/health/live`, `/health/ready`, `/api/ping`), the dev-only InitTables behavior, Serilog log file location (`src/Struo.Api/logs/struo-*.log`), and how to run tests (`dotnet test`, SQLite temp-file, no external services).

- [ ] **Step 2: Commit the guide**

```bash
git add -A
git commit -m "docs: add Phase 0 getting-started guide"
```

- [ ] **Step 3: Verification gate — full build & test**

Run: `dotnet build` → Expected: 0 errors, 0 warnings (warnings-as-errors).
Run: `dotnet test` → Expected: all tests pass.

- [ ] **Step 4: Verification gate — Postgres live connection**

Obtain the user-provided Postgres connection string. Set it for the Api:

```bash
cd src/Struo.Api
dotnet user-secrets init
dotnet user-secrets set "Database:ConnectionString" "<USER_PROVIDED>"
dotnet user-secrets set "Database:DbType" "PostgreSQL"
ASPNETCORE_ENVIRONMENT=Development dotnet run --project . &
```

Then verify (capture output as evidence):
- `curl -k https://localhost:<port>/health/live` → 200
- `curl -k https://localhost:<port>/health/ready` → 200 (proves Postgres reachable)
- Confirm the `articles` table was created in Postgres (dev InitTables).
- Open `/scalar` and confirm Mars theme + axios client render.
- Confirm `logs/struo-*.log` was written.

- [ ] **Step 5: Verification gate — Production guard (negative)**

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Struo.Api
```
Expected: app starts WITHOUT running InitTables (no table-creation attempt). Capture evidence and stop the process.

- [ ] **Step 6: Final commit (if any evidence docs added)**

```bash
git add -A
git commit -m "chore: record Phase 0 verification evidence" || echo "nothing to commit"
```

---

## Self-Review

**1. Spec coverage** (design §9 verification gate vs. tasks):

| Spec requirement | Task |
|---|---|
| Clean Arch four layers + samples + tests | Task 1 |
| DI composition root | Task 6 (Infra), Task 7 (Api root) |
| SqlSugar switchable DbType (default Postgres, tests SQLite) | Task 2, 3 |
| Serilog local file sink, config-driven | Task 7 |
| `/health/live` + `/health/ready` | Task 5, 7 |
| Dev-only InitTables | Task 4, 7 |
| IAuditable + audit auto-fill | Task 3 |
| One minimal sample entity | Task 3 |
| Scalar (Mars, axios) | Task 7 |
| Developer docs | Task 8 |
| Connects to Postgres (verify) | Task 8 |
| Production InitTables refused (negative) | Task 4 (unit), Task 8 (live) |
| camelCase JSON | Task 7 |
| Zero vendor SQL | Task 5 (IsValidConnection), all (SqlSugar only) |
| Packages latest via CLI, CPM | Task 1 + every install step |

No gaps found.

**2. Placeholder scan:** `REPLACE_ME` / `<USER_PROVIDED>` are intentional config placeholders for secrets, not plan placeholders. No "TBD"/"implement later"/vague steps. All code steps contain full code.

**3. Type consistency:** `StruoDbType`, `DatabaseOptions` (SectionName/DbType/ConnectionString), `ICurrentUserAccessor.GetCurrentUserId()`, `IAuditable` (CreatedAt/CreatedBy/UpdatedAt/UpdatedBy), `SqlSugarClientFactory.Create(DatabaseOptions, ICurrentUserAccessor)`, `DatabaseInitializer.InitializeDevelopmentSchema(ISqlSugarClient, IHostEnvironment, params Type[])`, `DbReadinessCheck(ISqlSugarClient)`, `AddStruoInfrastructure(IServiceCollection, IConfiguration)` are used consistently across tasks.

**Known verify-against-package points (flagged inline):** SqlSugar enum member names, `ExecuteReturnBigIdentity`/`InSingle`, `IsValidConnection[Async]`, and the Scalar fluent API. TDD catches mismatches at the first failing run.
