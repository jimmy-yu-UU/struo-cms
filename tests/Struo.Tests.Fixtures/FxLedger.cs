using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// A collection with link keys a body can not write (a ReadOnly foreign key, the audit key CreatedBy, a ReadOnly
// Image field) next to one it can (a foreign key that is also a Text field).
[SugarTable("fx_ledgers")]
[CmsCollection("Ledger", Icon = "book", Group = "Content", DefaultDisplayField = nameof(Name), Revisions = true)]
public sealed class FxLedger : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Owner", Interface = FieldInterface.Uuid, ReadOnly = true, Sort = 2)]
    public Guid? OwnerId { get; set; }

    [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Id}")]
    [SugarColumn(IsIgnore = true)]
    public FxGlossary? Owner { get; set; }

    [Navigate(NavigateType.ManyToOne, nameof(CreatedBy))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public FxTag? Creator { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Partner", Interface = FieldInterface.Text, Sort = 3)]
    public Guid? PartnerId { get; set; }

    [Navigate(NavigateType.ManyToOne, nameof(PartnerId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Id}")]
    [SugarColumn(IsIgnore = true)]
    public FxGlossary? Partner { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Stamp", Interface = FieldInterface.Image, ReadOnly = true, Sort = 4)]
    public Guid? StampId { get; set; }
}
