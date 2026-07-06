using AwesomeAssertions;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Metadata;
using Struo.Sample.Blog;
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
}
