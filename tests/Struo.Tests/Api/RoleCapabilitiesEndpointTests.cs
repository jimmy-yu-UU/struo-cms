using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class RoleCapabilitiesEndpointTests(ApiFactory factory)
{
    private const string Unlock = "login.unlock";

    private static async Task<Guid> CreateRoleAsync(HttpClient admin)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/role", new { name = $"cap-{Guid.NewGuid():N}" });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<Guid> RoleOfAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return (await db.Queryable<UserRole>().Where(ur => ur.UserId == userId).FirstAsync()).RoleId;
    }

    private static async Task<string[]> CodesAsync(HttpClient client, string url)
    {
        var json = await client.GetFromJsonAsync<JsonElement>(url);
        return json.GetProperty("data").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    private static async Task<string[]> MyCapabilitiesAsync(HttpClient client)
    {
        var json = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        return json.GetProperty("data").GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Fact]
    public async Task Non_super_admin_is_403_on_every_capability_endpoint_even_when_holding_the_capability()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (editor, userId) = await factory.CreateEditorClientAsync([], []);
        var roleId = await RoleOfAsync(userId);
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new[] { Unlock })).EnsureSuccessStatusCode();

        (await editor.GetAsync($"/api/roles/{roleId}/capabilities")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", Array.Empty<string>())).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await editor.GetAsync("/api/capabilities")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/capabilities")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_role_is_404()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.GetAsync($"/api/roles/{Guid.NewGuid()}/capabilities")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync($"/api/roles/{Guid.NewGuid()}/capabilities", new[] { Unlock })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Catalog_endpoint_lists_login_unlock_with_label_keys()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var json = await admin.GetFromJsonAsync<JsonElement>("/api/capabilities");

        var unlock = json.GetProperty("data").EnumerateArray().Single(e => e.GetProperty("code").GetString() == Unlock);
        unlock.GetProperty("labelKey").GetString().Should().Be("capabilities.login.unlock.label");
        unlock.GetProperty("descriptionKey").GetString().Should().Be("capabilities.login.unlock.description");
    }

    [Fact]
    public async Task Put_replaces_the_set_and_get_returns_it_with_duplicates_collapsed()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        (await CodesAsync(admin, $"/api/roles/{roleId}/capabilities")).Should().BeEmpty();

        var put = await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new[] { Unlock, Unlock });
        put.IsSuccessStatusCode.Should().BeTrue(await put.Content.ReadAsStringAsync());
        (await CodesAsync(admin, $"/api/roles/{roleId}/capabilities")).Should().Equal(Unlock);

        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", Array.Empty<string>())).EnsureSuccessStatusCode();
        (await CodesAsync(admin, $"/api/roles/{roleId}/capabilities")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("no.such.capability")]
    [InlineData("")]
    [InlineData("LOGIN.UNLOCK")]
    public async Task Put_with_a_code_outside_the_catalog_is_400_and_changes_nothing(string code)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new[] { Unlock })).EnsureSuccessStatusCode();

        var bad = await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new[] { Unlock, code });

        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodesAsync(admin, $"/api/roles/{roleId}/capabilities")).Should().Equal(Unlock);
    }

    [Fact]
    public async Task Put_with_a_body_that_is_not_an_array_of_strings_is_400()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);

        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", (string[]?)null)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new { a = 1 })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    // Permissions are resolved on every request (nothing is cached), so a change applies to the holder's very next request.
    [Fact]
    public async Task A_grant_and_a_revoke_apply_to_the_holders_next_request()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (editor, userId) = await factory.CreateEditorClientAsync([], []);
        var roleId = await RoleOfAsync(userId);
        (await MyCapabilitiesAsync(editor)).Should().BeEmpty();

        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new[] { Unlock })).EnsureSuccessStatusCode();
        (await MyCapabilitiesAsync(editor)).Should().Equal(Unlock);

        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", Array.Empty<string>())).EnsureSuccessStatusCode();
        (await MyCapabilitiesAsync(editor)).Should().BeEmpty();
    }

    [Fact]
    public async Task Me_lists_every_catalog_capability_for_a_super_admin()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        (await MyCapabilitiesAsync(admin)).Should().Contain(Unlock);
    }

    [Fact]
    public async Task Deleting_the_role_removes_its_capability_from_the_holder()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (editor, userId) = await factory.CreateEditorClientAsync([], []);
        var roleId = await RoleOfAsync(userId);
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new[] { Unlock })).EnsureSuccessStatusCode();
        (await MyCapabilitiesAsync(editor)).Should().Equal(Unlock);

        (await admin.DeleteAsync($"/api/items/role/{roleId}")).IsSuccessStatusCode.Should().BeTrue();

        (await MyCapabilitiesAsync(editor)).Should().BeEmpty();
    }
}
