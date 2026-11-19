using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// Structured NOT NULL columns, on the collection and on its translation sidecar, that hold null until a
// request sets them. The fields are Hidden, so only REST writes reach them.
[SugarTable("fx_null_shelves")]
[CmsCollection("NullShelf", Icon = "box", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class FxNullShelf : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsTranslations(typeof(FxNullShelfTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<FxNullShelfTranslation> Translations { get; set; } = [];

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public string Name { get; set; } = "";

    [CmsField(Label = "Labels", Interface = FieldInterface.Hidden, Sort = 2)]
    public List<string> Labels { get; set; } = null!;
}
