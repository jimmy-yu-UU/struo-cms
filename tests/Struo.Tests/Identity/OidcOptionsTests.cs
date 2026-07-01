using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class OidcOptionsTests
{
    [Fact]
    public void Defaults_are_safe_and_disabled()
    {
        var o = new OidcOptions();
        o.Enabled.Should().BeFalse();
        o.RequireEmailVerified.Should().BeFalse();
        o.CallbackPath.Should().Be("/signin-oidc");
        o.ReturnUrlDefault.Should().Be("/");
        o.Scopes.Should().BeEquivalentTo(["openid", "email", "profile"]);
        o.AllowedEmailDomains.Should().BeEmpty();
    }

    [Fact]
    public void ToPolicy_projects_the_trust_relevant_subset()
    {
        var o = new OidcOptions
        {
            RequireEmailVerified = true, AllowedTenantId = "tenant-1", AllowedEmailDomains = ["corp.com"]
        };
        var p = o.ToPolicy();
        p.RequireEmailVerified.Should().BeTrue();
        p.AllowedTenantId.Should().Be("tenant-1");
        p.AllowedEmailDomains.Should().BeEquivalentTo(["corp.com"]);
    }
}
