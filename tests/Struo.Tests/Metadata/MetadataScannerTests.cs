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
        MetadataScanner.ScanTypes([typeof(Article), typeof(Tag)])
            .Single(c => c.Name == "article");

    [Fact]
    public void Scans_collection_identity_in_camelCase()
    {
        var article = ArticleMeta();
        article.Name.Should().Be("article");
        article.Label.Should().Be("Article");
        article.Group.Should().Be("Content");
        article.DefaultDisplayField.Should().Be("title");
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
    public void Applies_seo_field_group_for_iseometa()
    {
        var article = ArticleMeta();
        article.FieldGroups.Should().Contain(g => g.Name == "SEO");
        var seoTitle = article.Fields.Single(f => f.Name == "seoTitle");
        seoTitle.Interface.Should().Be(FieldInterface.Text);
        seoTitle.Group.Should().Be("SEO");
        article.Fields.Should().Contain(f => f.Name == "seoMetaDescription" && f.Interface == FieldInterface.Textarea);
        article.Fields.Should().Contain(f => f.Name == "seoOgImageId" && f.Interface == FieldInterface.Number);
    }

    [Fact]
    public void Scans_multiple_collections()
    {
        var all = MetadataScanner.ScanTypes([typeof(Article), typeof(Tag)]);
        all.Select(c => c.Name).Should().BeEquivalentTo(["article", "tag"]);
    }

    [Fact]
    public void Non_option_field_has_no_options()
    {
        ArticleMeta().Fields.Single(f => f.Name == "title").Options.Should().BeNull();
    }
}
