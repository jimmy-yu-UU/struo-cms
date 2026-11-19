using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_null_shelf_translations")]
public sealed class FxNullShelfTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid FxNullShelfId { get; set; }
    public string Locale { get; set; } = "";

    [CmsField(Label = "Heading", Interface = FieldInterface.Text, Sort = 1)]
    public string Heading { get; set; } = "";

    [CmsField(Label = "Aliases", Interface = FieldInterface.Hidden, Sort = 2)]
    public List<string> Aliases { get; set; } = null!;
}
