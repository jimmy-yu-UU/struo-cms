using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("article_translations")]
// DB-5: CodeFirst parity with db/migrations/009-hot-path-indexes.sql — translation lookup key
// (articleid, locale): overlay read / translatable sort subquery / translatable search.
[SugarIndex("ix_article_translations_fk_locale", nameof(ArticleId), OrderByType.Asc, nameof(Locale), OrderByType.Asc)]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid ArticleId { get; set; }
    public string Locale { get; set; } = "";
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1, Group = "Content")]
    public string Title { get; set; } = "";
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Body", Interface = FieldInterface.RichText, Sort = 2, Group = "Content")]
    public string? Body { get; set; }
    // Hidden per-locale field (SEC-2 fixture): must be redacted from `translations.{locale}` in any
    // snapshot returned externally, but preserved for RevertAsync (see Article.InternalNote).
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Internal Slug", Interface = FieldInterface.Text, Hidden = true, Sort = 3, Group = "Content")]
    public string? InternalSlug { get; set; }
    // SeoTitle / SeoMetaDescription / SeoOgImageId inherited from SeoTranslation.
    // SqlSugar EntityService hook maps Nullable<T> → IsNullable=true for all backends,
    // so no [SugarColumn(IsNullable=true)] override is needed here.
}
