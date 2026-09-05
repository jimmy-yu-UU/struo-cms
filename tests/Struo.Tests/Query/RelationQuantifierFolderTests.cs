// tests/Struo.Tests/Query/RelationQuantifierFolderTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class RelationQuantifierFolderTests
{
    private static ComparisonFilter Cmp(string path, object v) => new(path, QueryOperator.Eq, v);

    [Fact]
    public void Plain_paths_pass_through_untouched()
    {
        var r = RelationQuantifierFolder.Fold([Cmp("status", "a"), Cmp("category.name", "b")]);
        r.Should().HaveCount(2).And.AllBeOfType<ComparisonFilter>();
    }

    [Fact]
    public void Same_prefix_and_quantifier_fold_into_one_predicate_with_anded_inner()
    {
        var r = RelationQuantifierFolder.Fold([Cmp("properties._some.code", "vds-v"), Cmp("properties._some.valueNum", 60), Cmp("status", "x")]);
        r.Should().HaveCount(2);
        var p = r.OfType<RelationPredicateFilter>().Single();
        p.RelationPath.Should().Be("properties");
        p.Quantifier.Should().Be(RelationQuantifier.Some);
        var inner = p.Inner.Should().BeOfType<LogicalFilter>().Subject;
        inner.Op.Should().Be(LogicalOperator.And);
        inner.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath).Should().BeEquivalentTo(["code", "valueNum"]);
    }

    [Fact]
    public void Some_and_none_on_the_same_prefix_are_two_predicates()
    {
        var r = RelationQuantifierFolder.Fold([Cmp("tags._some.name", "a"), Cmp("tags._none.name", "b")]);
        r.OfType<RelationPredicateFilter>().Select(p => p.Quantifier).Should().BeEquivalentTo([RelationQuantifier.Some, RelationQuantifier.None]);
    }

    [Fact]
    public void Dotted_prefix_before_the_quantifier_is_kept()
    {
        var r = RelationQuantifierFolder.Fold([Cmp("category.articles._some.status", "published")]);
        var p = r.Should().ContainSingle().Which.Should().BeOfType<RelationPredicateFilter>().Subject;
        p.RelationPath.Should().Be("category.articles");
        p.Inner.Should().BeOfType<ComparisonFilter>().Which.FieldPath.Should().Be("status");
    }

    [Fact]
    public void Nested_quantifiers_fold_recursively()
    {
        var r = RelationQuantifierFolder.Fold([Cmp("a._some.b._some.c", "x")]);
        var outer = r.Single().Should().BeOfType<RelationPredicateFilter>().Subject;
        outer.RelationPath.Should().Be("a");
        var innerP = outer.Inner.Should().BeOfType<RelationPredicateFilter>().Subject;
        innerP.RelationPath.Should().Be("b");
        innerP.Inner.Should().BeOfType<ComparisonFilter>().Which.FieldPath.Should().Be("c");
    }

    [Fact]
    public void Junction_segment_survives_inside_the_inner_path()
    {
        var r = RelationQuantifierFolder.Fold([Cmp("tags._some._junction.note", "hero")]);
        r.Single().Should().BeOfType<RelationPredicateFilter>()
            .Which.Inner.Should().BeOfType<ComparisonFilter>().Which.FieldPath.Should().Be("_junction.note");
    }

    [Theory]
    [InlineData("tags._some")]
    [InlineData("tags._some._none.name")]
    [InlineData("_some.name")]
    public void Malformed_quantifier_paths_throw(string path)
    {
        var act = () => RelationQuantifierFolder.Fold([Cmp(path, "x")]);
        act.Should().Throw<QueryException>();
    }
}
