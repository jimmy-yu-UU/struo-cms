using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// A password change, by the user or by an administrator, also revokes the user's API access token.
[Collection("ApiIntegration")]
public class PasswordChangeRevokesAccessTokenTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<HttpClient> IssueBearerClientAsync(HttpClient admin, Guid userId)
    {
        var gen = await admin.PostAsync($"/api/users/{userId}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;
        var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await bearer.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "the token works before the change");
        return bearer;
    }

    private static async Task AssertNoTokenAsync(HttpClient admin, Guid userId)
    {
        var res = await admin.GetAsync($"/api/users/{userId}/access-token");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await res.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("hasToken").GetBoolean()
            .Should().BeFalse();
    }

    [Fact]
    public async Task Admin_password_change_revokes_the_users_access_token()
    {
        var (admin, _) = await _factory.CreateOwnSuperAdminClientAsync();
        var (_, userId) = await _factory.CreateEditorClientAsync([], []);
        var bearer = await IssueBearerClientAsync(admin, userId);

        (await admin.PutAsJsonAsync($"/api/users/{userId}/password", new { newPassword = "admin-reset-pw-123456" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await bearer.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNoTokenAsync(admin, userId);
    }

    [Fact]
    public async Task Self_service_password_change_revokes_the_users_access_token()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var (client, userId) = await _factory.CreateEditorClientAsync([], []);
        var bearer = await IssueBearerClientAsync(admin, userId);

        (await client.PutAsJsonAsync($"/api/users/{userId}/password",
                new { newPassword = "self-rotated-pw-123456", currentPassword = "editor-pw-123" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await bearer.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNoTokenAsync(admin, userId);
    }
}
