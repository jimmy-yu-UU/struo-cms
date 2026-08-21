using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class UsersControllerRbacTests(ApiFactory factory)
{
    [Fact]
    public async Task NonAdmin_creating_a_user_is_403()
    {
        var (client, _) = await factory.CreateEditorClientAsync(readCollections: [], writeCollections: []);
        var resp = await client.PostAsJsonAsync("/api/users",
            new { email = "new@struo.test", password = "password-123", name = "N" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_creating_a_user_is_201()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/users",
            new { email = $"fresh-{Guid.NewGuid():N}@struo.test", password = "password-123", name = "Fresh" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // Renamed + re-pinned in A8a: this path is 400 INVALID_CURRENT_PASSWORD, not 401. The old
    // assertion pinned a status that made the SPA log the user out on a typo; see
    // PasswordChangeSemanticsTests for the code-level assertions.
    [Fact]
    public async Task Self_password_change_with_wrong_currentPassword_is_400()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123", currentPassword = "WRONG" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Self_password_change_with_correct_currentPassword_is_204()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123", currentPassword = "editor-pw-123" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task NonAdmin_changing_another_users_password_is_403()
    {
        var (client, _) = await factory.CreateEditorClientAsync([], []);
        // Ensure the admin user exists so we have a stable "other user" id to target.
        await factory.CreateAuthenticatedClientAsync();
        var otherUserId = factory.AdminUserId;
        var resp = await client.PutAsJsonAsync($"/api/users/{otherUserId}/password",
            new { newPassword = "new-password-123", currentPassword = "whatever" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
