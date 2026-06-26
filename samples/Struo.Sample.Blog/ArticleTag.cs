using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_tags")]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }
    public long ArticleId { get; set; }
    public long TagId { get; set; }
    public int SortOrder { get; set; }
}
