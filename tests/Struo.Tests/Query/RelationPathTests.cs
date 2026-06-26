// tests/Struo.Tests/Query/RelationPathTests.cs
using AwesomeAssertions;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class RelationPathTests
{
    // Minimal fakes so the unit test does not need a DB or the real scan.
    private sealed class FakeGraph : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public IReadOnlyList<(string, string)> InboundRestrict(string c) => [];
        public RelationMetadata? Resolve(string collection, string rel) => (collection, rel) switch
        {
            ("article", "category") => new RelationMetadata { Name = "category", Label = "Category",
                Kind = RelationKind.ManyToOne, TargetCollection = "category", Interface = RelationInterface.Dropdown, ForeignKey = "categoryId" },
            ("category", "parent") => new RelationMetadata { Name = "parent", Label = "Parent",
                Kind = RelationKind.ManyToOne, TargetCollection = "category", Interface = RelationInterface.TreeSelect, ForeignKey = "parentId", SelfReferencing = true },
            ("article", "tags") => new RelationMetadata { Name = "tags", Label = "Tags",
                Kind = RelationKind.ManyToMany, TargetCollection = "tag", Interface = RelationInterface.TagSelect },
            _ => null
        };
    }

    private sealed class FakeMeta : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string name) => name switch
        {
            "category" => new CollectionMetadata { Name = "category", Label = "Category", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            "tag" => new CollectionMetadata { Name = "tag", Label = "Tag", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            _ => null
        };
    }

    private static readonly IRelationshipGraph Graph = new FakeGraph();
    private static readonly IMetadataProvider Md = new FakeMeta();

    [Fact]
    public void Parses_single_hop_to_one()
    {
        var p = RelationPath.Parse("article", "category.name", Graph, Md, 5);
        p.Segments.Should().HaveCount(1);
        p.TerminalCollection.Should().Be("category");
        p.LeafField.Should().Be("name");
        p.IsSortable.Should().BeTrue();
    }

    [Fact]
    public void Parses_multi_level_to_one_and_is_sortable()
    {
        var p = RelationPath.Parse("article", "category.parent.name", Graph, Md, 5);
        p.Segments.Should().HaveCount(2);
        p.IsSortable.Should().BeTrue();
    }

    [Fact]
    public void Many_to_many_path_is_not_sortable()
    {
        var p = RelationPath.Parse("article", "tags.name", Graph, Md, 5);
        p.IsSortable.Should().BeFalse();
    }

    [Fact]
    public void Unknown_segment_throws()
    {
        var act = () => RelationPath.Parse("article", "ghost.name", Graph, Md, 5);
        act.Should().Throw<QueryException>().WithMessage("*ghost*");
    }

    [Fact]
    public void Unknown_leaf_field_throws()
    {
        var act = () => RelationPath.Parse("article", "category.nope", Graph, Md, 5);
        act.Should().Throw<QueryException>().WithMessage("*nope*");
    }

    [Fact]
    public void Over_depth_throws()
    {
        var act = () => RelationPath.Parse("article", "category.parent.name", Graph, Md, 1);
        act.Should().Throw<QueryException>().WithMessage("*depth*");
    }
}
