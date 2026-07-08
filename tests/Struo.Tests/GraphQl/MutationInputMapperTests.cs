// tests/Struo.Tests/GraphQl/MutationInputMapperTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Api.GraphQl;
using Xunit;

namespace Struo.Tests.GraphQl;

public class MutationInputMapperTests
{
    [Fact]
    public void Null_input_produces_empty_object()
    {
        var el = MutationInputMapper.ToJsonElement(null);
        el.ValueKind.Should().Be(JsonValueKind.Object);
        el.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Only_present_keys_appear()
    {
        var el = MutationInputMapper.ToJsonElement(new Dictionary<string, object?>
        {
            ["status"] = "published",
            ["categoryId"] = "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        });

        el.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["status", "categoryId"]);
        el.GetProperty("status").GetString().Should().Be("published");
        el.GetProperty("categoryId").GetString().Should().Be("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    }

    [Fact]
    public void Long_and_bool_serialize_as_json_primitives()
    {
        var el = MutationInputMapper.ToJsonElement(new Dictionary<string, object?>
        {
            ["version"] = 7L,
            ["active"] = true,
        });

        el.GetProperty("version").GetInt64().Should().Be(7);
        el.GetProperty("active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Explicit_null_value_is_preserved_as_json_null()
    {
        var el = MutationInputMapper.ToJsonElement(new Dictionary<string, object?> { ["categoryId"] = null });
        el.TryGetProperty("categoryId", out var v).Should().BeTrue();
        v.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
