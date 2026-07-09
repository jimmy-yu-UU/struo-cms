// tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs
using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class FilterInputTranslatorTests
{
    [Fact]
    public void Null_or_empty_filter_returns_null()
    {
        FilterInputTranslator.Translate(null).Should().BeNull();
        FilterInputTranslator.Translate(new Dictionary<string, object?>()).Should().BeNull();
    }

    [Fact]
    public void Single_field_eq_becomes_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["status"] = new Dictionary<string, object?> { ["eq"] = "published" }
        });

        var cmp = f.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("status");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("published");
    }

    [Fact]
    public void Multiple_fields_are_anded()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["status"] = new Dictionary<string, object?> { ["eq"] = "published" },
            ["title"] = new Dictionary<string, object?> { ["contains"] = "hello" }
        });

        var logical = f.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.And);
        logical.Children.Should().HaveCount(2);
    }

    [Fact]
    public void Explicit_or_group_is_honoured()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["or"] = new List<object?>
            {
                new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "a" } },
                new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "b" } },
            }
        });

        var logical = f.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.Or);
        logical.Children.Should().HaveCount(2);
    }

    [Fact]
    public void IsNull_true_and_false_map_to_Null_and_NNull()
    {
        var t = FilterInputTranslator.Translate(new Dictionary<string, object?>
            { ["publishedAt"] = new Dictionary<string, object?> { ["isNull"] = true } })
            .Should().BeOfType<ComparisonFilter>().Subject;
        t.Op.Should().Be(QueryOperator.Null);

        var fl = FilterInputTranslator.Translate(new Dictionary<string, object?>
            { ["publishedAt"] = new Dictionary<string, object?> { ["isNull"] = false } })
            .Should().BeOfType<ComparisonFilter>().Subject;
        fl.Op.Should().Be(QueryOperator.NNull);
    }

    [Theory]
    [InlineData(FieldInterface.Text, typeof(string), "StringFilter")]
    [InlineData(FieldInterface.Number, typeof(int), "IntFilter")]
    [InlineData(FieldInterface.Number, typeof(decimal), "FloatFilter")]
    [InlineData(FieldInterface.DateTime, typeof(System.DateTime), "DateTimeFilter")]
    [InlineData(FieldInterface.Boolean, typeof(bool), "BooleanFilter")]
    public void OperatorInputTypeName_by_interface(FieldInterface iface, System.Type clr, string expected)
        => FilterInputTranslator.OperatorInputTypeName(iface, clr).Should().Be(expected);

    // relationTarget stub: "category" and "parent" are M2O relations whose target is "category"
    // (mirrors the sample Article.category and Category.parent self-relation). Everything else is a
    // plain field. Independent of real fixtures/DB.
    private static readonly Func<string, string, string?> Rel =
        (_, key) => key is "category" or "parent" ? "category" : null;

    [Fact]
    public void Nested_relation_becomes_dotted_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
            }
        }, "article", Rel);

        var cmp = f.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("category.name");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("Tech");
    }

    [Fact]
    public void Multi_hop_relation_becomes_multi_dotted_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["parent"] = new Dictionary<string, object?>
                {
                    ["name"] = new Dictionary<string, object?> { ["eq"] = "Root" }
                }
            }
        }, "article", Rel);

        var cmp = f.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("category.parent.name");
        cmp.Value.Should().Be("Root");
    }

    [Fact]
    public void Own_field_and_nested_relation_are_anded()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["status"] = new Dictionary<string, object?> { ["eq"] = "published" },
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
            }
        }, "article", Rel);

        var logical = f.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.And);
        logical.Children.Should().HaveCount(2);
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain(new[] { "status", "category.name" });
    }

    [Fact]
    public void Or_group_with_a_nested_relation_child_is_honoured()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["or"] = new List<object?>
            {
                new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "a" } },
                new Dictionary<string, object?>
                {
                    ["category"] = new Dictionary<string, object?>
                    {
                        ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
                    }
                },
            }
        }, "article", Rel);

        var logical = f.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.Or);
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain("category.name");
    }

    [Fact]
    public void Null_operators_inside_nested_relation_are_skipped()
    {
        // HotChocolate backfills every declared operator field as null; only "eq" was set.
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?>
                {
                    ["eq"] = "Tech", ["neq"] = null, ["contains"] = null, ["in"] = null
                }
            }
        }, "article", Rel);

        f.Should().BeOfType<ComparisonFilter>().Which.FieldPath.Should().Be("category.name");
    }

    // A delegate that resolves to-many keys too (mirrors the relaxed RelationTargets): "tags" is a
    // relation of "article" whose target is "tag"; "articles" is a relation of "category" whose
    // target is "article". The translator does not care about the relation KIND — only that the
    // delegate returns a target for the key.
    private static readonly Func<string, string, string?> RelToMany =
        (coll, key) => (coll, key) switch
        {
            ("article", "tags") => "tag",
            ("category", "articles") => "article",
            _ => null,
        };

    [Fact]
    public void To_many_relation_key_becomes_dotted_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["tags"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "AI" }
            }
        }, "article", RelToMany);

        var cmp = f.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("tags.name");
        cmp.Value.Should().Be("AI");
    }

    [Fact]
    public void One_arg_overload_still_treats_relation_key_as_flat_field()
    {
        // Back-compat: with no relationTarget, "category" is NOT a relation, so its dict is read as
        // an operator bag; "name" is not a known operator -> nothing emitted -> null.
        FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
            }
        }).Should().BeNull();
    }
}
