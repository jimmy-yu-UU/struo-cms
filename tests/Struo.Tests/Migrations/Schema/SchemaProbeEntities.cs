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
    [SugarColumn(Length = 10, DecimalDigits = 2)] public decimal Amount { get; set; }
    public double Ratio { get; set; }
    public bool Flag { get; set; }
    public Guid Ref { get; set; }
    public byte[] Blob { get; set; } = [];
    [SugarColumn(IsIgnore = true)] public string Ignored { get; set; } = "";
}

[SugarTable("explicit_type_probe")]
public sealed class ExplicitTypeProbe
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(ColumnDataType = "varchar(500)")] public string Wide { get; set; } = "";
    [SugarColumn(Length = 10, DecimalDigits = 0)] public decimal Whole { get; set; }
}

[SugarTable("group_a_probe")]
public sealed class GroupAProbe
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(UniqueGroupNameList = ["unique1"])] public string Code { get; set; } = "";
}

[SugarTable("group_b_probe")]
public sealed class GroupBProbe
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(UniqueGroupNameList = ["unique1"])] public string Code { get; set; } = "";
    [SugarColumn(UniqueGroupNameList = ["ux_group_b_probe_slug"])] public string Slug { get; set; } = "";
}
