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
            // A hidden credential-style field (mirrors User.Password), matching the H2 fixture
            // in QueryValidatorTests. It must never be reachable as a relation-path leaf either —
            // otherwise meta.total becomes a blind-extraction oracle via any M2O relation.
            "category" => new CollectionMetadata { Name = "category", Label = "Category", FieldGroups = [],
                Fields =
                [
                    new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text },
                    new FieldMetadata { Name = "secret", Label = "Secret", Interface = FieldInterface.Text, Hidden = true },
                ] },
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

    // SEC-1: a Hidden field on the terminal collection of a relation path must be rejected as
    // a leaf, just like the own-field whitelist in QueryValidator already does — otherwise
    // meta.total becomes a blind-extraction oracle for hidden fields reachable across any
    // M2O relation (e.g. a "user" relation pointing at User.Password).
    [Fact]
    public void Hidden_leaf_field_on_relation_path_throws()
    {
        var act = () => RelationPath.Parse("article", "category.secret", Graph, Md, 5);
        act.Should().Throw<QueryException>().WithMessage("*Unknown field 'secret'*");
    }

    [Fact]
    public void Hidden_leaf_field_message_matches_unknown_field_shape()
    {
        // The rejection message must read exactly like a truly-unknown field — otherwise the
        // message itself becomes an oracle revealing that a hidden field named 'secret' exists.
        var hiddenAct = () => RelationPath.Parse("article", "category.secret", Graph, Md, 5);
        var unknownAct = () => RelationPath.Parse("article", "category.nope", Graph, Md, 5);
        var hiddenMessage = hiddenAct.Should().Throw<QueryException>().Which.Message;
        var unknownMessage = unknownAct.Should().Throw<QueryException>().Which.Message;
        hiddenMessage.Replace("secret", "X").Should().Be(unknownMessage.Replace("nope", "X"));
    }

    [Fact]
    public void Visible_leaf_field_on_relation_path_is_still_accepted()
    {
        var p = RelationPath.Parse("article", "category.name", Graph, Md, 5);
        p.LeafField.Should().Be("name");
    }

    [Fact]
    public void Over_depth_throws()
    {
        var act = () => RelationPath.Parse("article", "category.parent.name", Graph, Md, 1);
        act.Should().Throw<QueryException>().WithMessage("*depth*");
    }
}
