using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

public sealed class JunctionPayloadMetadataTests
{
    [SugarTable("jpm_parents")]
    [CmsCollection("Jpm parent")]
    public sealed class JpmParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        [Navigate(typeof(JpmLink), nameof(JpmLink.JpmParentId), nameof(JpmLink.JpmChildId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(JpmLink.Sort))]
        [SugarColumn(IsIgnore = true)]
        public List<JpmChild> Children { get; set; } = [];
    }

    [SugarTable("jpm_children")]
    [CmsCollection("Jpm child")]
    public sealed class JpmChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [SugarTable("jpm_links")]
    [CmsCollection("Jpm link", Hidden = true)]
    public sealed class JpmLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Parent", Interface = FieldInterface.Uuid)] public Guid JpmParentId { get; set; }
        [CmsField(Label = "Child", Interface = FieldInterface.Uuid)] public Guid JpmChildId { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public string? Note { get; set; }
        [CmsField(Label = "Secret", Interface = FieldInterface.Text, Hidden = true)] public string? Secret { get; set; }
        [CmsField(Label = "Sort", Interface = FieldInterface.Number)] public int Sort { get; set; }
    }

    // A junction that is NOT a collection — the pre-U2 shape — must yield no junction metadata.
    [SugarTable("jpm_plain_links")]
    public sealed class JpmPlainLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid JpmPlainParentId { get; set; }
        public Guid JpmChildId { get; set; }
    }

    [SugarTable("jpm_plain_parents")]
    [CmsCollection("Jpm plain parent")]
    public sealed class JpmPlainParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(JpmPlainLink), nameof(JpmPlainLink.JpmPlainParentId), nameof(JpmPlainLink.JpmChildId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<JpmChild> Children { get; set; } = [];
    }

    // A junction collection whose FKs are NOT [CmsField]s — must fail fast.
    [SugarTable("jpm_bad_links")]
    [CmsCollection("Jpm bad link", Hidden = true)]
    public sealed class JpmBadLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid JpmBadParentId { get; set; }
        public Guid JpmChildId { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public string? Note { get; set; }
    }

    [SugarTable("jpm_bad_parents")]
    [CmsCollection("Jpm bad parent")]
    public sealed class JpmBadParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(JpmBadLink), nameof(JpmBadLink.JpmBadParentId), nameof(JpmBadLink.JpmChildId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<JpmChild> Children { get; set; } = [];
    }

    private static (RelationshipGraph Graph, IReadOnlyList<Struo.Domain.Metadata.Models.CollectionMetadata> Collections) Build(params Type[] types)
    {
        var collections = MetadataScanner.ScanTypes(types);
        var collectionTypes = types
            .Where(t => t.GetCustomAttributes(typeof(CmsCollectionAttribute), false).Length > 0)
            .ToDictionary(t => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(t.Name), t => t, StringComparer.OrdinalIgnoreCase);
        return (new RelationshipGraph(collections, collectionTypes), collections);
    }

    [Fact]
    public void Relation_metadata_names_the_junction_collection_when_the_junction_is_a_collection()
    {
        var (_, collections) = Build(typeof(JpmParent), typeof(JpmChild), typeof(JpmLink));
        var rel = collections.Single(c => c.Name == "jpmParent").Relations.Single(r => r.Name == "children");
        rel.JunctionCollection.Should().Be("jpmLink");
    }

    [Fact]
    public void Relation_metadata_has_null_junction_collection_for_a_plain_junction()
    {
        var (_, collections) = Build(typeof(JpmPlainParent), typeof(JpmChild), typeof(JpmPlainLink));
        collections.Single(c => c.Name == "jpmPlainParent").Relations.Single().JunctionCollection.Should().BeNull();
    }

    [Fact]
    public void M2M_descriptor_carries_payload_fields_excluding_fks_and_sort_and_keeps_hidden_flag()
    {
        var (graph, _) = Build(typeof(JpmParent), typeof(JpmChild), typeof(JpmLink));
        var desc = graph.M2MDescriptors("jpmParent").Single();
        desc.JunctionCollection.Should().Be("jpmLink");
        desc.HasPayload.Should().BeTrue();
        desc.JunctionPayload!.Select(p => (p.Name, p.Property, p.Hidden)).Should().BeEquivalentTo(
        [
            ("note", "Note", false),
            ("secret", "Secret", true),
        ]);
    }

    [Fact]
    public void Plain_junction_descriptor_has_no_payload()
    {
        var (graph, _) = Build(typeof(JpmPlainParent), typeof(JpmChild), typeof(JpmPlainLink));
        var desc = graph.M2MDescriptors("jpmPlainParent").Single();
        desc.JunctionCollection.Should().BeNull();
        desc.HasPayload.Should().BeFalse();
        desc.JunctionPayload.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Inbound_descriptor_carries_the_same_payload()
    {
        var (graph, _) = Build(typeof(JpmParent), typeof(JpmChild), typeof(JpmLink));
        graph.InboundM2MDescriptors("jpmChild").Single().Descriptor.HasPayload.Should().BeTrue();
    }

    [Fact]
    public void Junction_collection_without_writable_fk_fields_fails_fast()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(JpmBadParent), typeof(JpmChild), typeof(JpmBadLink)]);
        act.Should().Throw<MetadataException>()
            .WithMessage("*jpmBadLink*").And.Message.Should().ContainAll("jpmBadParent", "children", "JpmBadParentId", "JpmChildId", "FieldInterface.Uuid");
    }
}
