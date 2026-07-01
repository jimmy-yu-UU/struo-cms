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
    public void Falls_back_for_non_local_or_empty(string? input) =>
        LocalRedirect.Sanitize(input, "/fallback").Should().Be("/fallback");
}
