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

    // The hidden junctions carry no grant of their own: they answer with the collection that owns them.
    [Theory]
    [InlineData("userRole", "user")]
    [InlineData("USERROLE", "user")]
    [InlineData("permission", "role")]
    public void A_hidden_junction_follows_its_owning_collection(string junction, string owner)
    {
        var grants = (Read: true, Write: false, Delete: false);
        var readOnly = new EffectivePermissions(false, new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            { [owner] = grants });
        readOnly.CanRead(junction).Should().BeTrue();
        readOnly.CanWrite(junction).Should().BeFalse();
        readOnly.CanDelete(junction).Should().BeFalse();

        var writer = new EffectivePermissions(false, new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            { [owner] = (false, true, false) });
        writer.CanRead(junction).Should().BeFalse();
        writer.CanWrite(junction).Should().BeTrue();
        writer.CanDelete(junction).Should().BeTrue(); // removing a link is a write on the owner, not a delete grant
    }

    [Fact]
    public void A_grant_named_on_a_hidden_junction_is_ignored()
    {
        var p = new EffectivePermissions(false, new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            { ["userRole"] = (true, true, true), ["permission"] = (true, true, true) });

        p.CanRead("userRole").Should().BeFalse();
        p.CanWrite("userRole").Should().BeFalse();
        p.CanWrite("permission").Should().BeFalse();
    }
}
