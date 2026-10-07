using SqlSugar;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.Migrations.Schema;

public enum ProbeKind { First, Second }

[SugarTable("schema_probe")]
[SugarIndex("ux_{table}_code", nameof(Code), OrderByType.Asc, true)]
[SugarIndex("ix_{table}_name_kind", nameof(DisplayName), OrderByType.Asc, nameof(Kind), OrderByType.Desc, false)]
public sealed class SchemaProbe
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public string Code { get; set; } = "";
    [SugarColumn(Length = 80)] public string DisplayName { get; set; } = "";
    public string? Note { get; set; }
    [ColumnShape(ColumnShape.LongText)] public string Body { get; set; } = "";
    [SugarColumn(IsJson = true)] public List<string> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    [ColumnShape(ColumnShape.TimestampWithTimeZone)] public DateTime Stamp { get; set; }
    public ProbeKind Kind { get; set; }
    public decimal Price { get; set; }
    public bool Flag { get; set; }
    public Guid Ref { get; set; }
    public byte[] Blob { get; set; } = [];
    [SugarColumn(IsIgnore = true)] public string Ignored { get; set; } = "";
}
