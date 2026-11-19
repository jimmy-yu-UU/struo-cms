using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_null_probe_translations")]
public sealed class FxNullProbeTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid FxNullProbeId { get; set; }
    public string Locale { get; set; } = "";

    [CmsField(Label = "Heading", Interface = FieldInterface.Text, Sort = 1)]
    public string Heading { get; set; } = null!;

}
