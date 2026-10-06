using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Configuration;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class ScannedAssembliesTests
{
    [Fact]
    public void AddStruoMetadata_registers_framework_and_caller_assemblies()
    {
        var caller = typeof(StruoDbType).Assembly;
        using var sp = new ServiceCollection().AddStruoMetadata(caller).BuildServiceProvider();

        var scanned = sp.GetRequiredService<ScannedAssemblies>().All;

        scanned.Should().Contain(caller);
        scanned.Should().Contain(typeof(MetadataServiceCollectionExtensions).Assembly);
        scanned.Should().OnlyHaveUniqueItems();
    }
}
