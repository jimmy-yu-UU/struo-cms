using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Metadata;

// An explicit MaxLength wider than the declared column fails the scan; a property without a column
// width, and a Repeater sub-field (which lives inside a JSON value), is not checked.
public sealed class MaxLengthColumnWidthTests
{
    [CmsCollection("Default width over", Name = "mlwDefaultOver")]
    public sealed class DefaultWidthOver
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text, MaxLength = 256)] public string? Wide { get; set; }
    }

    [CmsCollection("Default width at", Name = "mlwDefaultAt")]
    public sealed class DefaultWidthAt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text, MaxLength = 255)] public string? AtWidth { get; set; }
        [CmsField(Interface = FieldInterface.Text, MaxLength = 100)] public string? Below { get; set; }
    }

    [CmsCollection("Declared width over", Name = "mlwDeclaredOver")]
    public sealed class DeclaredWidthOver
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(Length = 100)]
        [CmsField(Interface = FieldInterface.Text, MaxLength = 200)] public string? Narrow { get; set; }
    }

    [CmsCollection("Declared width at", Name = "mlwDeclaredAt")]
    public sealed class DeclaredWidthAt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(Length = 100)]
        [CmsField(Interface = FieldInterface.Text, MaxLength = 100)] public string? Narrow { get; set; }
    }

    [CmsCollection("Typed width over", Name = "mlwTypedOver")]
    public sealed class TypedWidthOver
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "varchar", Length = 64)]
        [CmsField(Interface = FieldInterface.Text, MaxLength = 100)] public string? Typed { get; set; }
    }

    [CmsCollection("No width", Name = "mlwNoWidth")]
    public sealed class NoWidth
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Textarea, MaxLength = 5000)] public string? Content { get; set; }
        [CmsField(Interface = FieldInterface.Json, MaxLength = 5000)] public string? Raw { get; set; }
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Interface = FieldInterface.Text, MaxLength = 5000)] public string? Shaped { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Interface = FieldInterface.Text, MaxLength = 5000)] public string? JsonColumn { get; set; }
        [SugarColumn(ColumnDataType = "text")]
        [CmsField(Interface = FieldInterface.Text, MaxLength = 5000)] public string? TypedNoLength { get; set; }
    }

    [CmsCollection("Translated", Name = "mlwTranslated")]
    public sealed class Translated
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TranslatedTranslation))]
        [SugarColumn(IsIgnore = true)]
        public List<TranslatedTranslation> Translations { get; set; } = [];
    }

    public sealed class TranslatedTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public Guid TranslatedId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Interface = FieldInterface.Text, MaxLength = 1000)] public string? Title { get; set; }
    }

    [SugarTable("mlw_parents")]
    [CmsCollection("Link parent", Name = "mlwParent")]
    public sealed class LinkParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(PayloadLink), nameof(PayloadLink.ParentId), nameof(PayloadLink.ChildId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<LinkChild> Children { get; set; } = [];
    }

    [SugarTable("mlw_children")]
    [CmsCollection("Link child", Name = "mlwChild")]
    public sealed class LinkChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("mlw_links")]
    [CmsCollection("Payload link", Name = "mlwLink", Hidden = true)]
    public sealed class PayloadLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Uuid)] public Guid ParentId { get; set; }
        [CmsField(Interface = FieldInterface.Uuid)] public Guid ChildId { get; set; }
        [CmsField(Interface = FieldInterface.Text, MaxLength = 400)] public string? Note { get; set; }
    }

    public sealed class RepeaterItem
    {
        [CmsField(Interface = FieldInterface.Text, MaxLength = 1000)] public string? Line { get; set; }
    }

    [CmsCollection("Repeater host", Name = "mlwRepeater")]
    public sealed class RepeaterHost
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Interface = FieldInterface.Repeater)]
        public List<RepeaterItem> Items { get; set; } = [];
    }

    [Theory]
    [InlineData(typeof(DefaultWidthOver), "DefaultWidthOver", "Wide", 256, 255)]
    [InlineData(typeof(DeclaredWidthOver), "DeclaredWidthOver", "Narrow", 200, 100)]
    [InlineData(typeof(TypedWidthOver), "TypedWidthOver", "Typed", 100, 64)]
    public void A_MaxLength_wider_than_the_column_fails_the_scan(
        Type type, string className, string property, int declared, int width)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain(className).And.Contain(property)
            .And.Contain($"MaxLength {declared}").And.Contain($"{width}")
            .And.Contain("SugarColumn(Length").And.Contain("ColumnShape");
    }

    [Fact]
    public void A_MaxLength_equal_to_or_below_the_column_width_scans()
    {
        var at = MetadataScanner.ScanTypes([typeof(DefaultWidthAt)]).Single().Fields;
        at.Single(f => f.Name == "atWidth").MaxLength.Should().Be(255);
        at.Single(f => f.Name == "below").MaxLength.Should().Be(100);

        var declared = MetadataScanner.ScanTypes([typeof(DeclaredWidthAt)]).Single().Fields;
        declared.Single(f => f.Name == "narrow").MaxLength.Should().Be(100);
    }

    [Fact]
    public void A_property_without_a_column_width_keeps_its_large_MaxLength()
    {
        var fields = MetadataScanner.ScanTypes([typeof(NoWidth)]).Single().Fields;

        foreach (var name in new[] { "content", "raw", "shaped", "jsonColumn", "typedNoLength" })
            fields.Single(f => f.Name == name).MaxLength.Should().Be(5000, name);
    }

    [Fact]
    public void A_translation_entity_field_wider_than_its_column_fails_the_scan()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(Translated)]);

        act.Should().Throw<MetadataException>()
            .WithMessage("*TranslatedTranslation*Title*MaxLength 1000*255*");
    }

    [Fact]
    public void A_junction_payload_field_wider_than_its_column_fails_the_scan()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(LinkParent), typeof(LinkChild), typeof(PayloadLink)]);

        act.Should().Throw<MetadataException>()
            .WithMessage("*PayloadLink*Note*MaxLength 400*255*");
    }

    [Fact]
    public void A_Repeater_sub_field_is_not_checked_against_a_column()
    {
        var host = MetadataScanner.ScanTypes([typeof(RepeaterHost)]).Single();

        host.Fields.Single(f => f.Name == "items").Fields!.Single().MaxLength.Should().Be(1000);
    }
}
