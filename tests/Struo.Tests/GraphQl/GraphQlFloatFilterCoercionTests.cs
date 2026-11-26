// tests/Struo.Tests/GraphQl/GraphQlFloatFilterCoercionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// A <c>decimal</c> field filters through <c>FloatFilter</c>; an operand of the wrong kind, or a
/// literal beyond the Float range, answers <c>BAD_USER_INPUT</c> rather than an unhandled execution
/// error. The collection is declared here because no core or fixture collection has a floating field.
/// </summary>
public sealed class GraphQlFloatFilterCoercionTests
{
    private sealed class ProductPoco
    {
        public Guid Id { get; set; }
        public decimal Price { get; set; }
    }

    private sealed class ProductMetadata : IMetadataProvider
    {
        private static readonly CollectionMetadata Product = new()
        {
            Name = "product",
            Label = "Product",
            FieldGroups = [],
            Fields =
            [
                new FieldMetadata { Name = "price", Label = "Price", Interface = FieldInterface.Number, Sort = 1 },
            ],
        };

        public IReadOnlyList<CollectionMetadata> GetCollections() => [Product];

        public CollectionMetadata? GetCollection(string name) =>
            string.Equals(name, Product.Name, StringComparison.OrdinalIgnoreCase) ? Product : null;
    }

    private sealed class ProductRegistry : IEntityRegistry
    {
        public EntityDescriptor? Get(string collection) =>
            string.Equals(collection, "product", StringComparison.OrdinalIgnoreCase)
                ? new EntityDescriptor(
                    typeof(ProductPoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "Id", ["price"] = "Price" },
                    "Id")
                : null;
    }

    [Theory]
    [InlineData("eq: \"abc\"")]
    [InlineData("eq: true")]
    [InlineData("gt: \"abc\"")]
    [InlineData("in: [\"a\"]")]
    [InlineData("nin: [true]")]
    [InlineData("eq: 1e309")]
    public async Task A_Float_operand_that_cannot_be_coerced_is_BAD_USER_INPUT(string operand)
    {
        var executor = await GraphQlExecutionTests.ExecutorAsync(
            new FakeGraphQlDataSource(), new ProductMetadata(), new ProductRegistry(), classifyValidationErrors: true);

        var result = await executor.ExecuteAsync($"{{ products(filter: {{ price: {{ {operand} }} }}) {{ total }} }}");

        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue(json);
        errors.GetArrayLength().Should().Be(1, json);
        errors[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT", json);
        var message = errors[0].GetProperty("message").GetString();
        message.Should().NotContain("Unexpected Execution Error").And.NotContain("internal error");
        message.Should().NotContain("coerce").And.NotContain("does not match");
    }
}
