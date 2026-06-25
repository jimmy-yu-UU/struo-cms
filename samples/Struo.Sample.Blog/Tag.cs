using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("tags")]
[CmsCollection("Tag", Icon = "tag", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Tag : IAuditable
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
