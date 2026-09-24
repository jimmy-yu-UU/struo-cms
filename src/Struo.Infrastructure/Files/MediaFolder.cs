using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Framework-owned media-library folder: pure in-system organisation, decoupled from
/// physical storage keys. Self-referencing tree via <see cref="ParentId"/>. Hidden — the media
/// library is its only UI; CRUD goes through the generic items API (route <c>mediafolder</c>).
/// Deleting a folder that still contains files or subfolders is rejected by the framework's
/// OnDelete.Restrict guard (ItemPurgePipeline.CheckRestrictAsync) — declared on the inbound
/// relations (File.Folder, MediaFolder.Parent), no bespoke guard code.
/// </summary>
[SugarTable("media_folders")]
[SugarIndex("ix_{table}_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("MediaFolder", Group = "System", DefaultDisplayField = nameof(Name), Hidden = true)]
public sealed class MediaFolder : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    // New framework table → time-zone-aware timestamps. The baseline AuditableEntity tables predate
    // this convention and stay zone-less; the convention binds new schema only. Declared as a
    // dialect-neutral shape (not a vendor type literal) so CodeFirst table creation works on every
    // backend — see ColumnTypeMap.
    [ColumnShape(ColumnShape.TimestampWithTimeZone)] public override DateTime CreatedAt { get; set; }
    [ColumnShape(ColumnShape.TimestampWithTimeZone)] public override DateTime UpdatedAt { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public Guid? ParentId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public MediaFolder? Parent { get; set; }
}
