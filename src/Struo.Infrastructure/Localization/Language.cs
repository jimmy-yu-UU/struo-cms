using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Localization;

[SugarTable("languages")]
[CmsCollection("Language", Group = "System", DefaultDisplayField = nameof(Name))]
public sealed class Language : IAuditable
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [CmsField(Label = "Code", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Code { get; set; } = "";
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public string Name { get; set; } = "";
    [CmsField(Label = "Default", Interface = FieldInterface.Boolean, Sort = 3)]
    public bool IsDefault { get; set; }
    [CmsField(Label = "Enabled", Interface = FieldInterface.Boolean, Sort = 4)]
    public bool Enabled { get; set; } = true;
    [CmsField(Label = "Sort", Interface = FieldInterface.Number, Sort = 5)]
    public int Sort { get; set; }

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
