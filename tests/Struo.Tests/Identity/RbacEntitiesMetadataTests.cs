// tests/Struo.Tests/Identity/RbacEntitiesMetadataTests.cs
using AwesomeAssertions;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Identity;

public class RbacEntitiesMetadataTests
{
    [Fact]
    public void Scans_rbac_collections_with_expected_identity()
    {
        var collections = MetadataScanner.ScanTypes(
            [typeof(Role), typeof(Permission), typeof(UserRole)]);

        var names = collections.Select(c => c.Name).ToList();
        names.Should().Contain(["role", "permission", "userRole"]);

        var permission = collections.Single(c => c.Name == "permission");
        permission.Fields.Select(f => f.Name)
            .Should().Contain(["roleId", "collection", "canRead", "canWrite", "canDelete"]);
    }
}
