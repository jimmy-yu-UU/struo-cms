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

/// <summary>A failed or refused external sign-in lands the browser on the SPA login page with a
/// fixed machine-readable code; exception text never reaches the URL.</summary>
public sealed class OidcFailureRedirectTests : IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose() => _providers.ForEach(p => p.Dispose());

    private static readonly AuthenticationScheme Scheme =
        new(AuthSchemes.OidcFor("acme"), AuthSchemes.OidcFor("acme"), typeof(OpenIdConnectHandler));

    private DefaultHttpContext HttpWith(string[] origins, string returnUrlDefault = "/")
    {
        var settings = origins
            .Select((o, i) => new KeyValuePair<string, string?>($"Struo:Cors:AllowedOrigins:{i}", o))
            .ToList();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddSingleton(Options.Create(new OidcOptions { ReturnUrlDefault = returnUrlDefault }))
            .BuildServiceProvider();
        _providers.Add(services);
        return new DefaultHttpContext { RequestServices = services };
    }

    private async Task<(int Status, string? Location, bool Handled)> RemoteFailureAsync(
        Exception failure, HttpContext? http = null, string? returnUrl = null)
    {
        http ??= HttpWith([]);
        var ctx = new RemoteFailureContext(http, Scheme, new OpenIdConnectOptions(), failure)
        {
            Properties = returnUrl is null ? null : new AuthenticationProperties { RedirectUri = returnUrl }
        };
        await OidcWiring.OnRemoteFailure(ctx);
        return (http.Response.StatusCode, http.Response.Headers.Location.ToString(),
            ctx.Result?.Handled ?? false);
    }

    [Fact]
    public async Task Remote_failure_redirects_to_login_with_the_generic_code()
    {
        var (status, location, handled) = await RemoteFailureAsync(new Exception("invalid_grant: secret detail"));

        status.Should().Be(StatusCodes.Status302Found);
        location.Should().Be("/login?error=external_login_failed");
        handled.Should().BeTrue();
    }

    [Theory]
    [InlineData(ExternalLoginFailure.NoEmail)]
    [InlineData(ExternalLoginFailure.EmailNotVerified)]
    [InlineData(ExternalLoginFailure.DomainNotAllowed)]
    [InlineData(ExternalLoginFailure.TenantNotAllowed)]
    [InlineData(ExternalLoginFailure.Inactive)]
    public async Task Refused_external_login_keeps_its_specific_code(ExternalLoginFailure failure)
    {
        var (_, location, _) = await RemoteFailureAsync(new Exception($"external_login_{failure}"));

        location.Should().Be($"/login?error=external_login_{failure}");
    }

    [Fact]
    public async Task Exception_text_never_reaches_the_url()
    {
        var (_, location, _) = await RemoteFailureAsync(new Exception("external_login_Bogus&x=<script>"));

        location.Should().Be("/login?error=external_login_failed");
    }

    [Fact]
    public async Task Access_denied_redirects_to_login_with_the_denied_code()
    {
        var http = HttpWith([]);
        var ctx = new AccessDeniedContext(http, Scheme, new OpenIdConnectOptions());

        await OidcWiring.OnAccessDenied(ctx);

        http.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        http.Response.Headers.Location.ToString().Should().Be("/login?error=external_login_denied");
        ctx.Result?.Handled.Should().BeTrue();
    }

    private static readonly string[] Origins = ["https://admin.example.com"];

    [Fact]
    public async Task With_cors_the_failure_returns_to_the_origin_of_the_approved_return_url()
    {
        var (_, location, _) = await RemoteFailureAsync(new Exception("x"), HttpWith(Origins), "https://admin.example.com/articles");

        location.Should().Be("https://admin.example.com/login?error=external_login_failed");
    }

    [Fact]
    public async Task With_cors_the_failure_uses_the_default_origin_when_the_return_url_is_relative_or_absent()
    {
        var http = HttpWith(Origins, "https://admin.example.com/");

        (await RemoteFailureAsync(new Exception("x"), http, "/articles")).Location
            .Should().Be("https://admin.example.com/login?error=external_login_failed");
        (await RemoteFailureAsync(new Exception("x"), http)).Location
            .Should().Be("https://admin.example.com/login?error=external_login_failed");
    }

    [Fact]
    public async Task With_cors_a_return_url_outside_the_allowed_origins_is_ignored()
    {
        var (_, location, _) = await RemoteFailureAsync(new Exception("x"), HttpWith(Origins), "https://evil.com/");

        location.Should().Be("/login?error=external_login_failed");
    }

    [Fact]
    public async Task With_cors_a_relative_default_and_no_return_url_stay_relative()
    {
        var (_, location, _) = await RemoteFailureAsync(new Exception("x"), HttpWith(Origins));

        location.Should().Be("/login?error=external_login_failed");
    }

    [Fact]
    public async Task Without_cors_an_absolute_return_url_does_not_change_the_relative_redirect()
    {
        var (_, location, _) = await RemoteFailureAsync(new Exception("x"), HttpWith([]), "https://admin.example.com/a");

        location.Should().Be("/login?error=external_login_failed");
    }

    [Fact]
    public async Task With_cors_access_denied_returns_to_the_admin_origin()
    {
        var http = HttpWith(Origins);
        var ctx = new AccessDeniedContext(http, Scheme, new OpenIdConnectOptions())
        {
            Properties = new AuthenticationProperties { RedirectUri = "https://admin.example.com/x" }
        };

        await OidcWiring.OnAccessDenied(ctx);

        http.Response.Headers.Location.ToString()
            .Should().Be("https://admin.example.com/login?error=external_login_denied");
    }
}
