using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class OidcOptionsTests
{
    [Fact]
    public void Provider_defaults_are_safe_and_disabled()
    {
        // Scopes itself is [] on a raw provider — ConfigurationBinder appends bound array elements
        // to a non-empty property default instead of replacing it, so the default scopes live in
        // OidcProviderOptions.DefaultScopes and are applied by ApplyCollectionDefaults. Calling it
        // here reproduces exactly what a deployment that never configures Scopes ends up with.
        var o = new OidcProviderOptions();
        o.ApplyCollectionDefaults();
        o.Enabled.Should().BeFalse();
        o.RequireEmailVerified.Should().BeFalse();
        o.CallbackPath.Should().BeNull();
        o.Scopes.Should().BeEquivalentTo(["openid", "email", "profile"]);
        o.AllowedEmailDomains.Should().BeEmpty();
        new OidcOptions().ReturnUrlDefault.Should().Be("/");
        new OidcOptions().Providers.Should().BeEmpty();
    }

    [Fact]
    public void Callback_path_defaults_to_one_per_provider_id()
    {
        new OidcProviderOptions().EffectiveCallbackPath("microsoft").Should().Be("/signin-oidc-microsoft");
        new OidcProviderOptions { CallbackPath = "/custom" }.EffectiveCallbackPath("microsoft").Should().Be("/custom");
        new OidcProviderOptions { CallbackPath = "  " }.EffectiveCallbackPath("google").Should().Be("/signin-oidc-google");
    }

    [Fact]
    public void ToPolicy_projects_the_trust_relevant_subset()
    {
        var o = new OidcProviderOptions
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
        new OidcProviderOptions { Authority = authority }.IsUnpinnedMultiTenant().Should().Be(expected);
    }

    [Theory]
    [InlineData("tenant-1", null, false)]
    [InlineData("   ", null, true)]
    [InlineData(null, "x.com", false)]
    [InlineData(null, " ", true)]
    [InlineData(null, "", true)]
    public void IsUnpinnedMultiTenant_honours_either_pin(string? tenant, string? domain, bool expected)
    {
        var o = new OidcProviderOptions
        {
            Authority = "https://login.microsoftonline.com/common/v2.0",
            AllowedTenantId = tenant,
            AllowedEmailDomains = domain is null ? [] : [domain]
        };
        o.IsUnpinnedMultiTenant().Should().Be(expected);
    }

    [Fact]
    public void EnabledProviders_lists_only_enabled_ones_ordered_by_Order_then_id()
    {
        var o = new OidcOptions
        {
            Providers =
            {
                ["zeta"] = new OidcProviderOptions { Enabled = true, Order = 1 },
                ["off"] = new OidcProviderOptions { Enabled = false },
                ["beta"] = new OidcProviderOptions { Enabled = true },
                ["alpha"] = new OidcProviderOptions { Enabled = true },
                ["first"] = new OidcProviderOptions { Enabled = true, Order = -5 },
            }
        };

        o.EnabledProviders().Select(p => p.Id).Should().Equal("first", "alpha", "beta", "zeta");
    }

    [Fact]
    public void Configuration_enumerates_provider_keys_alphabetically_so_Order_is_the_only_stable_ordering()
    {
        // Documents why the explicit Order exists: IConfiguration does not preserve file order.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Oidc:Providers:microsoft:Enabled"] = "true",
            ["Oidc:Providers:google:Enabled"] = "true",
        }).Build();

        config.GetSection("Oidc:Providers").GetChildren().Select(c => c.Key)
            .Should().Equal("google", "microsoft");

        var bound = config.GetSection("Oidc").Get<OidcOptions>()!;
        bound.EnabledProviders().Select(p => p.Id).Should().Equal("google", "microsoft");
        bound.Providers["microsoft"].Order = -1;
        bound.EnabledProviders().Select(p => p.Id).Should().Equal("microsoft", "google");
    }

    [Fact]
    public void TryGetEnabled_returns_false_for_unknown_or_disabled_ids()
    {
        var o = new OidcOptions
        {
            Providers =
            {
                ["on"] = new OidcProviderOptions { Enabled = true },
                ["off"] = new OidcProviderOptions { Enabled = false },
            }
        };

        o.TryGetEnabled("on", out var on).Should().BeTrue();
        on.Should().BeSameAs(o.Providers["on"]);
        o.TryGetEnabled("off", out _).Should().BeFalse();
        o.TryGetEnabled("nope", out _).Should().BeFalse();
    }
}
