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
    private static HttpClient NoRedirectClient(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // The derived factory is only disposed with the parent fixture unless the test disposes it, so
    // every caller owns it with `using var`; a derived host keeps its configuration file watchers
    // open until then.
    private WebApplicationFactory<Program> TwoProviderFactory(params string[] corsOrigins)
    {
        return factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in TwoProviders) b.UseSetting(key, value);
            for (var i = 0; i < corsOrigins.Length; i++) b.UseSetting($"Struo:Cors:AllowedOrigins:{i}", corsOrigins[i]);
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
        using var f = TwoProviderFactory();
        using var client = NoRedirectClient(f);

        var resp = await client.GetAsync("/api/auth/login/oidc/off");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("alpha", "https://login.example.test/alpha/v2.0/authorize", "alpha-client", "signin-oidc-alpha")]
    [InlineData("beta", "https://accounts.example.test/authorize", "beta-client", "signin-oidc-beta")]
    public async Task Login_oidc_challenges_the_named_provider(string id, string authorize, string clientId, string callback)
    {
        using var f = TwoProviderFactory();
        using var client = NoRedirectClient(f);

        var resp = await client.GetAsync($"/api/auth/login/oidc/{id}?returnUrl=/admin");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect, await resp.Content.ReadAsStringAsync());
        var location = resp.Headers.Location!.ToString();
        location.Should().StartWith(authorize + "?");
        location.Should().Contain($"client_id={clientId}").And.Contain(callback);
    }

    // The sign-in destination travels in the protected OIDC state; reading it back shows what the
    // callback will redirect to.
    private static string? StateRedirectUri(WebApplicationFactory<Program> f, HttpResponseMessage challenge)
    {
        var query = System.Web.HttpUtility.ParseQueryString(challenge.Headers.Location!.Query);
        var options = f.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(AuthSchemes.OidcFor("alpha"));
        return options.StateDataFormat.Unprotect(query["state"])!.RedirectUri;
    }

    [Theory]
    [InlineData("https://admin.example.com/articles", "https://admin.example.com/articles")]
    [InlineData("/admin", "/admin")]
    [InlineData("https://evil.example.com/", "/")]
    [InlineData("https://admin.example.com.evil.com/", "/")]
    [InlineData("https://admin.example.com@evil.com/", "/")]
    public async Task With_cors_the_sign_in_destination_is_an_allowed_origin_or_a_relative_path(string returnUrl, string expected)
    {
        using var f = TwoProviderFactory("https://admin.example.com");
        using var client = NoRedirectClient(f);

        var resp = await client.GetAsync($"/api/auth/login/oidc/alpha?returnUrl={Uri.EscapeDataString(returnUrl)}");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        StateRedirectUri(f, resp).Should().Be(expected);
    }

    [Fact]
    public async Task Without_cors_an_absolute_return_url_falls_back_to_the_default()
    {
        using var f = TwoProviderFactory();
        using var client = NoRedirectClient(f);

        var resp = await client.GetAsync($"/api/auth/login/oidc/alpha?returnUrl={Uri.EscapeDataString("https://admin.example.com/a")}");

        StateRedirectUri(f, resp).Should().Be("/");
    }

    [Fact]
    public async Task With_cors_the_correlation_and_nonce_cookies_stay_samesite_none_secure()
    {
        using var f = TwoProviderFactory("https://admin.example.com");
        using var client = NoRedirectClient(f);

        var resp = await client.GetAsync("/api/auth/login/oidc/alpha?returnUrl=%2Fadmin");

        var cookies = resp.Headers.GetValues("Set-Cookie").Select(c => c.ToLowerInvariant()).ToList();
        var oidcCookies = cookies.Where(c => c.Contains("correlation") || c.Contains("nonce")).ToList();
        oidcCookies.Should().HaveCountGreaterThanOrEqualTo(2);
        oidcCookies.Should().OnlyContain(c => c.Contains("samesite=none") && c.Contains("secure"));
    }
}
