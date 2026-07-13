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
}
