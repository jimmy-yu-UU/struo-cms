using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Each input type has one rule for which field goes in (<c>WritableInput.OwnField</c>,
/// <c>TranslatedField</c>, <c>SubField</c>). The schema builder adds the fields the rule accepts, and the
/// startup check counts the same list.
/// </summary>
public sealed class WritableInputTests
{
    private static FieldMetadata Field(string name, FieldInterface face) =>
        new() { Name = name, Label = name, Interface = face };

    private static CollectionMetadata Collection(params FieldMetadata[] fields) =>
        new() { Name = "vault", Label = "Vault", FieldGroups = [], Fields = fields, Relations = [] };

    private static CollectionMetadata WithSidecar(params FieldMetadata[] fields) =>
        Collection(fields) with
        {
            Translation = new TranslationMetadata
            {
                ForeignKeyProperty = "VaultId", LocaleProperty = "Locale",
                Fields = fields.Where(f => f.Translatable).Select(f => f.Name).ToList(),
            },
        };

    private static FieldMetadata Rows(params FieldMetadata[] subs) => Field("rows", FieldInterface.Repeater) with { Fields = subs };

    private static string Own(FieldMetadata f) => WritableInput.OwnField(Collection(f), f, null).Skip!;

    [Theory]
    [InlineData(FieldInterface.Hidden)]
    [InlineData(FieldInterface.Divider)]
    [InlineData(FieldInterface.Password)]
    public void An_excluded_interface_is_not_an_input_field(FieldInterface face)
    {
        Own(Field("v", face)).Should().Be($"Interface = {face}, which is never written");
    }

    [Fact]
    public void A_hidden_read_only_system_or_translatable_field_is_not_an_input_field()
    {
        Own(Field("v", FieldInterface.Text) with { Hidden = true }).Should().Be("Hidden = true");
        Own(Field("v", FieldInterface.Text) with { ReadOnly = true }).Should().Be("ReadOnly = true");
        Own(Field("v", FieldInterface.Text) with { IsSystem = true }).Should().Be("an audit field");
        Own(Field("v", FieldInterface.Text) with { Translatable = true }).Should().Contain("translatable");
    }

    [Fact]
    public void A_Repeater_without_a_writable_sub_field_is_not_an_input_field()
    {
        Own(Field("rows", FieldInterface.Repeater)).Should().Contain("without sub-fields");
        Own(Rows(Field("a", FieldInterface.Text) with { Hidden = true })).Should().Contain("no writable sub-field");
    }

    [Theory]
    [InlineData(FieldInterface.Text)]
    [InlineData(FieldInterface.Number)]
    [InlineData(FieldInterface.Tags)]
    [InlineData(FieldInterface.Json)]
    [InlineData(FieldInterface.Files)]
    public void An_ordinary_field_is_an_input_field(FieldInterface face)
    {
        var f = Field("v", face);

        var decision = WritableInput.OwnField(Collection(f), f, null);

        decision.Skip.Should().BeNull();
        decision.Sdl.Should().NotBeNull();
    }

    [Fact]
    public void A_Repeater_with_a_writable_sub_field_is_an_input_field()
    {
        var rows = Rows(Field("a", FieldInterface.Text), Field("b", FieldInterface.Text) with { ReadOnly = true });

        WritableInput.OwnField(Collection(rows), rows, null).Sdl.Should().Be("[VaultRowsItemInput!]");
    }

    [Fact]
    public void A_translated_field_and_a_sub_field_follow_their_own_rule()
    {
        WritableInput.TranslatedField(Field("t", FieldInterface.Text), null).Sdl.Should().Be("String");
        WritableInput.TranslatedField(Field("t", FieldInterface.Password), null).Skip.Should().Contain("never written");
        WritableInput.TranslatedField(Field("t", FieldInterface.Text) with { Hidden = true }, null).Skip.Should().Be("Hidden = true");
        WritableInput.SubField(Field("s", FieldInterface.Number)).Sdl.Should().Be("Float");
        WritableInput.SubField(Field("s", FieldInterface.Number) with { ReadOnly = true }).Skip.Should().Be("ReadOnly = true");
    }

    [Fact]
    public void The_check_and_the_schema_count_the_same_fields()
    {
        var collection = Collection(
            Field("secret", FieldInterface.Hidden),
            Field("name", FieldInterface.Text),
            Field("hiddenText", FieldInterface.Text) with { Hidden = true });

        WritableInput.Fields(collection, null, []).Select(f => f.Name).Should().Equal("name");
        FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, [])).Should().NotThrow();
    }

    [Fact]
    public void A_collection_with_no_input_field_is_rejected_naming_the_fields_and_the_reason()
    {
        var collection = Collection(
            Field("secret", FieldInterface.Hidden),
            Field("hiddenText", FieldInterface.Text) with { Hidden = true },
            Field("createdAt", FieldInterface.DateTime) with { IsSystem = true });

        var message = FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, []))
            .Should().Throw<MetadataException>().Which.Message;

        message.Should().Be(
            "Collection 'vault' has nothing GraphQL can write, so its create input type would be empty and the schema " +
            "cannot be built. None of its fields counts: 'secret' (Interface = Hidden, which is never written), " +
            "'hiddenText' (Hidden = true). Add a [CmsField] property that GraphQL can write: it must not set Hidden = true " +
            "or ReadOnly = true, and its interface must not be Hidden, Divider or Password. " +
            "A many-to-one or many-to-many relation also counts.");
    }

    [Fact]
    public void The_list_of_fields_that_do_not_count_is_bounded()
    {
        var collection = Collection(Enumerable.Range(1, 8).Select(i => Field($"h{i}", FieldInterface.Hidden)).ToArray());

        var message = FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, []))
            .Should().Throw<MetadataException>().Which.Message;

        message.Should().Contain("'h5' (Interface = Hidden, which is never written) and 3 more.")
            .And.NotContain("'h6'");
    }

    [Fact]
    public void A_collection_without_any_field_is_rejected_too()
    {
        var message = FluentActions.Invoking(() => WritableInput.RequireAny(Collection(), null, []))
            .Should().Throw<MetadataException>().Which.Message;

        message.Should().Contain("Collection 'vault'").And.Contain("declares no [CmsField] property");
    }

    [Fact]
    public void A_many_to_one_relation_is_a_field()
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

    [Fact]
    public void A_sidecar_with_one_writable_translatable_field_is_the_only_input()
    {
        var collection = WithSidecar(
            Field("secret", FieldInterface.Hidden), Field("title", FieldInterface.Text) with { Translatable = true });

        WritableInput.Fields(collection, null, []).Select(f => f.Name).Should().Equal("translations");
        FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, [])).Should().NotThrow();
    }

    [Fact]
    public void A_sidecar_without_a_writable_translatable_field_is_rejected_before_the_create_input_is_counted()
    {
        var collection = WithSidecar(
            Field("name", FieldInterface.Text),
            Field("title", FieldInterface.Text) with { Translatable = true, Hidden = true },
            Field("note", FieldInterface.Password) with { Translatable = true });

        WritableInput.Fields(collection, null, []).Select(f => f.Name).Should().Equal("name");
        var message = FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, []))
            .Should().Throw<MetadataException>().Which.Message;

        message.Should().Be(
            "Collection 'vault' has a translation sidecar, but none of its translatable fields can be written through " +
            "GraphQL, so its translations input type would be empty and the schema cannot be built. " +
            "None of them counts: 'title' (Hidden = true), 'note' (Interface = Password, which is never written). " +
            "Make at least one translatable field writable: it must not set Hidden = true, and its " +
            "interface must not be Hidden, Divider or Password.");
    }

    [Fact]
    public void A_Repeater_without_a_writable_sub_field_is_rejected_naming_the_collection_and_the_field()
    {
        var collection = Collection(
            Field("name", FieldInterface.Text),
            Rows(Field("a", FieldInterface.Text) with { Hidden = true }, Field("b", FieldInterface.Text) with { ReadOnly = true }));

        var message = FluentActions.Invoking(() => WritableInput.RequireAny(collection, null, []))
            .Should().Throw<MetadataException>().Which.Message;

        message.Should().Be(
            "Repeater 'vault.rows' has no sub-field GraphQL can write, so its item input type would be empty and the " +
            "schema cannot be built. None of its sub-fields counts: 'a' (Hidden = true), 'b' (ReadOnly = true). " +
            "Make at least one sub-field writable: it must not set Hidden = true or ReadOnly = true.");
    }

    [Fact]
    public void A_Repeater_that_is_itself_hidden_still_needs_a_writable_sub_field()
    {
        var hiddenRows = Rows(Field("a", FieldInterface.Text) with { Hidden = true }) with { Hidden = true };

        FluentActions.Invoking(() => WritableInput.RequireAny(Collection(Field("name", FieldInterface.Text), hiddenRows), null, []))
            .Should().Throw<MetadataException>().Which.Message.Should().Contain("Repeater 'vault.rows'");
    }
}
