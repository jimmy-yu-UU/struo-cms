using SqlSugar;

namespace Struo.Tests.Support;

/// <summary>
/// 「未過濾的 InitTables 對既有表做什麼」的探針：兩個 entity 對到同一張表，只差一個屬性。
/// 先用 Wide 建表，再對 Narrow 跑未過濾的 InitTables，觀測 Doomed 欄位的下場。
/// SQLite（DatabaseInitializerTests）與 PostgreSQL（PostgresIntegrationTests）共用同一組定義，
/// 兩邊的結論才可比較。這兩個型別只存在於測試組件，不在 FrameworkEntityTypes.All 內，
/// 也不會被 EntityTypeCollector 掃到，因此永遠不會進入任何生產建表路徑。
/// </summary>
[SugarTable("destructive_init_probe")]
public sealed class DestructiveInitProbeWide
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public string? Keep { get; set; }

    /// <summary>Narrow 上不存在的屬性——本探針要觀測的就是這一欄。</summary>
    public string? Doomed { get; set; }
}

/// <summary>與 <see cref="DestructiveInitProbeWide"/> 同表，少一個 Doomed 屬性。</summary>
[SugarTable("destructive_init_probe")]
public sealed class DestructiveInitProbeNarrow
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public string? Keep { get; set; }
}
