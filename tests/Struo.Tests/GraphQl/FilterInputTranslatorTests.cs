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
}
