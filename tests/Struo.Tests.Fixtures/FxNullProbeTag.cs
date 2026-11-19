using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_null_probe_tags")]
[CmsCollection("NullProbe tag", Icon = "tag", Group = "Content", Hidden = true)]
public sealed class FxNullProbeTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Probe", Interface = FieldInterface.Uuid, Required = true, Sort = 1)]
    public Guid ProbeId { get; set; }

    [CmsField(Label = "Tag", Interface = FieldInterface.Uuid, Required = true, Sort = 2)]
    public Guid TagId { get; set; }

    [CmsField(Label = "Remark", Interface = FieldInterface.Text, Sort = 3)]
    public string Remark { get; set; } = null!;

    [CmsField(Label = "Note", Interface = FieldInterface.Text, Sort = 4)]
    public string Note { get; set; } = "";
}
