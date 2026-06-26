using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Metadata;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

public class RelationshipGraphValidationTests
{
    private static readonly CollectionMetadata TagCollection = new()
    {
        Name = "tag",
        Label = "Tag",
        FieldGroups = [],
        Fields = [],
        Relations = []
    };

    private static readonly IReadOnlyDictionary<string, Type> TagTypes =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["tag"] = typeof(Tag)
        };

    [Fact]
    public void Throws_when_relation_targets_unknown_collection()
    {
        var widgetMeta = new CollectionMetadata
        {
            Name = "widget",
            Label = "Widget",
            FieldGroups = [],
            Fields = [],
            Relations =
            [
                new RelationMetadata
                {
                    Name = "ghost",
                    Label = "Ghost",
                    Kind = RelationKind.ManyToOne,
                    TargetCollection = "doesnotexist",
                    Interface = RelationInterface.Dropdown,
                    ForeignKey = "ghostId",
                    OnDelete = OnDelete.Restrict,
                    Editable = true
                }
            ]
        };

        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["widget"] = typeof(Tag)
        };

        var act = () => new RelationshipGraph([widgetMeta], collectionTypes);

        act.Should().Throw<MetadataException>()
            .WithMessage("*doesnotexist*");
    }

    [Fact]
    public void Throws_when_m2o_has_no_foreign_key()
    {
        var widgetMeta = new CollectionMetadata
        {
            Name = "widget",
            Label = "Widget",
            FieldGroups = [],
            Fields = [],
            Relations =
            [
                new RelationMetadata
                {
                    Name = "tag",
                    Label = "Tag",
                    Kind = RelationKind.ManyToOne,
                    TargetCollection = "tag",
                    Interface = RelationInterface.Dropdown,
                    ForeignKey = null,
                    OnDelete = OnDelete.Restrict,
                    Editable = true
                }
            ]
        };

        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["widget"] = typeof(Tag),
            ["tag"] = typeof(Tag)
        };

        var act = () => new RelationshipGraph([widgetMeta, TagCollection], collectionTypes);

        act.Should().Throw<MetadataException>()
            .WithMessage("*no foreign key*");
    }
}
