using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class SqlSugarRolePermissionStoreTests
{
    private static ISqlSugarClient NewDb(ITestDatabase file)
    {
        var db = SqlSugarClientFactory.Create(
            file.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables(typeof(User), typeof(Role), typeof(Permission), typeof(UserRole), typeof(RoleCapability));
        return db;
    }

    [Fact]
    public async Task Anonymous_loads_public_role_permissions()
    {
        using var file = TestBackend.CreateUnitDatabase();
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

    // A capability row on the public role (written around the API) must never resolve: an anonymous
    // caller would otherwise hold it.
    [Fact]
    public async Task Capability_rows_on_the_public_role_are_ignored_for_every_caller()
    {
        using var file = TestBackend.CreateUnitDatabase();
        var db = NewDb(file);
        var userId = Guid.CreateVersion7();
        var pub = new Role { Id = Guid.CreateVersion7(), Name = "public" };
        var editor = new Role { Id = Guid.CreateVersion7(), Name = "editor" };
        await db.Insertable(new[] { pub, editor }).ExecuteCommandAsync();
        await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, RoleId = editor.Id }).ExecuteCommandAsync();
        await db.Insertable(new RoleCapability { Id = Guid.CreateVersion7(), RoleId = pub.Id, Capability = "login.unlock" }).ExecuteCommandAsync();
        await db.Insertable(new RoleCapability { Id = Guid.CreateVersion7(), RoleId = editor.Id, Capability = "reports.export" }).ExecuteCommandAsync();
        var store = new SqlSugarRolePermissionStore(db);

        PermissionResolver.Resolve(await store.LoadForUserAsync(null)).HasCapability("login.unlock").Should().BeFalse();
        var signedIn = PermissionResolver.Resolve(await store.LoadForUserAsync(userId));
        signedIn.HasCapability("login.unlock").Should().BeFalse();
        signedIn.HasCapability("reports.export").Should().BeTrue();
        PermissionResolver.Resolve(await store.LoadForRolesAsync([editor.Id])).HasCapability("login.unlock").Should().BeFalse();
    }

    // Anonymous callers are the hot path: with only the public role there is no capability to read, so
    // the role_capabilities table is never queried (dropping it would make the query throw).
    [Fact]
    public async Task Anonymous_resolution_does_not_query_capabilities_and_holds_none()
    {
        using var file = TestBackend.CreateUnitDatabase();
        var db = NewDb(file);
        await db.Insertable(new Role { Id = Guid.CreateVersion7(), Name = "public" }).ExecuteCommandAsync();
        db.DbMaintenance.DropTable<RoleCapability>();
        var store = new SqlSugarRolePermissionStore(db);

        var data = await store.LoadForUserAsync(null);

        data.Capabilities.Should().BeEmpty();
        PermissionResolver.Resolve(data).HasCapability("login.unlock").Should().BeFalse();
    }

    [Fact]
    public async Task User_with_roles_unions_the_public_floor_with_their_own_roles()
    {
        using var file = TestBackend.CreateUnitDatabase();
        var db = NewDb(file);
        var userId = Guid.CreateVersion7();
        var pub = new Role { Id = Guid.CreateVersion7(), Name = "public" };
        var editor = new Role { Id = Guid.CreateVersion7(), Name = "editor" };
        var other = new Role { Id = Guid.CreateVersion7(), Name = "other" };
        await db.Insertable(new[] { pub, editor, other }).ExecuteCommandAsync();
        await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, RoleId = editor.Id }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = pub.Id, Collection = "category", CanRead = true }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = editor.Id, Collection = "article", CanWrite = true }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = other.Id, Collection = "user", CanRead = true }).ExecuteCommandAsync();

        var store = new SqlSugarRolePermissionStore(db);
        var data = await store.LoadForUserAsync(userId);

        data.Roles.Should().Contain(r => r.Name == "editor");
        data.Roles.Should().Contain(r => r.Name == "public");
        data.Permissions.Should().ContainSingle(p => p.Collection == "article" && p.CanWrite);
        data.Permissions.Should().ContainSingle(p => p.Collection == "category" && p.CanRead);
        data.Permissions.Should().NotContain(p => p.Collection == "user"); // unassigned role still not inherited
    }

    [Fact]
    public async Task Hypothetical_role_preview_also_unions_the_public_floor()
    {
        using var file = TestBackend.CreateUnitDatabase();
        var db = NewDb(file);
        var pub = new Role { Id = Guid.CreateVersion7(), Name = "public" };
        var editor = new Role { Id = Guid.CreateVersion7(), Name = "editor" };
        await db.Insertable(new[] { pub, editor }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = pub.Id, Collection = "category", CanRead = true }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = editor.Id, Collection = "article", CanWrite = true }).ExecuteCommandAsync();

        var store = new SqlSugarRolePermissionStore(db);
        var data = await store.LoadForRolesAsync([editor.Id]);

        data.Permissions.Should().ContainSingle(p => p.Collection == "article" && p.CanWrite);
        data.Permissions.Should().ContainSingle(p => p.Collection == "category" && p.CanRead);
    }

    [Fact]
    public async Task Public_role_rows_are_not_duplicated_when_the_user_is_assigned_the_public_role()
    {
        using var file = TestBackend.CreateUnitDatabase();
        var db = NewDb(file);
        var userId = Guid.CreateVersion7();
        var pub = new Role { Id = Guid.CreateVersion7(), Name = "public" };
        await db.Insertable(pub).ExecuteCommandAsync();
        await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, RoleId = pub.Id }).ExecuteCommandAsync();
        await db.Insertable(new Permission { Id = Guid.CreateVersion7(), RoleId = pub.Id, Collection = "article", CanRead = true }).ExecuteCommandAsync();

        var store = new SqlSugarRolePermissionStore(db);
        var data = await store.LoadForUserAsync(userId);

        data.Roles.Should().ContainSingle(r => r.Name == "public");
        data.Permissions.Should().ContainSingle(p => p.Collection == "article" && p.CanRead);
    }

    [Fact]
    public async Task Authenticated_user_with_no_roles_falls_back_to_public()
    {
        using var file = TestBackend.CreateUnitDatabase();
        var db = NewDb(file);
        var pub = new Role { Id = Guid.CreateVersion7(), Name = "public" };
        await db.Insertable(pub).ExecuteCommandAsync();
        await db.Insertable(new Permission
        {
            Id = Guid.CreateVersion7(), RoleId = pub.Id, Collection = "article", CanRead = true
        }).ExecuteCommandAsync();

        var store = new SqlSugarRolePermissionStore(db);
        var data = await store.LoadForUserAsync(Guid.CreateVersion7()); // authenticated, no UserRole

        data.Roles.Should().ContainSingle(r => r.Name == "public");
        data.Permissions.Should().ContainSingle(p => p.Collection == "article" && p.CanRead);
    }
}
