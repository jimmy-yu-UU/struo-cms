using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class SystemCapabilityCatalogTests
{
    private sealed class Source(params SystemCapability[] items) : ISystemCapabilitySource
    {
        public IReadOnlyList<SystemCapability> Capabilities { get; } = items;
    }

    [Fact]
    public void Core_list_holds_login_unlock_with_label_and_description_keys()
    {
        SystemCapabilities.LoginUnlock.Should().Be("login.unlock");
        var unlock = SystemCapabilities.Core.Should().ContainSingle(c => c.Code == "login.unlock").Subject;
        unlock.LabelKey.Should().Be("capabilities.login.unlock.label");
        unlock.DescriptionKey.Should().Be("capabilities.login.unlock.description");
    }

    [Fact]
    public void Catalog_without_sources_is_the_core_list()
    {
        var catalog = new SystemCapabilityCatalog([]);

        catalog.All.Select(c => c.Code).Should().Equal(SystemCapabilities.Core.Select(c => c.Code));
        catalog.IsKnown("login.unlock").Should().BeTrue();
        catalog.IsKnown("nope").Should().BeFalse();
        catalog.IsKnown("LOGIN.UNLOCK").Should().BeFalse("codes are compared exactly");
    }

    [Fact]
    public void A_fork_source_adds_to_the_core_list()
    {
        var extra = new SystemCapability("reports.export", "capabilities.reports.export.label", "capabilities.reports.export.description");

        var catalog = new SystemCapabilityCatalog([new Source(extra)]);

        catalog.All.Select(c => c.Code).Should().Equal("login.unlock", "reports.export");
        catalog.IsKnown("reports.export").Should().BeTrue();
    }

    [Fact]
    public void A_duplicate_code_fails_fast_naming_the_code()
    {
        var dupe = new SystemCapability("login.unlock", "a", "b");

        var act = () => new SystemCapabilityCatalog([new Source(dupe)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*login.unlock*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_code_fails_fast(string code)
    {
        var act = () => new SystemCapabilityCatalog([new Source(new SystemCapability(code, "a", "b"))]);

        act.Should().Throw<InvalidOperationException>();
    }
}
