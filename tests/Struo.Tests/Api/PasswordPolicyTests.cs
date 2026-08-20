using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// The policy lives in one place (PasswordPolicy) and is enforced at BOTH call sites —
// POST /api/users (create) and PUT /api/users/{id}/password (change). A regression that
// re-inlines the check at one site only would pass a single-site test, so both are pinned.
[Collection("ApiIntegration")]
public class PasswordPolicyTests(ApiFactory factory)
{
    [Fact]
    public async Task Create_rejects_a_password_below_the_configured_minimum()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/users",
            new { email = $"short-{Guid.NewGuid():N}@struo.test", password = "1234567", name = "Short" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_accepts_a_password_exactly_at_the_minimum()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/users",
            new { email = $"exact-{Guid.NewGuid():N}@struo.test", password = "12345678", name = "Exact" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_rejects_a_password_above_the_configured_maximum()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/users",
            new { email = $"long-{Guid.NewGuid():N}@struo.test", password = new string('x', 129), name = "Long" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Change_password_rejects_a_new_password_below_the_minimum()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "1234567", currentPassword = "editor-pw-123" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Change_password_rejects_a_new_password_above_the_maximum()
    {
        var (client, userId) = await factory.CreateEditorClientAsync([], []);
        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = new string('x', 129), currentPassword = "editor-pw-123" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
