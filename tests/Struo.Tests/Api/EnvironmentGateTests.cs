// tests/Struo.Tests/Api/EnvironmentGateTests.cs
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// OpenAPI/Scalar exposure and the cookie Secure policy resolve from explicit settings
/// (<c>OpenApi:Enabled</c>, <c>Auth:Cookie:RequireSecure</c>); an unset value falls back to the
/// environment: Development-only for OpenAPI, Secure everywhere except Development for the cookie.
/// </summary>
[Collection("ApiIntegration")]
public class EnvironmentGateTests(ApiFactory factory)
{
    private HttpClient CreateClient(string environment, string? key = null, string? value = null) =>
        factory
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                if (key is not null)
                    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
                        new Dictionary<string, string?> { [key] = value }));
            })
            .CreateClient();

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Staging", HttpStatusCode.NotFound)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    [InlineData("UAT", HttpStatusCode.NotFound)]
    public async Task OpenApi_is_served_only_in_Development_by_default(string environment, HttpStatusCode expected)
    {
        using var client = CreateClient(environment);

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Staging", "true", HttpStatusCode.OK)]
    [InlineData("Development", "false", HttpStatusCode.NotFound)]
    public async Task OpenApi_Enabled_overrides_the_environment(string environment, string enabled, HttpStatusCode expected)
    {
        using var client = CreateClient(environment, "OpenApi:Enabled", enabled);

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> LoginSetCookieAsync(HttpClient client)
    {
        await factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return string.Join(";", response.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Staging", true)]
    [InlineData("Production", true)]
    [InlineData("UAT", true)]
    public async Task Session_cookie_is_Secure_unless_the_environment_is_Development(string environment, bool secure)
    {
        using var client = CreateClient(environment);

        var setCookie = await LoginSetCookieAsync(client);

        setCookie.Contains("secure").Should().Be(secure, setCookie);
    }

    [Fact]
    public async Task RequireSecure_false_removes_Secure_in_Staging()
    {
        using var client = CreateClient("Staging", "Auth:Cookie:RequireSecure", "false");

        var setCookie = await LoginSetCookieAsync(client);

        setCookie.Should().NotContain("secure");
    }

    [Fact]
    public async Task RequireSecure_true_adds_Secure_in_Development()
    {
        using var client = CreateClient("Development", "Auth:Cookie:RequireSecure", "true");

        var setCookie = await LoginSetCookieAsync(client);

        setCookie.Should().Contain("secure");
    }
}
