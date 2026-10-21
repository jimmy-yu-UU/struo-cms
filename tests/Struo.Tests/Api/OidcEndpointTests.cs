using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class OidcEndpointTests(ApiFactory factory)
{
    // Program registers the OpenIdConnect schemes from builder.Configuration BEFORE the factory's
    // ConfigureAppConfiguration overrides are spliced in (see ContentAssemblyEnvBootstrap), so the
    // providers reach the app through environment variables, set only while the host is built.
    private static readonly Dictionary<string, string> TwoProviders = new()
    {
        ["Oidc__Providers__alpha__Enabled"] = "true",
        ["Oidc__Providers__alpha__DisplayName"] = "Alpha",
        ["Oidc__Providers__alpha__Authority"] = "https://login.example.test/alpha/v2.0",
        ["Oidc__Providers__alpha__ClientId"] = "alpha-client",
        ["Oidc__Providers__alpha__ClientSecret"] = "alpha-secret",
        ["Oidc__Providers__beta__Enabled"] = "true",
        ["Oidc__Providers__beta__DisplayName"] = "Beta",
        ["Oidc__Providers__beta__Authority"] = "https://accounts.example.test",
        ["Oidc__Providers__beta__ClientId"] = "beta-client",
        ["Oidc__Providers__beta__ClientSecret"] = "beta-secret",
        ["Oidc__Providers__off__Enabled"] = "false",
    };

    // The OpenIdConnect handler would fetch discovery metadata from the (fake) authority; a static
    // configuration per scheme keeps the challenge offline.
    private HttpClient TwoProviderClient()
    {
        var f = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            foreach (var (id, authority) in new[]
                     { ("alpha", "https://login.example.test/alpha/v2.0"), ("beta", "https://accounts.example.test") })
            {
                var cfg = new OpenIdConnectConfiguration { AuthorizationEndpoint = authority + "/authorize" };
                services.PostConfigure<OpenIdConnectOptions>(AuthSchemes.OidcFor(id),
                    o => o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg));
            }
        }));

        foreach (var (key, value) in TwoProviders) Environment.SetEnvironmentVariable(key, value);
        try
        {
            return f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }
        finally
        {
            foreach (var key in TwoProviders.Keys) Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Theory]
    [InlineData("/api/auth/login/oidc/microsoft")]
    [InlineData("/api/auth/login/oidc/nope")]
    [InlineData("/api/auth/login/oidc")]
    public async Task Login_oidc_returns_404_for_a_disabled_unknown_or_unaddressed_provider(string path)
    {
        var client = factory.CreateClient();
        var resp = await client.GetAsync(path + "?returnUrl=/admin");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_oidc_returns_404_for_a_provider_configured_but_disabled()
    {
        var resp = await TwoProviderClient().GetAsync("/api/auth/login/oidc/off");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("alpha", "https://login.example.test/alpha/v2.0/authorize", "alpha-client", "signin-oidc-alpha")]
    [InlineData("beta", "https://accounts.example.test/authorize", "beta-client", "signin-oidc-beta")]
    public async Task Login_oidc_challenges_the_named_provider(string id, string authorize, string clientId, string callback)
    {
        var resp = await TwoProviderClient().GetAsync($"/api/auth/login/oidc/{id}?returnUrl=/admin");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect, await resp.Content.ReadAsStringAsync());
        var location = resp.Headers.Location!.ToString();
        location.Should().StartWith(authorize + "?");
        location.Should().Contain($"client_id={clientId}").And.Contain(callback);
    }
}
