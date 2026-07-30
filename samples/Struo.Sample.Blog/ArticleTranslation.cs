using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("article_translations")]
// CodeFirst-declared secondary index on the translation lookup key (articleid, locale) — used by
// the overlay read / translatable sort subquery / translatable search — created by InitTables in
// dev; a downstream fork that keeps this entity should add the equivalent index to its own
// migrations.
[SugarIndex("ix_article_translations_fk_locale", nameof(ArticleId), OrderByType.Asc, nameof(Locale), OrderByType.Asc)]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    // The composite UNIQUE (articleid, locale) keeps per-locale reads deterministic — a parent may have
    // at most one translation row per locale. The two columns share one group name, so SqlSugar
    // CodeFirst emits a single composite unique index (same mechanism as Revision.cs), created by
    // InitTables from UniqueGroupNameList in dev. The non-unique [SugarIndex] above is KEPT
    // (accepted-redundant dev index; see db/migrations/README.md). This sample schema is not part of the
    // core baseline (db/migrations/001-core-baseline.sql is core-only), so a downstream fork that keeps
    // this entity must add the equivalent composite UNIQUE to its own migrations.
    [SugarColumn(UniqueGroupNameList = ["ux_article_translations_fk_locale"])]
    public Guid ArticleId { get; set; }
    [SugarColumn(UniqueGroupNameList = ["ux_article_translations_fk_locale"])]
    public string Locale { get; set; } = "";
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1, Group = "Content")]
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
