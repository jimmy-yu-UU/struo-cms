// tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs
using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Xunit;

namespace Struo.Tests.GraphQl;

public class SchemaTypeMapperMutationTests
{
    [Fact]
    public void Mutation_field_and_input_names_follow_convention()
    {
        SchemaTypeMapper.CreateFieldName("article").Should().Be("createArticle");
        SchemaTypeMapper.UpdateFieldName("article").Should().Be("updateArticle");
        SchemaTypeMapper.DeleteFieldName("article").Should().Be("deleteArticle");
        SchemaTypeMapper.CreateInputName("article").Should().Be("ArticleCreateInput");
        SchemaTypeMapper.UpdateInputName("article").Should().Be("ArticleUpdateInput");
    }

    [Theory]
    [InlineData(FieldInterface.Text, "String")]
    [InlineData(FieldInterface.Select, "String")]
    [InlineData(FieldInterface.Radio, "String")]
    [InlineData(FieldInterface.Time, "String")]
    [InlineData(FieldInterface.Boolean, "Boolean")]
    [InlineData(FieldInterface.Date, "Date")]
    [InlineData(FieldInterface.DateTime, "DateTime")]
    [InlineData(FieldInterface.Uuid, "ID")]
    [InlineData(FieldInterface.File, "ID")]
    [InlineData(FieldInterface.Image, "ID")]
    [InlineData(FieldInterface.Files, "[ID!]")]
    [InlineData(FieldInterface.MultiSelect, "[String!]")]
    [InlineData(FieldInterface.CheckboxGroup, "[String!]")]
    [InlineData(FieldInterface.Json, "Any")]
    [InlineData(FieldInterface.KeyValue, "Any")]
    public void Writable_input_interfaces_map_to_sdl(FieldInterface iface, string expected)
    {
        SchemaTypeMapper.WritableInputSdl(iface, null).Should().Be(expected);
    }

    [Fact]
    public void Writable_input_number_maps_by_clr_type()
    {
        SchemaTypeMapper.WritableInputSdl(FieldInterface.Number, typeof(int)).Should().Be("Int");
        SchemaTypeMapper.WritableInputSdl(FieldInterface.Number, typeof(long)).Should().Be("Long");
        SchemaTypeMapper.WritableInputSdl(FieldInterface.Number, typeof(decimal)).Should().Be("Float");
    }

    [Theory]
    [InlineData(FieldInterface.Tags)]      // named type -> builder handles specially
    [InlineData(FieldInterface.Repeater)]  // named type -> builder handles specially
    [InlineData(FieldInterface.Hidden)]
    [InlineData(FieldInterface.Divider)]
    [InlineData(FieldInterface.Password)]
    public void Named_and_excluded_interfaces_have_no_input_sdl(FieldInterface iface)
    {
        SchemaTypeMapper.WritableInputSdl(iface, null).Should().BeNull();
    }

    [Fact]
    public void Input_type_name_helpers_follow_convention()
    {
        SchemaTypeMapper.TagItemInputName().Should().Be("TagItemInput");
        SchemaTypeMapper.RepeaterItemInputTypeName("article", "faqs").Should().Be("ArticleFaqsItemInput");
    }
}
