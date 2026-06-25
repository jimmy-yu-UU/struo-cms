using SqlSugar;
using Struo.Domain.Auditing;

namespace Struo.Sample.Blog;

[SugarTable("articles")]
public sealed class Article : IAuditable
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public string? Body { get; set; }

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? UpdatedBy { get; set; }
}
