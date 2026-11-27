using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// A soft-deletable collection that declares a many-to-one relation on its soft-delete column DeletedBy.
[SugarTable("fx_tombstones")]
[CmsCollection("Tombstone", Icon = "trash", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class FxTombstone : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)] public DateTime? DeletedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? DeletedBy { get; set; }

    [Navigate(NavigateType.ManyToOne, nameof(DeletedBy))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public FxTag? Remover { get; set; }
}
