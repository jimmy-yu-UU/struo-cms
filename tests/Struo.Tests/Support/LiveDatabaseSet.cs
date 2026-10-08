namespace Struo.Tests.Support;

/// <summary>
/// Class fixture: one fresh database per live backend, created on first use and dropped on teardown.
/// </summary>
public abstract class LiveDatabaseSet(string purpose) : IDisposable
{
    private readonly Dictionary<string, ITestDatabase> _databases = new();
    private readonly object _gate = new();

    internal string ConnectionFor(LiveBackend backend)
    {
        lock (_gate)
        {
            if (!_databases.TryGetValue(backend.Name, out var database))
            {
                database = TestBackend.CreateLiveDatabase(TestBackend.KindOf(backend), purpose);
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
    }
}

/// <summary>Databases for <c>LiveRepositoryTests</c>.</summary>
public sealed class RepositoryDatabases() : LiveDatabaseSet("repo");

/// <summary>Databases for <c>PostgresOnlyTests</c>.</summary>
public sealed class PostgresOnlyDatabases() : LiveDatabaseSet("pgonly");
