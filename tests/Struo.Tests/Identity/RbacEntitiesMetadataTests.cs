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

    // Permission and UserRole are implementation details behind the Role
    // permission matrix / User.Roles TagSelect; File's admin surface is the media library.
    [Fact]
    public void Permission_UserRole_and_File_collections_are_hidden_from_nav()
    {
        var metas = MetadataScanner.ScanTypes(
            [typeof(Permission), typeof(UserRole), typeof(Struo.Infrastructure.Files.File)]);
        metas.Should().OnlyContain(m => m.Hidden);
    }

    [Fact]
    public void Role_and_User_collections_stay_visible()
    {
        var metas = MetadataScanner.ScanTypes([typeof(Role), typeof(User)]);
        metas.Should().OnlyContain(m => !m.Hidden);
    }
}
