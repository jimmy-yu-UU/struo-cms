using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class PermissionResolverTests
{
    [Fact]
    public void Any_super_admin_role_yields_super_snapshot()
    {
        var data = new RolePermissionData(
            [new RoleRow(Guid.NewGuid(), "admin", true)],
            []);
        PermissionResolver.Resolve(data).IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void Unions_permission_rows_across_multiple_roles()
    {
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var data = new RolePermissionData(
            [new RoleRow(r1, "editor", false), new RoleRow(r2, "publisher", false)],
            [
                new PermissionRow(r1, "article", true, true, false),
                new PermissionRow(r2, "article", false, false, true)
            ]);

        var p = PermissionResolver.Resolve(data);
        p.IsSuperAdmin.Should().BeFalse();
        p.CanRead("article").Should().BeTrue();
        p.CanWrite("article").Should().BeTrue();
        p.CanDelete("article").Should().BeTrue(); // unioned from role 2
        p.CanRead("user").Should().BeFalse();
    }

    [Fact]
    public void Empty_data_denies_everything()
    {
        var p = PermissionResolver.Resolve(new RolePermissionData([], []));
        p.IsSuperAdmin.Should().BeFalse();
        p.CanRead("article").Should().BeFalse();
    }

    [Fact]
    public void Capabilities_are_unioned_across_roles_and_unknown_ones_are_absent()
    {
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var data = new RolePermissionData(
            [new RoleRow(r1, "a", false), new RoleRow(r2, "b", false)], [],
            [new RoleCapabilityRow(r1, "login.unlock"), new RoleCapabilityRow(r2, "reports.export")]);

        var p = PermissionResolver.Resolve(data);

        p.HasCapability("login.unlock").Should().BeTrue();
        p.HasCapability("reports.export").Should().BeTrue();
        p.HasCapability("other").Should().BeFalse();
        p.Capabilities.Should().BeEquivalentTo("login.unlock", "reports.export");
    }

    [Fact]
    public void Super_admin_has_every_capability_without_any_row()
    {
        var data = new RolePermissionData([new RoleRow(Guid.NewGuid(), "admin", true)], []);

        var p = PermissionResolver.Resolve(data);

        p.HasCapability("login.unlock").Should().BeTrue();
        p.HasCapability("anything.at.all").Should().BeTrue();
    }

    [Fact]
    public void Deny_all_and_empty_data_hold_no_capability()
    {
        EffectivePermissions.DenyAll.HasCapability("login.unlock").Should().BeFalse();
        PermissionResolver.Resolve(new RolePermissionData([], [])).HasCapability("login.unlock").Should().BeFalse();
    }

    // A store written by a fork may hand back capability rows for the public role, which every caller holds.
    [Fact]
    public void A_capability_row_on_the_public_role_is_dropped_whatever_the_store_returned()
    {
        var pub = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var data = new RolePermissionData(
            [new RoleRow(pub, "public", false), new RoleRow(editor, "editor", false)], [],
            [new RoleCapabilityRow(pub, "login.unlock"), new RoleCapabilityRow(editor, "reports.export")]);

        var p = PermissionResolver.Resolve(data);

        p.HasCapability("login.unlock").Should().BeFalse();
        p.HasCapability("reports.export").Should().BeTrue();
    }
}
