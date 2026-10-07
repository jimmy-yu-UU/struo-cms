using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// <see cref="FrameworkEntityTypes.All"/> 是手維護清單，是 schema 檢查（SchemaChecker、migrate:check）與
/// make:migration 認得的框架實體集合。漏列一個型別的後果是：那張表不會被比對，drift 不會被發現。
/// 這個測試讓「漏列」變成紅燈。
/// </summary>
public class FrameworkEntityTypesTests
{
    private static Type[] PersistedInfrastructureTypes() =>
        typeof(FrameworkEntityTypes).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract
                        && !t.IsGenericTypeDefinition
                        && t.GetCustomAttribute<SugarTable>() is not null)
            .ToArray();

    [Fact]
    public void Every_persisted_infrastructure_type_is_in_All()
    {
        var scanned = PersistedInfrastructureTypes();

        // 雙向比對：漏列（掃到但不在清單）與多列（在清單但已不是持久化型別）都必須紅。
        scanned.Should().BeEquivalentTo(FrameworkEntityTypes.All,
            "每個帶 [SugarTable] 的 Infrastructure 型別都必須在 FrameworkEntityTypes.All 內，" +
            "否則它的表不會被 schema 檢查比對");
    }

    [Fact]
    public void Every_member_of_All_carries_a_SugarTable_attribute()
    {
        // 上一條的 BeEquivalentTo 是雙向比對，其實已經會為同一種缺陷（All 內有型別缺 [SugarTable]）
        // 報錯；這條的價值不是「補上一條漏掉的角落」，而是把失敗訊息換成直接點名是哪個型別缺
        // [SugarTable]，省去從集合差異反推的步驟，並讓這條規則本身變成一則明寫、被檢查的斷言。
        // 兩條測試仍有共同死角：一個既不在 All、也沒有 [SugarTable] 的型別，永遠不會進入
        // scanned 或 accountedFor，兩邊都不會失衡——這是「用允許清單比對」這種偵測方式本身的
        // 極限（掃不到沒人引用過的型別），不是任一條斷言能補上的洞。另一個死角是掃描範圍：
        // PersistedInfrastructureTypes() 只掃 typeof(FrameworkEntityTypes).Assembly（即
        // Struo.Infrastructure），若日後有持久化的 framework 型別落在別的組件，這裡也看不到——
        // 目前沒有這樣的型別（Struo.Domain 的 SeoTranslation 是抽象基底、不帶 [SugarTable]，不算
        // 反例；`[SugarTable]` 目前全部落在 Struo.Infrastructure），但這是掃描機制本身的限制，
        // 不是任一條斷言能補上的洞。
        foreach (var type in FrameworkEntityTypes.All)
        {
            var attribute = type.GetCustomAttribute<SugarTable>();
            attribute.Should().NotBeNull(
                $"'{type.Name}' 必須明寫 [SugarTable(\"...\")]，否則防漏掃描看不到它");
        }
    }
}
