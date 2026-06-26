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
public sealed class Article : IAuditable, ISeoMeta
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

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
    [SugarColumn(IsNullable = true)] public long? SeoOgImageId { get; set; }

    // --- relations (Phase 3a) ---
    public long AuthorId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(AuthorId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public Author? Author { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? CategoryId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Category? Category { get; set; }

    [Navigate(typeof(ArticleTag), nameof(ArticleTag.ArticleId), nameof(ArticleTag.TagId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}", SortField = nameof(ArticleTag.SortOrder))]
    [SugarColumn(IsIgnore = true)]
    public List<Tag> Tags { get; set; } = [];

    // IAuditable
    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
