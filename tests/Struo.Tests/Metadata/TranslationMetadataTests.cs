using AwesomeAssertions;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.Metadata;

public class TranslationMetadataTests
{
    [Fact]
    public void TranslationMetadata_holds_shape()
    {
        var tm = new TranslationMetadata
        {
            TranslationEntityType = typeof(string),
            ForeignKeyProperty = "ArticleId",
            LocaleProperty = "Locale",
            Fields = ["title", "body"]
        };
        tm.Fields.Should().BeEquivalentTo("title", "body");
        tm.ForeignKeyProperty.Should().Be("ArticleId");
    }

    [Fact]
    public void CollectionMetadata_translation_defaults_null()
    {
        var c = new CollectionMetadata { Name = "x", Label = "X", FieldGroups = [], Fields = [] };
        c.Translation.Should().BeNull();
    }

    [Fact]
    public void LanguageInfo_holds_shape()
    {
        var l = new LanguageInfo("en", "English", true, true, 1);
        (l.Code, l.IsDefault, l.Enabled).Should().Be(("en", true, true));
    }
}
