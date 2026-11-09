using AwesomeAssertions;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Persistence;

public sealed class TableNamingTests
{
    [Theory]
    [InlineData(typeof(Struo.Tests.Fixtures.FxArticle))]
    [InlineData(typeof(Struo.Tests.Fixtures.FxArticleTag))]
    [InlineData(typeof(string))]
    public void Content_and_unrelated_types_are_not_framework_tables(Type type)
    {
        TableNaming.IsFrameworkTable(type).Should().BeFalse();
    }
}
