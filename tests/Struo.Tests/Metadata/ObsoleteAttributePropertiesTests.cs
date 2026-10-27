// tests/Struo.Tests/Metadata/ObsoleteAttributePropertiesTests.cs
using System.Reflection;
using AwesomeAssertions;
using Struo.Domain.Metadata.Attributes;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>Three attribute properties nothing reads are marked obsolete, so a collection that sets one gets a compiler warning.</summary>
public class ObsoleteAttributePropertiesTests
{
    [Theory]
    [InlineData(typeof(CmsFieldAttribute), nameof(CmsFieldAttribute.Label), false)]
    [InlineData(typeof(CmsRelationAttribute), nameof(CmsRelationAttribute.DisplayTemplate), false)]
    public void The_properties_that_do_something_are_not_obsolete(Type type, string property, bool obsolete) =>
        type.GetProperty(property)!.GetCustomAttribute<ObsoleteAttribute>().Should().Match(a => (a != null) == obsolete);

    [Theory]
    [InlineData(typeof(CmsFieldAttribute), "Display")]
    [InlineData(typeof(CmsRelationAttribute), "DisplayColumns")]
    [InlineData(typeof(CmsRelationAttribute), "MaxDepth")]
    public void The_dead_properties_are_obsolete_with_a_warning_not_an_error(Type type, string property)
    {
        var obsolete = type.GetProperty(property)!.GetCustomAttribute<ObsoleteAttribute>();

        obsolete.Should().NotBeNull();
        obsolete!.IsError.Should().BeFalse();
        obsolete.Message.Should().Contain("no effect");
    }
}
