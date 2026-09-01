using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using AwesomeAssertions;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// A password change (self-service or admin-reset — both run through the same
/// UsersController.ChangePassword action) must kill every existing cookie session for that user, and
/// must not touch any other user's sessions.
/// </summary>
[Collection("ApiIntegration")]
public class PasswordChangeSessionRevocationTests(ApiFactory factory)
{
    private async Task<HttpClient> LoginAsAsync(string email, string password)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<(string email, string password, Guid userId)> SeedUserAsync()
    {
        var userId = Guid.CreateVersion7();
        const string password = "session-pw-123";
        var email = $"session-{userId:N}@struo.test";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Insertable(new User
        {
            Id = userId, Email = email, Password = hasher.Hash(password), IsActive = true,
        }).ExecuteCommandAsync();
        return (email, password, userId);
    }

    [Fact]
    public async Task Self_service_password_change_revokes_every_existing_session_for_that_user()
    {
        var (email, password, userId) = await SeedUserAsync();

        // Two separate "browsers" logged in as the same user.
        var browserA = await LoginAsAsync(email, password);
        var browserB = await LoginAsAsync(email, password);

        var change = await browserA.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-session-pw-456", currentPassword = password });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await browserA.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await browserB.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_reset_revokes_every_existing_session_for_the_target_user()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (email, password, userId) = await SeedUserAsync();
        var browser = await LoginAsAsync(email, password);

        var change = await admin.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-session-pw-456" });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await browser.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_change_does_not_affect_another_users_session()
    {
        var (emailA, passwordA, userIdA) = await SeedUserAsync();
        var (emailB, passwordB, _) = await SeedUserAsync();

        var browserA = await LoginAsAsync(emailA, passwordA);
        var browserB = await LoginAsAsync(emailB, passwordB);

        var change = await browserA.PutAsJsonAsync($"/api/users/{userIdA}/password",
            new { newPassword = "new-session-pw-456", currentPassword = passwordA });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await browserB.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Regression: bearer tokens are out of scope for this feature (they have their own
    /// separate revocation path, DELETE /api/users/{id}/access-token) — revoking a user's cookie
    /// sessions on password change must not disturb that same user's bearer access token.</summary>
    [Fact]
    public async Task Password_change_does_not_disturb_the_users_bearer_access_token()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (email, password, userId) = await SeedUserAsync();

        var gen = await admin.PostAsync($"/api/users/{userId}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await gen.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("data").GetProperty("token").GetString()!;

        var bearerClient = factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        (await bearerClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var change = await admin.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-session-pw-456" });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await bearerClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
