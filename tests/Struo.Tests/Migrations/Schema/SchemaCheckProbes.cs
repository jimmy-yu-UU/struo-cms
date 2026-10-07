using SqlSugar;

namespace Struo.Tests.Migrations.Schema;

[SugarTable("chk_probe")]
[SugarIndex("ux_{table}_title", nameof(Title), OrderByType.Asc, true)]
public sealed class CheckProbe
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = false, Length = 255)] public string Title { get; set; } = "";
    [SugarColumn(IsNullable = true)] public string? Note { get; set; }
}

[SugarTable("chk_probe")]
public sealed class CheckProbeWithoutNote
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = false, Length = 255)] public string Title { get; set; } = "";
}

[SugarTable("chk_probe")]
public sealed class CheckProbeNullableTitle
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = true, Length = 255)] public string? Title { get; set; }
    [SugarColumn(IsNullable = true)] public string? Note { get; set; }
}

[SugarTable("chk_probe")]
public sealed class CheckProbeShortTitle
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = false, Length = 100)] public string Title { get; set; } = "";
    [SugarColumn(IsNullable = true)] public string? Note { get; set; }
}

[SugarTable("chk_probe")]
[SugarIndex("ux_{table}_title", nameof(Title), OrderByType.Asc, true)]
public sealed class CheckProbeExtraColumn
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = false, Length = 255)] public string Title { get; set; } = "";
    [SugarColumn(IsNullable = true)] public string? Note { get; set; }
    [SugarColumn(IsNullable = true)] public string? Extra { get; set; }
}

[SugarTable("chk_probe")]
public sealed class CheckProbeTypeShift
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = false)] public long Title { get; set; }
    [SugarColumn(IsNullable = true)] public string? Note { get; set; }
}
