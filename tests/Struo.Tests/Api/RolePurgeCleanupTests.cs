using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Deleting a role (not soft-deletable, so every delete is a purge) removes the rows that name it in
/// <c>permissions</c> and <c>user_roles</c>; other roles' rows stay.
/// </summary>
[Collection("ApiIntegration")]
public class RolePurgeCleanupTests(ApiFactory factory)
{
    private sealed record RoleRows(int Permissions, int UserRoles);

    private static async Task<Guid> CreateRoleAsync(HttpClient admin)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/role", new { name = $"purge-{Guid.NewGuid():N}" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<Guid> SeedUserAsync(ISqlSugarClient db)
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new User { Id = id, Email = $"purge-{id:N}@struo.test", Password = "x", IsActive = true })
            .ExecuteCommandAsync();
        return id;
    }

    private async Task SeedRowsAsync(Guid roleId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Insertable(new Permission
        {
            Id = Guid.CreateVersion7(), RoleId = roleId, Collection = "fxArticle", CanRead = true,
        }).ExecuteCommandAsync();
        await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = userId, RoleId = roleId })
            .ExecuteCommandAsync();
    }

    private async Task<RoleRows> CountAsync(Guid roleId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return new RoleRows(
            await db.Queryable<Permission>().Where(p => p.RoleId == roleId).CountAsync(),
            await db.Queryable<UserRole>().Where(ur => ur.RoleId == roleId).CountAsync());
    }

    private async Task<(Guid RoleId, Guid UserId)> ArrangeAsync(HttpClient admin)
    {
        var roleId = await CreateRoleAsync(admin);
        using var scope = factory.Services.CreateScope();
        var userId = await SeedUserAsync(scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
        await SeedRowsAsync(roleId, userId);
        return (roleId, userId);
    }

    [Fact]
    public async Task Purging_a_role_removes_its_rows_and_leaves_other_roles_untouched()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (roleId, _) = await ArrangeAsync(admin);
        var (otherId, _) = await ArrangeAsync(admin);

        (await admin.DeleteAsync($"/api/items/role/{roleId}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CountAsync(roleId)).Should().Be(new RoleRows(0, 0));
        (await CountAsync(otherId)).Should().Be(new RoleRows(1, 1));
    }

    // Role is not soft-deletable, so a plain DELETE is a purge too: there is no trash state to keep rows for.
    [Fact]
    public async Task Deleting_a_role_without_the_purge_flag_removes_its_rows()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (roleId, _) = await ArrangeAsync(admin);

        (await admin.DeleteAsync($"/api/items/role/{roleId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CountAsync(roleId)).Should().Be(new RoleRows(0, 0));
    }

    [Fact]
    public async Task Deleting_a_role_through_graphql_removes_its_rows_and_leaves_other_roles_untouched()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (roleId, _) = await ArrangeAsync(admin);
        var (otherId, _) = await ArrangeAsync(admin);

        var resp = await admin.PostAsJsonAsync("/graphql", new
        {
            query = $"mutation {{ deleteRole(id: \"{roleId}\", purge: true) }}"
        });

        var body = await resp.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("deleteRole").GetBoolean()
            .Should().BeTrue(body);
        (await CountAsync(roleId)).Should().Be(new RoleRows(0, 0));
        (await CountAsync(otherId)).Should().Be(new RoleRows(1, 1));
    }

    [Fact]
    public void Role_is_not_soft_deletable()
    {
        using var scope = factory.Services.CreateScope();
        var metadata = scope.ServiceProvider.GetRequiredService<Struo.Application.Metadata.IMetadataProvider>();

        metadata.GetCollection("role")!.SoftDelete.Should().BeFalse();
    }

    [Fact]
    public async Task Purging_a_user_removes_its_user_role_rows()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (roleId, userId) = await ArrangeAsync(admin);

        (await admin.DeleteAsync($"/api/items/user/{userId}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CountAsync(roleId)).UserRoles.Should().Be(0);
    }

    [Fact]
    public async Task Store_deletes_roll_back_with_an_outer_transaction()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (roleId, _) = await ArrangeAsync(admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        await db.Ado.BeginTranAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>().DeleteForRoleAsync(roleId);
        await db.Ado.RollbackTranAsync();

        (await CountAsync(roleId)).Should().Be(new RoleRows(1, 1));
    }
}
