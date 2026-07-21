using SqlSugar;

namespace Struo.Infrastructure.Settings;

/// <summary>
/// Singleton site settings that override the deploy-time <see cref="Struo.Application.Configuration.BrandingOptions"/>
/// defaults. Internal framework table — NOT a <c>[CmsCollection]</c>, never browsable through the generic
/// item API. Exactly one row, keyed by the well-known <see cref="SingletonId"/>; absence of the row means
/// "use appsettings defaults".
/// </summary>
[SugarTable("site_settings")]
public sealed class SiteSettings
{
    /// <summary>Fixed PK for the single settings row (the upsert target).</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    // text: avoids the recurring Postgres varchar(255) mapping (see Revision.Snapshot).
    [SugarColumn(ColumnDataType = "text")] public string BrandName { get; set; } = "";

    [SugarColumn(IsNullable = true)] public Guid? LogoFileId { get; set; }

    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? UpdatedBy { get; set; }
}
