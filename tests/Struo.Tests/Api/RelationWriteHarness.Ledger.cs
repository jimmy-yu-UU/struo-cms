using System.Net.Http.Json;
using System.Text.Json;
using SqlSugar;
using Struo.Tests.Fixtures;

namespace Struo.Tests.Api;

/// <summary>The link keys of a ledger as the table stores them, read through a row type without initialisers.</summary>
[SugarTable("fx_ledgers")]
internal sealed class LedgerRow
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? PartnerId { get; set; }
    public Guid? StampId { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>The keys a ledger stores.</summary>
internal sealed record LedgerKeys(Guid? Owner, Guid? Partner, Guid? Stamp, Guid? CreatedBy);

internal sealed partial class RelationWriteHarness
{
    /// <summary>A ledger inserted directly, its keys then set to the given values (null leaves a database NULL).</summary>
    public async Task<Guid> SeedLedgerAsync(LedgerKeys keys) => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxLedger { Id = id, Name = $"ledger-{id:N}" }).ExecuteCommandAsync();
        await db.Updateable<LedgerRow>()
            .SetColumns(r => r.OwnerId == keys.Owner)
            .SetColumns(r => r.PartnerId == keys.Partner)
            .SetColumns(r => r.StampId == keys.Stamp)
            .SetColumns(r => r.CreatedBy == keys.CreatedBy)
            .Where(r => r.Id == id)
            .ExecuteCommandAsync();
        return id;
    });

    /// <summary>A ledger created through the API by a super-admin, so that it has a first revision.</summary>
    public static async Task<Guid> CreateLedgerAsync(HttpClient admin)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/fxLedger", new { name = $"ledger-{Guid.NewGuid():N}" });
        resp.EnsureSuccessStatusCode();
        var root = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(root.GetProperty("data").GetProperty("id").GetString()!);
    }

    /// <summary>The keys of a ledger, read from the database.</summary>
    public Task<LedgerKeys> StoredLedgerAsync(Guid id) => WithDbAsync(async db =>
    {
        var row = await db.Queryable<LedgerRow>().Where(r => r.Id == id).FirstAsync();
        return new LedgerKeys(row.OwnerId, row.PartnerId, row.StampId, row.CreatedBy);
    });

    /// <summary>The id of the ledger a REST create answered with.</summary>
    public static Guid CreatedLedger(WriteOutcome created) =>
        Guid.Parse(JsonDocument.Parse(created.Body).RootElement.GetProperty("data").GetProperty("id").GetString()!);

    /// <summary>Sets the keys of a stored ledger, bypassing the API.</summary>
    public Task SetLedgerKeysAsync(Guid id, LedgerKeys keys) => WithDbAsync(async db =>
    {
        await db.Updateable<LedgerRow>()
            .SetColumns(r => r.OwnerId == keys.Owner)
            .SetColumns(r => r.PartnerId == keys.Partner)
            .SetColumns(r => r.StampId == keys.Stamp)
            .SetColumns(r => r.CreatedBy == keys.CreatedBy)
            .Where(r => r.Id == id)
            .ExecuteCommandAsync();
        return 0;
    });

    /// <summary>Reverts a ledger to <paramref name="revision"/>.</summary>
    public static async Task<WriteOutcome> RevertLedgerAsync(HttpClient client, string channel, Guid id, string revision)
    {
        if (channel == Rest)
            return await OutcomeAsync(
                await client.PostAsync($"/api/items/fxLedger/{id}/revisions/{revision}/revert", null), restEnvelope: true);
        var query = $"mutation {{ revertFxLedger(id: \"{id}\", revisionNumber: {revision}) {{ id }} }}";
        return await OutcomeAsync(await client.PostAsJsonAsync("/graphql", new { query }), restEnvelope: false);
    }
}
