using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations;

/// <summary>
/// Holds a session-scoped advisory lock for the lifetime of the instance. The lock lives on one dedicated
/// connection that stays open until disposal; SQLite and Oracle take no lock.
/// </summary>
public sealed class MigrationLock : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private readonly SqlSugarClient? _session;
    private readonly string? _releaseSql;
    private readonly SugarParameter[] _parameters;
    private readonly ILogger? _logger;
    private bool _disposed;

    private MigrationLock(SqlSugarClient? session, string? releaseSql, SugarParameter[] parameters, ILogger? logger)
    {
        _session = session;
        _releaseSql = releaseSql;
        _parameters = parameters;
        _logger = logger;
    }

    /// <summary>Polls until the lock is free or <paramref name="timeout"/> elapses.</summary>
    public static async Task<MigrationLock> AcquireAsync(
        StruoDbType db, string connectionString, string tablePrefix, TimeSpan timeout, CancellationToken ct,
        ILogger? logger = null)
    {
        var acquireSql = MigrationLockSql.TryAcquireSql(db);
        if (acquireSql is null) return new MigrationLock(null, null, [], logger);

        var name = MigrationLockSql.LockName(tablePrefix);
        SugarParameter[] parameters = db == StruoDbType.PostgreSQL
            ? [new SugarParameter("@key", MigrationLockSql.PostgresKey(name))]
            : [new SugarParameter("@name", name)];

        var session = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbTypeMapper.Map(db),
            ConnectionString = MigrationLockSql.SessionConnectionString(db, connectionString),
            IsAutoCloseConnection = false,
        });
        try
        {
            await session.Ado.OpenAsync();
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (Convert.ToInt32(await session.Ado.GetScalarAsync(acquireSql, parameters, ct)) == 1)
                    return new MigrationLock(session, MigrationLockSql.ReleaseSql(db), parameters, logger);
                if (DateTime.UtcNow >= deadline)
                    throw new MigrationLockTimeoutException(
                        $"Timed out after {timeout.TotalSeconds:0}s waiting for the migration lock '{name}'. " +
                        "Another migrate is running, or raise Database:MigrationLockTimeoutSeconds.");
                await Task.Delay(PollInterval, ct);
            }
        }
        catch
        {
            Close(session);
            throw;
        }
    }

    /// <summary>Releases the lock and closes the session. A failed release is logged, never thrown: the
    /// non-pooled connection closing ends the database session, which frees the lock anyway.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_session is null || _disposed) return;
        _disposed = true;
        try { await _session.Ado.GetScalarAsync(_releaseSql!, _parameters, CancellationToken.None); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Releasing the migration lock failed; closing its session frees it.");
        }
        finally { Close(_session); }
    }

    private static void Close(SqlSugarClient session)
    {
        try { session.Ado.Close(); }
        finally { session.Dispose(); }
    }
}
