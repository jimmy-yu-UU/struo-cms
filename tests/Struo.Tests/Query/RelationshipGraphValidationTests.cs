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
    private static readonly CollectionMetadata CategoryCollection = new()
    {
        Name = "category",
        Label = "Category",
        FieldGroups = [],
        Fields = [],
        Relations = []
    };

    private static readonly IReadOnlyDictionary<string, Type> CategoryTypes =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = typeof(Category)
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
            ["widget"] = typeof(Category)
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
                    Name = "category",
                    Label = "Category",
                    Kind = RelationKind.ManyToOne,
                    TargetCollection = "category",
                    Interface = RelationInterface.Dropdown,
                    ForeignKey = null,
                    OnDelete = OnDelete.Restrict,
                    Editable = true
                }
            ]
        };

        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["widget"] = typeof(Category),
            ["category"] = typeof(Category)
        };

        var act = () => new RelationshipGraph([widgetMeta, CategoryCollection], collectionTypes);

        act.Should().Throw<MetadataException>()
            .WithMessage("*no foreign key*");
    }
}
