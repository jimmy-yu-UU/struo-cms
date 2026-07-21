using SqlSugar;
using Struo.Application.Settings;

namespace Struo.Infrastructure.Settings;

public sealed class SqlSugarSiteSettingsStore(ISqlSugarClient db) : ISiteSettingsStore
{
    public async Task<SiteSettingsRecord?> GetAsync(CancellationToken ct = default)
    {
        var row = await db.Queryable<SiteSettings>()
            .Where(s => s.Id == SiteSettings.SingletonId).FirstAsync(ct);
        return row is null ? null : new SiteSettingsRecord(row.BrandName, row.LogoFileId);
    }

    public async Task UpsertAsync(string brandName, Guid? logoFileId, Guid? updatedBy, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var exists = await db.Queryable<SiteSettings>()
            .Where(s => s.Id == SiteSettings.SingletonId).AnyAsync(ct);
        if (exists)
        {
            // Entity-typed SetColumns so the nullable logofileid gets a typed NULL (Postgres 42804 fix).
            await db.Updateable<SiteSettings>()
                .SetColumns(s => new SiteSettings
                {
                    BrandName = brandName, LogoFileId = logoFileId, UpdatedAt = now, UpdatedBy = updatedBy
                })
                .Where(s => s.Id == SiteSettings.SingletonId)
                .ExecuteCommandAsync(ct);
        }
        else
        {
            await db.Insertable(new SiteSettings
            {
                Id = SiteSettings.SingletonId, BrandName = brandName, LogoFileId = logoFileId,
                UpdatedAt = now, UpdatedBy = updatedBy
            }).ExecuteCommandAsync(ct);
        }
    }
}
