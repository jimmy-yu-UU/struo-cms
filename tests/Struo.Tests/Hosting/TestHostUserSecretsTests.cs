using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Tests.Api;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// No test host that starts the API reads the developer's user secrets, which hold real connections. Only
/// the provider types and file names are inspected; no configuration value is read.
/// </summary>
[Collection("ApiIntegration")]
public sealed class TestHostUserSecretsTests(ApiFactory factory)
{
    private static IReadOnlyList<string> SourcesOf(IServiceProvider services) =>
        ForkHostConfigurationTests.UserSecretsSources(services.GetRequiredService<IConfiguration>());

    [Fact]
    public void The_shared_ApiFactory_host_has_no_user_secrets_source() =>
        SourcesOf(factory.Services).Should().BeEmpty();

    [Fact]
    public void A_derived_Development_host_has_no_user_secrets_source()
    {
        using var derived = factory.WithWebHostBuilder(_ => { });

        SourcesOf(derived.Services).Should().BeEmpty();
    }

    [Fact]
    public void The_CORS_host_has_no_user_secrets_source()
    {
        using var cors = new CorsAndCookieTests.CorsApiFactory();

        SourcesOf(cors.Services).Should().BeEmpty();
    }

    [Fact]
    public void Every_test_class_that_derives_WebApplicationFactory_of_Program_uses_the_shared_removal()
    {
        var root = RepoRoot.Find();
        var offenders = new[] { "tests", "samples" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(path => (path, text: File.ReadAllText(path)))
            .Where(f => f.text.Contains(": WebApplicationFactory<Program>")
                        && !f.text.Contains("HostIsolation.RemoveUserSecrets"))
            .Select(f => Path.GetRelativePath(root, f.path))
            .ToList();

        offenders.Should().BeEmpty(
            "a host that derives WebApplicationFactory<Program> directly must call HostIsolation.RemoveUserSecrets");
    }
}
