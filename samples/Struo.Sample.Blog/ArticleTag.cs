using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_tags")]
// DB-5: CodeFirst parity with db/migrations/009-hot-path-indexes.sql — M2M junction FKs, both
// directions (RelationExpander expansion + sync-on-write).
[SugarIndex("ix_article_tags_articleid", nameof(ArticleId), OrderByType.Asc)]
[SugarIndex("ix_article_tags_tagid", nameof(TagId), OrderByType.Asc)]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Guid TagId { get; set; }
}
