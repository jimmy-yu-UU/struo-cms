using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A translation class field is written by a <c>translations</c> payload without a read-only check, so
/// <c>ReadOnly = true</c> on it is refused at scan time.
/// </summary>
public sealed class TranslationReadOnlyCheckTests
{
    [CmsCollection("Tro read only", Name = "troReadOnly")]
    private sealed class ReadOnlyParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(ReadOnlyTr))]
        [SugarColumn(IsIgnore = true)] public List<ReadOnlyTr> Translations { get; set; } = [];
    }

    private sealed class ReadOnlyTr
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public Guid ReadOnlyParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
        [CmsField(Label = "Source", Interface = FieldInterface.Text, ReadOnly = true)] public string? Source { get; set; }
    }

    [CmsCollection("Tro writable", Name = "troWritable")]
    private sealed class WritableParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Source", Interface = FieldInterface.Text, ReadOnly = true)] public string? Source { get; set; }
        [CmsTranslations(typeof(WritableTr))]
        [SugarColumn(IsIgnore = true)] public List<WritableTr> Translations { get; set; } = [];
    }

    private sealed class WritableTr
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public Guid WritableParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
    }

    [Fact]
    public void A_read_only_translation_field_is_refused_and_the_message_names_the_ways_out()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(ReadOnlyParent)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Translation field 'ReadOnlyTr.Source' declares ReadOnly = true, but a translations payload writes it " +
            "on create and on update, so ReadOnly only changes what the schema reports. Remove ReadOnly = true, " +
            "or declare the property on the parent entity when server code owns the value.");
    }

    [Fact]
    public void A_read_only_field_on_the_parent_with_a_writable_translation_class_is_accepted()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(WritableParent)]);

        scan.Should().NotThrow();
    }
}
