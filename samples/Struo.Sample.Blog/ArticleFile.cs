using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_files")]
public sealed class ArticleFile
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public long ArticleId { get; set; }
    public Guid FileId { get; set; }
    public int SortOrder { get; set; }
}
