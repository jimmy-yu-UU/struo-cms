using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>Each enabled provider gets its own OpenIdConnect scheme, callback path and trust policy.</summary>
public class OidcMultiProviderTests
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Oidc:Providers:microsoft:Enabled"] = "true",
        ["Oidc:Providers:microsoft:DisplayName"] = "Microsoft 365",
        ["Oidc:Providers:microsoft:Authority"] = "https://login.example.test/ms/v2.0",
        ["Oidc:Providers:microsoft:ClientId"] = "ms-client",
        ["Oidc:Providers:microsoft:ClientSecret"] = "ms-secret",
        ["Oidc:Providers:microsoft:AllowedTenantId"] = "tenant-1",
        ["Oidc:Providers:google:Enabled"] = "true",
        ["Oidc:Providers:google:DisplayName"] = "Google",
        ["Oidc:Providers:google:Authority"] = "https://accounts.example.test",
        ["Oidc:Providers:google:ClientId"] = "g-client",
        ["Oidc:Providers:google:ClientSecret"] = "g-secret",
        ["Oidc:Providers:google:CallbackPath"] = "/custom-google",
        ["Oidc:Providers:google:RequireEmailVerified"] = "true",
        ["Oidc:Providers:off:Enabled"] = "false",
        ["Oidc:Providers:off:Authority"] = "https://off.example.test",
    };

    private static ServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDataProtection();
        services.AddStruoOidc(config);
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Each_enabled_provider_registers_its_own_scheme()
    {
        using var provider = Build();
        var schemes = (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync())
            .Select(s => s.Name).ToList();

        schemes.Should().Contain([AuthSchemes.OidcFor("microsoft"), AuthSchemes.OidcFor("google")]);
        schemes.Should().NotContain(AuthSchemes.OidcFor("off"));
        AuthSchemes.OidcFor("microsoft").Should().NotBe(AuthSchemes.OidcFor("google"));
    }

    [Fact]
    public void Schemes_carry_distinct_callback_paths_and_client_settings()
    {
        using var provider = Build();
        var monitor = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        var ms = monitor.Get(AuthSchemes.OidcFor("microsoft"));
        var g = monitor.Get(AuthSchemes.OidcFor("google"));

        ms.CallbackPath.Value.Should().Be("/signin-oidc-microsoft");
        g.CallbackPath.Value.Should().Be("/custom-google");
        ms.ClientId.Should().Be("ms-client");
        g.ClientId.Should().Be("g-client");
        ms.Authority.Should().Be("https://login.example.test/ms/v2.0");
        g.Authority.Should().Be("https://accounts.example.test");
        ms.SignInScheme.Should().Be(AuthSchemes.Cookie);
    }

    private sealed class RecordingLogin : IExternalLoginService
    {
        public ExternalLoginPolicy? Policy { get; private set; }

        public Task<ExternalLoginResult> ResolveOrProvisionAsync(
            ExternalIdentity identity, ExternalLoginPolicy policy, CancellationToken ct = default)
        {
            Policy = policy;
            return Task.FromResult(ExternalLoginResult.Fail(ExternalLoginFailure.Inactive));
        }
    }

    private static async Task<ExternalLoginPolicy?> PolicyUsedForAsync(string providerId)
    {
        var login = new RecordingLogin();
        using var provider = Build(s => s.AddSingleton<IExternalLoginService>(login));
        var http = new DefaultHttpContext { RequestServices = provider };

        await OidcWiring.ResolveLocalPrincipalAsync(http, new ClaimsPrincipal(new ClaimsIdentity()), providerId);
        return login.Policy;
    }

    [Fact]
    public async Task Token_validation_applies_the_policy_of_the_provider_that_signed_in()
    {
        var ms = await PolicyUsedForAsync("microsoft");
        ms!.AllowedTenantId.Should().Be("tenant-1");
        ms.RequireEmailVerified.Should().BeFalse();

        var g = await PolicyUsedForAsync("google");
        g!.AllowedTenantId.Should().BeNull();
        g.RequireEmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Each_schemes_TokenValidated_event_applies_its_own_providers_policy()
    {
        var login = new RecordingLogin();
        using var provider = Build(s => s.AddSingleton<IExternalLoginService>(login));
        var monitor = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        async Task<ExternalLoginPolicy> PolicyAsync(string id)
        {
            var name = AuthSchemes.OidcFor(id);
            var options = monitor.Get(name);
            var http = new DefaultHttpContext { RequestServices = provider };
            var scheme = new AuthenticationScheme(name, name, typeof(OpenIdConnectHandler));
            var ctx = new TokenValidatedContext(http, scheme, options, new ClaimsPrincipal(new ClaimsIdentity()),
                new AuthenticationProperties());
            await options.Events.TokenValidated(ctx);
            return login.Policy!;
        }

        var ms = await PolicyAsync("microsoft");
        ms.AllowedTenantId.Should().Be("tenant-1");
        ms.RequireEmailVerified.Should().BeFalse();
        var g = await PolicyAsync("google");
        g.AllowedTenantId.Should().BeNull();
        g.RequireEmailVerified.Should().BeTrue();
    }

    [Fact]
    public void Schemes_carry_distinct_sign_out_paths()
    {
        using var provider = Build();
        var monitor = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var ms = monitor.Get(AuthSchemes.OidcFor("microsoft"));
        var g = monitor.Get(AuthSchemes.OidcFor("google"));

        ms.SignedOutCallbackPath.Value.Should().Be("/signout-callback-oidc-microsoft");
        g.SignedOutCallbackPath.Value.Should().Be("/signout-callback-oidc-google");
        ms.RemoteSignOutPath.Value.Should().Be("/signout-oidc-microsoft");
        g.RemoteSignOutPath.Value.Should().Be("/signout-oidc-google");
    }

    [Fact]
    public async Task Token_validation_for_an_unknown_provider_is_refused_without_calling_the_login_service()
    {
        var login = new RecordingLogin();
        using var provider = Build(s => s.AddSingleton<IExternalLoginService>(login));
        var http = new DefaultHttpContext { RequestServices = provider };

        var result = await OidcWiring.ResolveLocalPrincipalAsync(
            http, new ClaimsPrincipal(new ClaimsIdentity()), "nope");

        result.Principal.Should().BeNull();
        login.Policy.Should().BeNull();
    }
}
