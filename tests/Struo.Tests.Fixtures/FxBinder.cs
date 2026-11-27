using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// The owner of a many-to-many junction collection (FxBinderLink) whose parent key is ReadOnly.
[SugarTable("fx_binders")]
[CmsCollection("Binder", Icon = "book", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class FxBinder : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public string Name { get; set; } = "";

    [Navigate(typeof(FxBinderLink), nameof(FxBinderLink.BinderId), nameof(FxBinderLink.TagId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<FxTag> Tags { get; set; } = [];
}
