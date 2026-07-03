using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_tags")]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Guid TagId { get; set; }
}
