using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Backend-neutral detection of a transaction the database ended because of a lock conflict that a retry
/// can resolve: a deadlock victim, or MariaDB's "record has changed since last read". Matches on each
/// provider's numeric error identity, never on message text, and walks the <c>InnerException</c> chain
/// because SqlSugar wraps provider exceptions. Lock wait timeouts are not matched.
/// </summary>
public static class DeadlockVictim
{
    private const string PostgresDeadlockDetected = "40P01";
    private const int MySqlDeadlock = 1213;
    private const int MariaDbRecordChanged = 1020;
    private const int SqlServerDeadlock = 1205;

    public static bool IsMatch(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (MatchesProvider(current)) return true;
        }
        return false;
    }

    private static bool MatchesProvider(Exception exception) => exception switch
    {
        SqlException sql => sql.Number == SqlServerDeadlock,
        MySqlException mysql => mysql.Number is MySqlDeadlock or MariaDbRecordChanged,
        // Npgsql's PostgresException overrides SqlState, so the base DbException matches it.
        DbException db => db.SqlState == PostgresDeadlockDetected,
        _ => false,
    };
}
