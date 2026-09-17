using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// 啟動期的 schema 建立與（選用的）結構同步。
///
/// <para>
/// 職責分工：<see cref="CreateMissingTables"/> 只建立**不存在**的表，在所有環境、所有後端執行；
/// 既有表的結構演進由 <see cref="SyncSchema"/>（僅 Development、需明確開啟）或
/// <see cref="MigrationRunner"/> 的受審查腳本負責。
/// </para>
///
/// <para>
/// <b>SqlSugar 的 <c>InitTables</c> 預設模式對既有表是破壞性的</b>：它會新增欄位、**修改**欄位型別，
/// 並在 entity 移除屬性時 **DROP COLUMN**（官方文件：「正式数据一定要禁删除列操作」）。這一點由
/// <c>DatabaseInitializerTests</c>（SQLite 上的實測行為）與
/// <c>PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres</c>
/// （真引擎上的破壞性證明）釘住，不再只是文件敘述——但兩者的結論相反：在真 PostgreSQL 上，移除的欄位
/// 確實被 DROP；在 SQLite 上，同一個探針卻被保留下來。SQLite 端的存活是這個儲存庫目前設定造成的，不是
/// SQLite 引擎或 SqlSugar 的 SQLite dialect 本身做不到——SqlSugar 的 <c>SqliteCodeFirst.ExistLogic</c>
/// 確實實作了 DROP COLUMN，只是把關在 <c>ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn</c>
/// 之後，而這個儲存庫的 <c>SqlSugarClientFactory</c> 從未設定過 <c>MoreSettings</c>。因此本節開頭的破壞性
/// 主張只在 PostgreSQL 上證得出來，這個儲存庫只跑 SQLite 的 CI 套件永遠無法自行證明它——但這是「目前設定
/// 下」的性質，不是 SQLite 這個引擎的固有性質，一個打開該旗標的 fork 在 SQLite 上也會看到 DROP
/// （僅限普通欄位；SQLite 原生拒絕刪除的欄位會讓該次 ALTER 拋例外、啟動中斷，見手冊第 21 章
/// 「結構同步的危險情境」第 7 項）。因此 <see cref="CreateMissingTables"/> 的安全性不建立在旗標上，
/// 而建立在物理事實上——只把「表尚不存在」的 entity 型別交給 <c>InitTables</c>，此時它只可能
/// CREATE；這一點與 <c>InitTables</c> 在既有表上究竟會不會刪欄位無關。
/// </para>
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// 對 <paramref name="entityTypes"/> 中「表名不在 <paramref name="existingTables"/> 內」者建立資料表，
    /// 回傳實際建立的型別。既有表一律不碰。所有環境、所有後端皆可執行。
    /// </summary>
    /// <param name="existingTables">啟動前的表名快照（小寫、ordinal），通常來自 <c>DataSeeder.GetTableNames</c>。</param>
    public static IReadOnlyList<Type> CreateMissingTables(
        ISqlSugarClient client, ISet<string> existingTables, ILogger? logger, params Type[] entityTypes)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(existingTables);

        if (entityTypes.Length == 0) return [];

        var missing = entityTypes
            .Where(t => !existingTables.Contains(
                client.EntityMaintenance.GetTableName(t).ToLowerInvariant()))
            .ToArray();

        if (missing.Length == 0)
        {
            logger?.LogInformation(
                "DatabaseInitializer: schema up to date — all {Count} entity table(s) already exist.",
                entityTypes.Length);
            return [];
        }

        client.CodeFirst.InitTables(missing);

        logger?.LogInformation(
            "DatabaseInitializer: created {Count} missing table(s): {Tables}.",
            missing.Length,
            string.Join(", ", missing.Select(client.EntityMaintenance.GetTableName)));

        return missing;
    }

    /// <summary>
    /// 完整 CodeFirst 結構同步（新增／修改／**刪除**欄位）。僅在 Development 執行；其他環境忽略並記錄
    /// Warning，回傳 <c>false</c>。由 <c>Database:AutoSyncSchema</c> 控制是否呼叫。
    /// </summary>
    public static bool SyncSchema(
        ISqlSugarClient client, IHostEnvironment environment, ILogger? logger, params Type[] entityTypes)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment())
        {
            logger?.LogWarning(
                "DatabaseInitializer: Database:AutoSyncSchema is enabled but the environment is " +
                "'{Environment}', not Development — the full CodeFirst schema sync was IGNORED. " +
                "Automatic structural sync can modify and DROP columns; outside Development, change " +
                "existing tables through reviewed migration scripts instead.",
                environment.EnvironmentName);
            return false;
        }

        if (entityTypes.Length == 0) return false;

        client.CodeFirst.InitTables(entityTypes);
        logger?.LogInformation(
            "DatabaseInitializer: CodeFirst full schema sync applied to {Count} entity type(s) " +
            "(Development only).", entityTypes.Length);
        return true;
    }
}
