using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Backend-neutral detection of a duplicate-key (unique or primary key) violation. Matches on each
/// provider's numeric error identity, never on message text, and walks the <c>InnerException</c>
/// chain because SqlSugar wraps provider exceptions.
/// </summary>
public static class UniqueViolation
{
    private const string PostgresUniqueViolation = "23505";
    private const int SqlServerUniqueConstraint = 2627;
    private const int SqlServerUniqueIndex = 2601;
    private const int MySqlDuplicateEntry = 1062;
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int OracleUniqueConstraint = 1;

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
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is SqliteConstraintUnique or SqliteConstraintPrimaryKey,
        SqlException sql => sql.Number is SqlServerUniqueConstraint or SqlServerUniqueIndex,
        MySqlException mysql => mysql.Number == MySqlDuplicateEntry,
        OracleException oracle => oracle.Number == OracleUniqueConstraint,
        // Npgsql's PostgresException overrides SqlState; no direct Npgsql reference is needed.
        DbException db => db.SqlState == PostgresUniqueViolation,
        _ => false,
    };
}
