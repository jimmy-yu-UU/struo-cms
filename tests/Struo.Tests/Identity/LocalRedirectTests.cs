using AwesomeAssertions;
using Struo.Api.Auth;
using Xunit;

namespace Struo.Tests.Identity;

public class LocalRedirectTests
{
    [Theory]
    [InlineData("/admin", "/admin")]
    [InlineData("/admin/articles", "/admin/articles")]
    public void Keeps_site_relative_paths(string input, string expected) =>
        LocalRedirect.Sanitize(input, "/").Should().Be(expected);

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("admin")] // not starting with '/'
    [InlineData("/\t/evil.com")] // browsers strip tab/CR/LF, leaving //evil.com
    [InlineData("/\n/evil.com")]
    [InlineData("/\r/evil.com")]
    [InlineData("/admin\u007f")]
    public void Falls_back_for_non_local_or_empty(string? input) =>
        LocalRedirect.Sanitize(input, "/fallback").Should().Be("/fallback");

    private static readonly string[] Allowed = ["https://admin.example.com", "http://localhost:5173"];

    [Theory]
    [InlineData("https://admin.example.com")]
    [InlineData("https://admin.example.com/")]
    [InlineData("https://admin.example.com/articles?x=1#f")]
    [InlineData("https://ADMIN.Example.COM/articles")]
    [InlineData("HTTPS://admin.example.com/articles")]
    [InlineData("https://admin.example.com:443/articles")]
    [InlineData("http://localhost:5173/")]
    public void Keeps_absolute_urls_of_an_allowed_origin(string input) =>
        LocalRedirect.Sanitize(input, "/fallback", Allowed).Should().Be(input);

    [Theory]
    [InlineData("https://admin.example.com:8443/")]
    [InlineData("http://admin.example.com/")]
    [InlineData("https://other.example.com/")]
    [InlineData("https://sub.admin.example.com/")]
    [InlineData("https://example.com/")]
    [InlineData("https://admin.example.com.evil.com/")]
    [InlineData("https://admin.example.com@evil.com/")]
    [InlineData("https://user:pw@admin.example.com/")]
    [InlineData("https://admin.example.com\\@evil.com/")]
    [InlineData("https://admin.example.com\\evil.com/")]
    [InlineData("https:/admin.example.com/")]
    [InlineData("https:admin.example.com")]
    [InlineData("//admin.example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript://admin.example.com/%0aalert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://admin.example.com/")]
    [InlineData("https://admin.example.com./")]
    [InlineData("https://admin.example.com/\t")]
    [InlineData("https://admin.exam\tple.com/")]
    [InlineData("https://admin.example.com /")]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost:5174/")]
    public void Falls_back_for_absolute_urls_outside_the_allowed_origins(string input) =>
        LocalRedirect.Sanitize(input, "/fallback", Allowed).Should().Be("/fallback");

    [Fact]
    public void Relative_paths_still_pass_when_origins_are_configured() =>
        LocalRedirect.Sanitize("/admin", "/fallback", Allowed).Should().Be("/admin");

    [Theory]
    [InlineData("https://admin.example.com/articles")]
    [InlineData("https://elsewhere.example.com/")]
    public void Absolute_urls_fall_back_when_no_origin_is_configured(string input)
    {
        LocalRedirect.Sanitize(input, "/fallback").Should().Be("/fallback");
        LocalRedirect.Sanitize(input, "/fallback", []).Should().Be("/fallback");
    }

    [Theory]
    [InlineData("https://ADMIN.example.com:443/x", "https://admin.example.com")]
    [InlineData("http://localhost:5173/x", "http://localhost:5173")]
    public void AllowedOrigin_returns_the_normalized_origin(string url, string expected) =>
        LocalRedirect.AllowedOrigin(url, Allowed).Should().Be(expected);

    [Theory]
    [InlineData("https://other.example.com/")]
    [InlineData("/relative")]
    [InlineData("")]
    [InlineData(null)]
    public void AllowedOrigin_is_null_for_everything_else(string? url) =>
        LocalRedirect.AllowedOrigin(url, Allowed).Should().BeNull();

    [Theory]
    [InlineData("https://admin.example.com/")]
    [InlineData("https://admin.example.com:443")]
    [InlineData("https://admin.example.com:443/")]
    [InlineData("https://admin.example.com/path")]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    public void Configured_entries_that_are_not_exact_origins_match_nothing(string configured) =>
        LocalRedirect.AllowedOrigin("https://admin.example.com/x", [configured]).Should().BeNull();

    [Theory]
    [InlineData("https://Admin.Example.com", "https://admin.example.com/x")]
    [InlineData("https://admin.example.com:8443", "https://admin.example.com:8443/x")]
    [InlineData("https://bücher.example", "https://xn--bcher-kva.example/x")]
    [InlineData("https://xn--bcher-kva.example", "https://xn--bcher-kva.example/x")]
    [InlineData("https://xn--bcher-kva.example", "https://BüCHER.example/x")]
    public void Configured_entries_match_the_way_the_cors_policy_matches(string configured, string url) =>
        LocalRedirect.AllowedOrigin(url, [configured]).Should().NotBeNull();

    [Theory]
    [InlineData("https://admin.example.com")]
    [InlineData("https://Admin.Example.com")]
    [InlineData("https://admin.example.com/")]
    [InlineData("https://admin.example.com:443")]
    [InlineData("https://admin.example.com:8443")]
    [InlineData("https://bücher.example")]
    [InlineData("https://xn--bcher-kva.example")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://*.example.com")]
    public void Matching_agrees_with_the_cors_policy_on_the_same_entries(string configured)
    {
        var policy = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder()
            .WithOrigins(configured).Build();
        string[] origins =
        [
            "https://admin.example.com", "https://admin.example.com:8443", "http://admin.example.com",
            "https://xn--bcher-kva.example", "http://localhost:5173", "https://other.example.com"
        ];

        foreach (var origin in origins)
            (LocalRedirect.AllowedOrigin(origin + "/page", [configured]) is not null)
                .Should().Be(policy.IsOriginAllowed(origin), $"entry '{configured}' vs origin '{origin}'");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    public void Wildcard_origins_match_nothing(string configured) =>
        LocalRedirect.Sanitize("https://admin.example.com/", "/f", [configured]).Should().Be("/f");

    [Theory]
    [InlineData("/", true)]
    [InlineData("/admin", true)]
    [InlineData("https://admin.example.com/", true)]
    [InlineData("https://evil.com/", false)]
    public void Destination_validity_follows_the_same_rule(string value, bool valid) =>
        LocalRedirect.IsAllowedDestination(value, Allowed).Should().Be(valid);

    [Fact]
    public void ReturnUrlDefault_absolute_is_invalid_without_origins() =>
        LocalRedirect.IsAllowedDestination("https://admin.example.com/", []).Should().BeFalse();

    [Fact]
    public void LoginFailureUrl_prefers_the_return_url_origin_then_the_default_origin_then_relative()
    {
        const string code = "external_login_failed";
        LocalRedirect.LoginFailureUrl(code, "https://admin.example.com/a", "http://localhost:5173/", Allowed)
            .Should().Be("https://admin.example.com/login?error=external_login_failed");
        LocalRedirect.LoginFailureUrl(code, "/relative", "http://localhost:5173/", Allowed)
            .Should().Be("http://localhost:5173/login?error=external_login_failed");
        LocalRedirect.LoginFailureUrl(code, null, "/", Allowed)
            .Should().Be("/login?error=external_login_failed");
        LocalRedirect.LoginFailureUrl(code, "https://evil.com/", "/", Allowed)
            .Should().Be("/login?error=external_login_failed");
    }

    [Fact]
    public void LoginFailureUrl_keeps_the_brackets_of_an_ipv6_literal_origin() =>
        LocalRedirect.LoginFailureUrl("x", "http://[::1]:5173/a", "/", ["http://[::1]:5173"])
            .Should().Be("http://[::1]:5173/login?error=x");

    [Fact]
    public void LoginFailureUrl_is_relative_when_no_origin_is_configured() =>
        LocalRedirect.LoginFailureUrl("external_login_denied", "https://admin.example.com/", "https://admin.example.com/", [])
            .Should().Be("/login?error=external_login_denied");
}
