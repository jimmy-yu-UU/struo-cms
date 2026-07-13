using AwesomeAssertions;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class DeepSpecTests
{
    [Fact]
    public void DeepRelationSpec_defaults_Deep_to_null()
    {
        // Existing depth-1 construction sites keep compiling and stay depth-1.
        var spec = new DeepRelationSpec(null, null);
        spec.Deep.Should().BeNull();
    }

    [Fact]
    public void DeepRelationSpec_carries_a_nested_DeepSpec()
    {
        var nested = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["parent"] = new DeepRelationSpec(null, null)
        });
        var spec = new DeepRelationSpec(Fields: null, Limit: null, Deep: nested);

        spec.Deep.Should().BeSameAs(nested);
        spec.Deep!.Relations.Should().ContainKey("parent");
    }

    [Fact]
    public void DeepRelationSpec_carries_nested_list_args()
    {
        var filter = new ComparisonFilter("name", QueryOperator.Eq, "x");
        var sort = new List<SortField> { new("name", false) };
        var spec = new DeepRelationSpec(null, 5, null) { Filter = filter, Sort = sort, Offset = 2 };

        spec.Filter.Should().BeSameAs(filter);
        spec.Sort.Should().ContainSingle().Which.Field.Should().Be("name");
        spec.Limit.Should().Be(5);
        spec.Offset.Should().Be(2);
    }

    [Fact]
    public void DeepRelationSpec_list_args_default_null()
    {
        var spec = new DeepRelationSpec(null, null);
        spec.Filter.Should().BeNull();
        spec.Sort.Should().BeNull();
        spec.Offset.Should().BeNull();
    }
}
