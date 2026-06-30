using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class RbacPermissionServiceTests
{
    [Fact]
    public void Delegates_to_the_current_snapshot()
    {
        var holder = new CurrentPermissions();
        holder.Set(new EffectivePermissions(false,
            new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = (true, false, false)
            }));
        var svc = new RbacPermissionService(holder);

        svc.CanRead("article").Should().BeTrue();
        svc.CanWrite("article").Should().BeFalse();
        svc.CanRead("user").Should().BeFalse();
    }

    [Fact]
    public void Default_holder_denies_until_set()
    {
        var svc = new RbacPermissionService(new CurrentPermissions());
        svc.CanRead("article").Should().BeFalse();
    }

    [Fact]
    public void ReadableFields_returns_all_fields_for_now()
    {
        var svc = new RbacPermissionService(new CurrentPermissions());
        svc.ReadableFields("article", ["title", "body"]).Should().BeEquivalentTo(["title", "body"]);
    }
}
