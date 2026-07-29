using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Translation sidecar for <see cref="File"/>. The FK property name follows the scanner convention
/// <c>{ParentTypeName}Id</c> = <c>FileId</c> (MetadataScanner.ScanTranslations).
/// </summary>
[SugarTable("file_translations")]
public sealed class FileTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    // Composite UNIQUE (fileid, locale) — one translation row per parent per locale (deterministic
    // overlay read). Same mechanism as Revision.cs. This unique index also serves the (fileid, locale)
    // lookup, so the previously-declared redundant plain btree ([SugarIndex] ix_file_translations_fk_locale)
    // was dropped — it was pure write amplification. Live-PostgreSQL DDL: db/migrations/001-core-baseline.sql
    // (dev InitTables and the prod baseline emit identical index names).
    [SugarColumn(UniqueGroupNameList = ["ux_file_translations_fk_locale"])]
    public Guid FileId { get; set; }
    [SugarColumn(UniqueGroupNameList = ["ux_file_translations_fk_locale"])]
    public string Locale { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Searchable = true, Sort = 1)]
    public string? Title { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Alt", Interface = FieldInterface.Text, Sort = 2)]
    public string? Alt { get; set; }
}
