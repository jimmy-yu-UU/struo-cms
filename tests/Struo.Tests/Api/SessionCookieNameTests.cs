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
public class SessionCookieNameTests(ApiFactory factory) : IDisposable
{
    private readonly List<IDisposable> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
    }

    private const string CustomName = "acme.session";

    private static readonly object ArticleBody =
        new { status = "draft", translations = new { en = new { title = "cookie-name" } } };

    private HttpClient CustomNameClient(bool handleCookies)
    {
        var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Cookie:Name"] = CustomName })));
        _hosts.Add(host);
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
        var forged = await client.PostAsJsonAsync("/api/items/fxArticle", ArticleBody);

        forged.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Session_rotation_follows_the_configured_name()
    {
        var (emailA, passwordA, _) = await factory.SeedEditorAsync(["fxArticle"], []);
        var (emailB, passwordB, idB) = await factory.SeedEditorAsync(["fxArticle"], []);
        using var client = CustomNameClient(handleCookies: false);
        client.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");

        var loginA = await client.PostAsJsonAsync("/api/auth/login", new { email = emailA, password = passwordA });
        var cookieA = CookieValue(loginA);

        // B signs in while A's cookie is presented under the custom name: A's session must die, never become B's.
        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = emailB, password = passwordB }),
        };
        second.Headers.Add("Cookie", $"{CustomName}={cookieA}");
        var loginB = await client.SendAsync(second);
        loginB.EnsureSuccessStatusCode();
        CookieValue(loginB).Should().NotBe(cookieA);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("Cookie", $"{CustomName}={cookieA}");
        (await client.SendAsync(me)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _ = idB;
    }

    private static string CookieValue(HttpResponseMessage response)
    {
        var line = response.Headers.GetValues("Set-Cookie")
            .Last(c => c.StartsWith(CustomName + "=", StringComparison.Ordinal));
        return line[(CustomName.Length + 1)..].Split(';')[0];
    }

    private HttpClient HostClient(string environment, params (string Key, string Value)[] settings)
    {
        var host = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
                settings.ToDictionary(s => s.Key, s => (string?)s.Value)));
        });
        _hosts.Add(host);
        var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");
        return client;
    }

    private async Task<string> LoginSessionCookieAsync(HttpClient client, string expectedName)
    {
        await factory.EnsureAdminSeededAsync();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var cookies = login.Headers.GetValues("Set-Cookie").ToList();
        cookies.Should().Contain(c => c.StartsWith(expectedName + "=", StringComparison.Ordinal),
            string.Join(" | ", cookies));
        return cookies.Last(c => c.StartsWith(expectedName + "=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Unnamed_always_secure_cookie_is_host_prefixed(string environment)
    {
        using var client = HostClient(environment);

        var cookie = await LoginSessionCookieAsync(client, "__Host-struo.session");

        var attrs = cookie.ToLowerInvariant();
        attrs.Should().Contain("; secure").And.Contain("path=/").And.NotContain("domain=");
    }

    [Fact]
    public async Task Unnamed_cookie_in_Development_keeps_the_plain_name()
    {
        using var client = HostClient("Development");

        var cookie = await LoginSessionCookieAsync(client, "struo.session");

        cookie.ToLowerInvariant().Should().NotContain("; secure");
    }

    [Fact]
    public async Task Unnamed_cookie_with_RequireSecure_false_keeps_the_plain_name()
    {
        using var client = HostClient("Production", ("Auth:Cookie:RequireSecure", "false"));

        await LoginSessionCookieAsync(client, "struo.session");
    }

    [Fact]
    public async Task Unnamed_cookie_with_a_cors_origin_is_host_prefixed()
    {
        using var client = HostClient("Development", ("Struo:Cors:AllowedOrigins:0", "https://admin.example.test"));

        await LoginSessionCookieAsync(client, "__Host-struo.session");
    }

    [Fact]
    public async Task An_explicit_name_wins_in_Production()
    {
        using var client = HostClient("Production", ("Auth:Cookie:Name", CustomName));

        await LoginSessionCookieAsync(client, CustomName);
    }

    [Fact]
    public async Task Session_rotation_follows_the_default_host_name()
    {
        const string hostName = "__Host-struo.session";
        var (emailA, passwordA, _) = await factory.SeedEditorAsync(["fxArticle"], []);
        var (emailB, passwordB, _) = await factory.SeedEditorAsync(["fxArticle"], []);
        using var client = HostClient("Production");

        var loginA = await client.PostAsJsonAsync("/api/auth/login", new { email = emailA, password = passwordA });
        var cookieA = HostCookieValue(loginA);

        // B signs in while A's cookie is presented under the default name: A's session must die.
        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = emailB, password = passwordB }),
        };
        second.Headers.Add("Cookie", $"{hostName}={cookieA}");
        var loginB = await client.SendAsync(second);
        loginB.EnsureSuccessStatusCode();
        HostCookieValue(loginB).Should().NotBe(cookieA);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("Cookie", $"{hostName}={cookieA}");
        (await client.SendAsync(me)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string HostCookieValue(HttpResponseMessage response)
    {
        const string prefix = "__Host-struo.session=";
        var line = response.Headers.GetValues("Set-Cookie").Last(c => c.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..].Split(';')[0];
    }

    [Fact]
    public async Task The_csrf_guard_follows_the_default_host_name()
    {
        using var client = HostClient("Production");
        var setCookie = await LoginSessionCookieAsync(client, "__Host-struo.session");
        var value = setCookie.Split(';')[0]["__Host-struo.session=".Length..];
        client.DefaultRequestHeaders.Remove(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/fxArticle")
        {
            Content = JsonContent.Create(ArticleBody),
        };
        request.Headers.Add("Cookie", $"__Host-struo.session={value}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_cookie_under_the_old_name_is_not_a_session_cookie_any_more()
    {
        using var client = CustomNameClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/fxArticle")
        {
            Content = JsonContent.Create(ArticleBody),
        };
        request.Headers.Add("Cookie", "struo.session=abc");

        var response = await client.SendAsync(request);

        // Not guarded as a session cookie (no 403 from the CSRF check), and not authenticated either.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
