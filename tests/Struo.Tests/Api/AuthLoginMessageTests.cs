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

[Collection("ApiIntegration")]
public class AuthLoginMessageTests(ApiFactory factory)
{
    private static async Task<string?> ErrorCodeOf(HttpResponseMessage resp)
    {
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    // A deactivated account is only reachable AFTER a correct password (AuthService verifies the
    // hash before it checks IsActive), so telling the caller their account is disabled leaks nothing
    // they had not already proven. Answering "invalid credentials" instead is simply misleading.
    [Fact]
    public async Task Correct_password_on_a_deactivated_account_returns_ACCOUNT_INACTIVE()
    {
        var email = $"inactive-{Guid.NewGuid():N}@struo.test";
        const string password = "inactive-pw-123456";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            await db.Insertable(new User
            {
                Id = Guid.CreateVersion7(), Email = email, Password = hasher.Hash(password),
                Name = "Inactive", IsActive = false
            }).ExecuteCommandAsync();
        }

        var resp = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ErrorCodeOf(resp)).Should().Be("ACCOUNT_INACTIVE");
    }

    // The account-enumeration surface. These two MUST stay indistinguishable — if a future change
    // splits them, an attacker learns which emails are registered without knowing any password.
    [Fact]
    public async Task Wrong_password_and_unknown_email_return_the_same_code()
    {
        var client = await factory.CreateAuthenticatedClientAsync(); // ensures the admin row exists
        _ = client;

        var wrongPassword = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = "definitely-not-the-password" });
        var unknownEmail = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = $"nobody-{Guid.NewGuid():N}@struo.test", password = "definitely-not-the-password" });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ErrorCodeOf(unknownEmail)).Should().Be(await ErrorCodeOf(wrongPassword));
        (await ErrorCodeOf(wrongPassword)).Should().Be("UNAUTHORIZED");
    }
}
