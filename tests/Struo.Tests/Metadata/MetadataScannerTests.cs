using AwesomeAssertions;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Metadata;
using Struo.Sample.Blog;
using SqlSugar;
using Xunit;

namespace Struo.Tests.Metadata;

public class MetadataScannerTests
{
    private static CollectionMetadata ArticleMeta() =>
        MetadataScanner.ScanTypes([typeof(Article), typeof(Category)])
            .Single(c => c.Name == "article");

    // SafeGetTypes returns the loadable types of a normal assembly without throwing (the
    // ReflectionTypeLoadException path only triggers on an assembly with an unresolvable type).
    [Fact]
    public void SafeGetTypes_returns_loadable_types()
    {
        var types = MetadataScanner.SafeGetTypes(typeof(Article).Assembly).ToList();
        types.Should().Contain(typeof(Article));
    }

    [Fact]
    public void Scans_collection_identity_in_camelCase()
    {
        var article = ArticleMeta();
        article.Name.Should().Be("article");
        article.Label.Should().Be("Article");
        article.Group.Should().Be("Content");
        // Title moved to the translation sidecar; display field is now Status.
        article.DefaultDisplayField.Should().Be("status");
    }

    [Fact]
    public void Maps_field_interface_and_flags()
    {
        var title = ArticleMeta().Fields.Single(f => f.Name == "title");
        title.Interface.Should().Be(FieldInterface.Text);
        title.Required.Should().BeTrue();
        title.Searchable.Should().BeTrue();
        title.Translatable.Should().BeTrue();
        title.Group.Should().Be("Content");
        // The one sortable field this repo ships: it is what makes SortableHeader's three-state
        // cycle and DataTable's "never re-sorts client-side" guards reachable in the demo.
        title.Sortable.Should().BeTrue();

        var body = ArticleMeta().Fields.Single(f => f.Name == "body");
        body.Interface.Should().Be(FieldInterface.RichText);
        body.Translatable.Should().BeTrue();
    }

    [Fact]
    public void Parses_cms_options_into_value_label_pairs()
    {
        var status = ArticleMeta().Fields.Single(f => f.Name == "status");
        status.Interface.Should().Be(FieldInterface.Select);
        status.Options.Should().NotBeNull();
        status.Options!.Should().ContainInOrder(
            new FieldOption("draft", "Draft"),
            new FieldOption("published", "Published"));
    }

    [Fact]
    public void Emits_audit_fields_as_system_readonly()
    {
        var createdAt = ArticleMeta().Fields.Single(f => f.Name == "createdAt");
        createdAt.IsSystem.Should().BeTrue();
        createdAt.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Article_seo_fields_are_translatable_on_the_sidecar()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)]);
        var article = collections.Single(c => c.Name == "article");

        var seoTitle = article.Fields.SingleOrDefault(f => f.Name == "seoTitle");
        seoTitle.Should().NotBeNull();
        seoTitle!.Translatable.Should().BeTrue();
        seoTitle.Group.Should().Be("SEO");

        article.Fields.Should().Contain(f => f.Name == "seoOgImageId" && f.Translatable);
        // No NON-translatable parent SEO field should remain:
        article.Fields.Should().NotContain(f => f.Name == "seoTitle" && !f.Translatable);
    }

    [Fact]
    public void Scans_multiple_collections()
    {
        var all = MetadataScanner.ScanTypes([typeof(Article), typeof(Category)]);
        all.Select(c => c.Name).Should().BeEquivalentTo(["article", "category"]);
    }

    [Fact]
    public void Non_option_field_has_no_options()
    {
        ArticleMeta().Fields.Single(f => f.Name == "title").Options.Should().BeNull();
    }

    [Fact]
    public void Does_not_emit_audit_fields_for_non_iauditable_types()
    {
        var fields = MetadataScanner.ScanTypes([typeof(NoAuditEntity)]).Single().Fields;
        fields.Should().Contain(f => f.Name == "name");
        fields.Should().NotContain(f => f.Name == "createdAt");
    }

    [Struo.Domain.Metadata.Attributes.CmsCollection("NoAudit")]
    private sealed class NoAuditEntity
    {
        [Struo.Domain.Metadata.Attributes.CmsField(Interface = Struo.Domain.Metadata.Enums.FieldInterface.Text)]
        public string Name { get; set; } = "";
        public System.DateTime CreatedAt { get; set; }   // NOT IAuditable -> must be ignored
    }

    // [CmsField(MaxLength)] resolution matrix.
    [CmsCollection("maxLenSample")]
    private sealed class MaxLenSample : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text, MaxLength = 100)] public string Declared { get; set; } = string.Empty;
        [CmsField(Interface = FieldInterface.Text)] public string ShortDefault { get; set; } = string.Empty;
        [CmsField(Interface = FieldInterface.Textarea)] public string ContentDefault { get; set; } = string.Empty;
        [CmsField(Interface = FieldInterface.Textarea, MaxLength = 5000)] public string ContentDeclared { get; set; } = string.Empty;
    }

    [Fact]
    public void MaxLength_resolution_matrix()
    {
        var meta = MetadataScanner.ScanTypes([typeof(MaxLenSample)]).Single();
        meta.Fields.Single(f => f.Name == "declared").MaxLength.Should().Be(100);
        meta.Fields.Single(f => f.Name == "shortDefault").MaxLength.Should().Be(255);
        meta.Fields.Single(f => f.Name == "contentDefault").MaxLength.Should().BeNull();
        meta.Fields.Single(f => f.Name == "contentDeclared").MaxLength.Should().Be(5000);
    }

    [CmsCollection("negativeMaxLen")]
    private sealed class NegativeMaxLen : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        [CmsField(MaxLength = -1)] public string Name { get; set; } = string.Empty;
    }

    [CmsCollection("maxLenOnNonString")]
    private sealed class MaxLenOnNonString : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Number, MaxLength = 10)] public int Count { get; set; }
    }

    [Fact]
    public void Negative_MaxLength_fails_fast()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(NegativeMaxLen)]);
        act.Should().Throw<MetadataException>().WithMessage("*MaxLength*negative*");
    }

    [Fact]
    public void MaxLength_on_non_string_property_fails_fast()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(MaxLenOnNonString)]);
        act.Should().Throw<MetadataException>().WithMessage("*MaxLength*string*");
    }

    [Fact]
    public void Article_exposes_multi_value_fields_with_correct_metadata()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)]);
        var article = collections.Single(c => string.Equals(c.Name, "article", StringComparison.OrdinalIgnoreCase));

        var regions = article.Fields.Single(f => f.Name == "regions");
        regions.Interface.Should().Be(FieldInterface.MultiSelect);
        regions.Sortable.Should().BeFalse();
        regions.Searchable.Should().BeFalse();
        regions.Options!.Should().ContainSingle(o => o.Value == "amer" && o.Label == "amer"); // value-fallback

        var audiences = article.Fields.Single(f => f.Name == "audiences");
        audiences.Interface.Should().Be(FieldInterface.CheckboxGroup);
        audiences.Sortable.Should().BeFalse();
        audiences.Searchable.Should().BeFalse();

        var keywords = article.Fields.Single(f => f.Name == "keywords");
        keywords.Interface.Should().Be(FieldInterface.Tags);
        keywords.Sortable.Should().BeFalse();
        keywords.Searchable.Should().BeFalse();
    }

    [Fact]
    public void Article_exposes_structured_fields_with_correct_metadata()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)]);
        var article = collections.Single(c => string.Equals(c.Name, "article", StringComparison.OrdinalIgnoreCase));

        var attributes = article.Fields.Single(f => f.Name == "attributes");
        attributes.Interface.Should().Be(FieldInterface.Json);
        attributes.Sortable.Should().BeFalse();
        attributes.Searchable.Should().BeFalse();
        attributes.MaxLength.Should().BeNull(); // Json is content-bearing => unlimited

        var meta = article.Fields.Single(f => f.Name == "meta");
        meta.Interface.Should().Be(FieldInterface.KeyValue);
        meta.Sortable.Should().BeFalse();
        meta.Searchable.Should().BeFalse();
    }

    [Fact]
    public void Article_exposes_files_field_with_correct_metadata()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)]);
        var article = collections.Single(c => string.Equals(c.Name, "article", StringComparison.OrdinalIgnoreCase));

        var gallery = article.Fields.Single(f => f.Name == "gallery");
        gallery.Interface.Should().Be(FieldInterface.Files);
        gallery.Sortable.Should().BeFalse();
        gallery.Searchable.Should().BeFalse();
        gallery.Translatable.Should().BeFalse();
    }

    [SugarTable("bad_translatable_files")]
    [CmsCollection("BadTranslatableFiles")]
    private sealed class BadTranslatableFiles : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Gallery", Interface = FieldInterface.Files, Translatable = true)]
        public List<Guid> Gallery { get; set; } = new();
    }

    [SugarTable("ok_translatable_json")]
    [CmsCollection("OkTranslatableJson")]
    private sealed class OkTranslatableJson : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        // Json is intentionally NOT caught by the guard (a string that could be translatable later).
        [CmsField(Label = "Attributes", Interface = FieldInterface.Json, Translatable = true)]
        public string? Attributes { get; set; }
    }

    [Fact]
    public void Translatable_files_field_fails_fast()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(BadTranslatableFiles)]);
        act.Should().Throw<MetadataException>()
            .WithMessage("*Gallery*cannot be translatable*");
    }

    [Fact]
    public void Translatable_json_field_is_allowed()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(OkTranslatableJson)]);
        act.Should().NotThrow();
    }

    // Repeater nested sub-field schema.
    public sealed class FaqRow
    {
        [CmsField(Label = "Question", Interface = FieldInterface.Text, Required = true)]
        public string Question { get; set; } = "";

        [CmsField(Label = "Answer", Interface = FieldInterface.Textarea)]
        public string Answer { get; set; } = "";

        [CmsField(Label = "Category", Interface = FieldInterface.Select)]
        [CmsOptions("general:General", "billing:Billing")]
        public string? Category { get; set; }
    }

    [CmsCollection("RepeaterHost")]
    public sealed class RepeaterHost
    {
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [CmsField(Label = "FAQs", Interface = FieldInterface.Repeater)]
        public List<FaqRow> Faqs { get; set; } = new();
    }

    [Fact]
    public void Repeater_field_carries_nested_sub_field_schema()
    {
        var meta = MetadataScanner.ScanTypes([typeof(RepeaterHost)]).Single();
        var faqs = meta.Fields.Single(f => f.Name == "faqs");

        faqs.Interface.Should().Be(FieldInterface.Repeater);
        faqs.Fields.Should().NotBeNull();
        faqs.Fields!.Select(f => f.Name).Should().Equal("question", "answer", "category");

        var question = faqs.Fields!.Single(f => f.Name == "question");
        question.Interface.Should().Be(FieldInterface.Text);
        question.Required.Should().BeTrue();

        var category = faqs.Fields!.Single(f => f.Name == "category");
        category.Options!.Select(o => o.Value).Should().Equal("general", "billing");
    }

    [Fact]
    public void Non_repeater_field_has_null_sub_fields()
    {
        var meta = MetadataScanner.ScanTypes([typeof(RepeaterHost)]).Single();
        meta.Fields.Single(f => f.Name == "name").Fields.Should().BeNull();
    }

    [Fact]
    public void Article_sample_has_a_repeater_faqs_field()
    {
        var meta = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)])
            .Single(c => c.Name == "article");
        var faqs = meta.Fields.Single(f => f.Name == "faqs");
        faqs.Interface.Should().Be(FieldInterface.Repeater);
        faqs.Fields!.Select(f => f.Name).Should().Contain("question");
    }

    [Fact]
    public void Scan_marks_soft_deletable_collection()
    {
        var metas = MetadataScanner.ScanTypes([typeof(SoftColl), typeof(HardColl)]);
        Assert.True(metas.Single(m => m.Name == "softColl").SoftDelete);
        Assert.False(metas.Single(m => m.Name == "hardColl").SoftDelete);
    }

    [CmsCollection("SoftColl")]
    private sealed class SoftColl : Struo.Domain.Auditing.ISoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }
    }

    [CmsCollection("HardColl")]
    private sealed class HardColl
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [Fact]
    public void Sample_article_and_category_are_soft_deletable()
    {
        var metas = MetadataScanner.Scan(typeof(Struo.Sample.Blog.Article).Assembly);
        Assert.True(metas.Single(m => m.Name == "article").SoftDelete);
        Assert.True(metas.Single(m => m.Name == "category").SoftDelete);
    }

    [Fact]
    public void Scan_marks_revisioned_collection()
    {
        var metas = MetadataScanner.ScanTypes([typeof(RevColl), typeof(PlainColl)]);
        Assert.True(metas.Single(m => m.Name == "revColl").Revisions);
        Assert.False(metas.Single(m => m.Name == "plainColl").Revisions);
    }

    [CmsCollection("RevColl", Revisions = true)]
    private sealed class RevColl
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [CmsCollection("PlainColl")]
    private sealed class PlainColl
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    // EntityDescriptor.Properties caches CLR-property -> PropertyInfo (built once from
    // EntityType) so projection/snapshot hot paths resolve accessors via a dictionary lookup instead
    // of per-row Type.GetProperty reflection.
    [Fact]
    public void Descriptor_exposes_Properties_keyed_by_clr_property_name()
    {
        var d = MetadataScanner.ScanDescriptors([typeof(Article), typeof(Category)])["article"];

        // Every public instance property of the entity is present, keyed by its exact CLR name,
        // and each entry is the real PropertyInfo declared on that type.
        d.Properties.Should().ContainKey("Status");
        d.Properties["Status"].Should().BeSameAs(typeof(Article).GetProperty("Status"));
        d.Properties["Id"].Should().BeSameAs(typeof(Article).GetProperty("Id"));
    }

    [Fact]
    public void Descriptor_Properties_lookup_is_case_insensitive()
    {
        var d = MetadataScanner.ScanDescriptors([typeof(Article), typeof(Category)])["article"];

        // Case-insensitive keying: a camelCase or lower-cased spelling resolves to the same accessor
        // as the exact CLR name.
        d.Properties.GetValueOrDefault("status").Should().BeSameAs(d.Properties["Status"]);
        d.Properties.GetValueOrDefault("STATUS").Should().BeSameAs(d.Properties["Status"]);

        d.Properties.GetValueOrDefault("noSuchProperty").Should().BeNull();
    }

    // The sample's article<->tag junction (ArticleTag) is a hidden [CmsCollection] carrying a Note
    // payload and a Sort column (see ArticleTag.cs) — this pins the two things that make it a real
    // demo of the junction-payload feature: the relation names its junction collection, and the M2M
    // descriptor's payload is exactly the one non-FK, non-sort field.
    [Fact]
    public void Sample_article_tags_relation_names_the_hidden_junction_collection_with_note_payload()
    {
        var article = ArticleMeta();
        var tags = article.Relations.Single(r => r.Name == "tags");
        tags.JunctionCollection.Should().Be("articleTag");

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag), typeof(ArticleTag) };
        var collections = MetadataScanner.ScanTypes(types);
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article),
            ["category"] = typeof(Category),
            ["tag"] = typeof(Tag),
            ["articleTag"] = typeof(ArticleTag),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var desc = graph.M2MDescriptors("article").Single(d => d.RelationName == "tags");

        desc.JunctionCollection.Should().Be("articleTag");
        desc.HasPayload.Should().BeTrue();
        desc.JunctionPayload!.Select(p => p.Name).Should().BeEquivalentTo(["note"]);
        desc.SortProperty.Should().Be("Sort");
    }
}
