using Struo.Application.Configuration;

namespace Struo.Tests.Support;

/// <summary>A database owned by one test host; disposing removes it.</summary>
internal interface ITestDatabase : IDisposable
{
    StruoDbType DbType { get; }
    string ConnectionString { get; }
    string Name { get; }
}

/// <summary>SQLite temp file, deleted on dispose.</summary>
internal sealed class SqliteTempTestDatabase : ITestDatabase
{
    private readonly SqliteTestDatabase _inner = new();

    public StruoDbType DbType => StruoDbType.Sqlite;
    public string ConnectionString => _inner.ConnectionString;
    public string Name => Path.GetFileName(_inner.FilePath);

    public void Dispose() => _inner.Dispose();
}

/// <summary>A database created on a live server; disposing drops it and never throws.</summary>
internal sealed class LiveTestDatabase : ITestDatabase
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
