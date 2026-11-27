using AwesomeAssertions;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// The duplicate check reads the finished type, so a field added without going through the registrar is compared too,
/// and names compare case-sensitively.
/// </summary>
public sealed class DeclaredFieldsTests
{
    private static ObjectFieldConfiguration Field(string name) => CollectionSchemaBuilder.Field(name, "String", _ => null);

    [Fact]
    public void A_field_added_straight_to_the_type_is_still_compared_with_the_generic_origin()
    {
        var declared = new DeclaredFields();
        var type = new ObjectTypeConfiguration("Post");
        declared.Add(type, Field("cover"), "the relation 'Post.Cover'");
        type.Fields.Add(Field("cover"));

        declared.Inspect(type);

        var refusal = () => declared.Require("post");
        refusal.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("collection 'post'")
            .And.Contain("The type 'Post' declares the field 'cover' more than once")
            .And.Contain("the relation 'Post.Cover' and the 'cover' field the CMS adds");
    }

    [Fact]
    public void Names_that_differ_only_in_case_are_two_fields()
    {
        var declared = new DeclaredFields();
        var type = new ObjectTypeConfiguration("Post");
        declared.Add(type, Field("title"));
        declared.Add(type, Field("tItle"));

        declared.Inspect(type);

        var refusal = () => declared.Require("post");
        refusal.Should().NotThrow();
    }
}
