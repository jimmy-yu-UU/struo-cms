using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class UserProvisioningTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Create_user_hashes_password_and_never_returns_it()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var email = $"new-{Guid.NewGuid():N}@b.com";
        var resp = await admin.PostAsJsonAsync("/api/users", new { email, password = "pw12345678", name = "New" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain("pw12345678");

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "pw12345678" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Duplicate_email_returns_409()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var email = $"dup-{Guid.NewGuid():N}@b.com";
        await admin.PostAsJsonAsync("/api/users", new { email, password = "pw12345678" });
        var second = await admin.PostAsJsonAsync("/api/users", new { email, password = "pw12345678" });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Weak_password_returns_400()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var resp = await admin.PostAsJsonAsync("/api/users", new { email = $"weak-{Guid.NewGuid():N}@b.com", password = "short" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
