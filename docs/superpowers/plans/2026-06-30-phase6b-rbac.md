# Phase 6b — Collection-based Authorization (RBAC) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `AllowAllPermissionService` with real role-based access control — roles grant per-collection read/write/delete, anonymous callers map to a `public` role, and a super-admin role bypasses all checks.

**Architecture:** Three new framework CMS collections (`role`, `permission`, `userRole`) in Infrastructure. A per-request middleware resolves the caller's effective permissions once (via a direct-query store that bypasses RBAC gating) into a scoped snapshot. `IPermissionService` keeps its synchronous, ambient signature and reads the snapshot — so `ItemService` is unchanged except for swapping the denial exception type. Denials throw `PermissionDeniedException`; the existing exception middleware maps it to 401 (anonymous) or 403 (authenticated) using `HttpContext.User`.

**Tech Stack:** .NET 10 / C#, SqlSugarCore (multi-DB; tests SQLite), ASP.NET Core controllers + middleware, xUnit + FluentAssertions + `WebApplicationFactory`, Serilog.

## Global Constraints

- **Dependency rule (§2):** Domain → nothing · Application → Domain · Infrastructure → Application+Domain · Api → Application+Infrastructure. Framework code never references `samples/*`.
- Domain stays free of external packages; persistence attributes (`[Sugar*]`) live on entities only (entities sit in Infrastructure).
- All DB access via SqlSugar ORM; **zero vendor SQL**. `InitTables` is dev-only.
- Outbound JSON = camelCase.
- Metadata scanned once at startup and cached; no per-request reflection.
- TDD: failing test first; acceptance = verification gate with evidence.
- YAGNI: no field-level per-role control, no general row/ownership engine, no cross-request permission cache.
- `dotnet build` runs **warnings-as-errors** — the build must stay clean.
- New packages (none expected) only via `dotnet add package` (latest), versions centralized in `Directory.Packages.props`.
- **Live verification gate:** SQLite-green ≠ Postgres-correct — DB features re-verified on live Postgres before the phase is declared done.

---

## File Structure

**Create (Application — ports + pure logic):**
- `src/Struo.Application/Security/EffectivePermissions.cs` — resolved per-request snapshot + lookups.
- `src/Struo.Application/Security/IRolePermissionStore.cs` — port + `RolePermissionData`/`RoleRow`/`PermissionRow` records.
- `src/Struo.Application/Security/PermissionResolver.cs` — static: folds `RolePermissionData` → `EffectivePermissions`.
- `src/Struo.Application/Security/ICurrentPermissions.cs` — scoped holder interface + `CurrentPermissions` impl.
- `src/Struo.Application/Security/RbacPermissionService.cs` — `IPermissionService` reading the snapshot.

**Create (Domain — exception family):**
- `src/Struo.Domain/Query/PermissionDeniedException.cs` — beside the existing `QueryException`.

**Create (Infrastructure — entities + implementations):**
- `src/Struo.Infrastructure/Identity/Role.cs`, `Permission.cs`, `UserRole.cs`
- `src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs`
- `src/Struo.Infrastructure/Identity/RbacSeeder.cs`

**Create (Api — web-coupled glue):**
- `src/Struo.Api/Auth/PermissionResolutionMiddleware.cs`

**Modify:**
- `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs:21` — `AllowAll` → `Rbac` (+ scoped lifetime).
- `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` — register store + current-permissions holder.
- `src/Struo.Api/Program.cs` — `InitTables` (+3 entities), middleware registration, `RbacSeeder` call, `PermissionDeniedException` catch, `Rbac:PublicReadCollections` config read.
- `src/Struo.Application/Query/ItemService.cs` — swap 5 denial `throw`s to `PermissionDeniedException`.
- `src/Struo.Api/Controllers/ItemsController.cs` — remove `GuardProtected` + its call sites.
- `src/Struo.Api/Controllers/UsersController.cs` — admin gate + self `currentPassword` verification.
- `tests/Struo.Tests/Support/ApiFactory.cs` — grant the seeded admin the super-admin role; set test `Rbac:PublicReadCollections`.

**Delete:**
- `src/Struo.Api/Auth/ProtectedCollections.cs` — subsumed by RBAC.

---

## Task 1: RBAC entities (`Role`, `Permission`, `UserRole`)

**Files:**
- Create: `src/Struo.Infrastructure/Identity/Role.cs`
- Create: `src/Struo.Infrastructure/Identity/Permission.cs`
- Create: `src/Struo.Infrastructure/Identity/UserRole.cs`
- Test: `tests/Struo.Tests/Identity/RbacEntitiesMetadataTests.cs`

**Interfaces:**
- Consumes: `AuditableEntity` (`src/Struo.Domain/Auditing/`), `[CmsCollection]`/`[CmsField]` (`Struo.Domain.Metadata.Attributes`), `FieldInterface` (`Struo.Domain.Metadata.Enums`), `MetadataScanner.ScanTypes` (`Struo.Infrastructure.Metadata`).
- Produces: entity types `Role` (props `Id:Guid`, `Name:string`, `IsSuperAdmin:bool`, `Description:string?`), `Permission` (`Id:Guid`, `RoleId:Guid`, `Collection:string`, `CanRead:bool`, `CanWrite:bool`, `CanDelete:bool`), `UserRole` (`Id:Guid`, `UserId:Guid`, `RoleId:Guid`). Collection route names: `role`, `permission`, `userRole`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Identity/RbacEntitiesMetadataTests.cs
using FluentAssertions;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Identity;

public class RbacEntitiesMetadataTests
{
    [Fact]
    public void Scans_rbac_collections_with_expected_identity()
    {
        var collections = MetadataScanner.ScanTypes(
            [typeof(Role), typeof(Permission), typeof(UserRole)]);

        var names = collections.Select(c => c.Name).ToList();
        names.Should().Contain(["role", "permission", "userRole"]);

        var permission = collections.Single(c => c.Name == "permission");
        permission.Fields.Select(f => f.Name)
            .Should().Contain(["roleId", "collection", "canRead", "canWrite", "canDelete"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter RbacEntitiesMetadataTests`
Expected: FAIL — `Role`/`Permission`/`UserRole` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Struo.Infrastructure/Identity/Role.cs
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>Framework-owned RBAC role. Collection route is <c>role</c>.
/// <see cref="IsSuperAdmin"/> short-circuits all permission checks (allow-all).</summary>
[SugarTable("roles")]
[CmsCollection("Role", Group = "System", DefaultDisplayField = nameof(Name))]
public sealed class Role : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_roles_name"])]
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = "";

    [CmsField(Label = "Super Admin", Interface = FieldInterface.Boolean, Sort = 2)]
    public bool IsSuperAdmin { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Description", Interface = FieldInterface.Text, Sort = 3)]
    public string? Description { get; set; }
}
```

```csharp
// src/Struo.Infrastructure/Identity/Permission.cs
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>A role's read/write/delete grant for one collection. Collection route is
/// <c>permission</c>. Unique on (<see cref="RoleId"/>, <see cref="Collection"/>).</summary>
[SugarTable("permissions")]
[CmsCollection("Permission", Group = "System", DefaultDisplayField = nameof(Collection))]
public sealed class Permission : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_permissions_role_collection"])]
    [CmsField(Label = "Role", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public Guid RoleId { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_permissions_role_collection"])]
    [CmsField(Label = "Collection", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public string Collection { get; set; } = "";

    [CmsField(Label = "Can Read", Interface = FieldInterface.Boolean, Sort = 3)]
    public bool CanRead { get; set; }

    [CmsField(Label = "Can Write", Interface = FieldInterface.Boolean, Sort = 4)]
    public bool CanWrite { get; set; }

    [CmsField(Label = "Can Delete", Interface = FieldInterface.Boolean, Sort = 5)]
    public bool CanDelete { get; set; }
}
```

```csharp
// src/Struo.Infrastructure/Identity/UserRole.cs
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>Junction modelling the user↔role many-to-many. Collection route is <c>userRole</c>.
/// Unique on (<see cref="UserId"/>, <see cref="RoleId"/>).</summary>
[SugarTable("user_roles")]
[CmsCollection("UserRole", Group = "System", DefaultDisplayField = nameof(UserId))]
public sealed class UserRole : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_user_roles_user_role"])]
    [CmsField(Label = "User", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public Guid UserId { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_user_roles_user_role"])]
    [CmsField(Label = "Role", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public Guid RoleId { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter RbacEntitiesMetadataTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Identity/Role.cs src/Struo.Infrastructure/Identity/Permission.cs src/Struo.Infrastructure/Identity/UserRole.cs tests/Struo.Tests/Identity/RbacEntitiesMetadataTests.cs
git commit -m "feat: RBAC entities (role/permission/userRole) as CMS collections"
```

---

## Task 2: `EffectivePermissions` snapshot model

**Files:**
- Create: `src/Struo.Application/Security/EffectivePermissions.cs`
- Test: `tests/Struo.Tests/Identity/EffectivePermissionsTests.cs`

**Interfaces:**
- Produces: `EffectivePermissions` with static `DenyAll`, ctor `EffectivePermissions(bool isSuperAdmin, IReadOnlyDictionary<string,(bool read, bool write, bool delete)> byCollection)`, property `bool IsSuperAdmin`, methods `bool CanRead(string)`, `bool CanWrite(string)`, `bool CanDelete(string)` (case-insensitive collection lookup; super-admin short-circuits to true; absent collection → false).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Identity/EffectivePermissionsTests.cs
using FluentAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class EffectivePermissionsTests
{
    [Fact]
    public void DenyAll_denies_every_operation()
    {
        var p = EffectivePermissions.DenyAll;
        p.CanRead("article").Should().BeFalse();
        p.CanWrite("article").Should().BeFalse();
        p.CanDelete("article").Should().BeFalse();
        p.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void SuperAdmin_allows_every_operation_on_any_collection()
    {
        var p = new EffectivePermissions(isSuperAdmin: true,
            new Dictionary<string, (bool, bool, bool)>());
        p.CanRead("anything").Should().BeTrue();
        p.CanWrite("anything").Should().BeTrue();
        p.CanDelete("anything").Should().BeTrue();
    }

    [Fact]
    public void Looks_up_per_collection_case_insensitively_and_defaults_deny()
    {
        var p = new EffectivePermissions(isSuperAdmin: false,
            new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = (true, true, false)
            });
        p.CanRead("ARTICLE").Should().BeTrue();
        p.CanWrite("article").Should().BeTrue();
        p.CanDelete("article").Should().BeFalse();
        p.CanRead("user").Should().BeFalse(); // absent → deny
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter EffectivePermissionsTests`
Expected: FAIL — `EffectivePermissions` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Struo.Application/Security/EffectivePermissions.cs
namespace Struo.Application.Security;

/// <summary>
/// The caller's resolved permissions for the current request. Super-admin short-circuits every
/// check; otherwise a per-collection grant is consulted and an absent collection is denied.
/// </summary>
public sealed class EffectivePermissions(
    bool isSuperAdmin,
    IReadOnlyDictionary<string, (bool Read, bool Write, bool Delete)> byCollection)
{
    /// <summary>Empty snapshot: not super-admin, no grants (denies everything). The safe default.</summary>
    public static EffectivePermissions DenyAll { get; } =
        new(false, new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase));

    public bool IsSuperAdmin { get; } = isSuperAdmin;

    public bool CanRead(string collection) => Check(collection, g => g.Read);
    public bool CanWrite(string collection) => Check(collection, g => g.Write);
    public bool CanDelete(string collection) => Check(collection, g => g.Delete);

    private bool Check(string collection, Func<(bool Read, bool Write, bool Delete), bool> pick) =>
        IsSuperAdmin || (byCollection.TryGetValue(collection, out var g) && pick(g));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter EffectivePermissionsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Security/EffectivePermissions.cs tests/Struo.Tests/Identity/EffectivePermissionsTests.cs
git commit -m "feat: EffectivePermissions per-request snapshot model"
```

---

## Task 3: `IRolePermissionStore` port + `PermissionResolver`

**Files:**
- Create: `src/Struo.Application/Security/IRolePermissionStore.cs`
- Create: `src/Struo.Application/Security/PermissionResolver.cs`
- Test: `tests/Struo.Tests/Identity/PermissionResolverTests.cs`

**Interfaces:**
- Consumes: `EffectivePermissions` (Task 2).
- Produces:
  - `record RoleRow(Guid Id, string Name, bool IsSuperAdmin)`
  - `record PermissionRow(Guid RoleId, string Collection, bool CanRead, bool CanWrite, bool CanDelete)`
  - `record RolePermissionData(IReadOnlyList<RoleRow> Roles, IReadOnlyList<PermissionRow> Permissions)`
  - `interface IRolePermissionStore { Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default); }`
  - `static class PermissionResolver { static EffectivePermissions Resolve(RolePermissionData data); }` — any `IsSuperAdmin` role → super snapshot; else union (OR) of all permission rows across the data's roles.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Identity/PermissionResolverTests.cs
using FluentAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class PermissionResolverTests
{
    [Fact]
    public void Any_super_admin_role_yields_super_snapshot()
    {
        var data = new RolePermissionData(
            [new RoleRow(Guid.NewGuid(), "admin", true)],
            []);
        PermissionResolver.Resolve(data).IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void Unions_permission_rows_across_multiple_roles()
    {
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var data = new RolePermissionData(
            [new RoleRow(r1, "editor", false), new RoleRow(r2, "publisher", false)],
            [
                new PermissionRow(r1, "article", true, true, false),
                new PermissionRow(r2, "article", false, false, true)
            ]);

        var p = PermissionResolver.Resolve(data);
        p.IsSuperAdmin.Should().BeFalse();
        p.CanRead("article").Should().BeTrue();
        p.CanWrite("article").Should().BeTrue();
        p.CanDelete("article").Should().BeTrue(); // unioned from role 2
        p.CanRead("user").Should().BeFalse();
    }

    [Fact]
    public void Empty_data_denies_everything()
    {
        var p = PermissionResolver.Resolve(new RolePermissionData([], []));
        p.IsSuperAdmin.Should().BeFalse();
        p.CanRead("article").Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter PermissionResolverTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Struo.Application/Security/IRolePermissionStore.cs
namespace Struo.Application.Security;

public sealed record RoleRow(Guid Id, string Name, bool IsSuperAdmin);

public sealed record PermissionRow(
    Guid RoleId, string Collection, bool CanRead, bool CanWrite, bool CanDelete);

public sealed record RolePermissionData(
    IReadOnlyList<RoleRow> Roles, IReadOnlyList<PermissionRow> Permissions);

/// <summary>
/// Loads the RBAC data for a caller, querying the DB <b>directly</b> and bypassing the generic
/// projection / permission gating — the query that resolves the gate must never itself be gated
/// (mirrors <see cref="IUserCredentialStore"/>). <paramref name="userId"/> null = anonymous, which
/// loads the <c>public</c> role's grants.
/// </summary>
public interface IRolePermissionStore
{
    Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default);
}
```

```csharp
// src/Struo.Application/Security/PermissionResolver.cs
namespace Struo.Application.Security;

/// <summary>Folds raw role/permission rows into an <see cref="EffectivePermissions"/> snapshot.</summary>
public static class PermissionResolver
{
    public static EffectivePermissions Resolve(RolePermissionData data)
    {
        if (data.Roles.Any(r => r.IsSuperAdmin))
            return new EffectivePermissions(isSuperAdmin: true,
                new Dictionary<string, (bool, bool, bool)>());

        var byCollection = new Dictionary<string, (bool Read, bool Write, bool Delete)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.Permissions)
        {
            byCollection.TryGetValue(p.Collection, out var cur);
            byCollection[p.Collection] =
                (cur.Read || p.CanRead, cur.Write || p.CanWrite, cur.Delete || p.CanDelete);
        }
        return new EffectivePermissions(isSuperAdmin: false, byCollection);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter PermissionResolverTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Security/IRolePermissionStore.cs src/Struo.Application/Security/PermissionResolver.cs tests/Struo.Tests/Identity/PermissionResolverTests.cs
git commit -m "feat: IRolePermissionStore port + PermissionResolver (super short-circuit, multi-role union)"
```

---

## Task 4: `SqlSugarRolePermissionStore`

**Files:**
- Create: `src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs`
- Test: `tests/Struo.Tests/Identity/SqlSugarRolePermissionStoreTests.cs`

**Interfaces:**
- Consumes: `IRolePermissionStore`/`RolePermissionData`/`RoleRow`/`PermissionRow` (Task 3), entities `Role`/`Permission`/`UserRole`/`User` (Task 1, 6a), `ISqlSugarClient`.
- Produces: `SqlSugarRolePermissionStore(ISqlSugarClient db) : IRolePermissionStore`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Identity/SqlSugarRolePermissionStoreTests.cs
using FluentAssertions;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class SqlSugarRolePermissionStoreTests
{
    private static ISqlSugarClient NewDb(SqliteTestDatabase file)
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables(typeof(User), typeof(Role), typeof(Permission), typeof(UserRole));
        return db;
    }

    [Fact]
    public async Task Anonymous_loads_public_role_permissions()
    {
        using var file = new SqliteTestDatabase();
        var db = NewDb(file);
        var pub = new Role { Id = Guid.CreateVersion7(), Name = "public" };
        await db.Insertable(pub).ExecuteCommandAsync();
        await db.Insertable(new Permission
        {
            Id = Guid.CreateVersion7(), RoleId = pub.Id, Collection = "article", CanRead = true
        }).ExecuteCommandAsync();

        var store = new SqlSugarRolePermissionStore(db);
        var data = await store.LoadForUserAsync(null);

        data.Roles.Should().ContainSingle(r => r.Name == "public");
        data.Permissions.Should().ContainSingle(p => p.Collection == "article" && p.CanRead);
    }

    [Fact]
    public async Task User_loads_only_assigned_roles_and_their_permissions()
    {
        using var file = new SqliteTestDatabase();
        var db = NewDb(file);
        var userId = Guid.CreateVersion7();
        var editor = new Role { Id = Guid.CreateVersion7(), Name = "editor" };
        var other = new Role { Id = Guid.CreateVersion7(), Name = "other" };
        await db.Insertable(new[] { editor, other }).ExecuteCommandAsync();
        await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, RoleId = editor.Id }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = editor.Id, Collection = "article", CanWrite = true }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = other.Id, Collection = "user", CanRead = true }).ExecuteCommandAsync();

        var store = new SqlSugarRolePermissionStore(db);
        var data = await store.LoadForUserAsync(userId);

        data.Roles.Should().ContainSingle(r => r.Name == "editor");
        data.Permissions.Should().ContainSingle(p => p.Collection == "article" && p.CanWrite);
        data.Permissions.Should().NotContain(p => p.Collection == "user"); // other role not assigned
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter SqlSugarRolePermissionStoreTests`
Expected: FAIL — `SqlSugarRolePermissionStore` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs
using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarRolePermissionStore(ISqlSugarClient db) : IRolePermissionStore
{
    public async Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default)
    {
        List<Role> roles = userId is null
            ? await db.Queryable<Role>().Where(r => r.Name == "public").ToListAsync(ct)
            : await db.Queryable<UserRole>()
                .InnerJoin<Role>((ur, r) => ur.RoleId == r.Id)
                .Where((ur, r) => ur.UserId == userId.Value)
                .Select((ur, r) => r)
                .ToListAsync(ct);

        if (roles.Count == 0)
            return new RolePermissionData([], []);

        var roleIds = roles.Select(r => r.Id).ToList();
        var perms = await db.Queryable<Permission>()
            .Where(p => roleIds.Contains(p.RoleId))
            .ToListAsync(ct);

        return new RolePermissionData(
            roles.Select(r => new RoleRow(r.Id, r.Name, r.IsSuperAdmin)).ToList(),
            perms.Select(p => new PermissionRow(p.RoleId, p.Collection, p.CanRead, p.CanWrite, p.CanDelete)).ToList());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter SqlSugarRolePermissionStoreTests`
Expected: PASS.

> If `ToListAsync(ct)` overload is unavailable in the pinned SqlSugar version, use `.ToListAsync()` (the credential store precedent uses `.FirstAsync(ct)`; `ToListAsync` accepts a `CancellationToken` in current SqlSugarCore). Keep the build warnings-as-errors clean.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs tests/Struo.Tests/Identity/SqlSugarRolePermissionStoreTests.cs
git commit -m "feat: SqlSugarRolePermissionStore (direct-query RBAC loader, bypasses gating)"
```

---

## Task 5: `ICurrentPermissions` holder + `RbacPermissionService`

**Files:**
- Create: `src/Struo.Application/Security/ICurrentPermissions.cs`
- Create: `src/Struo.Application/Security/RbacPermissionService.cs`
- Test: `tests/Struo.Tests/Identity/RbacPermissionServiceTests.cs`

**Interfaces:**
- Consumes: `EffectivePermissions` (Task 2), `IPermissionService` (`src/Struo.Application/Security/IPermissionService.cs`).
- Produces:
  - `interface ICurrentPermissions { EffectivePermissions Current { get; } void Set(EffectivePermissions permissions); }`
  - `sealed class CurrentPermissions : ICurrentPermissions` (defaults to `EffectivePermissions.DenyAll`).
  - `sealed class RbacPermissionService(ICurrentPermissions current) : IPermissionService` — delegates `CanRead/CanWrite/CanDelete` to the snapshot; `ReadableFields` returns all fields (YAGNI).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Identity/RbacPermissionServiceTests.cs
using FluentAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class RbacPermissionServiceTests
{
    [Fact]
    public void Delegates_to_the_current_snapshot()
    {
        var holder = new CurrentPermissions();
        holder.Set(new EffectivePermissions(false,
            new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = (true, false, false)
            }));
        var svc = new RbacPermissionService(holder);

        svc.CanRead("article").Should().BeTrue();
        svc.CanWrite("article").Should().BeFalse();
        svc.CanRead("user").Should().BeFalse();
    }

    [Fact]
    public void Default_holder_denies_until_set()
    {
        var svc = new RbacPermissionService(new CurrentPermissions());
        svc.CanRead("article").Should().BeFalse();
    }

    [Fact]
    public void ReadableFields_returns_all_fields_for_now()
    {
        var svc = new RbacPermissionService(new CurrentPermissions());
        svc.ReadableFields("article", ["title", "body"]).Should().BeEquivalentTo(["title", "body"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter RbacPermissionServiceTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Struo.Application/Security/ICurrentPermissions.cs
namespace Struo.Application.Security;

/// <summary>Scoped holder for the current request's resolved permissions. Populated once per
/// request by the resolution middleware; read by <see cref="RbacPermissionService"/> and by
/// controllers that need an admin check.</summary>
public interface ICurrentPermissions
{
    EffectivePermissions Current { get; }
    void Set(EffectivePermissions permissions);
}

public sealed class CurrentPermissions : ICurrentPermissions
{
    public EffectivePermissions Current { get; private set; } = EffectivePermissions.DenyAll;
    public void Set(EffectivePermissions permissions) => Current = permissions;
}
```

```csharp
// src/Struo.Application/Security/RbacPermissionService.cs
namespace Struo.Application.Security;

/// <summary>
/// Real RBAC policy: reads the per-request <see cref="ICurrentPermissions"/> snapshot. Keeps the
/// synchronous, ambient <see cref="IPermissionService"/> signature so <c>ItemService</c> is unchanged.
/// Replaces <c>AllowAllPermissionService</c>. Field-level control is out of scope (Phase 6b §1).
/// </summary>
public sealed class RbacPermissionService(ICurrentPermissions current) : IPermissionService
{
    public bool CanRead(string collection) => current.Current.CanRead(collection);
    public bool CanWrite(string collection) => current.Current.CanWrite(collection);
    public bool CanDelete(string collection) => current.Current.CanDelete(collection);

    public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
        allFieldNames.ToList();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter RbacPermissionServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Security/ICurrentPermissions.cs src/Struo.Application/Security/RbacPermissionService.cs tests/Struo.Tests/Identity/RbacPermissionServiceTests.cs
git commit -m "feat: ICurrentPermissions scoped holder + RbacPermissionService"
```

---

## Task 6: `PermissionDeniedException` + `ItemService` denial swap

**Files:**
- Create: `src/Struo.Domain/Query/PermissionDeniedException.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs` (lines 35, 63, 258, 273, 476 — the five `CanRead/CanWrite/CanDelete` guards)
- Test: `tests/Struo.Tests/Query/ItemServicePermissionTests.cs`

**Interfaces:**
- Consumes: `IPermissionService` (existing), `PermissionDeniedException`.
- Produces: `sealed class PermissionDeniedException(string message) : Exception(message)`. `ItemService` throws it on every denied read/write/delete (replacing the prior `QueryException("… not permitted")`). The 401-vs-403 decision is NOT made here — the exception handler (Task 7) decides from `HttpContext.User`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Query/ItemServicePermissionTests.cs
using FluentAssertions;
using Struo.Application.Security;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServicePermissionTests
{
    // A permission service that denies reads on a specific collection.
    private sealed class DenyReadPermissions : IPermissionService
    {
        public bool CanRead(string collection) => collection != "secret";
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    [Fact]
    public async Task Denied_read_throws_PermissionDeniedException()
    {
        var svc = ItemServiceTestHarness.Build(permissions: new DenyReadPermissions());
        var act = async () => await svc.QueryAsync("secret", new Struo.Domain.Query.QueryModel());
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }
}
```

> **Note for the implementer:** `ItemServiceTestHarness.Build(...)` stands in for the existing pattern that `tests/Struo.Tests/Query/ItemServiceTests.cs` uses to construct an `ItemService` with fakes. Reuse that harness; if it does not yet accept a `permissions:` override, add an optional parameter defaulting to `AllowAllPermissionService` so other tests are unaffected. **Read `ItemServiceTests.cs` first and mirror its construction exactly** (its constructor takes many collaborators); do not invent a new harness or a new `ItemService` ctor.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter ItemServicePermissionTests`
Expected: FAIL — `PermissionDeniedException` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Struo.Domain/Query/PermissionDeniedException.cs
namespace Struo.Domain.Query;

/// <summary>
/// Thrown when the caller's resolved permissions deny an operation. The HTTP status (401 for an
/// anonymous caller, 403 for an authenticated one) is decided by the API exception handler using
/// <c>HttpContext.User</c> — not here — so this type stays free of any web dependency.
/// </summary>
public sealed class PermissionDeniedException(string message) : Exception(message);
```

In `src/Struo.Application/Query/ItemService.cs`, replace each denial throw:

```csharp
// QueryAsync (was line ~35)
if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
// GetAsync (was line ~63)
if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
// CreateAsync (was line ~258)
if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
// UpdateAsync (was line ~273)
if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
// DeleteAsync (was line ~476)
if (!permissions.CanDelete(collection)) throw new PermissionDeniedException("Delete not permitted.");
```

`PermissionDeniedException` is in `Struo.Domain.Query` (same namespace as `QueryException`), already imported in `ItemService.cs` via `using Struo.Domain.Query;` — no new using needed.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter ItemServicePermissionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Query/PermissionDeniedException.cs src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/ItemServicePermissionTests.cs
git commit -m "feat: PermissionDeniedException; ItemService throws it on denied ops"
```

---

## Task 7: Wire RBAC into the pipeline (the behavior flip)

This task swaps the live permission service, adds the resolution middleware, the seeder, config, table creation, and the 401/403 mapping — then keeps the **existing** suite green by giving the test admin the super-admin role and granting `public` read for test content collections. It is the single point where behavior changes from allow-all to RBAC.

**Files:**
- Modify: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs:21`
- Modify: `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Create: `src/Struo.Infrastructure/Identity/RbacSeeder.cs`
- Create: `src/Struo.Api/Auth/PermissionResolutionMiddleware.cs`
- Modify: `src/Struo.Api/Program.cs`
- Modify: `src/Struo.Api/appsettings.json`
- Modify: `tests/Struo.Tests/Support/ApiFactory.cs`

**Interfaces:**
- Consumes: `IRolePermissionStore` (Task 4), `ICurrentPermissions`/`CurrentPermissions`/`RbacPermissionService` (Task 5), `PermissionResolver` (Task 3), `ICurrentUserAccessor` (6a), `PermissionDeniedException` (Task 6).
- Produces: `RbacSeeder.SeedAsync(ISqlSugarClient db, string? bootstrapAdminEmail, IEnumerable<string> publicReadCollections, CancellationToken ct = default)`; `PermissionResolutionMiddleware`; live DI graph with `RbacPermissionService` (Scoped) + `ICurrentPermissions` (Scoped) + `IRolePermissionStore` (Scoped).

- [ ] **Step 1: Register services (scoped) + the seeder + middleware**

In `DataServiceCollectionExtensions.cs`, replace line 21:

```csharp
// was: services.AddSingleton<IPermissionService, AllowAllPermissionService>();
services.AddScoped<IPermissionService, RbacPermissionService>();
services.AddScoped<ICurrentPermissions, CurrentPermissions>();
```

In `ServiceCollectionExtensions.cs`, after the `IUserCredentialStore` registration (line ~20) add:

```csharp
services.AddScoped<Struo.Application.Security.IRolePermissionStore, Identity.SqlSugarRolePermissionStore>();
```

> `AllowAllPermissionService` stays in the codebase (it is still referenced by unit-test harnesses as the default) but is no longer in the live DI graph. Do not delete it. `RbacPermissionService` must be **Scoped** (not Singleton) because it depends on the scoped `ICurrentPermissions`; `ItemService` already resolves as scoped (it depends on the scoped repository), so this is consistent.

Create the seeder:

```csharp
// src/Struo.Infrastructure/Identity/RbacSeeder.cs
using SqlSugar;

namespace Struo.Infrastructure.Identity;

/// <summary>Dev-only, idempotent. Seeds the <c>admin</c> (super) and <c>public</c> roles, assigns
/// the bootstrap admin user to <c>admin</c>, and grants the <c>public</c> role read on each
/// configured collection. The collection list is config (never a <c>samples/*</c> reference).</summary>
public static class RbacSeeder
{
    public static async Task SeedAsync(
        ISqlSugarClient db, string? bootstrapAdminEmail,
        IEnumerable<string> publicReadCollections, CancellationToken ct = default)
    {
        var admin = await db.Queryable<Role>().Where(r => r.Name == "admin").FirstAsync(ct);
        if (admin is null)
        {
            admin = new Role { Id = Guid.CreateVersion7(), Name = "admin", IsSuperAdmin = true, Description = "Full access" };
            await db.Insertable(admin).ExecuteCommandAsync(ct);
        }

        var pub = await db.Queryable<Role>().Where(r => r.Name == "public").FirstAsync(ct);
        if (pub is null)
        {
            pub = new Role { Id = Guid.CreateVersion7(), Name = "public", IsSuperAdmin = false, Description = "Anonymous callers" };
            await db.Insertable(pub).ExecuteCommandAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail))
        {
            var u = await db.Queryable<User>().Where(x => x.Email == bootstrapAdminEmail).FirstAsync(ct);
            if (u is not null)
            {
                var hasRole = await db.Queryable<UserRole>()
                    .Where(ur => ur.UserId == u.Id && ur.RoleId == admin.Id).AnyAsync(ct);
                if (!hasRole)
                    await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = u.Id, RoleId = admin.Id })
                        .ExecuteCommandAsync(ct);
            }
        }

        foreach (var coll in publicReadCollections)
        {
            var exists = await db.Queryable<Permission>()
                .Where(p => p.RoleId == pub.Id && p.Collection == coll).AnyAsync(ct);
            if (!exists)
                await db.Insertable(new Permission
                {
                    Id = Guid.CreateVersion7(), RoleId = pub.Id, Collection = coll, CanRead = true
                }).ExecuteCommandAsync(ct);
        }
    }
}
```

Create the middleware:

```csharp
// src/Struo.Api/Auth/PermissionResolutionMiddleware.cs
using Struo.Application.Abstractions;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Resolves the caller's effective permissions ONCE per request (after authentication)
/// into the scoped <see cref="ICurrentPermissions"/> snapshot. One DB load per request.</summary>
public sealed class PermissionResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserAccessor currentUser,
        IRolePermissionStore store,
        ICurrentPermissions current)
    {
        var data = await store.LoadForUserAsync(currentUser.GetCurrentUserId(), context.RequestAborted);
        current.Set(PermissionResolver.Resolve(data));
        await next(context);
    }
}
```

- [ ] **Step 2: Program.cs — tables, middleware, seeder, exception mapping**

In `src/Struo.Api/Program.cs`:

(a) Add the three entities to the dev `InitTables` list (lines 102-106):

```csharp
DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment,
    typeof(Article), typeof(ArticleTranslation), typeof(Category),
    typeof(Struo.Infrastructure.Localization.Language),
    typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Files.FileTranslation),
    typeof(User),
    typeof(Struo.Infrastructure.Identity.Role),
    typeof(Struo.Infrastructure.Identity.Permission),
    typeof(Struo.Infrastructure.Identity.UserRole));
```

(b) Register the middleware immediately after `app.UseAuthorization();` (line 51), before the exception-handling `app.Use(...)` block at line 53:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<Struo.Api.Auth.PermissionResolutionMiddleware>();
```

(c) Add a `PermissionDeniedException` catch inside the exception-handling block, before the `QueryException` catch. Decide 401 vs 403 from the authenticated state:

```csharp
catch (Struo.Domain.Query.PermissionDeniedException ex)
{
    if (!context.Response.HasStarted)
    {
        context.Response.StatusCode = context.User.Identity?.IsAuthenticated == true
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = new { message = ex.Message } });
    }
}
```

(d) Invoke `RbacSeeder` in the dev seeder block, immediately after the `AdminUserSeeder.SeedAsync(...)` call (line ~109):

```csharp
await Struo.Infrastructure.Identity.RbacSeeder.SeedAsync(db,
    builder.Configuration["Auth:BootstrapAdmin:Email"],
    builder.Configuration.GetSection("Rbac:PublicReadCollections").Get<string[]>() ?? []);
```

- [ ] **Step 3: appsettings — document the config key**

Add to `src/Struo.Api/appsettings.json` a placeholder (the dev host can populate it; for the blog demo set it to the public content collections):

```json
"Rbac": {
  "PublicReadCollections": []
}
```

- [ ] **Step 4: Keep the existing suite green — update ApiFactory**

The seeded test admin must be a super-admin (existing authenticated tests perform writes that now require permission), and anonymous read tests need `public` grants. In `tests/Struo.Tests/Support/ApiFactory.cs`:

(a) In `ConfigureWebHost`, add `Rbac:PublicReadCollections` to the in-memory config (lines 28-34) covering the content collections read anonymously by the existing suite. **Read `tests/Struo.Tests/Api/*` first** to confirm which collections are read without logging in; start with the content collections:

```csharp
["Database:DbType"] = "Sqlite",
["Database:ConnectionString"] = _db.ConnectionString,
["Struo:Files:Backend"] = "local",
["Struo:Files:Local:RootPath"] = FilesRoot,
["Rbac:PublicReadCollections:0"] = "article",
["Rbac:PublicReadCollections:1"] = "category",
["Rbac:PublicReadCollections:2"] = "file",
["Rbac:PublicReadCollections:3"] = "language",
```

(b) In `CreateAuthenticatedClientAsync`, after the admin `User` is ensured (lines 50-60) and `AdminUserId` is set, ensure the `admin` super-role exists and the admin user holds it:

```csharp
var adminRole = await db.Queryable<Role>().Where(r => r.Name == "admin").FirstAsync();
if (adminRole is null)
{
    adminRole = new Role { Id = Guid.CreateVersion7(), Name = "admin", IsSuperAdmin = true };
    await db.Insertable(adminRole).ExecuteCommandAsync();
}
var linked = await db.Queryable<UserRole>()
    .Where(ur => ur.UserId == AdminUserId && ur.RoleId == adminRole.Id).AnyAsync();
if (!linked)
    await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = AdminUserId, RoleId = adminRole.Id })
        .ExecuteCommandAsync();
```

> Add `using Struo.Infrastructure.Identity;` to `ApiFactory.cs` if not already present. The RBAC tables must exist in the test DB: confirm `ApiFactory`'s startup uses the app's dev `InitTables` (Step 2a) — if so, `roles`/`permissions`/`user_roles` are created automatically. If the factory uses a separate explicit `InitTables` list, add the three types there too.

- [ ] **Step 5: Run the FULL suite to verify it is green**

Run: `dotnet build` then `dotnet test tests/Struo.Tests`
Expected: build clean (warnings-as-errors); **all existing tests pass** plus the unit tests from Tasks 1-6. If a previously-anonymous read test fails with 401, add its collection to the `Rbac:PublicReadCollections` test config (Step 4a). If an authenticated write fails with 403, confirm Step 4b linked the admin role.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs src/Struo.Infrastructure/Identity/RbacSeeder.cs src/Struo.Api/Auth/PermissionResolutionMiddleware.cs src/Struo.Api/Program.cs src/Struo.Api/appsettings.json tests/Struo.Tests/Support/ApiFactory.cs
git commit -m "feat: wire RBAC (resolution middleware + RbacPermissionService + seeder + 401/403 mapping)"
```

---

## Task 8: RBAC enforcement integration tests

**Files:**
- Modify: `tests/Struo.Tests/Support/ApiFactory.cs` (add `CreateEditorClientAsync` helper)
- Test: `tests/Struo.Tests/Api/RbacEnforcementTests.cs`

**Interfaces:**
- Consumes: `ApiFactory` (`CreateClient()` for anonymous, `CreateAuthenticatedClientAsync()` for super-admin), the seeded `admin`/`public` roles and entities.
- Produces: `ApiFactory.CreateEditorClientAsync(string[] readCollections, string[] writeCollections) → Task<(HttpClient client, Guid userId)>`.

> **Implementer note:** Read an existing test in `tests/Struo.Tests/Api/` first to mirror `ApiFactory` usage, route shapes (`/api/items/{collection}`), and JSON assertion helpers. If `article`/`category` are not the content collections registered in the test host, substitute ones that are (confirm via an existing items test).

- [ ] **Step 1: Add the `CreateEditorClientAsync` helper to `ApiFactory`**

Add a helper that seeds a non-super role with the given grants, a fresh user, the user-role link, and returns a logged-in client + the user id. Mirror `CreateAuthenticatedClientAsync`'s scope/login pattern:

```csharp
// in tests/Struo.Tests/Support/ApiFactory.cs
public async Task<(HttpClient client, Guid userId)> CreateEditorClientAsync(
    string[] readCollections, string[] writeCollections)
{
    Guid userId;
    const string email = "editor@struo.test";
    const string password = "editor-pw-123";
    using (var scope = Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
        userId = Guid.CreateVersion7();
        await db.Insertable(new User { Id = userId, Email = email, Password = hasher.Hash(password), Name = "Editor", IsActive = true }).ExecuteCommandAsync();
        var role = new Role { Id = Guid.CreateVersion7(), Name = $"editor-{userId:N}" };
        await db.Insertable(role).ExecuteCommandAsync();
        await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, RoleId = role.Id }).ExecuteCommandAsync();
        foreach (var c in readCollections.Union(writeCollections).Distinct())
            await db.Insertable(new Permission
            {
                Id = Guid.CreateVersion7(), RoleId = role.Id, Collection = c,
                CanRead = readCollections.Contains(c), CanWrite = writeCollections.Contains(c)
            }).ExecuteCommandAsync();
    }
    var client = CreateClient();
    var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
    resp.EnsureSuccessStatusCode();
    return (client, userId);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Struo.Tests/Api/RbacEnforcementTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public class RbacEnforcementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anonymous_can_read_a_public_granted_collection()
    {
        var client = factory.CreateClient(); // anonymous → public role
        (await client.GetAsync("/api/items/article")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_reading_an_ungranted_collection_is_401()
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/api/items/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuperAdmin_can_read_any_collection()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        (await client.GetAsync("/api/items/user")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authenticated_editor_writing_an_ungranted_collection_is_403()
    {
        var (client, _) = await factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: []);
        var resp = await client.PostAsJsonAsync("/api/items/category", new { name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Struo.Tests --filter RbacEnforcementTests`
Expected: all four assertions PASS against the wired pipeline.

- [ ] **Step 4: Commit**

```bash
git add tests/Struo.Tests/Api/RbacEnforcementTests.cs tests/Struo.Tests/Support/ApiFactory.cs
git commit -m "test: RBAC enforcement (anonymous public read, deny, super-admin, editor 403)"
```

---

## Task 9: `UsersController` admin gate + self `currentPassword`; remove `ProtectedCollections`

**Files:**
- Modify: `src/Struo.Api/Controllers/UsersController.cs`
- Modify: `src/Struo.Api/Controllers/ItemsController.cs` (remove `GuardProtected` method + its call sites)
- Delete: `src/Struo.Api/Auth/ProtectedCollections.cs`
- Test: `tests/Struo.Tests/Api/UsersControllerRbacTests.cs`

**Interfaces:**
- Consumes: `ICurrentPermissions` (Task 5), `ICurrentUserAccessor` (6a), `IPasswordHasher`, `IUserCredentialStore`, `ISqlSugarClient`, `CreateEditorClientAsync`/`CreateAuthenticatedClientAsync` (Task 8/factory).
- Produces: admin-gated `POST /api/users`, `POST|DELETE /api/users/{id}/access-token`, and `PUT /api/users/{id}/password` for other users; self password-change verifies `currentPassword`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Api/UsersControllerRbacTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public class UsersControllerRbacTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task NonAdmin_creating_a_user_is_403()
    {
        var (client, _) = await factory.CreateEditorClientAsync(readCollections: [], writeCollections: []);
        var resp = await client.PostAsJsonAsync("/api/users",
            new { email = "new@struo.test", password = "password-123", name = "N" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_creating_a_user_is_201()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/users",
            new { email = "fresh@struo.test", password = "password-123", name = "Fresh" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Self_password_change_with_wrong_currentPassword_is_401()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123", currentPassword = "WRONG" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Self_password_change_with_correct_currentPassword_is_204()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123", currentPassword = "editor-pw-123" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter UsersControllerRbacTests`
Expected: FAIL — current `UsersController` does not gate on role or verify `currentPassword`.

- [ ] **Step 3: Implement the controller changes**

Replace the constructor and actions in `src/Struo.Api/Controllers/UsersController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Abstractions;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Controllers;

public sealed record CreateUserRequest(string Email, string Password, string? Name);
public sealed record ChangePasswordRequest(string NewPassword, string? CurrentPassword);

[ApiController]
[Route("api/users")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class UsersController(
    ISqlSugarClient db, IPasswordHasher hasher, IUserCredentialStore store,
    ICurrentPermissions permissions, ICurrentUserAccessor currentUser) : ControllerBase
{
    private const int MinPasswordLength = 8;

    private IActionResult? RequireAdmin() =>
        permissions.Current.IsSuperAdmin
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { error = new { message = "Admin role required." } });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest body, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(body.Email))
            return BadRequest(new { error = new { message = "Email is required." } });
        if (body.Password is null || body.Password.Length < MinPasswordLength)
            return BadRequest(new { error = new { message = $"Password must be at least {MinPasswordLength} characters." } });
        if (await store.FindByEmailAsync(body.Email, ct) is not null)
            return Conflict(new { error = new { message = "Email already in use." } });

        var id = Guid.CreateVersion7();
        await db.Insertable(new User
        {
            Id = id, Email = body.Email, Password = hasher.Hash(body.Password), Name = body.Name, IsActive = true
        }).ExecuteCommandAsync(ct);
        return Created($"/api/items/user/{id}", new { data = new { id, email = body.Email, name = body.Name } });
    }

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest body, CancellationToken ct)
    {
        if (body.NewPassword is null || body.NewPassword.Length < MinPasswordLength)
            return BadRequest(new { error = new { message = $"Password must be at least {MinPasswordLength} characters." } });

        var isSelf = currentUser.GetCurrentUserId() is { } me && me == id;
        if (!isSelf)
        {
            if (RequireAdmin() is { } denied) return denied; // changing another user → admin only
        }
        else
        {
            // Self-service: must prove knowledge of the current password.
            var existing = await db.Queryable<User>().Where(u => u.Id == id).FirstAsync(ct);
            if (existing is null) return NotFound();
            if (string.IsNullOrEmpty(body.CurrentPassword) || !hasher.Verify(existing.Password, body.CurrentPassword))
                return Unauthorized(new { error = new { message = "Current password is incorrect." } });
        }

        var updated = await db.Updateable<User>()
            .SetColumns(u => u.Password == hasher.Hash(body.NewPassword))
            .Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/access-token")]
    public async Task<IActionResult> GenerateToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var (token, hash) = AccessTokenHasher.Generate();
        var updated = await db.Updateable<User>()
            .SetColumns(u => u.AccessToken == hash).Where(u => u.Id == id).ExecuteCommandAsync(ct);
        if (updated == 0) return NotFound();
        return Ok(new { data = new { token } }); // shown once
    }

    [HttpDelete("{id:guid}/access-token")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var updated = await db.Updateable<User>()
            .SetColumns(u => u.AccessToken == null).Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }
}
```

> `IPasswordHasher.Verify(encoded, password)` is the existing 6a signature (`src/Struo.Application/Security/IPasswordHasher.cs`). Confirm the parameter order by reading that file before using it.

- [ ] **Step 4: Remove the obsolete read guard**

In `src/Struo.Api/Controllers/ItemsController.cs`, delete the `GuardProtected` method (lines ~48-51) and every `if (GuardProtected(collection) is { } denied...) return ...;` call at the start of the read actions. Reads now flow through `ItemService` → `IPermissionService.CanRead`, which denies the `user` collection for callers without a grant (→ 401/403 via Task 7's handler). Then delete the now-unused file:

```bash
git rm src/Struo.Api/Auth/ProtectedCollections.cs
```

- [ ] **Step 5: Run tests + full suite**

Run: `dotnet build` then `dotnet test tests/Struo.Tests`
Expected: `UsersControllerRbacTests` PASS; the former `ProtectedCollections` behavior is now covered by the anonymous `user` read → 401 (Task 8). Full suite green.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Api/Controllers/UsersController.cs src/Struo.Api/Controllers/ItemsController.cs tests/Struo.Tests/Api/UsersControllerRbacTests.cs
git rm src/Struo.Api/Auth/ProtectedCollections.cs
git commit -m "feat: UsersController admin gate + self currentPassword; remove ProtectedCollections (subsumed by RBAC)"
```

---

## Task 10: Live Postgres verification gate

**Files:** none (verification only; capture evidence).

> Memory: *SQLite green ≠ Postgres correct*. This gate runs the real host against live Postgres (and Redis from 6a). Use PowerShell `Invoke-RestMethod` / a UTF-8 file for any non-ASCII payloads (the Git Bash console here is Big5).

- [ ] **Step 1: Start the host against live Postgres**

Configure `Database:DbType=PostgreSQL` + connection string, `Auth:BootstrapAdmin:{Email,Password}`, and `Rbac:PublicReadCollections` (e.g. `["article","category"]`) in dev config/env, then run:

Run: `dotnet run --project src/Struo.Api`
Expected: startup clean; dev `InitTables` creates `roles`, `permissions`, `user_roles`.

- [ ] **Step 2: Verify schema + seed (evidence)**

Inspect the live DB (psql or the items API):
- `roles`, `permissions`, `user_roles` tables exist with the expected columns and unique indexes (`uq_roles_name`, `uq_permissions_role_collection`, `uq_user_roles_user_role`).
- A `roles` row `admin` (`is_super_admin = true`) and `public` exist.
- The bootstrap admin user has a `user_roles` row linking it to `admin`.

Capture the column/index listing and the seeded rows as evidence.

- [ ] **Step 3: Verify resolution round-trip (evidence)**

- Log in as the bootstrap admin (cookie) → `GET /api/items/user` → 200 (super-admin reads a system collection).
- Anonymous `GET /api/items/article` (a configured public-read collection) → 200.
- Anonymous `GET /api/items/user` → 401.
- Create a non-super role + a user + permission rows via the API; log in as that user; confirm a granted write succeeds and an ungranted write returns 403.

Capture request/response status lines as evidence.

- [ ] **Step 4: Update the roadmap**

Mark Phase 6b done in `docs/ROADMAP.md` (status ✅, link this plan), mirroring the 6a row format.

- [ ] **Step 5: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: mark Phase 6b done (live PG verified) + link plan"
```

---

## Self-Review

**Spec coverage** (against `2026-06-30-phase6b-rbac-design.md`):
- §3 data model → Task 1. §4 Application ports/pure → Tasks 2, 3, 5. §4 Infrastructure → Tasks 1, 4, 7 (seeder). §4 Api middleware/DI → Task 7. §5 enforcement + 401/403 + ItemService swap → Tasks 6, 7; `UsersController` closure → Task 9. §6 data flow → Task 7 (middleware). §7 edge cases → Tasks 2, 3, 8. §8 tests → Tasks 2-9. §9 migration (InitTables) → Task 7. §10 risks (chicken-and-egg store, behavior-change seeding, default-deny) → Tasks 4, 7. §12 acceptance → Tasks 7 (suite green), 10 (live PG).
- **Removed `ProtectedCollections`** (§1) → Task 9. **`AllowAllPermissionService` out of live graph** (§12) → Task 7.

**Placeholder scan:** No "TBD"/"implement later". The three confirm-by-reading notes (the `ItemServiceTestHarness` shape in Task 6, the `IPasswordHasher.Verify` order in Task 9, the anonymous-read collection set in Tasks 7/8) each name the exact file and the specific decision — they are "read this file first" instructions, not unspecified work.

**Type consistency:** `EffectivePermissions(bool, IReadOnlyDictionary<string,(bool,bool,bool)>)`, `IsSuperAdmin`, `CanRead/CanWrite/CanDelete` consistent across Tasks 2/3/5. `RolePermissionData(Roles, Permissions)` / `RoleRow(Id,Name,IsSuperAdmin)` / `PermissionRow(RoleId,Collection,CanRead,CanWrite,CanDelete)` consistent across Tasks 3/4. `ICurrentPermissions.Current`/`.Set(...)` consistent across Tasks 5/7/9. `PermissionDeniedException(string)` consistent across Tasks 6/7. `RbacSeeder.SeedAsync` (Task 7) and `CreateEditorClientAsync` (Task 8) signatures consistent where consumed (Tasks 8/9).

**Decision recorded vs spec §11:** The 401-vs-403 choice lives in the exception handler (reads `HttpContext.User`), so `PermissionDeniedException` carries no anonymity flag — this realizes spec §5 ("mapped in the global exception handler") and supersedes §11's tentative "exception carries anonymity". An authenticated user with no roles does **not** inherit `public` (store queries only assigned roles) — matches spec §7/§11.
