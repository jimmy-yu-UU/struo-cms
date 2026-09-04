using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("article_translations")]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    // ArticleId/Locale carry no unique attribute here: SqlSugarClientFactory's EntityService hook reads
    // this table's [CmsTranslations] metadata through TranslationSidecarIndexPolicy and adds the
    // composite unique (articleid, locale) to the generated column model at CodeFirst time; SchemaGuard
    // re-checks the resulting index in Development. This sample schema is not part of core, so a
    // downstream fork keeping this entity gets the index for free on a fresh CodeFirst table, and only
    // needs to write its own migration for a table that already exists in the target database.
    public Guid ArticleId { get; set; }
    public string Locale { get; set; } = "";
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true, Searchable = true, Sortable = true, Sort = 1, Group = "Content")]
    public string Title { get; set; } = "";
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Body", Interface = FieldInterface.RichText, Sort = 2, Group = "Content")]
    public string? Body { get; set; }
    // Hidden per-locale field (a redaction-test fixture): must be redacted from `translations.{locale}` in any
    // snapshot returned externally, but preserved for RevertAsync (see Article.InternalNote).
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Internal Slug", Interface = FieldInterface.Text, Hidden = true, Sort = 3, Group = "Content")]
    public string? InternalSlug { get; set; }
    // SeoTitle / SeoMetaDescription / SeoOgImageId inherited from SeoTranslation.
    // SqlSugar EntityService hook maps Nullable<T> → IsNullable=true for all backends,
    // so no [SugarColumn(IsNullable=true)] override is needed here.
}
