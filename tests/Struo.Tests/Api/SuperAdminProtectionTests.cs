using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// User and role management are ordinary collections: a grant on `user` / `role` lets a caller manage normal
// accounts and roles, while super-admin accounts and roles stay a super-admin's, and the last active
// super-admin cannot be removed. Every write path is covered: items REST, the hidden junctions, the
// users and roles controllers and GraphQL.
[Collection("ApiIntegration")]
public class SuperAdminProtectionTests(ApiFactory factory)
{
    private const string UserWrite = "user";

    private static async Task<JsonElement> DataAsync(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    private async Task<T> WithDbAsync<T>(Func<ISqlSugarClient, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }

    private async Task<Guid> SuperRoleIdAsync()
    {
        await factory.EnsureAdminSeededAsync();
        return await WithDbAsync(async db => (await db.Queryable<Role>().Where(r => r.Name == "admin").FirstAsync()).Id);
    }

    private async Task<Guid> NormalRoleAsync() =>
        await WithDbAsync(async db =>
        {
            var role = new Role { Id = Guid.CreateVersion7(), Name = $"plain-{Guid.NewGuid():N}" };
            await db.Insertable(role).ExecuteCommandAsync();
            return role.Id;
        });

    private async Task<(string Email, Guid Id)> SeedUserAsync(bool superAdmin, bool active = true)
    {
        var superRole = await SuperRoleIdAsync();
        return await WithDbAsync(async db =>
        {
            var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
            var id = Guid.CreateVersion7();
            var email = $"{(superAdmin ? "root" : "plain")}-{id:N}@struo.test";
            await db.Insertable(new User { Id = id, Email = email, Password = hasher.Hash("pw-123456"), IsActive = active })
                .ExecuteCommandAsync();
            if (superAdmin)
                await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = id, RoleId = superRole })
                    .ExecuteCommandAsync();
            return (email, id);
        });
    }

    // A non-super-admin caller with the given grants. Delete is its own flag, set on the collections named
    // in `delete` (each must also appear in `read` or `write`).
    private async Task<HttpClient> CallerAsync(string[] read, string[] write, string[]? delete = null) =>
        (await CallerWithIdAsync(read, write, delete)).Client;

    private async Task<(HttpClient Client, Guid Id)> CallerWithIdAsync(
        string[] read, string[] write, string[]? delete = null)
    {
        var (email, password, userId) = await factory.SeedEditorAsync(read, write);
        if (delete is { Length: > 0 })
        {
            await WithDbAsync(async db =>
            {
                var roleId = (await db.Queryable<UserRole>().Where(ur => ur.UserId == userId).FirstAsync()).RoleId;
                return await db.Updateable<Permission>().SetColumns(p => p.CanDelete == true)
                    .Where(p => p.RoleId == roleId && delete.Contains(p.Collection)).ExecuteCommandAsync();
            });
        }
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).EnsureSuccessStatusCode();
        return (client, userId);
    }

    private Task<HttpClient> UserManagerAsync() => CallerAsync(["user", "role"], ["user"], ["user"]);
    private Task<HttpClient> RoleManagerAsync() => CallerAsync(["user", "role"], ["role"], ["role"]);

    private static Task<HttpResponseMessage> Put(HttpClient c, string path, object body) => c.PutAsJsonAsync(path, body);

    private static async Task ShouldBeAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string? code = null)
    {
        var resp = await call;
        var text = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(status, text);
        if (code is not null) text.Should().Contain(code);
    }

    // ---------------------------------------------------------------- no grant

    [Fact]
    public async Task Without_a_grant_every_user_and_role_management_path_is_403()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        var role = await NormalRoleAsync();
        using var caller = await CallerAsync([], []);

        await ShouldBeAsync(Put(caller, $"/api/items/user/{plain.Id}", new { name = "x" }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.DeleteAsync($"/api/items/user/{plain.Id}?purge=true"), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.PostAsJsonAsync("/api/users",
            new { email = $"n-{Guid.NewGuid():N}@struo.test", password = "password-123" }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(Put(caller, $"/api/users/{plain.Id}/password", new { newPassword = "password-456" }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.PostAsync($"/api/users/{plain.Id}/access-token", null), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.GetAsync($"/api/users/{plain.Id}/access-token"), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.GetAsync($"/api/users/{plain.Id}/effective-permissions"), HttpStatusCode.Forbidden);
        await ShouldBeAsync(Put(caller, $"/api/items/role/{role}", new { name = "y" }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.GetAsync($"/api/roles/{role}/permissions"), HttpStatusCode.Forbidden);
        await ShouldBeAsync(Put(caller, $"/api/roles/{role}/permissions", Array.Empty<object>()), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.PostAsJsonAsync("/api/items/userRole", new { userId = plain.Id, roleId = role }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.PostAsJsonAsync("/api/items/permission",
            new { roleId = role, collection = "language", canRead = true }), HttpStatusCode.Forbidden);
    }

    // The hidden junctions are not grantable on their own.
    [Fact]
    public async Task A_grant_on_a_hidden_junction_alone_does_not_allow_a_write()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        var role = await NormalRoleAsync();
        using var caller = await CallerAsync([], ["userRole", "permission"]);

        await ShouldBeAsync(caller.PostAsJsonAsync("/api/items/userRole", new { userId = plain.Id, roleId = role }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(caller.PostAsJsonAsync("/api/items/permission",
            new { roleId = role, collection = "language", canRead = true }), HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- write on user

    [Fact]
    public async Task Write_on_user_manages_normal_accounts_through_every_path()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        var role = await NormalRoleAsync();
        using var manager = await UserManagerAsync();

        await ShouldBeAsync(Put(manager, $"/api/items/user/{plain.Id}", new { name = "renamed" }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(manager, $"/api/items/user/{plain.Id}", new { roles = new[] { role } }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(manager, $"/api/users/{plain.Id}/password", new { newPassword = "password-456" }), HttpStatusCode.NoContent);
        await ShouldBeAsync(manager.PostAsync($"/api/users/{plain.Id}/access-token", null), HttpStatusCode.OK);
        await ShouldBeAsync(manager.GetAsync($"/api/users/{plain.Id}/access-token"), HttpStatusCode.OK);
        await ShouldBeAsync(manager.DeleteAsync($"/api/users/{plain.Id}/access-token"), HttpStatusCode.NoContent);
        await ShouldBeAsync(manager.GetAsync($"/api/users/{plain.Id}/effective-permissions"), HttpStatusCode.OK);
        await ShouldBeAsync(manager.PostAsJsonAsync("/api/users",
            new { email = $"made-{Guid.NewGuid():N}@struo.test", password = "password-123" }), HttpStatusCode.Created);
        await ShouldBeAsync(manager.DeleteAsync($"/api/items/user/{plain.Id}?purge=true"), HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Read_on_user_alone_can_read_but_not_write_users()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        using var reader = await CallerAsync(["user"], []);

        await ShouldBeAsync(reader.GetAsync($"/api/users/{plain.Id}/access-token"), HttpStatusCode.OK);
        await ShouldBeAsync(reader.GetAsync($"/api/users/{plain.Id}/effective-permissions"), HttpStatusCode.OK);
        await ShouldBeAsync(Put(reader, $"/api/items/user/{plain.Id}", new { name = "x" }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(reader.PostAsync($"/api/users/{plain.Id}/access-token", null), HttpStatusCode.Forbidden);
        await ShouldBeAsync(Put(reader, $"/api/users/{plain.Id}/password", new { newPassword = "password-456" }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Write_on_user_cannot_touch_a_super_admin_account_through_any_path()
    {
        var root = await SeedUserAsync(superAdmin: true);
        using var manager = await UserManagerAsync();
        const string only = "Only a super-admin";

        await ShouldBeAsync(Put(manager, $"/api/items/user/{root.Id}", new { name = "x" }), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(Put(manager, $"/api/items/user/{root.Id}", new { isActive = false }), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.DeleteAsync($"/api/items/user/{root.Id}?purge=true"), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.PostAsync($"/api/items/user/{root.Id}/restore", null), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(Put(manager, $"/api/users/{root.Id}/password", new { newPassword = "password-456" }), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.PostAsync($"/api/users/{root.Id}/access-token", null), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.DeleteAsync($"/api/users/{root.Id}/access-token"), HttpStatusCode.Forbidden, only);
        // Reading stays open to a reader.
        await ShouldBeAsync(manager.GetAsync($"/api/users/{root.Id}/access-token"), HttpStatusCode.OK);
        await ShouldBeAsync(manager.GetAsync($"/api/users/{root.Id}/effective-permissions"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Write_on_user_cannot_assign_a_super_admin_role_by_any_path()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        var superRole = await SuperRoleIdAsync();
        using var manager = await UserManagerAsync();

        await ShouldBeAsync(Put(manager, $"/api/items/user/{plain.Id}", new { roles = new[] { superRole } }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(manager.PostAsJsonAsync("/api/items/userRole", new { userId = plain.Id, roleId = superRole }), HttpStatusCode.Forbidden);

        var roles = await WithDbAsync(db => db.Queryable<UserRole>().Where(ur => ur.UserId == plain.Id).CountAsync());
        roles.Should().Be(0);
    }

    [Fact]
    public async Task Write_on_user_manages_memberships_of_normal_roles_through_the_junction()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        var role = await NormalRoleAsync();
        using var manager = await UserManagerAsync();

        var created = await manager.PostAsJsonAsync("/api/items/userRole", new { userId = plain.Id, roleId = role });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var rowId = (await DataAsync(created)).Deserialize<JsonElement>().GetProperty("id").GetGuid();

        await ShouldBeAsync(manager.DeleteAsync($"/api/items/userRole/{rowId}?purge=true"), HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Write_on_user_cannot_change_a_membership_of_a_super_admin_account_through_the_junction()
    {
        var root = await SeedUserAsync(superAdmin: true);
        var role = await NormalRoleAsync();
        var superRole = await SuperRoleIdAsync();
        var rowId = await WithDbAsync(async db =>
            (await db.Queryable<UserRole>().Where(ur => ur.UserId == root.Id && ur.RoleId == superRole).FirstAsync()).Id);
        using var manager = await UserManagerAsync();

        await ShouldBeAsync(manager.PostAsJsonAsync("/api/items/userRole", new { userId = root.Id, roleId = role }), HttpStatusCode.Forbidden);
        await ShouldBeAsync(manager.DeleteAsync($"/api/items/userRole/{rowId}?purge=true"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Graphql_mutations_follow_the_same_rules()
    {
        var root = await SeedUserAsync(superAdmin: true);
        var plain = await SeedUserAsync(superAdmin: false);
        using var manager = await UserManagerAsync();

        async Task<JsonElement> Run(string query) =>
            JsonDocument.Parse(await (await manager.PostAsJsonAsync("/graphql", new { query })).Content.ReadAsStringAsync()).RootElement;

        var refused = await Run($"mutation {{ updateUser(id: \"{root.Id}\", input: {{ name: \"x\" }}) {{ id }} }}");
        refused.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("Only a super-admin");
        var refusedDelete = await Run($"mutation {{ deleteUser(id: \"{root.Id}\", purge: true) }}");
        refusedDelete.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("Only a super-admin");

        var allowed = await Run($"mutation {{ updateUser(id: \"{plain.Id}\", input: {{ name: \"x\" }}) {{ id }} }}");
        allowed.TryGetProperty("errors", out _).Should().BeFalse(allowed.ToString());
    }

    // ---------------------------------------------------------------- write on role

    [Fact]
    public async Task Write_on_role_manages_normal_roles_and_their_grants()
    {
        var role = await NormalRoleAsync();
        using var manager = await RoleManagerAsync();

        await ShouldBeAsync(Put(manager, $"/api/items/role/{role}", new { name = $"renamed-{Guid.NewGuid():N}" }), HttpStatusCode.OK);
        await ShouldBeAsync(manager.GetAsync($"/api/roles/{role}/permissions"), HttpStatusCode.OK);
        await ShouldBeAsync(Put(manager, $"/api/roles/{role}/permissions",
            new[] { new { collection = "language", canRead = true, canWrite = false, canDelete = false } }), HttpStatusCode.OK);
        var grant = await manager.PostAsJsonAsync("/api/items/permission",
            new { roleId = role, collection = "file", canRead = true });
        grant.StatusCode.Should().Be(HttpStatusCode.Created, await grant.Content.ReadAsStringAsync());
        await ShouldBeAsync(manager.PostAsJsonAsync("/api/items/role", new { name = $"made-{Guid.NewGuid():N}" }), HttpStatusCode.Created);
        await ShouldBeAsync(manager.DeleteAsync($"/api/items/role/{role}?purge=true"), HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Write_on_role_cannot_touch_a_super_admin_role_or_its_grants()
    {
        var superRole = await SuperRoleIdAsync();
        var grantRow = await WithDbAsync(async db =>
        {
            var row = new Permission { Id = Guid.CreateVersion7(), RoleId = superRole, Collection = "language", CanRead = true };
            await db.Insertable(row).ExecuteCommandAsync();
            return row.Id;
        });
        using var manager = await RoleManagerAsync();
        const string only = "Only a super-admin";

        await ShouldBeAsync(Put(manager, $"/api/items/role/{superRole}", new { description = "x" }), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.DeleteAsync($"/api/items/role/{superRole}?purge=true"), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.PostAsync($"/api/items/role/{superRole}/restore", null), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(Put(manager, $"/api/roles/{superRole}/permissions", Array.Empty<object>()), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.PostAsJsonAsync("/api/items/permission",
            new { roleId = superRole, collection = "file", canRead = true }), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(Put(manager, $"/api/items/permission/{grantRow}", new { canRead = false }), HttpStatusCode.Forbidden, only);
        await ShouldBeAsync(manager.DeleteAsync($"/api/items/permission/{grantRow}?purge=true"), HttpStatusCode.Forbidden, only);
        // Reading the grants stays open to a reader.
        await ShouldBeAsync(manager.GetAsync($"/api/roles/{superRole}/permissions"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_super_admin_role_cannot_be_deleted_by_anyone()
    {
        var superRole = await SuperRoleIdAsync();
        using var admin = await factory.CreateAuthenticatedClientAsync();

        await ShouldBeAsync(admin.DeleteAsync($"/api/items/role/{superRole}?purge=true"), HttpStatusCode.Conflict, "CONFLICT");
        await ShouldBeAsync(admin.DeleteAsync($"/api/items/role/{superRole}"), HttpStatusCode.Conflict, "CONFLICT");
        (await WithDbAsync(db => db.Queryable<Role>().Where(r => r.Id == superRole).AnyAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task The_super_admin_flag_cannot_be_set_or_cleared_by_anyone()
    {
        var superRole = await SuperRoleIdAsync();
        var plainRole = await NormalRoleAsync();
        using var admin = await factory.CreateAuthenticatedClientAsync();

        var created = await admin.PostAsJsonAsync("/api/items/role", new { name = $"flag-{Guid.NewGuid():N}", isSuperAdmin = true });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var createdId = (await DataAsync(created)).GetProperty("id").GetGuid();

        await ShouldBeAsync(Put(admin, $"/api/items/role/{plainRole}", new { isSuperAdmin = true }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(admin, $"/api/items/role/{superRole}", new { isSuperAdmin = false }), HttpStatusCode.OK);

        var flags = await WithDbAsync(async db =>
            (await db.Queryable<Role>().Where(r => r.Id == createdId || r.Id == plainRole || r.Id == superRole).ToListAsync())
            .ToDictionary(r => r.Id, r => r.IsSuperAdmin));
        flags[createdId].Should().BeFalse();
        flags[plainRole].Should().BeFalse();
        flags[superRole].Should().BeTrue();
    }

    // ---------------------------------------------------------------- super-admin caller

    [Fact]
    public async Task A_super_admin_manages_other_super_admins_and_assigns_super_admin_roles()
    {
        var root = await SeedUserAsync(superAdmin: true);
        var plain = await SeedUserAsync(superAdmin: false);
        var superRole = await SuperRoleIdAsync();
        using var admin = await factory.CreateAuthenticatedClientAsync();

        await ShouldBeAsync(Put(admin, $"/api/items/user/{root.Id}", new { name = "renamed" }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(admin, $"/api/items/user/{plain.Id}", new { roles = new[] { superRole } }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(admin, $"/api/users/{root.Id}/password", new { newPassword = "password-456" }), HttpStatusCode.NoContent);
        await ShouldBeAsync(admin.PostAsync($"/api/users/{root.Id}/access-token", null), HttpStatusCode.OK);
        await ShouldBeAsync(admin.DeleteAsync($"/api/items/user/{root.Id}?purge=true"), HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------- last active super-admin

    // Makes `sole` the only active super-admin for the duration of a test: every other member is
    // deactivated in the database and put back afterwards.
    private sealed class SoleSuperAdmin(Func<Task> restore, HttpClient client, Guid id, Guid roleId) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Id { get; } = id;
        public Guid SuperRoleId { get; } = roleId;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await restore();
        }
    }

    private async Task<SoleSuperAdmin> BecomeSoleSuperAdminAsync()
    {
        var superRole = await SuperRoleIdAsync();
        var (email, id) = await SeedUserAsync(superAdmin: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "pw-123456" })).EnsureSuccessStatusCode();

        var others = await WithDbAsync(db => db.Queryable<User, UserRole, Role>((u, ur, r) => u.Id == ur.UserId && ur.RoleId == r.Id)
            .Where((u, ur, r) => r.IsSuperAdmin && u.IsActive && u.Id != id).Select((u, ur, r) => u.Id).ToListAsync());
        await WithDbAsync(db => db.Updateable<User>().SetColumns(u => u.IsActive == false)
            .Where(u => others.Contains(u.Id)).ExecuteCommandAsync());
        return new SoleSuperAdmin(
            () => WithDbAsync(db => db.Updateable<User>().SetColumns(u => u.IsActive == true)
                .Where(u => others.Contains(u.Id)).ExecuteCommandAsync()),
            client, id, superRole);
    }

    [Fact]
    public async Task The_last_active_super_admin_cannot_be_deactivated_stripped_of_the_role_or_deleted()
    {
        await using var sole = await BecomeSoleSuperAdminAsync();
        var rowId = await WithDbAsync(async db =>
            (await db.Queryable<UserRole>().Where(ur => ur.UserId == sole.Id && ur.RoleId == sole.SuperRoleId).FirstAsync()).Id);
        var normalRole = await NormalRoleAsync();

        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{sole.Id}", new { isActive = false }), HttpStatusCode.Conflict, "CONFLICT");
        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{sole.Id}", new { roles = new[] { normalRole } }), HttpStatusCode.Conflict, "CONFLICT");
        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{sole.Id}", new { roles = Array.Empty<Guid>() }), HttpStatusCode.Conflict, "CONFLICT");
        await ShouldBeAsync(sole.Client.DeleteAsync($"/api/items/user/{sole.Id}?purge=true"), HttpStatusCode.Conflict, "CONFLICT");
        await ShouldBeAsync(sole.Client.DeleteAsync($"/api/items/userRole/{rowId}?purge=true"), HttpStatusCode.Conflict, "CONFLICT");

        // Every refusal rolled back: the account is still active and still in the role.
        var state = await WithDbAsync(async db => (
            (await db.Queryable<User>().Where(u => u.Id == sole.Id).FirstAsync()).IsActive,
            await db.Queryable<UserRole>().Where(ur => ur.UserId == sole.Id && ur.RoleId == sole.SuperRoleId).AnyAsync()));
        state.Should().Be((true, true));
    }

    [Fact]
    public async Task The_last_active_super_admin_rule_leaves_other_updates_alone()
    {
        await using var sole = await BecomeSoleSuperAdminAsync();

        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{sole.Id}", new { name = "still fine" }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{sole.Id}", new { isActive = true, roles = new[] { sole.SuperRoleId } }), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_second_active_super_admin_makes_the_first_removable()
    {
        await using var sole = await BecomeSoleSuperAdminAsync();
        var second = await SeedUserAsync(superAdmin: true);

        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{second.Id}", new { isActive = false }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{second.Id}", new { isActive = true }), HttpStatusCode.OK);
        await ShouldBeAsync(Put(sole.Client, $"/api/items/user/{sole.Id}", new { isActive = false }), HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- crafted bodies

    private static Task<HttpResponseMessage> PutRaw(HttpClient c, string path, string json) =>
        c.PutAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<bool> IsSuperAdminAsync(Guid userId)
    {
        var superRole = await SuperRoleIdAsync();
        return await WithDbAsync(db =>
            db.Queryable<UserRole>().Where(ur => ur.UserId == userId && ur.RoleId == superRole).AnyAsync());
    }

    // The role-assignment guard and the relation sync must read the same `roles` property: a body that
    // names it twice, or in two casings, must never reach a super-admin role through a caller with only
    // write on `user`.
    [Fact]
    public async Task A_duplicate_roles_key_cannot_assign_a_super_admin_role()
    {
        var superRole = await SuperRoleIdAsync();
        var (manager, ownId) = await CallerWithIdAsync(["user", "role"], ["user"], ["user"]);

        var resp = await PutRaw(manager, $"/api/items/user/{ownId}", "{\"roles\":[],\"roles\":[\"" + superRole + "\"]}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resp.Content.ReadAsStringAsync());
        (await IsSuperAdminAsync(ownId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_roles_key_repeated_in_another_casing_cannot_assign_a_super_admin_role()
    {
        var superRole = await SuperRoleIdAsync();
        var (manager, ownId) = await CallerWithIdAsync(["user", "role"], ["user"], ["user"]);

        var resp = await PutRaw(manager, $"/api/items/user/{ownId}", "{\"Roles\":[],\"roles\":[\"" + superRole + "\"]}");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, await resp.Content.ReadAsStringAsync());
        (await IsSuperAdminAsync(ownId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_roles_key_in_a_different_casing_alone_never_changes_the_super_admin_status()
    {
        var superRole = await SuperRoleIdAsync();
        var (manager, ownId) = await CallerWithIdAsync(["user", "role"], ["user"], ["user"]);

        var resp = await PutRaw(manager, $"/api/items/user/{ownId}", "{\"Roles\":[\"" + superRole + "\"]}");

        ((int)resp.StatusCode).Should().BeLessThan(500);
        (await IsSuperAdminAsync(ownId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_body_that_names_a_property_twice_is_a_400_on_every_item_write()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        using var manager = await CallerAsync(["user", "role"], ["user", "role"]);

        var create = await manager.PostAsync("/api/items/role",
            new StringContent("{\"name\":\"first\",\"name\":\"second\"}", Encoding.UTF8, "application/json"));
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest, await create.Content.ReadAsStringAsync());
        (await create.Content.ReadAsStringAsync()).Should().Contain("BAD_USER_INPUT");

        var update = await PutRaw(manager, $"/api/items/user/{plain.Id}", "{\"name\":\"a\",\"name\":\"b\"}");
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest, await update.Content.ReadAsStringAsync());

        // Names that differ only in casing are two properties, not a duplicate.
        var cased = await PutRaw(manager, $"/api/items/user/{plain.Id}", "{\"name\":\"a\",\"Name\":\"b\"}");
        ((int)cased.StatusCode).Should().BeLessThan(500);
        cased.StatusCode.Should().NotBe(HttpStatusCode.BadRequest, await cased.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- email match

    [Fact]
    public async Task A_super_admin_stored_with_a_mixed_case_email_is_not_clearable_for_another_caller()
    {
        var superRole = await SuperRoleIdAsync();
        var id = Guid.CreateVersion7();
        var stored = $"Root-{id:N}@Struo.Test";
        await WithDbAsync(async db =>
        {
            await db.Insertable(new User { Id = id, Email = stored, Password = "x", IsActive = true }).ExecuteCommandAsync();
            return await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = id, RoleId = superRole }).ExecuteCommandAsync();
        });
        using var manager = await CallerAsync(["user"], ["user"]);

        var resp = await manager.PostAsJsonAsync("/api/login-locks/query", new { emails = new[] { stored.ToLowerInvariant() } });
        var entry = (await DataAsync(resp)).EnumerateArray().Single();

        entry.GetProperty("clearable").GetBoolean().Should().BeFalse();
    }

    // ---------------------------------------------------------------- serialised last-member check

    public class RecordingProxy : DispatchProxy
    {
        public object Inner { get; set; } = null!;
        public List<string> Calls { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            lock (Calls) Calls.Add(targetMethod!.Name);
            return targetMethod.Invoke(Inner, args);
        }
    }

    // Two super-admins deactivating each other at once must not both pass the count: the check takes a
    // row lock on the super-admin roles before it counts, so the second transaction waits for the first.
    [Fact]
    public async Task The_last_member_check_locks_the_super_admin_roles_before_it_counts()
    {
        var second = await SeedUserAsync(superAdmin: true);
        var calls = new List<string>();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddScoped<ISuperAdminDirectory>(sp =>
            {
                var proxy = DispatchProxy.Create<ISuperAdminDirectory, RecordingProxy>();
                var recording = (RecordingProxy)(object)proxy;
                recording.Inner = new SqlSugarSuperAdminDirectory(sp.GetRequiredService<ISqlSugarClient>());
                recording.Calls = calls;
                return proxy;
            })));
        await factory.EnsureAdminSeededAsync();
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");
        (await admin.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        lock (calls) calls.Clear();

        await ShouldBeAsync(Put(admin, $"/api/items/user/{second.Id}", new { isActive = false }), HttpStatusCode.OK);

        string[] seen;
        lock (calls) seen = calls.ToArray();
        var lockAt = Array.FindIndex(seen, c => c.Contains("Lock", StringComparison.Ordinal));
        var countAt = Array.FindLastIndex(seen, c => c == "ActiveSuperAdminUserIdsAsync");
        lockAt.Should().BeGreaterThanOrEqualTo(0, string.Join(",", seen));
        lockAt.Should().BeLessThan(countAt, string.Join(",", seen));
    }

    // ---------------------------------------------------------------- GraphQL

    private static async Task<JsonElement> GraphQlAsync(HttpClient c, string query) =>
        JsonDocument.Parse(await (await c.PostAsJsonAsync("/graphql", new { query })).Content.ReadAsStringAsync()).RootElement;

    private static string FirstError(JsonElement result) =>
        result.TryGetProperty("errors", out var errors) ? errors[0].GetProperty("message").GetString() ?? "" : "";

    [Fact]
    public async Task Graphql_cannot_delete_a_super_admin_role()
    {
        var superRole = await SuperRoleIdAsync();
        using var admin = await factory.CreateAuthenticatedClientAsync();

        var result = await GraphQlAsync(admin, "mutation { deleteRole(id: \"" + superRole + "\", purge: true) }");

        FirstError(result).Should().Contain("super-admin role cannot be deleted");
        (await WithDbAsync(db => db.Queryable<Role>().Where(r => r.Id == superRole).AnyAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task Graphql_cannot_remove_the_last_active_super_admin()
    {
        await using var sole = await BecomeSoleSuperAdminAsync();

        var result = await GraphQlAsync(sole.Client,
            "mutation { updateUser(id: \"" + sole.Id + "\", input: { isActive: false }) { id } }");

        FirstError(result).Should().Contain("At least one active account");
        (await WithDbAsync(async db => (await db.Queryable<User>().Where(u => u.Id == sole.Id).FirstAsync()).IsActive)).Should().BeTrue();
    }

    [Fact]
    public async Task Graphql_role_assignment_follows_the_same_rules()
    {
        var plain = await SeedUserAsync(superAdmin: false);
        var superRole = await SuperRoleIdAsync();
        var ordinary = await NormalRoleAsync();
        using var manager = await UserManagerAsync();

        var refused = await GraphQlAsync(manager,
            "mutation { updateUser(id: \"" + plain.Id + "\", input: { roles: [\"" + superRole + "\"] }) { id } }");
        FirstError(refused).Should().Contain("Only a super-admin");
        (await IsSuperAdminAsync(plain.Id)).Should().BeFalse();

        var allowed = await GraphQlAsync(manager,
            "mutation { updateUser(id: \"" + plain.Id + "\", input: { roles: [\"" + ordinary + "\"] }) { id } }");
        allowed.TryGetProperty("errors", out _).Should().BeFalse(allowed.ToString());
    }
}
