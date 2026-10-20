// tests/Struo.Tests/Template/HealthcheckConventionTests.cs
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards the API health probes. The image's <c>HEALTHCHECK</c> and the Compose <c>api</c>
/// healthcheck both read the port from <c>ASPNETCORE_HTTP_PORTS</c> (first entry of a
/// <c>;</c>-separated list), so changing the listening port never leaves a probe on a stale literal.
/// </summary>
public sealed class HealthcheckConventionTests
{
    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot.Find(), relativePath));

    [Fact]
    public void Dockerfile_healthcheck_reads_the_configured_port()
    {
        var text = Read("Dockerfile");
        var start = text.IndexOf("HEALTHCHECK", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var instruction = text[start..text.IndexOf("ENTRYPOINT", start, StringComparison.Ordinal)];

        instruction.Should().Contain("ASPNETCORE_HTTP_PORTS");
        instruction.Should().NotContain("localhost:8080");
    }

    [Fact]
    public void Compose_api_healthcheck_reads_the_configured_port_with_escaped_dollars()
    {
        var lines = Read("docker-compose.yml").Split('\n');
        var probe = lines.Single(l => l.Contains("/health/live", StringComparison.Ordinal) && l.Contains("test:", StringComparison.Ordinal));

        probe.Should().Contain("ASPNETCORE_HTTP_PORTS");
        probe.Should().NotContain("localhost:8080");
        probe.Should().Contain("$$", "compose interpolates a single $ before the container shell sees it");
        probe.Replace("$$", "").Should().NotContain("$", "every $ in a compose command must be escaped as $$");
    }
}
