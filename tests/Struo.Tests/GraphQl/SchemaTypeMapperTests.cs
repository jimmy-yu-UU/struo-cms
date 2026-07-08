// tests/Struo.Tests/GraphQl/SchemaTypeMapperTests.cs
using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Xunit;

namespace Struo.Tests.GraphQl;

public class SchemaTypeMapperTests
{
    [Theory]
    [InlineData("article", "Article")]
    [InlineData("file", "File")]
    [InlineData("userRole", "UserRole")]
    public void TypeName_is_pascal_case(string c, string expected)
        => SchemaTypeMapper.TypeName(c).Should().Be(expected);

    [Theory]
    [InlineData("article", "articles")]
    [InlineData("category", "categories")]
    [InlineData("box", "boxes")]
    [InlineData("dish", "dishes")]
    public void ListFieldName_pluralises(string c, string expected)
        => SchemaTypeMapper.ListFieldName(c).Should().Be(expected);

    [Fact]
    public void SingleFieldName_is_camel_case_singular()
        => SchemaTypeMapper.SingleFieldName("article").Should().Be("article");

    [Fact]
    public void RepeaterItemTypeName_combines_collection_and_field()
        => SchemaTypeMapper.RepeaterItemTypeName("article", "faqs").Should().Be("ArticleFaqsItem");

    [Theory]
    [InlineData(FieldInterface.Text, typeof(string), "String")]
    [InlineData(FieldInterface.RichText, typeof(string), "String")]
    [InlineData(FieldInterface.Number, typeof(int), "Int")]
    [InlineData(FieldInterface.Number, typeof(long), "Long")]
    [InlineData(FieldInterface.Number, typeof(decimal), "Float")]
    [InlineData(FieldInterface.Boolean, typeof(bool), "Boolean")]
    [InlineData(FieldInterface.DateTime, typeof(System.DateTime), "DateTime")]
    [InlineData(FieldInterface.Date, typeof(System.DateTime), "Date")]
    [InlineData(FieldInterface.Select, typeof(string), "String")]
    [InlineData(FieldInterface.MultiSelect, typeof(object), "[String!]")]
    [InlineData(FieldInterface.CheckboxGroup, typeof(object), "[String!]")]
    [InlineData(FieldInterface.Json, typeof(string), "Any")]
    [InlineData(FieldInterface.KeyValue, typeof(object), "Any")]
    [InlineData(FieldInterface.Image, typeof(System.Guid), "ID")]
    [InlineData(FieldInterface.File, typeof(System.Guid), "ID")]
    [InlineData(FieldInterface.Files, typeof(object), "[ID!]")]
    public void ScalarSdl_maps_interface_and_clr_type(FieldInterface iface, System.Type clr, string expected)
        => SchemaTypeMapper.ScalarSdl(iface, clr).Should().Be(expected);

    [Theory]
    [InlineData(FieldInterface.Hidden)]
    [InlineData(FieldInterface.Divider)]
    [InlineData(FieldInterface.Password)]
    public void Excluded_interfaces_return_null(FieldInterface iface)
    {
        SchemaTypeMapper.IsExcluded(iface).Should().BeTrue();
        SchemaTypeMapper.ScalarSdl(iface, typeof(string)).Should().BeNull();
    }

    [Theory]
    [InlineData(FieldInterface.Tags)]
    [InlineData(FieldInterface.Repeater)]
    public void Named_type_interfaces_return_null_scalar(FieldInterface iface)
        => SchemaTypeMapper.ScalarSdl(iface, typeof(object)).Should().BeNull();
}
