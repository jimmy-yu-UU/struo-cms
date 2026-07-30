// tests/Struo.Tests/Support/RepoRootTests.cs
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Support;

public sealed class RepoRootTests
{
    [Fact]
    public void Find_ReturnsTheDirectoryContainingTheSolutionFile()
    {
        var root = RepoRoot.Find();
        File.Exists(Path.Combine(root, "StruoCMS.slnx")).Should().BeTrue();
    }

    [Fact]
    public void SchemaSnapshotPath_PointsAtTheCoreCollectionsFile()
    {
        RepoRoot.SchemaSnapshotPath().Should()
            .Be(Path.Combine(RepoRoot.Find(), "schema", "core-collections.json"));
    }
}
