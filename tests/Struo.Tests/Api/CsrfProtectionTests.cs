using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// Cookie-authenticated mutations must carry the CSRF header; bearer and login are exempt.
[Collection("ApiIntegration")]
public class CsrfProtectionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static readonly object ArticleBody =
        new { status = "draft", translations = new { en = new { title = "csrf" } } };

    [Fact]
    public async Task Cookie_mutation_without_csrf_header_is_forbidden()
    {
        await _factory.CreateAuthenticatedClientAsync(); // ensure the admin is seeded

        // A bare client carries NO default CSRF header. Login itself is exempt (no session yet),
        // so it succeeds and leaves a session cookie; the follow-up mutation must be rejected.
        var bare = _factory.CreateClient();
        var login = await bare.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        login.EnsureSuccessStatusCode();

        var resp = await bare.PostAsJsonAsync("/api/items/article", ArticleBody);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cookie_mutation_with_csrf_header_is_allowed()
    {
        // The standard helper client sends the CSRF header (like the SPA).
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/items/article", ArticleBody);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Bearer_mutation_without_csrf_header_is_allowed()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var gen = await admin.PostAsync($"/api/users/{_factory.AdminUserId}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;

        // Bearer auth is not forgeable via ambient credentials → CSRF header not required.
        var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await bearer.PostAsJsonAsync("/api/items/article", ArticleBody);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
