// tests/Struo.Tests.Template/DockerfilePublishConventionTests.cs
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards the API image's publish step. The image carries the native files of its own platform only,
/// which needs a runtime identifier on <c>dotnet publish</c>; <c>restore</c> takes the same identifier,
/// so <c>publish --no-restore</c> finds a matching assets file.
/// </summary>
public sealed class DockerfilePublishConventionTests
{
    private static string BuildStage()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot.Find(), "Dockerfile"));
        var start = text.IndexOf("AS build", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = text.IndexOf("AS runtime", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return text[start..end];
    }

    private static string Instruction(string stage, string verb)
    {
        var start = stage.IndexOf($"dotnet {verb} ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the build stage runs dotnet {verb}");
        var end = stage.IndexOf(';', start);
        return stage[start..(end < 0 ? stage.Length : end)];
    }

    [Fact]
    public void Restore_and_publish_take_the_same_runtime_identifier()
    {
        var stage = BuildStage();

        Instruction(stage, "restore").Should().Contain("--runtime \"$rid\"");
        Instruction(stage, "publish").Should().Contain("--runtime \"$rid\"");
    }

    [Fact]
    public void Publish_is_framework_dependent_and_does_not_restore_again()
    {
        var publish = Instruction(BuildStage(), "publish");

        publish.Should().Contain("--self-contained false");
        publish.Should().Contain("--no-restore");
    }

    [Fact]
    public void Architecture_comes_from_the_machine_not_from_a_builder_specific_argument()
    {
        var stage = BuildStage();

        stage.Should().Contain("uname -m");
        stage.Should().NotContain("TARGETARCH", "the legacy builder does not set it");
        stage.Should().Contain("linux-x64");
        stage.Should().Contain("linux-arm64");
    }
}
