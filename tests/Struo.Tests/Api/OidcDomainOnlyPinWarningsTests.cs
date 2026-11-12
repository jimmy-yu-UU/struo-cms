using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public sealed class OidcDomainOnlyPinWarningsTests
{
    private const string MultiTenant = "https://login.example.test/common/v2.0";
    private const string SingleTenant = "https://login.example.test/0f0e0d0c-0b0a-0908-0706-050403020100/v2.0";

    private static OidcProviderOptions Provider(
        string authority, string? tenant = null, string[]? domains = null, bool enabled = true) =>
        new()
        {
            Enabled = enabled, Authority = authority, AllowedTenantId = tenant, AllowedEmailDomains = domains ?? [],
        };

    private static ListLogger Run(params (string Id, OidcProviderOptions Options)[] providers)
    {
        var options = new OidcOptions();
        foreach (var (id, provider) in providers) options.Providers[id] = provider;
        var logger = new ListLogger();
        OidcDomainOnlyPinWarnings.Log(options, logger);
        return logger;
    }

    [Fact]
    public void A_multi_tenant_authority_pinned_by_domain_alone_warns_naming_the_provider()
    {
        var entry = Run(("acme", Provider(MultiTenant, domains: ["corp.example"]))).Entries.Should().ContainSingle().Which;

        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("acme").And.Contain("AllowedTenantId").And.Contain("AllowedEmailDomains");
        entry.Message.Should().NotContain("corp.example");
    }

    [Fact]
    public void Every_such_provider_is_named_in_the_one_warning()
    {
        var entry = Run(
                ("one", Provider(MultiTenant, domains: ["a.example"])),
                ("two", Provider("https://login.example.test/organizations/v2.0", domains: ["b.example"])))
            .Entries.Should().ContainSingle().Which;

        entry.Message.Should().Contain("one").And.Contain("two");
    }

    [Fact]
    public void A_tenant_pin_alone_or_with_domains_logs_nothing()
    {
        Run(("a", Provider(MultiTenant, tenant: "tenant-1"))).Entries.Should().BeEmpty();
        Run(("b", Provider(MultiTenant, tenant: "tenant-1", domains: ["corp.example"]))).Entries.Should().BeEmpty();
    }

    [Fact]
    public void A_single_tenant_authority_with_domains_logs_nothing() =>
        Run(("a", Provider(SingleTenant, domains: ["corp.example"]))).Entries.Should().BeEmpty();

    [Fact]
    public void A_disabled_provider_logs_nothing() =>
        Run(("a", Provider(MultiTenant, domains: ["corp.example"], enabled: false))).Entries.Should().BeEmpty();

    [Fact]
    public void Blank_domains_do_not_count_as_a_pin() =>
        Run(("a", Provider(MultiTenant, domains: [" "]))).Entries.Should().BeEmpty();

    [Fact]
    public void An_unpinned_multi_tenant_authority_is_left_to_the_startup_validator() =>
        Run(("a", Provider(MultiTenant))).Entries.Should().BeEmpty();
}
