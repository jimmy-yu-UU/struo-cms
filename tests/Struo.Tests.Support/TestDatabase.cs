using Struo.Application.Configuration;

namespace Struo.Tests.Support;

/// <summary>A database owned by one test or test host; disposing removes it or returns it to its pool.</summary>
public interface ITestDatabase : IDisposable
{
    StruoDbType DbType { get; }
    string ConnectionString { get; }
    string Name { get; }
}

/// <summary>SQLite temp file, deleted on dispose.</summary>
public sealed class SqliteTempTestDatabase : ITestDatabase
{
    private readonly SqliteTestDatabase _inner = new();

    public StruoDbType DbType => StruoDbType.Sqlite;
    public string ConnectionString => _inner.ConnectionString;
    public string Name => Path.GetFileName(_inner.FilePath);

    public void Dispose() => _inner.Dispose();
}

/// <summary>A database created on a live server; disposing drops it and never throws.</summary>
public sealed class LiveTestDatabase : ITestDatabase
{
    private readonly TestBackendKind _kind;
    private int _disposed;

    public LiveTestDatabase(TestBackendKind kind, string name, string connectionString)
    {
        _kind = kind;
        Name = name;
        ConnectionString = connectionString;
    }

    public StruoDbType DbType => TestBackend.DbTypeOf(_kind);
    public string ConnectionString { get; }
    public string Name { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) LiveDatabaseDdl.Drop(_kind, Name);
    }
}

/// <summary>
/// A database leased from a <see cref="LiveDatabasePool"/> on first use of <see cref="ConnectionString"/>
/// or <see cref="Name"/>; disposing returns it to the pool and never throws.
/// </summary>
public sealed class PooledTestDatabase : ITestDatabase
{
    private readonly LiveDatabasePool _pool;
    private readonly Lock _gate = new();
    private ITestDatabase? _leased;
    private bool _disposed;

    public PooledTestDatabase(LiveDatabasePool pool) => _pool = pool;

    public StruoDbType DbType => TestBackend.DbTypeOf(_pool.Kind);
    public string ConnectionString => Leased().ConnectionString;
    public string Name => Leased().Name;

    public void Dispose()
    {
        ITestDatabase? leased;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            leased = _leased;
            _leased = null;
        }
        if (leased is not null) _pool.Return(leased);
    }

    private ITestDatabase Leased()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _leased ??= _pool.Take();
        }
    }
}
