using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class EffectivePermissionsTests
{
    [Fact]
    public void DenyAll_denies_every_operation()
    {
        var p = EffectivePermissions.DenyAll;
        p.CanRead("article").Should().BeFalse();
        p.CanWrite("article").Should().BeFalse();
        p.CanDelete("article").Should().BeFalse();
        p.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void SuperAdmin_allows_every_operation_on_any_collection()
    {
        var p = new EffectivePermissions(isSuperAdmin: true,
            new Dictionary<string, (bool, bool, bool)>());
        p.CanRead("anything").Should().BeTrue();
        p.CanWrite("anything").Should().BeTrue();
        p.CanDelete("anything").Should().BeTrue();
    }

    [Fact]
    public void Looks_up_per_collection_case_insensitively_and_defaults_deny()
    {
        var p = new EffectivePermissions(isSuperAdmin: false,
            new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = (true, true, false)
            });
        p.CanRead("ARTICLE").Should().BeTrue();
        p.CanWrite("article").Should().BeTrue();
        p.CanDelete("article").Should().BeFalse();
        p.CanRead("user").Should().BeFalse(); // absent → deny
    }
}
