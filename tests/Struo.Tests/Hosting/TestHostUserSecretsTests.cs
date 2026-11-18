using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Struo.Tests.Api;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// No test host that starts the API reads the developer's user secrets, which hold real connections. Only
/// provider types, source paths and whether a file exists are inspected; no configuration value is read.
/// </summary>
[Collection("ApiIntegration")]
public sealed partial class TestHostUserSecretsTests(ApiFactory factory)
{
    private static IReadOnlyList<string> SourcesOf(IServiceProvider services) =>
        HostIsolation.UserSecretsSources(services.GetRequiredService<IConfiguration>());

    private static string ApiUserSecretsPath() =>
        PathHelper.GetSecretsPathFromSecretsId(
            typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId);

    [Fact]
    public void The_process_looks_for_the_API_user_secrets_file_under_the_pinned_empty_directory()
    {
        var path = ApiUserSecretsPath();

        path.Should().StartWith(TestHostEnvironment.EmptyAppDataRoot);
        File.Exists(path).Should().BeFalse($"nothing exists at {path}");
    }

    [Fact]
    public void The_source_Program_adds_before_the_host_is_built_points_at_no_file()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });

        var sources = builder.Configuration.Sources.Where(HostIsolation.IsUserSecretsSource)
            .Cast<JsonConfigurationSource>().ToList();

        sources.Should().ContainSingle("the framework adds the source whether or not the file exists");
        var file = sources[0].FileProvider?.GetFileInfo(sources[0].Path!);
        (file?.Exists ?? false).Should().BeFalse($"nothing exists at {file?.PhysicalPath}");
    }

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

    [Theory]
    [InlineData("secrets.json", true)]
    [InlineData("my-secrets.json", false)]
    [InlineData("appsettings.secrets.json", false)]
    public void Only_the_user_secrets_source_is_removed(string path, bool removed)
    {
        var configuration = new ConfigurationBuilder();
        configuration.Add(new JsonConfigurationSource { Path = path, Optional = true });

        HostIsolation.RemoveUserSecrets(configuration);

        configuration.Sources.Should().HaveCount(removed ? 0 : 1);
    }

    [Fact]
    public void Every_test_class_that_derives_WebApplicationFactory_of_Program_uses_the_shared_removal()
    {
        var root = RepoRoot.Find();
        var offenders = new[] { "tests", "samples" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path) && !path.EndsWith(nameof(TestHostUserSecretsTests) + ".cs"))
            .Where(path => HostsWithoutRemoval(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        offenders.Should().BeEmpty(
            "each host that derives or creates WebApplicationFactory<Program> calls HostIsolation.RemoveUserSecrets");
    }

    [Theory]
    [InlineData("class A : WebApplicationFactory<Program> { void F(){ HostIsolation.RemoveUserSecrets(c); } }", false)]
    [InlineData("class A : WebApplicationFactory<Program> { }", true)]
    [InlineData("class A :\n  global::Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory< Program > { }", true)]
    [InlineData("var f = new WebApplicationFactory<Program>();", true)]
    [InlineData("class A : WebApplicationFactory<Program> { } // HostIsolation.RemoveUserSecrets(c)", true)]
    [InlineData("class A : WebApplicationFactory<Program> { /* HostIsolation.RemoveUserSecrets(c) */ }", true)]
    [InlineData("class A : WebApplicationFactory<Program> { void F(){ HostIsolation . RemoveUserSecrets (c); } }", false)]
    [InlineData("class A : WebApplicationFactory<Program> { void F(){ HostIsolation.RemoveUserSecrets(c); } }\n" +
                "class B : WebApplicationFactory<Program> { }", true)]
    [InlineData("class A : StruoApiFactory { }", false)]
    public void The_scan_recognizes_a_host_without_the_removal(string source, bool offends) =>
        HostsWithoutRemoval(source).Should().Be(offends);

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    private static bool HostsWithoutRemoval(string source)
    {
        var code = CommentRegex().Replace(source, " ");
        return FactoryRegex().Matches(code).Count > RemovalRegex().Matches(code).Count;
    }

    [GeneratedRegex(@"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"(:\s*|\bnew\s+)(global::)?(\w+\s*\.\s*)*WebApplicationFactory\s*<\s*(global::)?(\w+\s*\.\s*)*Program\s*>")]
    private static partial Regex FactoryRegex();

    [GeneratedRegex(@"HostIsolation\s*\.\s*RemoveUserSecrets\s*\(")]
    private static partial Regex RemovalRegex();
}
