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

// "Your current password is wrong" is NOT an authentication failure — the caller proved they hold a
// valid session to get here. Returning 401 made the SPA's global unauthorized handler log the user
// out on a typo (apiClient fires it for every 401), so the status is 400 with its own code.
[Collection("ApiIntegration")]
public class PasswordChangeSemanticsTests(ApiFactory factory)
{
    private static async Task<string?> ErrorCodeOf(HttpResponseMessage resp)
    {
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    [Fact]
    public async Task Wrong_current_password_is_400_with_INVALID_CURRENT_PASSWORD()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123", currentPassword = "WRONG" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(resp)).Should().Be("INVALID_CURRENT_PASSWORD");
    }

    [Fact]
    public async Task Missing_current_password_is_400_with_INVALID_CURRENT_PASSWORD()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(resp)).Should().Be("INVALID_CURRENT_PASSWORD");
    }

    // An OIDC-provisioned account has an EMPTY password hash — it has no local password at all.
    // Handing that empty string to the verifier is not acceptable in either outcome: a throw becomes
    // a masked 500, and a false becomes the misleading "your current password is wrong". Guard first.
    [Fact]
    public async Task Self_change_on_an_account_with_no_local_password_is_400_with_NO_LOCAL_PASSWORD()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);

        // Blank the stored hash to reproduce the OIDC-JIT-provisioned shape.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Updateable<User>().SetColumns(u => u.Password == "")
                .Where(u => u.Id == userId).ExecuteCommandAsync();
        }

        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123", currentPassword = "anything" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(resp)).Should().Be("NO_LOCAL_PASSWORD");
    }
}
