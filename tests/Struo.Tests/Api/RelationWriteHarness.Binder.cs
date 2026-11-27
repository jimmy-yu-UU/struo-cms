using SqlSugar;
using Struo.Tests.Fixtures;

namespace Struo.Tests.Api;

internal sealed partial class RelationWriteHarness
{
    /// <summary>A binder-tag junction row inserted directly.</summary>
    public async Task<Guid> SeedBinderLinkAsync(Guid binder, Guid tag) => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxBinderLink { Id = id, BinderId = binder, TagId = tag }).ExecuteCommandAsync();
        return id;
    });

    /// <summary>A binder inserted directly.</summary>
    public async Task<Guid> SeedBinderAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxBinder { Id = id, Name = $"binder-{id:N}" }).ExecuteCommandAsync();
        return id;
    });

    public Task<FxBinderLink> BinderLinkAsync(Guid id) =>
        WithDbAsync(db => db.Queryable<FxBinderLink>().Where(l => l.Id == id).FirstAsync());
}
