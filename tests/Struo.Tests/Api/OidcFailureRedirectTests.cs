using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Struo.Api.Auth;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>A failed or refused external sign-in lands the browser on the SPA login page with a
/// fixed machine-readable code; exception text never reaches the URL.</summary>
public class OidcFailureRedirectTests
{
    private static readonly AuthenticationScheme Scheme =
        new(AuthSchemes.OidcFor("acme"), AuthSchemes.OidcFor("acme"), typeof(OpenIdConnectHandler));

    private static async Task<(int Status, string? Location, bool Handled)> RemoteFailureAsync(Exception failure)
    {
        var http = new DefaultHttpContext();
        var ctx = new RemoteFailureContext(http, Scheme, new OpenIdConnectOptions(), failure);
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
        var http = new DefaultHttpContext();
        var ctx = new AccessDeniedContext(http, Scheme, new OpenIdConnectOptions());

        await OidcWiring.OnAccessDenied(ctx);

        http.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        http.Response.Headers.Location.ToString().Should().Be("/login?error=external_login_denied");
        ctx.Result?.Handled.Should().BeTrue();
    }
}
