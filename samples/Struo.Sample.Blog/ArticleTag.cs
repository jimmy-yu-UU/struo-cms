using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_tags")]
// CodeFirst-declared secondary indexes on both M2M junction FKs, both directions (RelationExpander
// expansion + sync-on-write), created by InitTables in dev; a downstream fork that keeps this
// entity should add the equivalent indexes to its own migrations.
[SugarIndex("ix_article_tags_articleid", nameof(ArticleId), OrderByType.Asc)]
[SugarIndex("ix_article_tags_tagid", nameof(TagId), OrderByType.Asc)]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Guid TagId { get; set; }
}
