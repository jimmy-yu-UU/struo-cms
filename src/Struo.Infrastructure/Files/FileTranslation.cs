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
    // FileId/Locale carry no unique attribute here: SqlSugarClientFactory's EntityService hook reads
    // this table's [CmsTranslations] metadata through TranslationSidecarIndexPolicy and adds the
    // composite unique (fileid, locale) to the generated column model at CodeFirst time.
    // SchemaGuard.AssertCriticalConstraintsAsync re-checks the resulting index in Development.
    public Guid FileId { get; set; }
    public string Locale { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Searchable = true, Sort = 1)]
    public string? Title { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Alt", Interface = FieldInterface.Text, Sort = 2)]
    public string? Alt { get; set; }
}
