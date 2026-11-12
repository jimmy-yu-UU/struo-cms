using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.Metadata;

// String fields across the width rules: the default varchar(255), an explicit [SugarColumn(Length)],
// an explicit ColumnDataType (with a Length it is that width, without one it states no length), and the
// columns that state no length (LongText interfaces, [ColumnShape]).
[SugarTable("width_probe")]
[CmsCollection("Width probe", Name = "widthProbe")]
public sealed class WidthProbeEntity : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Plain", Interface = FieldInterface.Text)]
    public string? PlainText { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Hidden", Interface = FieldInterface.Hidden)]
    public string? HiddenText { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Clock", Interface = FieldInterface.Time)]
    public string? ClockText { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Uuid", Interface = FieldInterface.Uuid)]
    public string? UuidText { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Explicit limit", Interface = FieldInterface.Text, MaxLength = 120)]
    public string? ExplicitLimit { get; set; }

    [SugarColumn(IsNullable = true, Length = 100)]
    [CmsField(Label = "Narrow", Interface = FieldInterface.Text)]
    public string? NarrowText { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Body", Interface = FieldInterface.Textarea)]
    public string? BodyText { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Rich", Interface = FieldInterface.RichText)]
    public string? RichBody { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Raw JSON", Interface = FieldInterface.Json)]
    public string? RawJson { get; set; }

    [ColumnShape(ColumnShape.LongText)]
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Shaped", Interface = FieldInterface.Text)]
    public string? ShapedText { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    [CmsField(Label = "Typed", Interface = FieldInterface.Text)]
    public string? TypedText { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "varchar", Length = 64)]
    [CmsField(Label = "Typed with length", Interface = FieldInterface.Text)]
    public string? TypedLengthText { get; set; }
}
