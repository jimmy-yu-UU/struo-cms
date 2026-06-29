using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("article_translations")]
public sealed class ArticleTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid ArticleId { get; set; }
    public string Locale { get; set; } = "";
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1, Group = "Content")]
    public string Title { get; set; } = "";
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Body", Interface = FieldInterface.RichText, Sort = 2, Group = "Content")]
    public string? Body { get; set; }
}
