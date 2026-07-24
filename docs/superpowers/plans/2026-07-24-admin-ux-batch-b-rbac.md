# Admin UX Batch B — RBAC Management UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 問題 3 + 4 — permissions become editable as a matrix inside the Role form; a user's roles become a readable, editable TagSelect on the User form (plus a read-only effective-permissions preview); `Permission`/`UserRole`/`File` leave the sidebar.

**Architecture:** Backend: `User.Roles` M2M navigation (same pattern as `Article.Tags`), a `Hidden` collection flag through attribute→metadata→schema JSON, `GET/PUT /api/roles/{id}/permissions` (full-replace, transactional), `GET /api/users/{id}/effective-permissions` (reuses `PermissionResolver` so the preview matches real authorization). Frontend: `PermissionMatrix` + `EffectivePermissionsPanel` components mounted by `ItemFormView` for `role`/`user`, `buildNav` filters `hidden`. Spec: `docs/superpowers/specs/2026-07-23-admin-ux-batch-b-rbac-design.md`.

**Tech Stack:** .NET 10 / SqlSugarCore / xUnit + AwesomeAssertions (SQLite for tests, live PG gate); Vue 3 + TypeScript + PrimeVue + vitest (pnpm).

## Global Constraints

- Work on branch `admin-ux-batch-b` (created from `main` before Task 1).
- Implementation subagents run on **Sonnet**; per-task and final reviews run on **Fable 5** (user directive).
- Backend gate: `dotnet test` from repo root stays green (~850 tests). Frontend gates: `pnpm test` AND `pnpm build` from `frontend/` pass.
- All DB access via SqlSugar ORM; zero vendor SQL. No schema change in this batch (`User.Roles` is `IsIgnore`; `permissions`/`user_roles` tables already exist) — `db/migrations/001-core-baseline.sql` is untouched.
- i18n: every new user-facing string needs keys in BOTH `frontend/src/locales/en.ts` and `frontend/src/locales/zh-TW.ts`.
- Commit messages: conventional commits, **no attribution footer**.
- Do not touch `frontend/vite.config.ts` (uncommitted user changes) or `docs/struo-cms-frontend-design/`.
- API tests: use the `ApiFactory` integration fixture (`tests/Struo.Tests/Support/ApiFactory.cs`); its clients already attach the CSRF header, so writes just work. `[Collection("ApiIntegration")]` on every new API test class.
- Envelope: all `/api/*` responses are auto-wrapped `{success,data}` / `{success:false,error}` by the 9a filters — controllers return `Ok(payload)` / `ApiResults.Fail(...)` and never build envelopes by hand.
- If an existing test fails because it asserts an exhaustive field/relation list for the `user` collection or exact schema JSON, extend the assertion additively; never weaken new behavior.

---

### Task 1: `Hidden` collection flag, end to end (spec §1b)

**Files:**
- Modify: `src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs`
- Modify: `src/Struo.Domain/Metadata/Models/CollectionMetadata.cs`
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs:185-198` (collection mapping — the `Hidden` at lines 288/370 is the *field-level* flag; don't confuse them)
- Modify: `src/Struo.Infrastructure/Identity/Permission.cs:14`, `src/Struo.Infrastructure/Identity/UserRole.cs:16`, `src/Struo.Infrastructure/Files/File.cs:15`
- Test: `tests/Struo.Tests/Identity/RbacEntitiesMetadataTests.cs` (extend), `tests/Struo.Tests/Api/SchemaEndpointTests.cs` (extend)

**Interfaces:**
- Produces: `CmsCollectionAttribute.Hidden: bool` (default false), `CollectionMetadata.Hidden: bool`, serialized as `hidden` by `GET /api/schema`. Task 5 (frontend `buildNav`) consumes the JSON `hidden` field.

- [ ] **Step 1: Write the failing tests**

In `tests/Struo.Tests/Identity/RbacEntitiesMetadataTests.cs` add (match the file's existing usings; `MetadataScanner.ScanTypes` is the same helper the file's neighbors use):

```csharp
    // Batch B (問題 3/4): Permission and UserRole are implementation details behind the Role
    // permission matrix / User.Roles TagSelect; File's admin surface is the media library.
    [Fact]
    public void Permission_UserRole_and_File_collections_are_hidden_from_nav()
    {
        var metas = MetadataScanner.ScanTypes(
            [typeof(Permission), typeof(UserRole), typeof(Struo.Infrastructure.Files.File)]);
        metas.Should().OnlyContain(m => m.Hidden);
    }

    [Fact]
    public void Role_and_User_collections_stay_visible()
    {
        var metas = MetadataScanner.ScanTypes([typeof(Role), typeof(User)]);
        metas.Should().OnlyContain(m => !m.Hidden);
    }
```

In `tests/Struo.Tests/Api/SchemaEndpointTests.cs` add (adapt to the file's existing client/JSON helpers — the assertions are what matters):

```csharp
    [Fact]
    public async Task Schema_serializes_the_hidden_collection_flag()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var json = await (await client.GetAsync("/api/schema")).Content.ReadFromJsonAsync<JsonElement>();
        var collections = json.GetProperty("data").EnumerateArray().ToList();

        collections.First(c => c.GetProperty("name").GetString() == "permission")
            .GetProperty("hidden").GetBoolean().Should().BeTrue();
        collections.First(c => c.GetProperty("name").GetString() == "role")
            .GetProperty("hidden").GetBoolean().Should().BeFalse();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~RbacEntitiesMetadataTests|FullyQualifiedName~SchemaEndpointTests"`
Expected: FAIL to compile (`Hidden` doesn't exist on `CollectionMetadata`) — compile failure is the RED state.

- [ ] **Step 3: Implement**

`CmsCollectionAttribute.cs` — add after the `AdminOnly` property:

```csharp
    /// <summary>
    /// When true, the collection is omitted from the admin sidebar/nav. It stays fully reachable
    /// via REST/GraphQL and direct admin URLs — this is a presentation flag, not an access rule.
    /// Used for collections whose admin surface lives elsewhere (File → media library) or that are
    /// implementation details behind a dedicated editor (Permission/UserRole → Role permission
    /// matrix, User.Roles TagSelect).
    /// </summary>
    public bool Hidden { get; set; }
```

`CollectionMetadata.cs` — add below `AdminOnly` (line 16):

```csharp
    /// <summary>Omit from the admin sidebar/nav (presentation only). See CmsCollectionAttribute.Hidden.</summary>
    public bool Hidden { get; init; }
```

`MetadataScanner.cs` — in the `new CollectionMetadata` initializer (line ~185), after `AdminOnly = attr.AdminOnly,` add:

```csharp
            Hidden = attr.Hidden,
```

Entity attributes:
- `Permission.cs:14` → `[CmsCollection("Permission", Group = "System", DefaultDisplayField = nameof(Collection), AdminOnly = true, Hidden = true)]`
- `UserRole.cs:16` → `[CmsCollection("UserRole", Group = "System", DefaultDisplayField = nameof(UserId), AdminOnly = true, Hidden = true)]`
- `File.cs:15` → `[CmsCollection("File", Group = "System", DefaultDisplayField = nameof(FileName), Hidden = true)]`

- [ ] **Step 4: Run the new tests, then the full backend suite**

Run: `dotnet test --filter "FullyQualifiedName~RbacEntitiesMetadataTests|FullyQualifiedName~SchemaEndpointTests"` → PASS.
Run: `dotnet test` → all green.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs src/Struo.Domain/Metadata/Models/CollectionMetadata.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs src/Struo.Infrastructure/Identity/Permission.cs src/Struo.Infrastructure/Identity/UserRole.cs src/Struo.Infrastructure/Files/File.cs tests/Struo.Tests/Identity/RbacEntitiesMetadataTests.cs tests/Struo.Tests/Api/SchemaEndpointTests.cs
git commit -m "feat(metadata): Hidden collection flag; hide Permission/UserRole/File from nav metadata"
```

---

### Task 2: `User.Roles` M2M TagSelect (spec §1a, 問題 4)

**Files:**
- Modify: `src/Struo.Infrastructure/Identity/User.cs` (after the `AccessToken` property, before the token-lifecycle comment block)
- Test: `tests/Struo.Tests/Api/UserRolesRelationTests.cs` (create)

**Interfaces:**
- Consumes: existing M2M machinery (`ItemWriteSideSync` → `SyncManyToManyAsync`), proven by `Article.Tags`. `UserRole` is `IAuditable`, so `AuditAop` stamps its audit columns on the junction inserts; the junction PK fills the same way `ArticleTag`'s does today.
- Produces: schema for `user` gains relation `roles` (target `role`, interface `tagSelect`, `displayTemplate "{Name}"`, editable) — the generic item form renders it with no frontend work. Task 4's tests assign roles through this path. Items API payload shape: `{"roles": ["<roleId>", ...]}`.

- [ ] **Step 1: Write the failing integration test**

Create `tests/Struo.Tests/Api/UserRolesRelationTests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class UserRolesRelationTests(ApiFactory factory)
{
    private static async Task<Guid> CreateRoleAsync(HttpClient admin, string name)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/role", new { name });
        resp.IsSuccessStatusCode.Should().BeTrue($"role create failed: {await resp.Content.ReadAsStringAsync()}");
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateUserAsync(HttpClient admin)
    {
        var resp = await admin.PostAsJsonAsync("/api/users",
            new { email = $"m2m-{Guid.NewGuid():N}@struo.test", password = "password-123", name = "M2M" });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("id").GetGuid();
    }

    // 問題 4: roles are assigned on the User form via TagSelect, not by hand-crafting userRole rows.
    [Fact]
    public async Task Admin_sets_user_roles_through_the_generic_item_path_and_reads_names_back()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin, $"batch-b-{Guid.NewGuid():N}");
        var userId = await CreateUserAsync(admin);

        // Echo the optimistic-concurrency token if the item carries one (D2 convention).
        var item = (await admin.GetFromJsonAsync<JsonElement>($"/api/items/user/{userId}")).GetProperty("data");
        var body = item.TryGetProperty("version", out var v)
            ? (object)new { roles = new[] { roleId }, version = v.GetInt32() }
            : new { roles = new[] { roleId } };
        var update = await admin.PutAsJsonAsync($"/api/items/user/{userId}", body);
        update.IsSuccessStatusCode.Should().BeTrue($"update failed: {await update.Content.ReadAsStringAsync()}");

        var after = (await admin.GetFromJsonAsync<JsonElement>($"/api/items/user/{userId}")).GetProperty("data");
        var roles = after.GetProperty("roles").EnumerateArray().ToList();
        roles.Should().ContainSingle();
        roles[0].GetProperty("id").GetGuid().Should().Be(roleId);
        roles[0].GetProperty("name").GetString().Should().StartWith("batch-b-");
    }

    [Fact]
    public async Task NonAdmin_cannot_write_user_roles()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin, $"batch-b-{Guid.NewGuid():N}");
        var userId = await CreateUserAsync(admin);

        var (editor, _) = await factory.CreateEditorClientAsync(readCollections: [], writeCollections: []);
        var resp = await editor.PutAsJsonAsync($"/api/items/user/{userId}", new { roles = new[] { roleId } });
        ((int)resp.StatusCode).Should().Be(403);
    }

    [Fact]
    public async Task Schema_exposes_the_roles_relation_on_user()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var json = await admin.GetFromJsonAsync<JsonElement>("/api/schema");
        var user = json.GetProperty("data").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "user");
        var roles = user.GetProperty("relations").EnumerateArray()
            .First(r => r.GetProperty("name").GetString() == "roles");
        roles.GetProperty("targetCollection").GetString().Should().Be("role");
    }
}
```

NOTE for implementer: adapt the JSON access to reality if the update path rejects the version-less body or the GET shape differs (check a passing test in `ItemsEndpointTests.cs` for the exact envelope/version conventions) — keep the three behavioral assertions (M2M write round-trip with readable name; 403 for non-admin; schema relation).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~UserRolesRelationTests"`
Expected: FAIL — `roles` property missing from schema/user item (`InvalidOperationException` from `GetProperty("roles")` / `First(...)`).

- [ ] **Step 3: Implement**

`User.cs` — add after the `AccessToken` property (all needed usings — `SqlSugar`, `Struo.Domain.Metadata.Attributes`, `Struo.Domain.Metadata.Enums` — are already imported):

```csharp
    // 問題 4: roles are edited on the User form as a TagSelect (readable names), not by
    // hand-crafting userRole junction rows. Same M2M pattern as the sample's Article.Tags.
    [Navigate(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<Role> Roles { get; set; } = [];
```

- [ ] **Step 4: Run the new tests, then the full backend suite**

Run: `dotnet test --filter "FullyQualifiedName~UserRolesRelationTests"` → PASS (3/3).
Run: `dotnet test` → all green. GraphQL schema/user-collection tests that assert exhaustive member lists may need an additive `roles` entry — extend those assertions; do not remove the relation.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Identity/User.cs tests/Struo.Tests/Api/UserRolesRelationTests.cs
git commit -m "feat(rbac): User.Roles M2M TagSelect — roles editable with readable names on the User form"
```

---

### Task 3: Role permissions endpoints (spec §1c, 問題 3)

**Files:**
- Create: `src/Struo.Api/Controllers/RolesController.cs`
- Test: `tests/Struo.Tests/Api/RolePermissionsEndpointTests.cs` (create)

**Interfaces:**
- Consumes: `IMetadataProvider.GetCollection(string)` (null for unknown names), `ICurrentPermissions`, `ISqlSugarClient`, `Permission`/`Role` entities.
- Produces: `GET/PUT /api/roles/{id:guid}/permissions`. Wire DTO `RolePermissionEntry(string Collection, bool CanRead, bool CanWrite, bool CanDelete)` — JSON `{collection, canRead, canWrite, canDelete}`. PUT = full replace (delete-all + insert inside one transaction; observably identical to upsert+prune and simpler). All-false rows never stored. Task 6's `rbacApi` mirrors these shapes.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Api/RolePermissionsEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class RolePermissionsEndpointTests(ApiFactory factory)
{
    private static async Task<Guid> CreateRoleAsync(HttpClient admin)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/role", new { name = $"perm-{Guid.NewGuid():N}" });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static List<(string Coll, bool R, bool W, bool D)> Entries(JsonElement data) =>
        data.EnumerateArray().Select(e => (
            e.GetProperty("collection").GetString()!,
            e.GetProperty("canRead").GetBoolean(),
            e.GetProperty("canWrite").GetBoolean(),
            e.GetProperty("canDelete").GetBoolean())).ToList();

    [Fact]
    public async Task NonAdmin_is_403()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var (editor, _) = await factory.CreateEditorClientAsync([], []);
        (await editor.GetAsync($"/api/roles/{roleId}/permissions")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_role_is_404()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.GetAsync($"/api/roles/{Guid.NewGuid()}/permissions")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // 問題 3: the matrix PUTs the whole grant set; unsent rows disappear, all-false rows are absent.
    [Fact]
    public async Task Put_replaces_the_full_grant_set_and_drops_all_false_rows()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);

        var put1 = await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions", new[]
        {
            new { collection = "article", canRead = true, canWrite = true, canDelete = false },
            new { collection = "tag", canRead = true, canWrite = false, canDelete = false },
        });
        put1.IsSuccessStatusCode.Should().BeTrue($"PUT failed: {await put1.Content.ReadAsStringAsync()}");

        var got = await admin.GetFromJsonAsync<JsonElement>($"/api/roles/{roleId}/permissions");
        Entries(got.GetProperty("data")).Should().BeEquivalentTo(
        [
            ("article", true, true, false),
            ("tag", true, false, false),
        ]);

        // Replace: tag vanishes; category comes in all-false and must not be stored.
        await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions", new[]
        {
            new { collection = "article", canRead = true, canWrite = false, canDelete = false },
            new { collection = "category", canRead = false, canWrite = false, canDelete = false },
        });
        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/roles/{roleId}/permissions");
        Entries(after.GetProperty("data")).Should().BeEquivalentTo([("article", true, false, false)]);
    }

    [Fact]
    public async Task Put_with_unknown_collection_is_400_and_changes_nothing()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions",
            new[] { new { collection = "article", canRead = true, canWrite = false, canDelete = false } });

        var bad = await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions",
            new[] { new { collection = "no-such-collection", canRead = true, canWrite = false, canDelete = false } });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var got = await admin.GetFromJsonAsync<JsonElement>($"/api/roles/{roleId}/permissions");
        Entries(got.GetProperty("data")).Should().BeEquivalentTo([("article", true, false, false)]);
    }

    [Fact]
    public async Task Put_with_duplicate_collection_is_400()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var bad = await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions", new[]
        {
            new { collection = "article", canRead = true, canWrite = false, canDelete = false },
            new { collection = "Article", canRead = false, canWrite = true, canDelete = false },
        });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~RolePermissionsEndpointTests"`
Expected: FAIL — `GET /api/roles/{id}/permissions` is 404-without-envelope (no route), so status/shape assertions break.

- [ ] **Step 3: Implement RolesController**

Create `src/Struo.Api/Controllers/RolesController.cs`:

```csharp
// src/Struo.Api/Controllers/RolesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from HotChocolate.ErrorCodes
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Controllers;

/// <summary>Wire DTO for one grant row of the Role permission matrix (問題 3).</summary>
public sealed record RolePermissionEntry(string Collection, bool CanRead, bool CanWrite, bool CanDelete);

[ApiController]
[Route("api/roles")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class RolesController(
    ISqlSugarClient db, IMetadataProvider metadata, ICurrentPermissions permissions) : ControllerBase
{
    private IActionResult? RequireAdmin() =>
        permissions.Current.IsSuperAdmin
            ? null
            : ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Admin role required.");

    [HttpGet("{id:guid}/permissions")]
    public async Task<IActionResult> GetPermissions(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (!await db.Queryable<Role>().Where(r => r.Id == id).AnyAsync(ct))
            return ApiResults.Fail(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Role not found.");

        var rows = await db.Queryable<Permission>().Where(p => p.RoleId == id).ToListAsync(ct);
        return Ok(rows.Select(ToEntry).OrderBy(e => e.Collection, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Full-replace of the role's grant set (the matrix always PUTs everything it knows).
    /// Implemented as delete-all + insert-all in one transaction — observably identical to
    /// upsert+prune and simpler; Permission rows are hidden implementation detail, so their
    /// identity is not part of any contract. Rows whose three flags are all false carry no grant
    /// and are treated as absent (deleted, never stored).
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> PutPermissions(
        Guid id, [FromBody] List<RolePermissionEntry>? body, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (body is null)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                "Body must be a JSON array of permission entries.");
        if (!await db.Queryable<Role>().Where(r => r.Id == id).AnyAsync(ct))
            return ApiResults.Fail(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Role not found.");

        // Duplicates checked on the raw body (an all-false duplicate still signals a client bug).
        var dupes = body.GroupBy(e => e.Collection, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Duplicate collection entries: {string.Join(", ", dupes)}.");

        var unknown = body.Where(e => string.IsNullOrWhiteSpace(e.Collection)
                                      || metadata.GetCollection(e.Collection) is null)
            .Select(e => e.Collection).Distinct().ToList();
        if (unknown.Count > 0)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Unknown collections: {string.Join(", ", unknown)}.");

        var rows = body
            .Where(e => e.CanRead || e.CanWrite || e.CanDelete)
            .Select(e => new Permission
            {
                Id = Guid.CreateVersion7(), RoleId = id, Collection = e.Collection,
                CanRead = e.CanRead, CanWrite = e.CanWrite, CanDelete = e.CanDelete,
            })
            .ToList();

        // BeginTranAsync has no CancellationToken overload (same note as SqlSugarItemRepository).
        try
        {
            await db.Ado.BeginTranAsync();
            await db.Deleteable<Permission>().Where(p => p.RoleId == id).ExecuteCommandAsync(ct);
            if (rows.Count > 0) await db.Insertable(rows).ExecuteCommandAsync(ct);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        return Ok(rows.Select(ToEntry).OrderBy(e => e.Collection, StringComparer.Ordinal).ToList());
    }

    private static RolePermissionEntry ToEntry(Permission p) =>
        new(p.Collection, p.CanRead, p.CanWrite, p.CanDelete);
}
```

NOTE for implementer: confirm `IMetadataProvider` is the interface `SchemaService` consumes (`GetCollection`/`GetCollections`) and is DI-registered — it is (SchemaService resolves today); if the lookup is named differently, adjust the two call sites only.

- [ ] **Step 4: Run the new tests, then the full backend suite**

Run: `dotnet test --filter "FullyQualifiedName~RolePermissionsEndpointTests"` → PASS (5/5).
Run: `dotnet test` → all green.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/RolesController.cs tests/Struo.Tests/Api/RolePermissionsEndpointTests.cs
git commit -m "feat(rbac): GET/PUT /api/roles/{id}/permissions — transactional full-replace grant set for the Role matrix"
```

---

### Task 4: Effective-permissions endpoint (spec §1d) — after Tasks 2+3

**Files:**
- Modify: `src/Struo.Api/Controllers/UsersController.cs`
- Test: `tests/Struo.Tests/Api/EffectivePermissionsEndpointTests.cs` (create)

**Interfaces:**
- Consumes: `IRolePermissionStore.LoadForUserAsync(Guid?, ct)` + `PermissionResolver.Resolve` (the exact per-request resolution pair), `SchemaService.GetAll()` for the collection universe (same projection idiom as `AuthController.Me`). Role assignment via Task 2's `{"roles":[...]}` payload; grants via Task 3's PUT.
- Produces: `GET /api/users/{id:guid}/effective-permissions` → `data: { isSuperAdmin, permissions: { [collection]: { read, write, delete } } }`. Task 6's `rbacApi.getEffectivePermissions` mirrors this.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Api/EffectivePermissionsEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class EffectivePermissionsEndpointTests(ApiFactory factory)
{
    private static async Task<Guid> CreateRoleWithGrantAsync(
        HttpClient admin, string collection, bool read, bool write)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/role", new { name = $"eff-{Guid.NewGuid():N}" });
        var roleId = (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
        await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions",
            new[] { new { collection, canRead = read, canWrite = write, canDelete = false } });
        return roleId;
    }

    private static async Task<Guid> CreateUserWithRolesAsync(HttpClient admin, Guid[] roleIds)
    {
        var resp = await admin.PostAsJsonAsync("/api/users",
            new { email = $"eff-{Guid.NewGuid():N}@struo.test", password = "password-123", name = "Eff" });
        var userId = (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
        if (roleIds.Length > 0)
        {
            var item = (await admin.GetFromJsonAsync<JsonElement>($"/api/items/user/{userId}")).GetProperty("data");
            var body = item.TryGetProperty("version", out var v)
                ? (object)new { roles = roleIds, version = v.GetInt32() }
                : new { roles = roleIds };
            (await admin.PutAsJsonAsync($"/api/items/user/{userId}", body)).IsSuccessStatusCode.Should().BeTrue();
        }
        return userId;
    }

    [Fact]
    public async Task NonAdmin_is_403_and_unknown_user_is_404()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (editor, editorId) = await factory.CreateEditorClientAsync([], []);
        (await editor.GetAsync($"/api/users/{editorId}/effective-permissions")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/users/{Guid.NewGuid()}/effective-permissions")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // 問題 4: the User-form preview must OR-merge grants across roles exactly like real authz.
    [Fact]
    public async Task Multi_role_grants_are_or_merged()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var readRole = await CreateRoleWithGrantAsync(admin, "article", read: true, write: false);
        var writeRole = await CreateRoleWithGrantAsync(admin, "article", read: false, write: true);
        var userId = await CreateUserWithRolesAsync(admin, [readRole, writeRole]);

        var data = (await admin.GetFromJsonAsync<JsonElement>($"/api/users/{userId}/effective-permissions"))
            .GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeFalse();
        var article = data.GetProperty("permissions").GetProperty("article");
        article.GetProperty("read").GetBoolean().Should().BeTrue();
        article.GetProperty("write").GetBoolean().Should().BeTrue();
        article.GetProperty("delete").GetBoolean().Should().BeFalse();
    }

    // A role-less user inherits the public floor (read-only) — the preview must show that truth.
    [Fact]
    public async Task Roleless_user_shows_the_public_floor()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserWithRolesAsync(admin, []);
        var data = (await admin.GetFromJsonAsync<JsonElement>($"/api/users/{userId}/effective-permissions"))
            .GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeFalse();
        foreach (var p in data.GetProperty("permissions").EnumerateObject())
        {
            p.Value.GetProperty("write").GetBoolean().Should().BeFalse("public floor is read-only");
            p.Value.GetProperty("delete").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public async Task SuperAdmin_user_reports_isSuperAdmin_with_empty_map()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var data = (await admin.GetFromJsonAsync<JsonElement>(
                $"/api/users/{factory.AdminUserId}/effective-permissions"))
            .GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeTrue();
        data.GetProperty("permissions").EnumerateObject().Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~EffectivePermissionsEndpointTests"`
Expected: FAIL — no such route.

- [ ] **Step 3: Implement**

In `UsersController.cs` add (below the password endpoint; `SchemaService` comes from `Struo.Application.Metadata` — add the using):

```csharp
    /// <summary>
    /// Read-only preview for the User form (問題 4). Reuses the exact per-request resolution pair
    /// (IRolePermissionStore + PermissionResolver), so the preview is by construction identical to
    /// real authorization — including the public-role floor for role-less users and the super-admin
    /// short-circuit. Projection mirrors AuthController.Me: probe each schema collection.
    /// </summary>
    [HttpGet("{id:guid}/effective-permissions")]
    public async Task<IActionResult> GetEffectivePermissions(
        Guid id,
        [FromServices] IRolePermissionStore rolePermissions,
        [FromServices] SchemaService schema,
        CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (!await db.Queryable<User>().Where(u => u.Id == id).AnyAsync(ct))
            return ApiResults.Fail(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "User not found.");

        var eff = PermissionResolver.Resolve(await rolePermissions.LoadForUserAsync(id, ct));
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!eff.IsSuperAdmin)
        {
            foreach (var c in schema.GetAll())
            {
                var read = eff.CanRead(c.Name);
                var write = eff.CanWrite(c.Name);
                var del = eff.CanDelete(c.Name);
                if (read || write || del)
                    map[c.Name] = new { read, write, @delete = del };
            }
        }
        return Ok(new { isSuperAdmin = eff.IsSuperAdmin, permissions = map });
    }
```

`IRolePermissionStore` is in `Struo.Application.Security` (already imported by the file).

- [ ] **Step 4: Run the new tests, then the full backend suite**

Run: `dotnet test --filter "FullyQualifiedName~EffectivePermissionsEndpointTests"` → PASS (4/4).
Run: `dotnet test` → all green.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/UsersController.cs tests/Struo.Tests/Api/EffectivePermissionsEndpointTests.cs
git commit -m "feat(rbac): GET /api/users/{id}/effective-permissions — preview via the real resolver"
```

---

### Task 5: Frontend schema types + nav hides `hidden` collections (spec §2c)

**Files:**
- Modify: `frontend/src/types/schema.ts` (the `CollectionMeta` type, ~line 40)
- Modify: `frontend/src/lib/buildNav.ts:13-15`
- Test: `frontend/src/lib/buildNav.test.ts` (extend)

**Interfaces:**
- Consumes: Task 1's `hidden` + existing `adminOnly` JSON fields on `/api/schema` items.
- Produces: `CollectionMeta` gains `adminOnly?: boolean; hidden?: boolean` (optional — existing test seeds don't set them). Tasks 7/8 read `adminOnly` off `CollectionMeta`.

- [ ] **Step 1: Write the failing tests**

In `frontend/src/lib/buildNav.test.ts` add (reuse the file's existing seed helpers/shape):

```ts
  it('hidden collections never appear in nav, even for super-admins', () => {
    const collections = [
      { name: 'article', label: 'Article', fields: [], relations: [] },
      { name: 'permission', label: 'Permission', hidden: true, fields: [], relations: [] },
    ] as any
    const nav = buildNav(collections, true, {})
    const names = nav.flatMap((g) => g.items.map((i) => i.name))
    expect(names).toContain('article')
    expect(names).not.toContain('permission')
  })

  it('file visibility is driven by the hidden flag, not a hardcoded name', () => {
    const collections = [
      { name: 'file', label: 'File', fields: [], relations: [] }, // not hidden -> visible
    ] as any
    const nav = buildNav(collections, true, {})
    expect(nav.flatMap((g) => g.items.map((i) => i.name))).toContain('file')
  })
```

- [ ] **Step 2: Run tests to verify they fail**

Run from `frontend/`: `pnpm vitest run src/lib/buildNav.test.ts`
Expected: FAIL — `permission` shows up; `file` stays hidden by the hardcode.

- [ ] **Step 3: Implement**

`types/schema.ts` — in `CollectionMeta`, after `revisions?: boolean` add:

```ts
  adminOnly?: boolean // writes always require super-admin (matrix disables write/delete for these)
  hidden?: boolean // Batch B: omit from the sidebar; still reachable via API/direct URL
```

`buildNav.ts` — replace the `readable` filter:

```ts
  const readable = collections.filter(
    // Batch B: visibility is metadata-driven (CmsCollection Hidden flag) — no hardcoded names.
    (c) => !c.hidden && (isSuperAdmin || permissions[c.name]?.read === true),
  )
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pnpm vitest run src/lib/buildNav.test.ts` → PASS. Then `pnpm test` + `pnpm build` → green.
If an existing buildNav test asserted the `file` hardcode, update it to seed `hidden: true` instead.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/types/schema.ts frontend/src/lib/buildNav.ts frontend/src/lib/buildNav.test.ts
git commit -m "feat(admin-ui): nav visibility driven by the schema hidden flag (permission/userRole/file leave the sidebar)"
```

---

### Task 6: `rbacApi` module + `rbac` i18n namespace (spec §2d)

**Files:**
- Create: `frontend/src/api/rbacApi.ts`
- Create: `frontend/src/api/rbacApi.test.ts`
- Modify: `frontend/src/locales/en.ts`, `frontend/src/locales/zh-TW.ts` (new top-level `rbac` block)

**Interfaces:**
- Consumes: `apiClient.get<T>(path)` / `apiClient.put<T>(path, body)`; Task 3/4 endpoints.
- Produces (Tasks 7/8 import these exact names):

```ts
export type RolePermissionEntry = { collection: string; canRead: boolean; canWrite: boolean; canDelete: boolean }
export type EffectivePermissions = {
  isSuperAdmin: boolean
  permissions: Record<string, { read: boolean; write: boolean; delete: boolean }>
}
rbacApi.getRolePermissions(roleId: string): Promise<RolePermissionEntry[]>
rbacApi.putRolePermissions(roleId: string, entries: RolePermissionEntry[]): Promise<RolePermissionEntry[]>
rbacApi.getEffectivePermissions(userId: string): Promise<EffectivePermissions>
```

- [ ] **Step 1: Write the failing test**

Create `frontend/src/api/rbacApi.test.ts` (mirror `settingsApi.test.ts`'s mocking of `apiClient`):

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { rbacApi } from './rbacApi'

vi.mock('./apiClient', () => ({
  apiClient: { get: vi.fn(), put: vi.fn() },
}))

describe('rbacApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('getRolePermissions hits /roles/{id}/permissions', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([])
    await rbacApi.getRolePermissions('r1')
    expect(apiClient.get).toHaveBeenCalledWith('/roles/r1/permissions')
  })

  it('putRolePermissions PUTs the entries array', async () => {
    vi.mocked(apiClient.put).mockResolvedValue([])
    const entries = [{ collection: 'article', canRead: true, canWrite: false, canDelete: false }]
    await rbacApi.putRolePermissions('r1', entries)
    expect(apiClient.put).toHaveBeenCalledWith('/roles/r1/permissions', entries)
  })

  it('getEffectivePermissions hits /users/{id}/effective-permissions', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ isSuperAdmin: false, permissions: {} })
    await rbacApi.getEffectivePermissions('u1')
    expect(apiClient.get).toHaveBeenCalledWith('/users/u1/effective-permissions')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm vitest run src/api/rbacApi.test.ts`
Expected: FAIL — module `./rbacApi` doesn't exist.

- [ ] **Step 3: Implement**

Create `frontend/src/api/rbacApi.ts`:

```ts
import { apiClient } from './apiClient'

// Wire shapes of the Batch B RBAC endpoints (RolesController / UsersController).
export type RolePermissionEntry = {
  collection: string
  canRead: boolean
  canWrite: boolean
  canDelete: boolean
}

export type EffectivePermissions = {
  isSuperAdmin: boolean
  permissions: Record<string, { read: boolean; write: boolean; delete: boolean }>
}

export const rbacApi = {
  getRolePermissions(roleId: string): Promise<RolePermissionEntry[]> {
    return apiClient.get<RolePermissionEntry[]>(`/roles/${roleId}/permissions`)
  },
  // Full-replace: send every grant the matrix holds; unsent collections lose their grants.
  putRolePermissions(roleId: string, entries: RolePermissionEntry[]): Promise<RolePermissionEntry[]> {
    return apiClient.put<RolePermissionEntry[]>(`/roles/${roleId}/permissions`, entries)
  },
  getEffectivePermissions(userId: string): Promise<EffectivePermissions> {
    return apiClient.get<EffectivePermissions>(`/users/${userId}/effective-permissions`)
  },
}
```

Locales — `en.ts`, add a top-level `rbac` block (after `revisions`):

```ts
  rbac: {
    matrixTitle: 'Permissions',
    colCollection: 'Collection',
    colRead: 'Read',
    colWrite: 'Write',
    colDelete: 'Delete',
    save: 'Save permissions',
    saved: 'Permissions saved',
    loadFailed: 'Failed to load permissions',
    saveFailed: 'Failed to save permissions',
    superAdminAll: 'This role is a super admin and has full access to everything.',
    adminOnlyWriteHint: 'Writes to this collection always require a super admin',
    effectiveTitle: 'Effective permissions',
    effectiveSuperAdmin: 'This user is a super admin and has full access to everything.',
    effectiveEmpty: 'No permissions',
    effectiveLoadFailed: 'Failed to load effective permissions',
  },
```

`zh-TW.ts`, same position:

```ts
  rbac: {
    matrixTitle: '權限',
    colCollection: '集合',
    colRead: '讀取',
    colWrite: '寫入',
    colDelete: '刪除',
    save: '儲存權限',
    saved: '權限已儲存',
    loadFailed: '權限載入失敗',
    saveFailed: '權限儲存失敗',
    superAdminAll: '此角色為超級管理員，擁有所有權限。',
    adminOnlyWriteHint: '此集合的寫入永遠需要超級管理員',
    effectiveTitle: '有效權限',
    effectiveSuperAdmin: '此使用者為超級管理員，擁有所有權限。',
    effectiveEmpty: '沒有任何權限',
    effectiveLoadFailed: '有效權限載入失敗',
  },
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pnpm vitest run src/api/rbacApi.test.ts` → PASS. Then `pnpm test` + `pnpm build` → green (a locale-parity test exists; both files got the same keys).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/rbacApi.ts frontend/src/api/rbacApi.test.ts frontend/src/locales/en.ts frontend/src/locales/zh-TW.ts
git commit -m "feat(admin-ui): rbacApi client + rbac i18n namespace"
```

---

### Task 7: `PermissionMatrix` component (spec §2a) — after Tasks 5+6

**Files:**
- Create: `frontend/src/components/rbac/PermissionMatrix.vue`
- Create: `frontend/src/components/rbac/PermissionMatrix.test.ts`

**Interfaces:**
- Consumes: `rbacApi` (Task 6), `useSchemaStore().collections` (`CollectionMeta.adminOnly` from Task 5), `unsavedConfirm` from `lib/formDirty`.
- Produces: `<PermissionMatrix :role-id="string" :is-super-admin-role="boolean" />`. No emits. Task 8 mounts it.
- Constraint: do NOT mount a `<ConfirmDialog>` inside — `ItemFormView` already provides one; a second instance duplicates dialogs (FE-R7 lesson). `confirm.require(...)` alone is correct.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/rbac/PermissionMatrix.test.ts` (pinia + PrimeVue mount pattern as in `MediaDetailDialog.test.ts` — reuse its plugin/stub arrangement; `PrimeVue`, `ToastService`, `ConfirmationService` plugins are needed):

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import PermissionMatrix from './PermissionMatrix.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi } from '../../api/rbacApi'

vi.mock('../../api/rbacApi', () => ({
  rbacApi: { getRolePermissions: vi.fn(), putRolePermissions: vi.fn() },
}))
vi.mock('vue-router', () => ({ onBeforeRouteLeave: vi.fn() }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountMatrix(props: { roleId?: string; isSuperAdminRole?: boolean } = {}) {
  return mount(PermissionMatrix, {
    props: { roleId: props.roleId ?? 'r1', isSuperAdminRole: props.isSuperAdminRole ?? false },
    global: { plugins: [PrimeVue, ToastService, ConfirmationService, i18n] },
  })
}

function seedSchema() {
  useSchemaStore().collections = [
    { name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] },
    { name: 'user', label: 'User', group: 'System', adminOnly: true, fields: [], relations: [] },
  ] as any
}

describe('PermissionMatrix', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([
      { collection: 'article', canRead: true, canWrite: false, canDelete: false },
    ])
  })

  it('renders one row per collection with grants from the API', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    expect(w.text()).toContain('Article')
    expect(w.text()).toContain('User')
    expect(rbacApi.getRolePermissions).toHaveBeenCalledWith('r1')
  })

  it('disables write/delete checkboxes for adminOnly collections', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    const userRow = w.findAll('tbody tr').find((tr) => tr.text().includes('User'))!
    const boxes = userRow.findAllComponents({ name: 'Checkbox' })
    expect(boxes[0].props('disabled')).toBeFalsy() // read stays grantable
    expect(boxes[1].props('disabled')).toBe(true)
    expect(boxes[2].props('disabled')).toBe(true)
  })

  it('super-admin role shows a notice instead of the matrix', async () => {
    seedSchema()
    const w = mountMatrix({ isSuperAdminRole: true })
    await flushPromises()
    expect(w.find('table').exists()).toBe(false)
    expect(w.text()).toContain('super admin')
    expect(rbacApi.getRolePermissions).not.toHaveBeenCalled()
  })

  it('save PUTs only rows with at least one grant and resets dirty', async () => {
    seedSchema()
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValue([
      { collection: 'article', canRead: true, canWrite: true, canDelete: false },
    ])
    const w = mountMatrix()
    await flushPromises()
    const vm: any = w.vm
    vm.toggle('article', 'write', true)
    vm.toggle('user', 'read', false) // stays all-false -> must not be sent
    await vm.save()
    expect(rbacApi.putRolePermissions).toHaveBeenCalledWith('r1', [
      { collection: 'article', canRead: true, canWrite: true, canDelete: false },
    ])
    expect(vm.dirty).toBe(false)
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pnpm vitest run src/components/rbac/PermissionMatrix.test.ts`
Expected: FAIL — component file doesn't exist.

- [ ] **Step 3: Implement**

Create `frontend/src/components/rbac/PermissionMatrix.vue`:

```vue
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi, type RolePermissionEntry } from '../../api/rbacApi'
import { unsavedConfirm } from '../../lib/formDirty'

// 問題 3: the Role form's permission matrix — one row per collection, read/write/delete grants,
// saved as a full-replace PUT independent from the generic form's save.
const props = defineProps<{ roleId: string; isSuperAdminRole: boolean }>()

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const schema = useSchemaStore()

type Grant = { read: boolean; write: boolean; delete: boolean }
const grants = ref<Record<string, Grant>>({})
const baseline = ref('{}')
const loading = ref(true)
const loadFailed = ref(false)
const saving = ref(false)

// Every collection is a row — including hidden ones (grants on `file` etc. still matter for
// API consumers). AdminOnly rows keep Read grantable; writes are super-admin-only by design.
const rows = computed(() =>
  [...schema.collections].sort(
    (a, b) => (a.group ?? '').localeCompare(b.group ?? '') || a.label.localeCompare(b.label),
  ),
)

const dirty = computed(() => JSON.stringify(grants.value) !== baseline.value)

function grantFor(name: string): Grant {
  return grants.value[name] ?? { read: false, write: false, delete: false }
}

function toggle(name: string, key: keyof Grant, value: boolean): void {
  // Immutable update: fresh record per change.
  grants.value = { ...grants.value, [name]: { ...grantFor(name), [key]: value } }
}

function fold(entries: RolePermissionEntry[]): Record<string, Grant> {
  const next: Record<string, Grant> = {}
  for (const e of entries) next[e.collection] = { read: e.canRead, write: e.canWrite, delete: e.canDelete }
  return next
}

async function load(): Promise<void> {
  loading.value = true
  loadFailed.value = false
  try {
    grants.value = fold(await rbacApi.getRolePermissions(props.roleId))
    baseline.value = JSON.stringify(grants.value)
  } catch {
    loadFailed.value = true
  } finally {
    loading.value = false
  }
}

async function save(): Promise<void> {
  saving.value = true
  try {
    const entries: RolePermissionEntry[] = Object.entries(grants.value)
      .filter(([, g]) => g.read || g.write || g.delete)
      .map(([collection, g]) => ({
        collection, canRead: g.read, canWrite: g.write, canDelete: g.delete,
      }))
    grants.value = fold(await rbacApi.putRolePermissions(props.roleId, entries))
    baseline.value = JSON.stringify(grants.value)
    toast.add({ severity: 'success', summary: t('rbac.saved'), life: 3000 })
  } catch {
    toast.add({ severity: 'error', summary: t('rbac.saveFailed'), life: 5000 })
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  if (!props.isSuperAdminRole) void load()
  else loading.value = false
})

// Route-leave guard for unsaved matrix edits. Uses the parent-provided ConfirmDialog
// (ItemFormView mounts one) — mounting a second instance here would duplicate dialogs (FE-R7).
onBeforeRouteLeave(() => {
  if (!dirty.value) return true
  const { header, message } = unsavedConfirm(t)
  return new Promise<boolean>((resolve) => {
    confirm.require({
      header,
      message,
      accept: () => resolve(true),
      reject: () => resolve(false),
      onHide: () => resolve(false), // dismiss = stay (matches ItemFormView.guardLeave)
    })
  })
})

defineExpose({ toggle, save, dirty, load })
</script>

<template>
  <section class="permission-matrix">
    <h2 class="matrix-title">{{ t('rbac.matrixTitle') }}</h2>

    <p v-if="isSuperAdminRole" class="notice">{{ t('rbac.superAdminAll') }}</p>
    <p v-else-if="loadFailed" class="notice">{{ t('rbac.loadFailed') }}</p>
    <template v-else-if="!loading">
      <div class="matrix-scroll">
        <table class="matrix-table">
          <thead>
            <tr>
              <th class="col-name">{{ t('rbac.colCollection') }}</th>
              <th>{{ t('rbac.colRead') }}</th>
              <th>{{ t('rbac.colWrite') }}</th>
              <th>{{ t('rbac.colDelete') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="c in rows" :key="c.name">
              <td class="col-name">{{ c.label }}</td>
              <td>
                <Checkbox :model-value="grantFor(c.name).read" binary
                          @update:model-value="(v: boolean) => toggle(c.name, 'read', !!v)" />
              </td>
              <td :title="c.adminOnly ? t('rbac.adminOnlyWriteHint') : undefined">
                <Checkbox :model-value="grantFor(c.name).write" binary :disabled="!!c.adminOnly"
                          @update:model-value="(v: boolean) => toggle(c.name, 'write', !!v)" />
              </td>
              <td :title="c.adminOnly ? t('rbac.adminOnlyWriteHint') : undefined">
                <Checkbox :model-value="grantFor(c.name).delete" binary :disabled="!!c.adminOnly"
                          @update:model-value="(v: boolean) => toggle(c.name, 'delete', !!v)" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="matrix-actions">
        <Button :label="t('rbac.save')" :disabled="!dirty || saving" :loading="saving" @click="save" />
      </div>
    </template>
  </section>
</template>

<style scoped>
.permission-matrix { display: grid; gap: 12px; margin-top: 24px; }
.matrix-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.notice { color: var(--muted); margin: 0; }
.matrix-scroll { overflow-x: auto; }
.matrix-table { border-collapse: collapse; min-width: 480px; }
.matrix-table th, .matrix-table td { padding: 8px 16px; text-align: center; border-bottom: 1px solid var(--border); }
.matrix-table .col-name { text-align: left; }
.matrix-actions { display: flex; justify-content: flex-end; }
</style>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pnpm vitest run src/components/rbac/PermissionMatrix.test.ts` → PASS. Then `pnpm test` + `pnpm build` → green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/rbac/PermissionMatrix.vue frontend/src/components/rbac/PermissionMatrix.test.ts
git commit -m "feat(admin-ui): PermissionMatrix — per-collection read/write/delete grants edited inside the Role form"
```

---

### Task 8: `EffectivePermissionsPanel` + ItemFormView wiring (spec §2a/§2b) — after Tasks 6+7

**Files:**
- Create: `frontend/src/components/rbac/EffectivePermissionsPanel.vue`
- Create: `frontend/src/components/rbac/EffectivePermissionsPanel.test.ts`
- Modify: `frontend/src/lib/frameworkCollections.ts`
- Modify: `frontend/src/views/ItemFormView.vue`
- Test: `frontend/src/views/ItemFormView.test.ts` (extend)

**Interfaces:**
- Consumes: `rbacApi.getEffectivePermissions` (Task 6), `PermissionMatrix` (Task 7).
- Produces: `<EffectivePermissionsPanel :user-id="string" ref>` exposing `reload(): Promise<void>`; `ROLE_COLLECTION = 'role'` / `USER_COLLECTION = 'user'` in `lib/frameworkCollections.ts`.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/rbac/EffectivePermissionsPanel.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import EffectivePermissionsPanel from './EffectivePermissionsPanel.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi } from '../../api/rbacApi'

vi.mock('../../api/rbacApi', () => ({
  rbacApi: { getEffectivePermissions: vi.fn() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPanel() {
  return mount(EffectivePermissionsPanel, {
    props: { userId: 'u1' },
    global: { plugins: [i18n] },
  })
}

describe('EffectivePermissionsPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useSchemaStore().collections = [
      { name: 'article', label: 'Article', fields: [], relations: [] },
    ] as any
  })

  it('renders the merged grants with collection labels', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({
      isSuperAdmin: false,
      permissions: { article: { read: true, write: true, delete: false } },
    })
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain('Article')
    expect(rbacApi.getEffectivePermissions).toHaveBeenCalledWith('u1')
  })

  it('super-admin users get a notice instead of a table', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({
      isSuperAdmin: true, permissions: {},
    })
    const w = mountPanel()
    await flushPromises()
    expect(w.find('table').exists()).toBe(false)
    expect(w.text()).toContain('super admin')
  })

  it('reload() refetches', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({
      isSuperAdmin: false, permissions: {},
    })
    const w = mountPanel()
    await flushPromises()
    await (w.vm as any).reload()
    expect(rbacApi.getEffectivePermissions).toHaveBeenCalledTimes(2)
  })
})
```

In `frontend/src/views/ItemFormView.test.ts` add (adapt to the file's mount/seed helpers; stub heavy children as its neighbors do):

```ts
import PermissionMatrix from '../components/rbac/PermissionMatrix.vue'
import EffectivePermissionsPanel from '../components/rbac/EffectivePermissionsPanel.vue'

  it('mounts PermissionMatrix only when editing a role as super-admin', async () => {
    // Seed a minimal 'role' collection schema + a loaded item, mirroring the file's other seeds.
    useSchemaStore().collections = [
      { name: 'role', label: 'Role', fields: [], relations: [] },
    ] as any
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.get).mockResolvedValue({ id: 'r1', name: 'editor', isSuperAdmin: false })

    const editing = mountView({ name: 'role', id: 'r1' })
    await flushPromises()
    expect(editing.findComponent(PermissionMatrix).exists()).toBe(true)

    const creating = mountView({ name: 'role' }) // create mode: no id yet -> no matrix
    await flushPromises()
    expect(creating.findComponent(PermissionMatrix).exists()).toBe(false)

    useAuthStore().user = { id: 'u2', isSuperAdmin: false, permissions: { role: { read: true, write: false, delete: false } } }
    const nonAdmin = mountView({ name: 'role', id: 'r1' })
    await flushPromises()
    expect(nonAdmin.findComponent(PermissionMatrix).exists()).toBe(false)
  })

  it('mounts EffectivePermissionsPanel when editing a user as super-admin, and saving reloads it', async () => {
    useSchemaStore().collections = [
      { name: 'user', label: 'User', fields: [], relations: [] },
    ] as any
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.get).mockResolvedValue({ id: 'u9', email: 'x@struo.test' })
    vi.mocked(itemsApi.update).mockResolvedValue({ id: 'u9' })

    const wrapper = mountView({ name: 'user', id: 'u9' })
    await flushPromises()
    const panel = wrapper.findComponent(EffectivePermissionsPanel)
    expect(panel.exists()).toBe(true)

    const reload = vi.spyOn(panel.vm as any, 'reload').mockResolvedValue(undefined)
    await (wrapper.vm as any).onSubmit()
    await flushPromises()
    expect(reload).toHaveBeenCalled()
  })
```

NOTE for implementer: `mountView`/`seedLanguage` are this test file's existing helpers — match their real signatures (route params may be provided via the file's router mock instead of an argument), stub `rbacApi` like Task 7's tests do (`vi.mock('../api/rbacApi', ...)`), and reuse the file's `itemsApi` mock setup. Keep the five assertions exactly; if spying on the panel's exposed `reload` proves brittle, assert `rbacApi.getEffectivePermissions` call count instead (FE-27 precedent: call-count assertions).

- [ ] **Step 2: Run tests to verify they fail**

Run: `pnpm vitest run src/components/rbac/EffectivePermissionsPanel.test.ts src/views/ItemFormView.test.ts`
Expected: FAIL — panel module missing; ItemFormView renders neither component.

- [ ] **Step 3: Implement**

`lib/frameworkCollections.ts` — append:

```ts
// Batch B: framework collections with dedicated RBAC editors on the generic item form.
export const ROLE_COLLECTION = 'role'
export const USER_COLLECTION = 'user'
```

Create `frontend/src/components/rbac/EffectivePermissionsPanel.vue`:

```vue
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi, type EffectivePermissions } from '../../api/rbacApi'

// 問題 4: read-only preview of a user's merged grants, fed by the same resolver the backend
// authorizes with. Parent (ItemFormView) calls reload() after a successful save.
const props = defineProps<{ userId: string }>()

const { t } = useI18n()
const schema = useSchemaStore()

const eff = ref<EffectivePermissions | null>(null)
const loadFailed = ref(false)

const rows = computed(() => {
  if (!eff.value) return []
  return Object.entries(eff.value.permissions)
    .map(([name, g]) => ({ name, label: schema.get(name)?.label ?? name, ...g }))
    .sort((a, b) => a.label.localeCompare(b.label))
})

async function reload(): Promise<void> {
  loadFailed.value = false
  try {
    eff.value = await rbacApi.getEffectivePermissions(props.userId)
  } catch {
    loadFailed.value = true
    eff.value = null
  }
}

onMounted(reload)
defineExpose({ reload })
</script>

<template>
  <section class="effective-permissions">
    <h2 class="panel-title">{{ t('rbac.effectiveTitle') }}</h2>

    <p v-if="loadFailed" class="notice">{{ t('rbac.effectiveLoadFailed') }}</p>
    <p v-else-if="eff?.isSuperAdmin" class="notice">{{ t('rbac.effectiveSuperAdmin') }}</p>
    <p v-else-if="eff && rows.length === 0" class="notice">{{ t('rbac.effectiveEmpty') }}</p>
    <div v-else-if="eff" class="panel-scroll">
      <table class="panel-table">
        <thead>
          <tr>
            <th class="col-name">{{ t('rbac.colCollection') }}</th>
            <th>{{ t('rbac.colRead') }}</th>
            <th>{{ t('rbac.colWrite') }}</th>
            <th>{{ t('rbac.colDelete') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in rows" :key="r.name">
            <td class="col-name">{{ r.label }}</td>
            <td><i v-if="r.read" class="pi pi-check" aria-hidden="true" /><span class="sr-only">{{ r.read }}</span></td>
            <td><i v-if="r.write" class="pi pi-check" aria-hidden="true" /><span class="sr-only">{{ r.write }}</span></td>
            <td><i v-if="r.delete" class="pi pi-check" aria-hidden="true" /><span class="sr-only">{{ r.delete }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.effective-permissions { display: grid; gap: 12px; margin-top: 24px; }
.panel-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.notice { color: var(--muted); margin: 0; }
.panel-scroll { overflow-x: auto; }
.panel-table { border-collapse: collapse; min-width: 480px; }
.panel-table th, .panel-table td { padding: 8px 16px; text-align: center; border-bottom: 1px solid var(--border); }
.panel-table .col-name { text-align: left; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
</style>
```

`ItemFormView.vue` changes:

a. Imports (with the other component/lib imports):

```ts
import PermissionMatrix from '../components/rbac/PermissionMatrix.vue'
import EffectivePermissionsPanel from '../components/rbac/EffectivePermissionsPanel.vue'
```

and extend the `frameworkCollections` import line:

```ts
import { LANGUAGE_COLLECTION, ROLE_COLLECTION, USER_COLLECTION } from '../lib/frameworkCollections'
```

b. Script — after the `meta` computed:

```ts
// Batch B: RBAC editors on the generic form. Gated to super-admins — a non-admin with a read
// grant on role/user could open the form, but the matrix/preview endpoints would 403.
const savedRoleIsSuperAdmin = ref(false)
const effPanel = ref<InstanceType<typeof EffectivePermissionsPanel> | null>(null)
const showMatrix = computed(
  () => !isCreate.value && name.value === ROLE_COLLECTION && auth.user?.isSuperAdmin === true,
)
const showEffective = computed(
  () => !isCreate.value && name.value === USER_COLLECTION && auth.user?.isSuperAdmin === true,
)
```

c. In `init()` and `reloadLatest()`, right after the fetched item is parsed into the model (`setModel(parseItemToForm(...))`), add:

```ts
    // The matrix's super-admin notice reflects the last-SAVED state, not the unsaved checkbox.
    if (name.value === ROLE_COLLECTION)
      savedRoleIsSuperAdmin.value = (item as Record<string, unknown>).isSuperAdmin === true
```

(match the local variable name holding the fetched item in each function).

d. In `onSubmit()`, in the success path (next to the existing `if (name.value === LANGUAGE_COLLECTION)` reload), add:

```ts
    if (name.value === ROLE_COLLECTION) savedRoleIsSuperAdmin.value = model.shared.isSuperAdmin === true
    if (name.value === USER_COLLECTION) void effPanel.value?.reload() // roles may have changed
```

e. Template — after `</ItemForm>` (before `<RevisionHistoryDrawer>`):

```html
      <PermissionMatrix
        v-if="showMatrix"
        :role-id="idStr"
        :is-super-admin-role="savedRoleIsSuperAdmin"
      />
      <EffectivePermissionsPanel v-if="showEffective" ref="effPanel" :user-id="idStr" />
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pnpm vitest run src/components/rbac/EffectivePermissionsPanel.test.ts src/views/ItemFormView.test.ts` → PASS.
Then `pnpm test` + `pnpm build` → green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/rbac/EffectivePermissionsPanel.vue frontend/src/components/rbac/EffectivePermissionsPanel.test.ts frontend/src/lib/frameworkCollections.ts frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts
git commit -m "feat(admin-ui): mount PermissionMatrix on Role form + EffectivePermissionsPanel on User form"
```

---

## Batch-final verification (run by the orchestrator, not a task subagent)

1. `dotnet test` from repo root — all green.
2. `cd frontend && pnpm test && pnpm build` — all green.
3. **Rebuild + restart the backend on :5221** (new endpoints; plain `dotnet run`, never :5080) + `pnpm dev --host 127.0.0.1`. Live smoke on PG via Playwright MCP as the dev admin (`admin@admin.com`):
   - Sidebar: Permission / UserRole / File entries are gone; Role / User remain.
   - Role form: open a role → matrix renders below the form; AdminOnly rows (User/Role/Permission/UserRole) have disabled write/delete; tick read+write on Article → save → toast; reload page → grants persisted. Open the seeded `admin` role → super-admin notice, no matrix.
   - User form: Roles TagSelect shows role NAMES; assign the test role → save → effective-permissions panel refreshes and shows Article read+write; roleless user shows the public floor.
   - Real authorization: log in as a test user with the granted role → nav shows Article, list/edit work per grants.
   - Clean up: remove test role/user.
4. `pnpm playwright test` e2e (env facts: `E2E_API` → :5221, SEC-7 limiter `Enabled=false`, `--workers=1`, unique `E2E_STAMP`).
5. Update memory + merge decision per `superpowers:finishing-a-development-branch`.
