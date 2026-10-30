using System.Net.Http.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>Without cross-origin configuration the session cookie keeps the framework's SameSite=Lax;
/// the cross-origin variant is covered by <see cref="CorsAndCookieTests"/>.</summary>
[Collection("ApiIntegration")]
public class SessionCookieSameSiteTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_without_cors_sets_a_samesite_lax_cookie()
    {
        await factory.EnsureAdminSeededAsync();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });

        login.EnsureSuccessStatusCode();
        var setCookie = string.Join(";", login.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        setCookie.Should().Contain("samesite=lax");
        setCookie.Should().NotContain("samesite=none");
    }
}
