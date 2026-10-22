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

/// <summary>The OIDC authorize redirect carries the configured <c>Oidc:Providers:{id}:Prompt</c>; blank sends none.</summary>
public class OidcPromptTests
{
    private static async Task<string?> AuthorizePromptAsync(string? prompt, bool setPrompt = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Oidc:Providers:acme:Enabled"] = "true",
            ["Oidc:Providers:acme:DisplayName"] = "Acme",
            ["Oidc:Providers:acme:Authority"] = "https://login.example.test/tenant/v2.0",
            ["Oidc:Providers:acme:ClientId"] = "client",
            ["Oidc:Providers:acme:ClientSecret"] = "secret",
        };
        if (setPrompt) settings["Oidc:Providers:acme:Prompt"] = prompt;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddStruoOidc(config);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthSchemes.OidcFor("acme"));
        var scheme = new AuthenticationScheme(AuthSchemes.OidcFor("acme"), AuthSchemes.OidcFor("acme"), typeof(OpenIdConnectHandler));
        var ctx = new RedirectContext(new DefaultHttpContext(), scheme, options, new AuthenticationProperties())
        {
            ProtocolMessage = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectMessage(),
        };

        await options.Events.RedirectToIdentityProvider(ctx);
        return ctx.ProtocolMessage.Prompt;
    }

    [Fact]
    public async Task Authorize_redirect_carries_select_account_by_default()
        => (await AuthorizePromptAsync(null, setPrompt: false)).Should().Be("select_account");

    [Fact]
    public async Task Configured_prompt_is_sent()
        => (await AuthorizePromptAsync("login")).Should().Be("login");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_prompt_sends_none(string prompt)
        => (await AuthorizePromptAsync(prompt)).Should().BeNull();

    [Theory]
    [InlineData("select_account", true)]
    [InlineData("login consent", true)]
    [InlineData("none", true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("pick_one", false)]
    [InlineData("login bogus", false)]
    [InlineData("SELECT_ACCOUNT", false)]
    public void Prompt_validation_accepts_only_known_values(string prompt, bool valid)
        => new OidcProviderOptions { Prompt = prompt }.HasValidPrompt().Should().Be(valid);

    [Fact]
    public void Prompt_defaults_to_select_account()
        => new OidcProviderOptions().Prompt.Should().Be("select_account");
}
