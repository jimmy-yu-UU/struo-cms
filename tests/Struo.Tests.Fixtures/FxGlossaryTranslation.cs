using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_glossary_translations")]
public sealed class FxGlossaryTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid FxGlossaryId { get; set; }
    public string Locale { get; set; } = "";

    [CmsField(Label = "Title", Interface = FieldInterface.Text, Sort = 1)]
    public string Title { get; set; } = "";

    [CmsField(Label = "Aliases", Interface = FieldInterface.Hidden, Sort = 2)]
    public List<string> Aliases { get; set; } = [];
}
