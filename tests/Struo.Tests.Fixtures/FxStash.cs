using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_stashes")]
[CmsCollection("Stash", Icon = "box", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class FxStash : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;

    [CmsField(Label = "Labels", Interface = FieldInterface.Hidden, Sort = 2)]
    public List<string> Labels { get; set; } = [];
}
