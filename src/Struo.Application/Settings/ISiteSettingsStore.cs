namespace Struo.Application.Settings;

/// <summary>The effective persisted settings, or absent when no row exists.</summary>
public sealed record SiteSettingsRecord(string BrandName, Guid? LogoFileId);

/// <summary>Reads/writes the singleton site-settings row.</summary>
public interface ISiteSettingsStore
{
    Task<SiteSettingsRecord?> GetAsync(CancellationToken ct = default);
    Task UpsertAsync(string brandName, Guid? logoFileId, Guid? updatedBy, CancellationToken ct = default);
}
