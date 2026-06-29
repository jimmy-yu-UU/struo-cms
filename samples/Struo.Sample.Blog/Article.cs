using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Seo;

namespace Struo.Sample.Blog;

[SugarTable("articles")]
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Status))]
[CmsFieldGroup("Content", Label = "Content", Sort = 1)]
[CmsFieldGroup("SEO", Label = "SEO", Sort = 2)]
public sealed class Article : AuditableEntity, ISeoMeta
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    // Title/Body moved to the ArticleTranslation sidecar (Phase 4 i18n).
    [CmsTranslations(typeof(ArticleTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<ArticleTranslation> Translations { get; set; } = [];

    [CmsField(Label = "Status", Interface = FieldInterface.Select, Sort = 3, Group = "Content")]
    [CmsOptions("draft:Draft", "published:Published")]
    public string Status { get; set; } = "draft";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Published At", Interface = FieldInterface.DateTime, Sort = 4, Group = "Content")]
    public DateTime? PublishedAt { get; set; }

    // ISeoMeta — SEO field group auto-applied by the scanner (no [CmsField] here by design)
    [SugarColumn(IsNullable = true)] public string? SeoTitle { get; set; }
    [SugarColumn(IsNullable = true)] public string? SeoMetaDescription { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? SeoOgImageId { get; set; }

    // --- relations ---
    [SugarColumn(IsNullable = true)]
    public Guid? CategoryId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Category? Category { get; set; }

    // SEO OG image — single parent relation, retained as-is (Phase 5.6 will move SEO to translations)
    [Navigate(NavigateType.OneToOne, nameof(SeoOgImageId))]
    [CmsRelation(Interface = RelationInterface.ImagePicker, OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Struo.Infrastructure.Files.File? SeoOgImage { get; set; }
}
