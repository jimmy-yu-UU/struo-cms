// tests/Struo.Tests/Metadata/RemovedAttributePropertiesTests.cs
using AwesomeAssertions;
using Struo.Domain.Metadata.Attributes;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>Three attribute properties that nothing reads do not exist, so a collection that sets one fails to compile.</summary>
public class RemovedAttributePropertiesTests
{
    [Theory]
    [InlineData(typeof(CmsFieldAttribute), "Display")]
    [InlineData(typeof(CmsRelationAttribute), "DisplayColumns")]
    [InlineData(typeof(CmsRelationAttribute), "MaxDepth")]
    public void The_dead_properties_do_not_exist(Type type, string property) =>
        type.GetProperty(property).Should().BeNull();
}
