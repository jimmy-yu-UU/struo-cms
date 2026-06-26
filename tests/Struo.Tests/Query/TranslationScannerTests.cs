using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Query;

public class TranslationScannerTests
{
    [CmsCollection("TPost")]
    private sealed class TPost
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsField(Label = "Slug", Interface = FieldInterface.Text)] public string Slug { get; set; } = "";
        [CmsTranslations(typeof(TPostTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TPostTranslation> Translations { get; set; } = [];
    }
    private sealed class TPostTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TPostId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true)] public string Title { get; set; } = "";
    }

    [CmsCollection("TBad")]
    private sealed class TBad
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(TBadTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TBadTranslation> Translations { get; set; } = [];
    }
    private sealed class TBadTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TBadId { get; set; }
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
    }

    [Fact]
    public void Builds_translation_metadata_and_merges_fields()
    {
        var collections = MetadataScanner.ScanTypes([typeof(TPost)]);
        var post = collections.Single(c => c.Name == "tPost");
        post.Translation.Should().NotBeNull();
        post.Translation!.TranslationEntityType.Should().Be(typeof(TPostTranslation));
        post.Translation.ForeignKeyProperty.Should().Be("TPostId");
        post.Translation.LocaleProperty.Should().Be("Locale");
        post.Translation.Fields.Should().BeEquivalentTo("title");
        post.Fields.Should().Contain(f => f.Name == "slug" && !f.Translatable);
        post.Fields.Should().Contain(f => f.Name == "title" && f.Translatable);
    }

    [Fact]
    public void Malformed_translation_entity_throws_at_scan()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(TBad)]);
        act.Should().Throw<MetadataException>().WithMessage("*Locale*");
    }
}
