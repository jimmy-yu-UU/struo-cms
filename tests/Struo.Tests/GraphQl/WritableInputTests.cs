using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// One predicate decides whether an own field goes into the create and update input types:
/// <c>WritableInput.SkipReason</c>. The schema builder adds the fields <c>WritableInput.Fields</c> returns,
/// and the startup check counts the same list.
/// </summary>
public sealed class WritableInputTests
{
    private static FieldMetadata Field(string name, FieldInterface face)
    {
        var field = new FieldMetadata { Name = name, Label = name, Interface = face };
        return field;
    }

    private static CollectionMetadata Collection(params FieldMetadata[] fields) =>
        new() { Name = "vault", Label = "Vault", FieldGroups = [], Fields = fields, Relations = [] };

    [Theory]
    [InlineData(FieldInterface.Hidden)]
    [InlineData(FieldInterface.Divider)]
    [InlineData(FieldInterface.Password)]
    public void An_excluded_interface_is_not_an_input_field(FieldInterface face)
    {
        WritableInput.SkipReason(Field("v", face)).Should().Contain($"Interface = {face}");
    }

    [Fact]
    public void A_hidden_read_only_system_or_translatable_field_is_not_an_input_field()
    {
        WritableInput.SkipReason(Field("v", FieldInterface.Text) with { Hidden = true }).Should().Be("Hidden = true");
        WritableInput.SkipReason(Field("v", FieldInterface.Text) with { ReadOnly = true }).Should().Be("ReadOnly = true");
        WritableInput.SkipReason(Field("v", FieldInterface.Text) with { IsSystem = true }).Should().Be("an audit field");
        WritableInput.SkipReason(Field("v", FieldInterface.Text) with { Translatable = true }).Should().Contain("translatable");
    }

    [Fact]
    public void A_Repeater_without_sub_fields_is_not_an_input_field()
    {
        WritableInput.SkipReason(Field("rows", FieldInterface.Repeater)).Should().Contain("without sub-fields");
    }

    [Theory]
    [InlineData(FieldInterface.Text)]
    [InlineData(FieldInterface.Number)]
    [InlineData(FieldInterface.Tags)]
    [InlineData(FieldInterface.Json)]
    [InlineData(FieldInterface.Files)]
    public void An_ordinary_field_is_an_input_field(FieldInterface face)
    {
        WritableInput.SkipReason(Field("v", face)).Should().BeNull();
    }

    [Fact]
    public void The_check_and_the_schema_count_the_same_fields()
    {
        var collection = Collection(
            Field("secret", FieldInterface.Hidden),
            Field("name", FieldInterface.Text),
            Field("hiddenText", FieldInterface.Text) with { Hidden = true });

        var fields = WritableInput.Fields(collection, null, []).Select(f => f.Name);

        fields.Should().Equal("name");
        FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, [])).Should().NotThrow();
    }

    [Fact]
    public void A_collection_with_no_input_field_is_rejected_naming_every_field_and_the_reason()
    {
        var collection = Collection(
            Field("secret", FieldInterface.Hidden),
            Field("hiddenText", FieldInterface.Text) with { Hidden = true },
            Field("createdAt", FieldInterface.DateTime) with { IsSystem = true });

        var message = FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, []))
            .Should().Throw<MetadataException>().Which.Message;

        message.Should().Contain("Collection 'vault'").And.Contain("create input type would be empty")
            .And.Contain("'secret' (Interface = Hidden, which is never written)")
            .And.Contain("'hiddenText' (Hidden = true)")
            .And.NotContain("createdAt")
            .And.Contain("Add a [CmsField] property").And.Contain("relation");
    }

    [Fact]
    public void A_collection_without_any_field_is_rejected_too()
    {
        var message = FluentActions.Invoking(() => WritableInput.RequireAny(Collection(), null, []))
            .Should().Throw<MetadataException>().Which.Message;

        message.Should().Contain("Collection 'vault'").And.Contain("declares no [CmsField] property");
    }

    [Fact]
    public void A_many_to_one_relation_or_a_translation_sidecar_is_a_field()
    {
        var withRelation = Collection(Field("secret", FieldInterface.Hidden)) with
        {
            Relations =
            [
                new RelationMetadata
                {
                    Name = "owner", Label = "Owner", Kind = RelationKind.ManyToOne, TargetCollection = "user",
                    ForeignKey = "ownerId", Interface = RelationInterface.Dropdown,
                },
            ],
        };

        WritableInput.Fields(withRelation, null, []).Select(f => f.Name).Should().Equal("ownerId");
        FluentActions.Invoking(() => WritableInput.RequireAny(withRelation, null, [])).Should().NotThrow();
    }
}
