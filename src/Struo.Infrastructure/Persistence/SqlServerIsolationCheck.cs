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
    public static void WarnIfSnapshotIsolationIsOff(ISqlSugarClient db, StruoDbType dbType, ILogger logger)
    {
        if (dbType != StruoDbType.SqlServer) return;

        object? flag;
        try
        {
            // The one raw-SQL read in the application: SqlSugar has no typed API for the catalog flag.
            // Approved exception: docs/ai/decisions/sqlserver-requires-read-committed-snapshot.md.
            flag = db.Ado.GetScalar("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()");
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Could not read the READ_COMMITTED_SNAPSHOT setting of the SQL Server database.");
            return;
        }

        if (flag is null || flag is DBNull || Convert.ToInt32(flag) != 0) return;

        logger.LogWarning(
            "The SQL Server database does not have READ_COMMITTED_SNAPSHOT on. Concurrent writes can deadlock " +
            "against readers under the default READ COMMITTED locking. Run once, with no other connections open: " +
            "ALTER DATABASE [<database>] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
    }
}
