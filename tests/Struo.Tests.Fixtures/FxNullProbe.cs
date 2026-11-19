using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Tests.Fixtures;

// Fields of every NOT NULL / nullable shape a write can send null to. A non-nullable string that
// carries no initializer holds null until a request sets it.
[SugarTable("fx_null_probes")]
[CmsCollection("NullProbe", Icon = "box", Group = "Content", DefaultDisplayField = nameof(Plain), Revisions = true)]
public sealed class FxNullProbe : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsTranslations(typeof(FxNullProbeTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<FxNullProbeTranslation> Translations { get; set; } = [];

    [CmsField(Label = "Plain", Interface = FieldInterface.Text, Sort = 1)]
    public string Plain { get; set; } = null!;

    [CmsField(Label = "Defaulted", Interface = FieldInterface.Text, Sort = 2)]
    public string Defaulted { get; set; } = "";

    [CmsField(Label = "Optional", Interface = FieldInterface.Text, Sort = 3)]
    public string? Optional { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Declared nullable", Interface = FieldInterface.Text, Sort = 4)]
    public string Declared { get; set; } = null!;

    [Navigate(typeof(FxNullProbeTag), nameof(FxNullProbeTag.ProbeId), nameof(FxNullProbeTag.TagId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<FxTag> Tags { get; set; } = [];
}
