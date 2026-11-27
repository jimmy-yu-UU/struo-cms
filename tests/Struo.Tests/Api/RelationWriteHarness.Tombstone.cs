using SqlSugar;
using Struo.Tests.Fixtures;

namespace Struo.Tests.Api;

/// <summary>The soft-delete columns of a tombstone as the table stores them, read through a row type without initialisers.</summary>
[SugarTable("fx_tombstones")]
internal sealed class TombstoneRow
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

/// <summary>What a tombstone stores in its soft-delete columns.</summary>
internal sealed record TombstoneKeys(DateTime? DeletedAt, Guid? DeletedBy);

internal sealed partial class RelationWriteHarness
{
    /// <summary>A live tombstone inserted directly.</summary>
    public async Task<Guid> SeedTombstoneAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxTombstone { Id = id, Name = $"tombstone-{id:N}" }).ExecuteCommandAsync();
        return id;
    });

    /// <summary>The soft-delete columns of a tombstone, read from the database.</summary>
    public Task<TombstoneKeys> StoredTombstoneAsync(Guid id) => WithDbAsync(async db =>
    {
        var row = await db.Queryable<TombstoneRow>().Where(r => r.Id == id).FirstAsync();
        return new TombstoneKeys(row.DeletedAt, row.DeletedBy);
    });

    /// <summary>The id of the tombstone a REST create answered with.</summary>
    public static Guid CreatedTombstone(WriteOutcome created) => CreatedLedger(created);
}
