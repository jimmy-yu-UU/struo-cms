using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("categories")]
// CodeFirst-declared secondary index on the self-referencing M2O FK column (ix_categories_parentid),
// created by InitTables in dev; a downstream fork that keeps this entity should add the equivalent
// index to its own migrations.
[SugarIndex("ix_categories_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Category : AuditableEntity, ISoftDeletable
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
    public Category? Parent { get; set; }

    [Navigate(NavigateType.OneToMany, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<Category> Children { get; set; } = [];

    [Navigate(NavigateType.OneToMany, nameof(Article.CategoryId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
    [SugarColumn(IsIgnore = true)]
    public List<Article> Articles { get; set; } = [];
}
