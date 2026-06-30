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
