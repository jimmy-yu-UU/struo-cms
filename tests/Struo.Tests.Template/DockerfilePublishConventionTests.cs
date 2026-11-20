// tests/Struo.Tests.Template/DockerfilePublishConventionTests.cs
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards the API image's publish step. The image carries the native files of its own platform only,
/// which needs a runtime identifier on <c>dotnet restore</c> and <c>dotnet publish</c>, taken from
/// <c>TARGETARCH</c> with <c>uname -m</c> as the fallback for the legacy builder. The runtime stage
/// checks that those native files load on its own platform.
/// </summary>
public sealed partial class DockerfilePublishConventionTests
{
    private static string Dockerfile() =>
        File.ReadAllText(Path.Combine(RepoRoot.Find(), "Dockerfile"));

    private static string BuildStage(string dockerfile)
    {
        var start = dockerfile.IndexOf("AS build", StringComparison.OrdinalIgnoreCase);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = dockerfile.IndexOf("AS runtime", start, StringComparison.OrdinalIgnoreCase);
        end.Should().BeGreaterThan(start);
        return dockerfile[start..end];
    }

    private static string RuntimeStage(string dockerfile)
    {
        var start = dockerfile.IndexOf("AS runtime", StringComparison.OrdinalIgnoreCase);
        start.Should().BeGreaterThanOrEqualTo(0);
        return dockerfile[start..];
    }

    private static readonly string[] InstructionEnds = [";", "&&", "dotnet "];

    private static string Instruction(string stage, string verb)
    {
        var start = stage.IndexOf($"dotnet {verb} ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the build stage runs dotnet {verb}");
        var from = start + 1;
        var ends = InstructionEnds
            .Select(marker => stage.IndexOf(marker, from, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .ToList();
        return stage[start..(ends.Count == 0 ? stage.Length : ends.Min())];
    }

    [GeneratedRegex(@"(^|\s)(--runtime|-r)(\s+|=)\S+")]
    private static partial Regex RuntimeOption();

    [Fact]
    public void Restore_and_publish_take_the_same_runtime_identifier()
    {
        var stage = BuildStage(Dockerfile());

        RuntimeOption().IsMatch(Instruction(stage, "restore")).Should().BeTrue();
        RuntimeOption().IsMatch(Instruction(stage, "publish")).Should().BeTrue();
    }

    [Fact]
    public void A_restore_without_a_runtime_identifier_is_detected_in_an_and_chain()
    {
        const string stage = "RUN dotnet restore a.csproj && dotnet publish a.csproj --runtime \"$rid\"";

        RuntimeOption().IsMatch(Instruction(stage, "restore")).Should().BeFalse();
        RuntimeOption().IsMatch(Instruction(stage, "publish")).Should().BeTrue();
    }

    [Fact]
    public void Publish_is_framework_dependent_and_does_not_restore_again()
    {
        var publish = Instruction(BuildStage(Dockerfile()), "publish");

        publish.Should().Contain("--self-contained false");
        publish.Should().Contain("--no-restore");
    }

    [Fact]
    public void Architecture_comes_from_the_target_first_with_the_machine_as_fallback()
    {
        var stage = BuildStage(Dockerfile());

        stage.Should().Contain("ARG TARGETARCH");
        stage.Should().Contain("${TARGETARCH:-$(uname -m)}");
        stage.Should().Contain("linux-x64");
        stage.Should().Contain("linux-arm64");
    }

    [Fact]
    public void The_runtime_stage_checks_that_the_published_native_libraries_load()
    {
        var dockerfile = Dockerfile();
        var runtime = RuntimeStage(dockerfile);
        var copy = runtime.IndexOf("COPY --from=build /app/publish", StringComparison.Ordinal);
        var check = runtime.IndexOf("ldd \"$f\"", StringComparison.Ordinal);

        copy.Should().BeGreaterThanOrEqualTo(0);
        check.Should().BeGreaterThan(copy, "the check runs after the publish output is copied");
        runtime.Should().Contain("/app/*.so");
        runtime.Should().Contain("not found");
        runtime.Should().Contain("runtime identifier must match");
    }
}
