using System.Reflection;
using AwesomeAssertions;
using Xunit;
using Xunit.Sdk;

namespace Struo.Tests.Support;

public sealed class StruoTestFrameworkTests
{
    [Fact]
    public void The_assembly_registers_the_struo_test_framework()
    {
        var data = typeof(StruoTestFrameworkTests).Assembly.GetCustomAttributesData()
            .Single(d => d.AttributeType == typeof(TestFrameworkAttribute));

        data.ConstructorArguments.Select(a => a.Value)
            .Should().Equal(typeof(StruoTestFramework).FullName, typeof(StruoTestFramework).Assembly.GetName().Name);
    }
}
