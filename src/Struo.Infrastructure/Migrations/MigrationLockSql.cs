using System.Text;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations;

/// <summary>Lock names and per-backend advisory-lock SQL. A null statement means the backend takes no lock.</summary>
internal static class MigrationLockSql
{
    public static string LockName(string tablePrefix) => "struo_migrate:" + tablePrefix;

    /// <summary>64-bit FNV-1a over the UTF-8 bytes, so the key is identical across processes and runtimes.</summary>
    public static long PostgresKey(string lockName)
    {
        var hash = 0xcbf29ce484222325UL;
        foreach (var b in Encoding.UTF8.GetBytes(lockName))
        {
            hash ^= b;
            hash *= 0x100000001b3UL;
        }
        return unchecked((long)hash);
    }

    public static string? TryAcquireSql(StruoDbType db) => db switch
    {
        StruoDbType.PostgreSQL => "SELECT CASE WHEN pg_try_advisory_lock(@key) THEN 1 ELSE 0 END",
        StruoDbType.MySql => "SELECT COALESCE(GET_LOCK(@name, 0), 0)",
        StruoDbType.SqlServer =>
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @name, @LockMode = 'Exclusive', " +
            "@LockOwner = 'Session', @LockTimeout = 0; SELECT CASE WHEN @r >= 0 THEN 1 ELSE 0 END",
        _ => null,
    };

    public static string? ReleaseSql(StruoDbType db) => db switch
    {
        StruoDbType.PostgreSQL => "SELECT pg_advisory_unlock(@key)",
        StruoDbType.MySql => "SELECT RELEASE_LOCK(@name)",
        StruoDbType.SqlServer => "EXEC sp_releaseapplock @Resource = @name, @LockOwner = 'Session'",
        _ => null,
    };
}
