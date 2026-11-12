using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using AwesomeAssertions;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// A provider's <c>/signout-oidc-{id}</c> path is not an endpoint: a request to it, which a page can make
/// with nothing but a cross-site GET, ends nobody's session.
/// </summary>
[Collection("ApiIntegration")]
public class OidcRemoteSignOutTests(ApiFactory factory)
{
    private WebApplicationFactory<Program> HostWithProvider() =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Oidc:Providers:alpha:Enabled", "true");
            b.UseSetting("Oidc:Providers:alpha:DisplayName", "Alpha");
            b.UseSetting("Oidc:Providers:alpha:Authority", "https://login.example.test/alpha/v2.0");
            b.UseSetting("Oidc:Providers:alpha:ClientId", "alpha-client");
            b.UseSetting("Oidc:Providers:alpha:ClientSecret", "alpha-secret");
            b.ConfigureTestServices(services =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    AuthorizationEndpoint = "https://login.example.test/alpha/v2.0/authorize"
                };
                services.PostConfigure<OpenIdConnectOptions>(AuthSchemes.OidcFor("alpha"),
                    o => o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
            });
        });

    [Theory]
    [InlineData("/signout-oidc-alpha")]
    [InlineData("/signout-oidc-alpha?sid=anything&iss=https%3A%2F%2Flogin.example.test%2Falpha%2Fv2.0")]
    [InlineData("/signout-oidc")]
    public async Task A_remote_sign_out_request_does_not_end_the_session(string path)
    {
        var (email, password, _) = await factory.SeedEditorAsync([], []);
        using var host = HostWithProvider();
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false, AllowAutoRedirect = false,
        });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var prefix = SessionCookieName.Default + "=";
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Last(v => v.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].Split(';')[0];

        HttpRequestMessage With(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", $"{SessionCookieName.Default}={cookie}");
            return request;
        }

        var signOut = await client.SendAsync(With(path));
        var me = await client.SendAsync(With("/api/auth/me"));

        signOut.StatusCode.Should().Be(HttpStatusCode.NotFound);
        signOut.Headers.Contains("Set-Cookie").Should().BeFalse("the request must not touch the session cookie");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
