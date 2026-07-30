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

    // The matrix PUTs the whole grant set; unsent rows disappear, all-false rows are absent.
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

    // Grants are stored under the canonical collection name regardless of
    // the caller's casing, so the admin matrix (which keys by canonical name) always sees them.
    [Fact]
    public async Task Put_canonicalizes_collection_casing()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var put = await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions",
            new[] { new { collection = "ARTICLE", canRead = true, canWrite = false, canDelete = false } });
        put.IsSuccessStatusCode.Should().BeTrue($"PUT failed: {await put.Content.ReadAsStringAsync()}");

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
