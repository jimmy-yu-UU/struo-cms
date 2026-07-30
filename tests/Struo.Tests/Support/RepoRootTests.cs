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
    public void Find_ThrowsWhenNoAncestorContainsTheSolutionFile()
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), "RepoRootTests-" + Guid.NewGuid());
        Directory.CreateDirectory(probeDirectory);

        try
        {
            var act = () => RepoRoot.Find(probeDirectory);
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(probeDirectory, recursive: true);
        }
    }

    [Fact]
    public void SchemaSnapshotPath_PointsAtTheCoreCollectionsFile()
    {
        var path = RepoRoot.SchemaSnapshotPath();

        path.Should().EndWith(Path.Combine("schema", "core-collections.json"));

        var root = Path.GetDirectoryName(Path.GetDirectoryName(path));
        File.Exists(Path.Combine(root!, "StruoCMS.slnx")).Should().BeTrue();
    }
}
