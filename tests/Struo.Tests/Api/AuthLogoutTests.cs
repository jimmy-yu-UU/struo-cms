using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>Logging out removes the server-side session, not only the browser's cookie.</summary>
[Collection("ApiIntegration")]
public class AuthLogoutTests(ApiFactory factory)
{
    private HttpClient NewClient()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        return client;
    }

    private static string? SessionCookieOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var prefix = SessionCookieName.Default + "=";
        var line = values.LastOrDefault(v => v.StartsWith(prefix, StringComparison.Ordinal));
        var value = line?[prefix.Length..].Split(';')[0];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static HttpRequestMessage With(HttpMethod method, string url, string? cookie)
    {
        var request = new HttpRequestMessage(method, url);
        if (cookie is not null) request.Headers.Add("Cookie", $"{SessionCookieName.Default}={cookie}");
        return request;
    }

    [Fact]
    public async Task Logout_answers_204_and_the_session_cookie_no_longer_authenticates()
    {
        await factory.EnsureAdminSeededAsync();
        var client = NewClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        var cookie = SessionCookieOf(login)!;
        (await client.SendAsync(With(HttpMethod.Get, "/api/auth/me", cookie))).StatusCode.Should().Be(HttpStatusCode.OK);

        var logout = await client.SendAsync(With(HttpMethod.Post, "/api/auth/logout", cookie));

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.SendAsync(With(HttpMethod.Get, "/api/auth/me", cookie))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the ticket is gone from the server, so the old cookie is worthless");
    }

    [Fact]
    public async Task Logout_without_a_session_is_refused()
    {
        var response = await NewClient().SendAsync(With(HttpMethod.Post, "/api/auth/logout", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
