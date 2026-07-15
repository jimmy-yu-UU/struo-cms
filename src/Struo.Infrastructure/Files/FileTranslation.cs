using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Translation sidecar for <see cref="File"/>. The FK property name follows the scanner convention
/// <c>{ParentTypeName}Id</c> = <c>FileId</c> (MetadataScanner.ScanTranslations).
/// </summary>
[SugarTable("file_translations")]
// DB-5: CodeFirst parity with db/migrations/009-hot-path-indexes.sql — translation lookup key
// (fileid, locale): same overlay/read pattern as article_translations.
[SugarIndex("ix_file_translations_fk_locale", nameof(FileId), OrderByType.Asc, nameof(Locale), OrderByType.Asc)]
public sealed class FileTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid FileId { get; set; }
    public string Locale { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Searchable = true, Sort = 1)]
    public string? Title { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Alt", Interface = FieldInterface.Text, Sort = 2)]
    public string? Alt { get; set; }
}
