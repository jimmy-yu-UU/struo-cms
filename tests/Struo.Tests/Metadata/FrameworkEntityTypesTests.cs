using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// <see cref="FrameworkEntityTypes.All"/> 是手維護清單，而 CodeFirst/Migration 職責切分之後它成為
/// Production 建表的唯一權威（不再有 001-core-baseline.sql 當第二道保險）。漏列一個型別的後果是：
/// 那張表在乾淨的 Production DB 上不會被建，host 正常啟動，直到第一個相關查詢才炸。
/// 這個測試讓「漏列」變成紅燈。
/// </summary>
public class FrameworkEntityTypesTests
{
    /// <summary>
    /// 刻意不在 All 內的持久化型別。schema_migrations 由 MigrationRunner 自己在不存在時建立
    /// （MigrationRunner.cs:133-140），刻意不走 EntityTypeCollector——migration 追蹤表必須先於
    /// 任何 migration 存在。
    /// </summary>
    private static readonly Type[] IntentionallyOutsideAll = [typeof(SchemaMigration)];

    private static Type[] PersistedInfrastructureTypes() =>
        typeof(FrameworkEntityTypes).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract
                        && !t.IsGenericTypeDefinition
                        && t.GetCustomAttribute<SugarTable>() is not null)
            .ToArray();

    [Fact]
    public void Every_persisted_infrastructure_type_is_either_in_All_or_an_explicit_exception()
    {
        var scanned = PersistedInfrastructureTypes();
        var accountedFor = FrameworkEntityTypes.All.Concat(IntentionallyOutsideAll).ToArray();

        // 雙向比對：漏列（掃到但不在清單）與多列（在清單但已不是持久化型別）都必須紅。
        scanned.Should().BeEquivalentTo(accountedFor,
            "每個帶 [SugarTable] 的 Infrastructure 型別都必須在 FrameworkEntityTypes.All 內，" +
            "否則乾淨的 Production DB 不會建出它的表；若它刻意不該被建，加進 IntentionallyOutsideAll " +
            "並寫明理由");
    }

    [Fact]
    public void Every_member_of_All_carries_a_SugarTable_attribute()
    {
        // 上一條的 BeEquivalentTo 是雙向比對，其實已經會為同一種缺陷（All 內有型別缺 [SugarTable]）
        // 報錯；這條的價值不是「補上一條漏掉的角落」，而是把失敗訊息換成直接點名是哪個型別缺
        // [SugarTable]，省去從集合差異反推的步驟，並讓這條規則本身變成一則明寫、被檢查的斷言。
        // 兩條測試仍有共同死角：一個既不在 All、也沒有 [SugarTable] 的型別，永遠不會進入
        // scanned 或 accountedFor，兩邊都不會失衡——這是「用允許清單比對」這種偵測方式本身的
        // 極限（掃不到沒人引用過的型別），不是任一條斷言能補上的洞。
        foreach (var type in FrameworkEntityTypes.All)
        {
            var attribute = type.GetCustomAttribute<SugarTable>();
            attribute.Should().NotBeNull(
                $"'{type.Name}' 必須明寫 [SugarTable(\"...\")]，否則防漏掃描看不到它");
        }
    }
}
