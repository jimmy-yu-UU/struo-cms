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
/// 已測：<c>DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite</c> 顯示
/// SQLite 上未過濾的 <c>InitTables</c> 保留被移除的欄位；
/// <c>PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres</c> 顯示
/// 同一情境在真 PostgreSQL 上會 DROP 該欄位。已排除：這不是 SQLite 引擎或 SqlSugar SQLite dialect
/// 的限制，而是 <c>SqlSugarClientFactory</c> 從未開啟 <c>SqliteCodeFirstEnableDropColumn</c> 旗標所致。
/// 未知：旗標打開後 SQLite 的行為未在本儲存庫量測。若失去此保護：既有表上未過濾的 <c>InitTables</c> 在 PostgreSQL
/// 上會 DROP 移除的欄位。<see cref="CreateMissingTables"/> 的安全性建立在「表尚不存在」的過濾上，
/// 不是這個旗標上。完整說明：<c>docs/ai/decisions/codefirst-creates-missing-tables-only.md</c>。
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
