using System.Runtime.ExceptionServices;

namespace Struo.Tests.Support;

/// <summary>
/// Class fixture: one fresh database per live backend, created on first use and dropped on teardown.
/// A failed create is remembered per backend and rethrown to later callers, so an unreachable server
/// costs one connection attempt per fixture.
/// </summary>
public abstract class LiveDatabaseSet : IDisposable
{
    private readonly Dictionary<string, ITestDatabase> _databases = new();
    private readonly Dictionary<string, ExceptionDispatchInfo> _failures = new();
    private readonly object _gate = new();
    private readonly string _purpose;
    private readonly Func<TestBackendKind, string, ITestDatabase> _create;

    protected LiveDatabaseSet(string purpose)
        : this(purpose, TestBackend.CreateLiveDatabase)
    {
    }

    internal LiveDatabaseSet(string purpose, Func<TestBackendKind, string, ITestDatabase> create)
    {
        _purpose = purpose;
        _create = create;
    }

    internal string ConnectionFor(LiveBackend backend)
    {
        lock (_gate)
        {
            if (_failures.TryGetValue(backend.Name, out var failure)) failure.Throw();
            if (!_databases.TryGetValue(backend.Name, out var database))
            {
                try
                {
                    database = _create(TestBackend.KindOf(backend), _purpose);
                }
                catch (Exception ex)
                {
                    _failures[backend.Name] = ExceptionDispatchInfo.Capture(ex);
                    throw;
                }
                _databases[backend.Name] = database;
            }
            return database.ConnectionString;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var database in _databases.Values) database.Dispose();
            _databases.Clear();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>Databases for <c>LiveRepositoryTests</c>.</summary>
public sealed class RepositoryDatabases() : LiveDatabaseSet("repo");

/// <summary>Databases for <c>PostgresOnlyTests</c>.</summary>
public sealed class PostgresOnlyDatabases() : LiveDatabaseSet("pgonly");
