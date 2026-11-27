using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// The binder<->tag junction collection. Its parent key is ReadOnly and also the foreign key of a many-to-one
// relation, which the scanner accepts as a writable key of the junction; a body never sets it.
[SugarTable("fx_binder_links")]
[CmsCollection("Binder link", Icon = "tag", Group = "Content", Hidden = true)]
public sealed class FxBinderLink
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Binder", Interface = FieldInterface.Uuid, ReadOnly = true, Sort = 1)]
    public Guid BinderId { get; set; }

    [Navigate(NavigateType.ManyToOne, nameof(BinderId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public FxBinder? Binder { get; set; }

    [CmsField(Label = "Tag", Interface = FieldInterface.Uuid, Sort = 2)]
    public Guid TagId { get; set; }
}
