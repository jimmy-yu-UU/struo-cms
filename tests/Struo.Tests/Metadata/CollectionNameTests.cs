using AwesomeAssertions;
using SqlSugar;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

public class CollectionNameTests
{
    [CmsCollection("Pinned", Name = "legacyArticle")]
    private sealed class CnPinnedArticle
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Title { get; set; } = "";
        public Guid? CnDerivedId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CnDerivedId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public CnDerived? Derived { get; set; }
    }

    [CmsCollection("Target", Name = "pinnedTarget")]
    private sealed class CnPinnedTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Title { get; set; } = "";
    }

    [CmsCollection("Source")]
    private sealed class CnSource
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? CnPinnedTargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CnPinnedTargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public CnPinnedTarget? Target { get; set; }

        [Navigate(typeof(CnPinnedLink), nameof(CnPinnedLink.CnSourceId), nameof(CnPinnedLink.CnPinnedTargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<CnPinnedTarget> Targets { get; set; } = [];
    }

    [CmsCollection("Link", Name = "pinnedLink", Hidden = true)]
    private sealed class CnPinnedLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Uuid)] public Guid CnSourceId { get; set; }
        [CmsField(Interface = FieldInterface.Uuid)] public Guid CnPinnedTargetId { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string? Note { get; set; }
    }

    [CmsCollection("Derived")]
    private sealed class CnDerived
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Title { get; set; } = "";
    }

    [CmsCollection("Fixed", Creatable = false)]
    private sealed class CnNotCreatable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [CmsCollection("Bad", Name = "Bad-Name")]
    private sealed class CnBadDash { }

    [CmsCollection("Bad", Name = "Upper")]
    private sealed class CnBadUpper { }

    [CmsCollection("Bad", Name = "")]
    private sealed class CnBadEmpty { }

    [CmsCollection("Bad", Name = "abc\n")]
    private sealed class CnBadNewline { }

    [CmsCollection("Bad", Name = "1abc")]
    private sealed class CnBadDigit { }

    [CmsCollection("A", Name = "shared")]
    private sealed class CnSharedA { }

    [CmsCollection("B", Name = "shared")]
    private sealed class CnSharedB { }

    [CmsCollection("Clash")]
    private sealed class CnClash { }

    [CmsCollection("Clash2", Name = "cnClash")]
    private sealed class CnClashExplicit { }

    [Fact]
    public void Creatable_defaults_to_true_and_the_attribute_turns_it_off()
    {
        MetadataScanner.ScanTypes([typeof(CnDerived)]).Single().Creatable.Should().BeTrue();
        MetadataScanner.ScanTypes([typeof(CnNotCreatable)]).Single().Creatable.Should().BeFalse();
    }

    [Fact]
    public void Explicit_name_replaces_the_derived_name()
    {
        var meta = MetadataScanner.ScanTypes([typeof(CnPinnedArticle), typeof(CnDerived)]);

        meta.Select(c => c.Name).Should().BeEquivalentTo("legacyArticle", "cnDerived");
    }

    [Fact]
    public void Type_without_a_name_keeps_the_camel_cased_type_name()
    {
        MetadataScanner.ScanTypes([typeof(CnDerived)]).Single().Name.Should().Be("cnDerived");
    }

    [Fact]
    public void Descriptors_are_keyed_by_the_explicit_name()
    {
        var descriptors = MetadataScanner.ScanDescriptors([typeof(CnPinnedArticle), typeof(CnDerived)]);

        descriptors.Keys.Should().BeEquivalentTo("legacyArticle", "cnDerived");
        descriptors["legacyArticle"].EntityType.Should().Be<CnPinnedArticle>();
    }

    private static readonly Type[] RelationTypes = [typeof(CnSource), typeof(CnPinnedTarget), typeof(CnPinnedLink)];

    [Fact]
    public void Relations_target_a_collection_and_junction_by_explicit_name()
    {
        var meta = MetadataScanner.ScanTypes(RelationTypes);
        var source = meta.Single(c => c.Name == "cnSource");

        source.Relations.Single(r => r.Name == "target").TargetCollection.Should().Be("pinnedTarget");
        var m2m = source.Relations.Single(r => r.Name == "targets");
        m2m.TargetCollection.Should().Be("pinnedTarget");
        m2m.JunctionCollection.Should().Be("pinnedLink");
        m2m.JunctionPayloadFields.Should().Equal("note");
    }

    [Fact]
    public void Relationship_graph_builds_from_the_collection_type_map_used_at_startup()
    {
        var meta = MetadataScanner.ScanTypes(RelationTypes);

        var graph = new RelationshipGraph(meta, MetadataScanner.CollectionTypeMap(RelationTypes));

        MetadataScanner.CollectionTypeMap(RelationTypes).Keys
            .Should().BeEquivalentTo("cnSource", "pinnedTarget", "pinnedLink");
        graph.Relations("cnSource").Select(r => r.TargetCollection).Should().OnlyContain(t => t == "pinnedTarget");
        graph.M2MDescriptors("cnSource").Single().JunctionCollection.Should().Be("pinnedLink");
    }

    [Fact]
    public void Graphql_and_revision_names_follow_the_explicit_name()
    {
        var name = MetadataScanner.ScanTypes([typeof(CnPinnedArticle)]).Single().Name;

        SchemaTypeMapper.TypeName(name).Should().Be("LegacyArticle");
        SchemaTypeMapper.SingleFieldName(name).Should().Be("legacyArticle");
        SchemaTypeMapper.ListFieldName(name).Should().Be("legacyArticles");
        SchemaTypeMapper.RevisionsFieldName(name).Should().Be("legacyArticleRevisions");
    }

    [Theory]
    [InlineData(typeof(CnBadDash), "Bad-Name")]
    [InlineData(typeof(CnBadUpper), "Upper")]
    [InlineData(typeof(CnBadEmpty), "")]
    [InlineData(typeof(CnBadDigit), "1abc")]
    [InlineData(typeof(CnBadNewline), "abc\n")]
    public void Invalid_name_names_the_type_and_the_rule(Type type, string name)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        act.Should().Throw<MetadataException>()
            .WithMessage($"*'{type.Name}'*'{name}'*^[a-z][A-Za-z0-9]*$*");
    }

    [Fact]
    public void Duplicate_explicit_name_names_both_types()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(CnSharedA), typeof(CnSharedB)]);

        act.Should().Throw<MetadataException>()
            .WithMessage("*Duplicate collection name 'shared'*CnSharedA*CnSharedB*");
    }

    [Fact]
    public void Explicit_name_colliding_with_a_derived_name_names_both_types()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(CnClash), typeof(CnClashExplicit)]);

        act.Should().Throw<MetadataException>()
            .WithMessage("*Duplicate collection name 'cnClash'*CnClash*CnClashExplicit*");
    }
}
