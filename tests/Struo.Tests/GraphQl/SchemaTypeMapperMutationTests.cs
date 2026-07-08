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
    [InlineData(FieldInterface.Boolean, "Boolean")]
    [InlineData(FieldInterface.Date, "Date")]
    [InlineData(FieldInterface.DateTime, "DateTime")]
    [InlineData(FieldInterface.Uuid, "ID")]
    public void Writable_scalar_interfaces_map_to_sdl(FieldInterface iface, string expected)
    {
        SchemaTypeMapper.WritableScalarInputSdl(iface, null).Should().Be(expected);
    }

    [Fact]
    public void Number_maps_by_clr_type()
    {
        SchemaTypeMapper.WritableScalarInputSdl(FieldInterface.Number, typeof(int)).Should().Be("Int");
        SchemaTypeMapper.WritableScalarInputSdl(FieldInterface.Number, typeof(long)).Should().Be("Long");
        SchemaTypeMapper.WritableScalarInputSdl(FieldInterface.Number, typeof(decimal)).Should().Be("Float");
    }

    [Theory]
    [InlineData(FieldInterface.MultiSelect)]
    [InlineData(FieldInterface.CheckboxGroup)]
    [InlineData(FieldInterface.Tags)]
    [InlineData(FieldInterface.Json)]
    [InlineData(FieldInterface.KeyValue)]
    [InlineData(FieldInterface.File)]
    [InlineData(FieldInterface.Image)]
    [InlineData(FieldInterface.Files)]
    [InlineData(FieldInterface.Repeater)]
    [InlineData(FieldInterface.Hidden)]
    [InlineData(FieldInterface.Divider)]
    [InlineData(FieldInterface.Password)]
    public void Deferred_and_excluded_interfaces_are_not_writable_scalars(FieldInterface iface)
    {
        SchemaTypeMapper.WritableScalarInputSdl(iface, null).Should().BeNull();
    }
}
