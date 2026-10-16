using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class OidcOptionsTests
{
    [Fact]
    public void Defaults_are_safe_and_disabled()
    {
        // Scopes itself is [] on a raw OidcOptions — ConfigurationBinder appends bound array elements
        // to a non-empty property default instead of replacing it, so the default scopes live in
        // OidcOptions.DefaultScopes and are applied by ApplyCollectionDefaults (called via
        // PostConfigure after binding, in production, at both of AddStruoOidc's bind sites). Calling it
        // here reproduces exactly what a deployment that never configures Scopes ends up with.
        var o = new OidcOptions();
        o.ApplyCollectionDefaults();
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

    [Theory]
    [InlineData("https://login.microsoftonline.com/common/v2.0", true)]
    [InlineData("https://login.microsoftonline.com/organizations/v2.0", true)]
    [InlineData("https://login.microsoftonline.com/Consumers/v2.0", true)]
    [InlineData("https://login.microsoftonline.com/COMMON", true)]
    [InlineData("https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0", true)]
    [InlineData("https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0", false)]
    [InlineData("https://login.microsoftonline.com/commonwealth/v2.0", false)]
    [InlineData("https://login.microsoftonline.com/REPLACE_TENANT_ID/v2.0", false)]
    [InlineData("https://accounts.google.com", false)]
    [InlineData("not a uri", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUnpinnedMultiTenant_matches_whole_path_segments(string? authority, bool expected)
    {
        new OidcOptions { Authority = authority }.IsUnpinnedMultiTenant().Should().Be(expected);
    }

    [Theory]
    [InlineData("tenant-1", null, false)]
    [InlineData("   ", null, true)]
    [InlineData(null, "x.com", false)]
    [InlineData(null, " ", true)]
    [InlineData(null, "", true)]
    public void IsUnpinnedMultiTenant_honours_either_pin(string? tenant, string? domain, bool expected)
    {
        var o = new OidcOptions
        {
            Authority = "https://login.microsoftonline.com/common/v2.0",
            AllowedTenantId = tenant,
            AllowedEmailDomains = domain is null ? [] : [domain]
        };
        o.IsUnpinnedMultiTenant().Should().Be(expected);
    }
}
