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
/// 並在 entity 移除屬性時 **DROP COLUMN**（官方文件：「正式数据一定要禁删除列操作」）。因此
/// <see cref="CreateMissingTables"/> 的安全性不建立在旗標上，而建立在物理事實上——只把「表尚不存在」的
/// entity 型別交給 <c>InitTables</c>，此時它只可能 CREATE。
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
