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
    // providers reach the app as host settings (UseSetting), which are part of the builder's
    // configuration from the start.
    private static readonly Dictionary<string, string> TwoProviders = new()
    {
        ["Oidc:Providers:alpha:Enabled"] = "true",
        ["Oidc:Providers:alpha:DisplayName"] = "Alpha",
        ["Oidc:Providers:alpha:Authority"] = "https://login.example.test/alpha/v2.0",
        ["Oidc:Providers:alpha:ClientId"] = "alpha-client",
        ["Oidc:Providers:alpha:ClientSecret"] = "alpha-secret",
        ["Oidc:Providers:beta:Enabled"] = "true",
        ["Oidc:Providers:beta:DisplayName"] = "Beta",
        ["Oidc:Providers:beta:Authority"] = "https://accounts.example.test",
        ["Oidc:Providers:beta:ClientId"] = "beta-client",
        ["Oidc:Providers:beta:ClientSecret"] = "beta-secret",
        ["Oidc:Providers:off:Enabled"] = "false",
    };

    // The OpenIdConnect handler would fetch discovery metadata from the (fake) authority; a static
    // configuration per scheme keeps the challenge offline.
    private HttpClient TwoProviderClient()
    {
        var f = factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in TwoProviders) b.UseSetting(key, value);
            b.ConfigureTestServices(services =>
            {
                foreach (var (id, authority) in new[]
                         { ("alpha", "https://login.example.test/alpha/v2.0"), ("beta", "https://accounts.example.test") })
                {
                    var cfg = new OpenIdConnectConfiguration { AuthorizationEndpoint = authority + "/authorize" };
                    services.PostConfigure<OpenIdConnectOptions>(AuthSchemes.OidcFor(id),
                        o => o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg));
                }
            });
        });

        return f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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
