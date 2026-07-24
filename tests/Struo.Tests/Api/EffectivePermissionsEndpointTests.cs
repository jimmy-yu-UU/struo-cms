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
            // The item write path validates Required own-fields on every PUT (no partial-patch
            // semantics), so `email` must be echoed back alongside the new `roles` value.
            var item = (await admin.GetFromJsonAsync<JsonElement>($"/api/items/user/{userId}")).GetProperty("data");
            var email = item.GetProperty("email").GetString();
            var body = item.TryGetProperty("version", out var v)
                ? (object)new { email, roles = roleIds, version = v.GetInt32() }
                : new { email, roles = roleIds };
            var update = await admin.PutAsJsonAsync($"/api/items/user/{userId}", body);
            update.IsSuccessStatusCode.Should().BeTrue($"update failed: {await update.Content.ReadAsStringAsync()}");
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
