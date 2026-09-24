using AwesomeAssertions;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Persistence;

public sealed class TableNamingTests
{
    public static TheoryData<Type> FrameworkTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var t in FrameworkEntityTypes.All) data.Add(t);
        data.Add(typeof(SchemaMigration));
        return data;
    }

    [Theory]
    [MemberData(nameof(FrameworkTypes))]
    public void Every_framework_entity_and_the_migration_tracker_is_a_framework_table(Type type)
    {
        TableNaming.IsFrameworkTable(type).Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(Struo.Sample.Blog.Article))]
    [InlineData(typeof(Struo.Sample.Blog.ArticleTag))]
    [InlineData(typeof(string))]
    public void Sample_and_unrelated_types_are_not_framework_tables(Type type)
    {
        TableNaming.IsFrameworkTable(type).Should().BeFalse();
    }

    [Theory]
    [InlineData("struo_", "users", "struo_users")]
    [InlineData("", "users", "users")]
    [InlineData("acme", "schema_migrations", "acmeschema_migrations")]
    public void Apply_concatenates_prefix_and_attribute_name(string prefix, string name, string expected)
    {
        TableNaming.Apply(prefix, name).Should().Be(expected);
    }

    [Fact]
    public void Twelve_framework_tables_exactly()
    {
        FrameworkEntityTypes.All.Count.Should().Be(11);
        TableNaming.IsFrameworkTable(typeof(SchemaMigration)).Should().BeTrue();
    }
}
