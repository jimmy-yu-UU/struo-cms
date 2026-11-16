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

    [CmsField(Label = "Numbers", Interface = FieldInterface.Hidden, Sort = 3)]
    public int[] Numbers { get; set; } = [];

    [CmsField(Label = "Scores", Interface = FieldInterface.Hidden, Sort = 4)]
    public Dictionary<string, int> Scores { get; set; } = new();

    [CmsField(Label = "Note", Interface = FieldInterface.Hidden, Sort = 5)]
    public FxStashNote Note { get; set; } = new();
}

/// <summary>A small custom class held in a Hidden field of <see cref="FxStash"/>.</summary>
public sealed class FxStashNote
{
    public string Text { get; set; } = string.Empty;
    public int Weight { get; set; }
}
