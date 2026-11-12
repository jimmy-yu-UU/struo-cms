using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// One row per FieldInterface member: its membership in each Infrastructure grouping and what the
// GraphQL mapper gives it. A member without a row fails Every_member_has_a_row, so adding a member
// forces a decision for each column here. Number interfaces are listed with an int property.
public class FieldInterfaceSetsTests
{
    private sealed record Row(
        FieldInterface Member, bool JsonColumn, bool ContentBearing, string? ScalarSdl, string? WritableInputSdl);

    private static Row Plain(FieldInterface member, string? scalar, string? writable) =>
        new(member, false, false, scalar, writable);

    private static readonly Row[] Table =
    [
        Plain(FieldInterface.Text, "String", "String"),
        new(FieldInterface.Textarea, false, true, "String", "String"),
        new(FieldInterface.RichText, false, true, "String", "String"),
        new(FieldInterface.Markdown, false, true, "String", "String"),
        new(FieldInterface.Code, false, true, "String", "String"),
        Plain(FieldInterface.Slug, "String", "String"),
        Plain(FieldInterface.Email, "String", "String"),
        Plain(FieldInterface.Url, "String", "String"),
        Plain(FieldInterface.Password, null, null),
        Plain(FieldInterface.Color, "String", "String"),
        Plain(FieldInterface.Phone, "String", "String"),
        Plain(FieldInterface.Number, "Int", "Int"),
        Plain(FieldInterface.Slider, "Int", "Int"),
        Plain(FieldInterface.Rating, "Int", "Int"),
        Plain(FieldInterface.Boolean, "Boolean", "Boolean"),
        Plain(FieldInterface.Checkbox, "Boolean", "Boolean"),
        Plain(FieldInterface.Date, "Date", "Date"),
        Plain(FieldInterface.Time, "String", "String"),
        Plain(FieldInterface.DateTime, "DateTime", "DateTime"),
        Plain(FieldInterface.Select, "String", "String"),
        new(FieldInterface.MultiSelect, true, false, "[String!]", "[String!]"),
        Plain(FieldInterface.Radio, "String", "String"),
        new(FieldInterface.CheckboxGroup, true, false, "[String!]", "[String!]"),
        new(FieldInterface.Tags, true, false, null, null),
        new(FieldInterface.Json, false, true, "Any", "Any"),
        new(FieldInterface.KeyValue, true, false, "Any", "Any"),
        new(FieldInterface.Repeater, true, false, null, null),
        Plain(FieldInterface.File, "ID", "ID"),
        Plain(FieldInterface.Image, "ID", "ID"),
        new(FieldInterface.Files, true, false, "[ID!]", "[ID!]"),
        Plain(FieldInterface.Hidden, null, null),
        Plain(FieldInterface.Divider, null, null),
        Plain(FieldInterface.Uuid, "ID", "ID")
    ];

    [Fact]
    public void Every_member_has_a_row()
    {
        Table.Select(r => r.Member).Should().OnlyHaveUniqueItems();
        Enum.GetValues<FieldInterface>().Except(Table.Select(r => r.Member))
            .Should().BeEmpty("a new FieldInterface member needs a row in this table");
    }

    [Fact]
    public void Json_column_membership_matches_the_table()
    {
        FieldInterfaceSets.JsonColumnInterfaces.Should()
            .BeEquivalentTo(Table.Where(r => r.JsonColumn).Select(r => r.Member));
    }

    [Fact]
    public void Content_bearing_membership_matches_the_table()
    {
        FieldInterfaceSets.ContentBearingInterfaces.Should()
            .BeEquivalentTo(Table.Where(r => r.ContentBearing).Select(r => r.Member));
    }

    [Fact]
    public void A_member_is_never_both_json_column_and_content_bearing()
    {
        FieldInterfaceSets.JsonColumnInterfaces.Intersect(FieldInterfaceSets.ContentBearingInterfaces)
            .Should().BeEmpty();
    }

    [Fact]
    public void Graphql_scalar_mapping_matches_the_table()
    {
        foreach (var row in Table)
            SchemaTypeMapper.ScalarSdl(row.Member, typeof(int)).Should().Be(row.ScalarSdl, $"ScalarSdl({row.Member})");
    }

    [Fact]
    public void Graphql_writable_input_mapping_matches_the_table()
    {
        foreach (var row in Table)
            SchemaTypeMapper.WritableInputSdl(row.Member, typeof(int)).Should()
                .Be(row.WritableInputSdl, $"WritableInputSdl({row.Member})");
    }
}
