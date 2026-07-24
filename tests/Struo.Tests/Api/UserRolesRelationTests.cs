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

        // The item write path validates Required own-fields on every PUT (no partial-patch
        // semantics), so `email` must be echoed back alongside the new `roles` value. Also echo
        // the optimistic-concurrency token if the item carries one (D2 convention).
        var item = (await admin.GetFromJsonAsync<JsonElement>($"/api/items/user/{userId}")).GetProperty("data");
        var email = item.GetProperty("email").GetString();
        var body = item.TryGetProperty("version", out var v)
            ? (object)new { email, roles = new[] { roleId }, version = v.GetInt32() }
            : new { email, roles = new[] { roleId } };
        var update = await admin.PutAsJsonAsync($"/api/items/user/{userId}", body);
        update.IsSuccessStatusCode.Should().BeTrue($"update failed: {await update.Content.ReadAsStringAsync()}");

        // M2M relations are only expanded when requested via `?deep=` (mirrors the frontend's
        // itemsApi.get({ deep: editableRelations }) convention — see ItemFormView.vue).
        var after = (await admin.GetFromJsonAsync<JsonElement>($"/api/items/user/{userId}?deep=roles")).GetProperty("data");
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
