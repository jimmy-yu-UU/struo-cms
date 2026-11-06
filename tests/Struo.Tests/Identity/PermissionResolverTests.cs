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
}
