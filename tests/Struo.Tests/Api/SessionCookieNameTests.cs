// tests/Struo.Tests/Api/SessionCookieNameTests.cs
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// <c>Auth:Cookie:Name</c> renames the session cookie, and every reader of that name follows: the cookie
/// handler that writes it and the CSRF guard that keys off its presence.
/// </summary>
[Collection("ApiIntegration")]
public class SessionCookieNameTests(ApiFactory factory)
{
    private const string CustomName = "acme.session";

    private static readonly object ArticleBody =
        new { status = "draft", translations = new { en = new { title = "cookie-name" } } };

    private HttpClient CustomNameClient(bool handleCookies)
    {
        var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Cookie:Name"] = CustomName })));
        return host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = handleCookies,
        });
    }

    [Fact]
    public async Task Login_sets_the_cookie_under_the_configured_name()
    {
        await factory.EnsureAdminSeededAsync();
        using var client = CustomNameClient(handleCookies: false);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });

        login.EnsureSuccessStatusCode();
        var cookies = login.Headers.GetValues("Set-Cookie").ToList();
        cookies.Should().Contain(c => c.StartsWith(CustomName + "=", StringComparison.Ordinal));
        cookies.Should().NotContain(c => c.StartsWith("struo.session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_csrf_guard_follows_the_configured_name()
    {
        await factory.EnsureAdminSeededAsync();
        using var client = CustomNameClient(handleCookies: true);
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();

        // Signed in under the custom name and no CSRF header: the guard must fire.
        var forged = await client.PostAsJsonAsync("/api/items/article", ArticleBody);

        forged.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_cookie_under_the_old_name_is_not_a_session_cookie_any_more()
    {
        using var client = CustomNameClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/article")
        {
            Content = JsonContent.Create(ArticleBody),
        };
        request.Headers.Add("Cookie", "struo.session=abc");

        var response = await client.SendAsync(request);

        // Not guarded as a session cookie (no 403 from the CSRF check), and not authenticated either.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
