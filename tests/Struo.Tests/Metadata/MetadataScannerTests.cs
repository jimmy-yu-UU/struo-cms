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

    // A4: SafeGetTypes returns the loadable types of a normal assembly without throwing (the
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
        // Title moved to the translation sidecar (Phase 4); display field is now Status.
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

    // 7g.5: [CmsField(MaxLength)] resolution matrix.
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

        article.Fields.Single(f => f.Name == "audiences").Interface.Should().Be(FieldInterface.CheckboxGroup);
        article.Fields.Single(f => f.Name == "keywords").Interface.Should().Be(FieldInterface.Tags);
    }
}
