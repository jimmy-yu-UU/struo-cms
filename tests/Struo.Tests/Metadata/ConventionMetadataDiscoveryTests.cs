using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Xunit;

namespace Struo.Tests.Metadata;

public sealed class ConventionMetadataDiscoveryTests
{
    private static IConfiguration Config(params string[] contentAssemblies)
    {
        var dict = new Dictionary<string, string?>();
        for (var i = 0; i < contentAssemblies.Length; i++)
            dict[$"Struo:ContentAssemblies:{i}"] = contentAssemblies[i];
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Empty_config_scans_only_framework_and_host_assemblies()
    {
        using var sp = new ServiceCollection()
            .AddStruoMetadata(Config())          // no content assemblies, no host assembly
            .BuildServiceProvider();
        var provider = sp.GetRequiredService<IMetadataProvider>();

        provider.GetCollection("article").Should().BeNull();     // sample not scanned
        provider.GetCollection("user").Should().NotBeNull();     // framework built-in collection
    }

    [Fact]
    public void Configured_content_assembly_is_scanned()
    {
        using var sp = new ServiceCollection()
            .AddStruoMetadata(Config("Struo.Sample.Blog"))
            .BuildServiceProvider();
        var provider = sp.GetRequiredService<IMetadataProvider>();

        provider.GetCollection("article").Should().NotBeNull();
    }

    [Fact]
    public void Unloadable_content_assembly_fails_fast_naming_the_entry()
    {
        var act = () => new ServiceCollection().AddStruoMetadata(Config("Nope.Not.Real"));

        act.Should().Throw<MetadataException>().WithMessage("*Nope.Not.Real*");
    }

    [Fact]
    public void Registers_entity_type_collector()
    {
        using var sp = new ServiceCollection()
            .AddStruoMetadata(Config("Struo.Sample.Blog"))
            .BuildServiceProvider();

        sp.GetService<IEntityTypeCollector>().Should().NotBeNull();
    }

    [Fact]
    public void Shipped_appsettings_declares_no_content_assemblies()
    {
        // The template ships without sample content wired in: Struo:ContentAssemblies is empty and the
        // host project has no reference to samples/. Opting the sample in is a documented downstream step.
        var appsettingsPath = Path.Combine(
            AppContext.BaseDirectory, "appsettings.json");
        var json = File.ReadAllText(appsettingsPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var contentAssemblies = doc.RootElement
            .GetProperty("Struo").GetProperty("ContentAssemblies")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        contentAssemblies.Should().BeEmpty();
    }
}
