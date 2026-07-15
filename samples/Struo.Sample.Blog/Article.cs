using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Sample.Blog;

[SugarTable("articles")]
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Status), Revisions = true)]
[CmsFieldGroup("Content", Label = "Content", Sort = 1)]
[CmsFieldGroup("SEO", Label = "SEO", Sort = 2)]
public sealed class Article : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

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

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Hero Image", Interface = FieldInterface.Image, Sort = 5, Group = "Content")]
    public Guid? HeroImageId { get; set; }

    [CmsField(Label = "Regions", Interface = FieldInterface.MultiSelect, Sort = 6, Group = "Content")]
    [CmsOptions("apac:APAC", "emea:EMEA", "amer")] // "amer" has no explicit label -> falls back to "amer"
    public List<string> Regions { get; set; } = [];

    [CmsField(Label = "Audiences", Interface = FieldInterface.CheckboxGroup, Sort = 7, Group = "Content")]
    [CmsOptions("b2b:B2B", "b2c:B2C")]
    public List<string> Audiences { get; set; } = [];

    [CmsField(Label = "Keywords", Interface = FieldInterface.Tags, Sort = 8, Group = "Content")]
    public List<TagItem> Keywords { get; set; } = [];

    [CmsField(Label = "Attributes", Interface = FieldInterface.Json, Sort = 9, Group = "Content")]
    public string? Attributes { get; set; }

    [CmsField(Label = "Meta", Interface = FieldInterface.KeyValue, Sort = 10, Group = "Content")]
    public Dictionary<string, string> Meta { get; set; } = new();

    [CmsField(Label = "Gallery", Interface = FieldInterface.Files, Sort = 11, Group = "Content")]
    public List<Guid> Gallery { get; set; } = [];

    [CmsField(Label = "FAQs", Interface = FieldInterface.Repeater, Sort = 12, Group = "Content")]
    public List<FaqItem> Faqs { get; set; } = [];

    // --- relations ---
    [SugarColumn(IsNullable = true)]
    public Guid? CategoryId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Category? Category { get; set; }

    [Navigate(typeof(ArticleTag), nameof(ArticleTag.ArticleId), nameof(ArticleTag.TagId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<Tag> Tags { get; set; } = [];
}
