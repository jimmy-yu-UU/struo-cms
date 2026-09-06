// tests/Struo.Tests/Query/FacetFilterPrunerTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class FacetFilterPrunerTests
{
    private static readonly RelationMetadata Category = new()
    {
        Name = "category", Label = "Category", Kind = RelationKind.ManyToOne, TargetCollection = "sqCategory",
        Interface = RelationInterface.Dropdown, ForeignKey = "categoryId",
    };
    private static readonly RelationMetadata Properties = new()
    {
        Name = "properties", Label = "Properties", Kind = RelationKind.OneToMany, TargetCollection = "sqProperty",
        Interface = RelationInterface.RelatedList,
    };
    private static readonly ResolvedFacetPath StatusFacet = new("status", FacetPathKind.OwnField, "status", null, null);
    private static readonly ResolvedFacetPath CategoryIdFacet = new("categoryId", FacetPathKind.ForeignKey, "categoryId", Category, null);
    private static readonly ResolvedFacetPath CategoryNameFacet = new("category.name", FacetPathKind.RelationLeaf, null, Category, "name");
    private static readonly ResolvedFacetPath PropertiesFacet = new("properties", FacetPathKind.Relation, null, Properties, null);

    private static ComparisonFilter Eq(string p, object v) => new(p, QueryOperator.Eq, v);

    [Fact]
    public void Null_filter_stays_null()
    {
        FacetFilterPruner.Prune(null, StatusFacet).Should().BeNull();
    }

    [Fact]
    public void Own_field_condition_on_the_facet_field_is_removed_case_insensitively()
    {
        FacetFilterPruner.Prune(Eq("Status", "published"), StatusFacet).Should().BeNull();
    }

    [Fact]
    public void Conditions_on_other_fields_are_kept()
    {
        var f = Eq("name", "x");
        FacetFilterPruner.Prune(f, StatusFacet).Should().BeSameAs(f);
    }

    [Fact]
    public void Foreign_key_facet_prunes_fk_and_dotted_relation_conditions_of_the_same_family()
    {
        var filter = new LogicalFilter(LogicalOperator.And, [Eq("categoryId", "1"), Eq("category.name", "Tech"), Eq("name", "x")]);
        var pruned = FacetFilterPruner.Prune(filter, CategoryIdFacet);
        pruned.Should().BeEquivalentTo(Eq("name", "x"));
    }

    [Fact]
    public void Relation_leaf_facet_prunes_the_fk_condition_too()
    {
        FacetFilterPruner.Prune(Eq("categoryId", "1"), CategoryNameFacet).Should().BeNull();
    }

    [Fact]
    public void Relation_facet_prunes_some_none_predicates_on_that_relation()
    {
        var inner = new LogicalFilter(LogicalOperator.And, [Eq("code", "vds-v"), Eq("valueNum", 80)]);
        var filter = new LogicalFilter(LogicalOperator.And,
            [new RelationPredicateFilter("properties", RelationQuantifier.Some, inner), Eq("properties.code", "x"), Eq("status", "published")]);
        FacetFilterPruner.Prune(filter, PropertiesFacet).Should().BeEquivalentTo(Eq("status", "published"));
    }

    [Fact]
    public void Or_group_pruned_to_one_child_is_flattened_and_to_zero_is_removed()
    {
        var or = new LogicalFilter(LogicalOperator.Or, [Eq("status", "a"), Eq("name", "x")]);
        FacetFilterPruner.Prune(or, StatusFacet).Should().BeEquivalentTo(Eq("name", "x"));

        var orAll = new LogicalFilter(LogicalOperator.Or, [Eq("status", "a"), Eq("status", "b")]);
        FacetFilterPruner.Prune(orAll, StatusFacet).Should().BeNull();
    }

    [Fact]
    public void Nested_group_with_survivors_keeps_its_operator()
    {
        var filter = new LogicalFilter(LogicalOperator.And,
            [Eq("name", "x"), new LogicalFilter(LogicalOperator.Or, [Eq("status", "a"), Eq("name", "y"), Eq("name", "z")])]);
        var pruned = (LogicalFilter)FacetFilterPruner.Prune(filter, StatusFacet)!;
        pruned.Op.Should().Be(LogicalOperator.And);
        pruned.Children.Should().HaveCount(2);
        ((LogicalFilter)pruned.Children[1]).Children.Should().HaveCount(2);
    }

    [Fact]
    public void A_field_whose_name_merely_starts_with_the_relation_name_is_not_pruned()
    {
        var f = Eq("categoryIdBackup", "1");
        FacetFilterPruner.Prune(f, CategoryIdFacet).Should().BeSameAs(f);
    }
}
