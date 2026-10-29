using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_categories")]
// Secondary index on the self-referencing M2O FK column (ix_fx_categories_parentid); the fixture
// migration Migrations/202610080200_CreateFixtureSchema.cs creates it.
[SugarIndex("ix_fx_categories_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class FxCategory : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public Guid? ParentId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public FxCategory? Parent { get; set; }

    [Navigate(NavigateType.OneToMany, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<FxCategory> Children { get; set; } = [];

    [Navigate(NavigateType.OneToMany, nameof(FxArticle.CategoryId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
    [SugarColumn(IsIgnore = true)]
    public List<FxArticle> Articles { get; set; } = [];
}
