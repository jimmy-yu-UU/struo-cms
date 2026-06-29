using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Framework-owned media asset. Collection route is <c>file</c> (camelCase of the type name).
/// The translatable <c>title</c>/<c>alt</c> live on the <see cref="FileTranslation"/> sidecar;
/// system-derived metadata (filename/contentType/size/width/height/storageKey) is set at upload
/// and is read-only thereafter. PK is a <see cref="Guid"/>, assigned by the create flow.
/// </summary>
[SugarTable("files")]
[CmsCollection("File", Group = "System", DefaultDisplayField = nameof(FileName))]
public sealed class File : IAuditable
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public string StorageKey { get; set; } = "";   // internal: no [CmsField]

    [CmsField(Label = "File Name", Interface = FieldInterface.Text, Searchable = true, ReadOnly = true, Sort = 1)]
    public string FileName { get; set; } = "";
    [CmsField(Label = "Content Type", Interface = FieldInterface.Text, ReadOnly = true, Sort = 2)]
    public string ContentType { get; set; } = "";
    [CmsField(Label = "Size", Interface = FieldInterface.Number, ReadOnly = true, Sort = 3)]
    public long Size { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Width", Interface = FieldInterface.Number, ReadOnly = true, Sort = 4)]
    public int? Width { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Height", Interface = FieldInterface.Number, ReadOnly = true, Sort = 5)]
    public int? Height { get; set; }
    [CmsField(Label = "Status", Interface = FieldInterface.Select, Sort = 6)]
    [CmsOptions("draft:Draft", "published:Published", "archived:Archived")]
    public string Status { get; set; } = "draft";

    [CmsTranslations(typeof(FileTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<FileTranslation> Translations { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
