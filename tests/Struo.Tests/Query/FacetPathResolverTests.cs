// tests/Struo.Tests/Query/FacetPathResolverTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Query;

public class FacetPathResolverTests
{
    private static readonly IReadOnlyList<Struo.Domain.Metadata.Models.CollectionMetadata> Collections =
        MetadataScanner.ScanTypes(SubqueryPushdownHarness.Types);
    private static readonly CachedMetadataProvider Md = new(Collections);
    private static readonly RelationshipGraph Graph = new(Collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["sqCategory"] = typeof(SqCategory), ["sqProduct"] = typeof(SqProduct), ["sqProperty"] = typeof(SqProperty),
        ["sqLabel"] = typeof(SqLabel), ["sqProductLabel"] = typeof(SqProductLabel),
        ["sqSoftLabel"] = typeof(SqSoftLabel), ["sqProductSoftLabel"] = typeof(SqProductSoftLabel),
    });
    private static Struo.Domain.Metadata.Models.CollectionMetadata Product => Md.GetCollection("sqProduct")!;

    private static ResolvedFacetPath Resolve(string raw) => FacetPathResolver.Resolve(Product, raw, Graph, Md);

    [Fact]
    public void Own_scalar_field_resolves_to_OwnField()
    {
        var r = Resolve("name");
        r.Kind.Should().Be(FacetPathKind.OwnField);
        r.OwnField.Should().Be("name");
        r.Relation.Should().BeNull();
    }

    [Fact]
    public void Many_to_one_foreign_key_resolves_to_ForeignKey_with_its_relation()
    {
        var r = Resolve("categoryId");
        r.Kind.Should().Be(FacetPathKind.ForeignKey);
        r.Relation!.Name.Should().Be("category");
        r.OwnField.Should().Be("categoryId");
    }

    [Theory]
    [InlineData("category", RelationKind.ManyToOne)]
    [InlineData("properties", RelationKind.OneToMany)]
    [InlineData("labels", RelationKind.ManyToMany)]
    public void Relation_name_resolves_to_Relation_for_every_kind(string raw, RelationKind kind)
    {
        var r = Resolve(raw);
        r.Kind.Should().Be(FacetPathKind.Relation);
        r.Relation!.Kind.Should().Be(kind);
        r.LeafField.Should().BeNull();
    }

    [Fact]
    public void One_hop_plus_leaf_resolves_to_RelationLeaf()
    {
        var r = Resolve("properties.code");
        r.Kind.Should().Be(FacetPathKind.RelationLeaf);
        r.Relation!.Name.Should().Be("properties");
        r.LeafField.Should().Be("code");
        r.TargetCollection.Should().Be("sqProperty");
    }

    [Fact]
    public void Two_hops_are_rejected()
    {
        var act = () => Resolve("category.articles.title");
        act.Should().Throw<QueryException>().WithMessage("*exactly one relation hop*");
    }

    [Theory]
    [InlineData("properties._some")]
    [InlineData("labels._junction.note")]
    [InlineData("_none")]
    public void Quantifiers_and_junction_are_rejected(string raw)
    {
        var act = () => Resolve(raw);
        act.Should().Throw<QueryException>().WithMessage("*cannot contain quantifiers or '_junction'*");
    }

    [Fact]
    public void Unknown_own_name_is_an_unknown_field()
    {
        var act = () => Resolve("nope");
        act.Should().Throw<QueryException>().WithMessage("Unknown field 'nope' on collection 'sqProduct'.");
    }

    [Fact]
    public void Unknown_relation_in_a_dotted_path_is_reported_as_unknown_relation()
    {
        var act = () => Resolve("nope.code");
        act.Should().Throw<QueryException>().WithMessage("Unknown relation 'nope' on collection 'sqProduct'.");
    }

    [Fact]
    public void Unknown_leaf_is_reported_against_the_target_collection()
    {
        var act = () => Resolve("properties.nope");
        act.Should().Throw<QueryException>().WithMessage("Unknown field 'nope' on collection 'sqProperty'.");
    }

    [Fact]
    public void Empty_path_is_rejected()
    {
        var act = () => Resolve(" ");
        act.Should().Throw<QueryException>().WithMessage("Facet path must not be empty.");
    }

    [Fact]
    public void Facetable_set_excludes_long_text_and_multi_value_interfaces()
    {
        FacetPathResolver.Facetable.Should().Contain([FieldInterface.Text, FieldInterface.Select, FieldInterface.Number, FieldInterface.Boolean, FieldInterface.DateTime, FieldInterface.Uuid, FieldInterface.Image]);
        FacetPathResolver.Facetable.Should().NotContain([FieldInterface.RichText, FieldInterface.Markdown, FieldInterface.Code, FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags, FieldInterface.Repeater, FieldInterface.Json, FieldInterface.KeyValue, FieldInterface.Files, FieldInterface.Hidden, FieldInterface.Divider, FieldInterface.Password]);
    }
}
