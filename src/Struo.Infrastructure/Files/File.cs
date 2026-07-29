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
[SugarIndex("ix_files_folderid", nameof(FolderId), OrderByType.Asc)]
[CmsCollection("File", Group = "System", DefaultDisplayField = nameof(FileName), Hidden = true)]
public sealed class File : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

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
    [CmsOptions("draft:Draft", "published:Published")]
    public string Status { get; set; } = "draft";

    // Media folders: nullable organisational FK — NOT ReadOnly (moving a file = items update;
    // UpdateCoreAsync's M2O-FK overlay handles it). OnDelete.Restrict on the nav relation means a
    // folder still containing files cannot be deleted (framework guard, 409 CONFLICT).
    [SugarColumn(IsNullable = true)]
    public Guid? FolderId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(FolderId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public MediaFolder? Folder { get; set; }

    [CmsTranslations(typeof(FileTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<FileTranslation> Translations { get; set; } = [];

    // File soft-delete — package-free ISoftDeletable members (framework query filter +
    // repository SoftDeleteAsync/RestoreAsync light up automatically once these are present).
    [SugarColumn(IsNullable = true)] public DateTime? DeletedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? DeletedBy { get; set; }
}
