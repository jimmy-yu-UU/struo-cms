using System.Data;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// SQL Server startup check: warns when the database does not have READ_COMMITTED_SNAPSHOT on.
/// Under the default locking READ COMMITTED, concurrent writes deadlock against readers.
/// Runs nothing on other backends and never fails startup.
/// </summary>
public static class SqlServerIsolationCheck
{
    /// <summary>The database's name and whether READ_COMMITTED_SNAPSHOT is on for it.</summary>
    public readonly record struct SnapshotSetting(bool IsOn, string Database);

    public static Task WarnIfSnapshotIsolationIsOffAsync(ISqlSugarClient db, StruoDbType dbType, ILogger logger) =>
        WarnIfSnapshotIsolationIsOffAsync(dbType, () => ReadAsync(db), logger);

    /// <summary>
    /// The decision and the logging, with the catalog read supplied by <paramref name="read"/>, which
    /// returns null when the catalog has no row for the database. Invoked only on SQL Server.
    /// </summary>
    public static async Task WarnIfSnapshotIsolationIsOffAsync(
        StruoDbType dbType, Func<Task<SnapshotSetting?>> read, ILogger logger)
    {
        if (dbType != StruoDbType.SqlServer) return;

        SnapshotSetting? setting;
        try
        {
            setting = await read();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the READ_COMMITTED_SNAPSHOT setting of the SQL Server database.");
            return;
        }

        if (setting is not { IsOn: false } off) return;

        var quoted = "[" + off.Database.Replace("]", "]]") + "]";
        logger.LogWarning(
            "The SQL Server database does not have READ_COMMITTED_SNAPSHOT on. Concurrent writes can deadlock " +
            "against readers under the default READ COMMITTED locking. Run once, with no other connections open: " +
            "ALTER DATABASE {Database} SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE",
            quoted);
    }

    // The one raw-SQL read in the application: SqlSugar has no typed API for the catalog flag.
    // Approved exception: docs/ai/decisions/sqlserver-requires-read-committed-snapshot.md.
    private static async Task<SnapshotSetting?> ReadAsync(ISqlSugarClient db)
    {
        var result = await db.Ado.GetDataTableAsync(
            "SELECT is_read_committed_snapshot_on, DB_NAME() AS name FROM sys.databases WHERE name = DB_NAME()");
        if (result.Rows.Count == 0) return null;
        var row = result.Rows[0];
        if (row[0] is DBNull) return null;
        return new SnapshotSetting(Convert.ToInt32(row[0]) != 0, Convert.ToString(row[1])!);
    }
}
