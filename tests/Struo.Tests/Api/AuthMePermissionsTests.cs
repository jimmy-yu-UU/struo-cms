// tests/Struo.Tests/Api/AuthMePermissionsTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public sealed class AuthMePermissionsTests
{
    [Fact]
    public async Task Me_for_super_admin_reports_isSuperAdmin_true()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateAuthenticatedClientAsync(); // admin role is IsSuperAdmin
        var doc = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var data = doc.GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Me_for_editor_lists_only_granted_collections()
    {
        using var factory = new ApiFactory();
        var (client, _) = await factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: ["article"]);

        var doc = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var data = doc.GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeFalse();

        var perms = data.GetProperty("permissions");
        // Role grant on "article":
        perms.GetProperty("article").GetProperty("read").GetBoolean().Should().BeTrue();
        perms.GetProperty("article").GetProperty("write").GetBoolean().Should().BeTrue();
        perms.GetProperty("article").GetProperty("delete").GetBoolean().Should().BeFalse();
        // "user" is neither role-granted nor public-read → absent from the map.
        perms.TryGetProperty("user", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Me_includes_email_and_name()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var data = doc.GetProperty("data");
        data.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
        data.TryGetProperty("name", out _).Should().BeTrue(); // nullable but always present
    }
}
