using System.Collections.Concurrent;

namespace Struo.Tests.Support;

/// <summary>
/// Reusable empty databases on one live backend. A lease hands out an idle database or creates one;
/// returning it drops every table, and a database that cannot be emptied is dropped instead of reused.
/// </summary>
internal sealed class LiveDatabasePool
{
    private const string Purpose = "unit";
    private const int ResetCommandTimeoutSeconds = 10;
    private const int MaxResetPasses = 5;

    private static readonly ConcurrentDictionary<TestBackendKind, Lazy<LiveDatabasePool>> Shared = new();

    private readonly ConcurrentBag<ITestDatabase> _idle = [];
    private readonly ConcurrentDictionary<string, ITestDatabase> _owned = new(StringComparer.Ordinal);
    private int _createdCount;

    internal LiveDatabasePool(TestBackendKind kind)
    {
        if (kind == TestBackendKind.Sqlite)
            throw new ArgumentException("SQLite has no live server to pool databases on.", nameof(kind));
        Kind = kind;
    }

    public TestBackendKind Kind { get; }

    /// <summary>Databases this pool has created.</summary>
    public int CreatedCount => Volatile.Read(ref _createdCount);

    public int IdleCount => _idle.Count;

    /// <summary>The process-wide pool for <paramref name="kind"/>; its databases are dropped at process exit.</summary>
    public static LiveDatabasePool For(TestBackendKind kind) =>
        Shared.GetOrAdd(kind, k => new Lazy<LiveDatabasePool>(() =>
        {
            var pool = new LiveDatabasePool(k);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => pool.DropAll();
            return pool;
        })).Value;

    /// <summary>A wrapper that takes a database from the pool on first use of its connection or name.</summary>
    public ITestDatabase Lease() => new PooledTestDatabase(this);

    /// <summary>Drops every database this pool created, idle or leased. Never throws.</summary>
    public void DropAll()
    {
        while (_idle.TryTake(out _))
        {
            // Drained here; the drop below covers every owned database.
        }
        var names = _owned.Keys.ToList();
        Parallel.ForEach(names, name =>
        {
            LiveDatabaseDdl.Drop(Kind, name);
            _owned.TryRemove(name, out _);
        });
    }

    internal ITestDatabase Take()
    {
        if (_idle.TryTake(out var idle)) return idle;
        var created = TestBackend.CreateLiveDatabase(Kind, Purpose);
        _owned[created.Name] = created;
        Interlocked.Increment(ref _createdCount);
        return created;
    }

    /// <summary>Empties <paramref name="db"/> and makes it idle, or drops it. Never throws.</summary>
    internal void Return(ITestDatabase db)
    {
        if (!_owned.ContainsKey(db.Name))
        {
            // Already dropped by DropAll.
            db.Dispose();
            return;
        }
        try
        {
            if (TryEmpty(db))
            {
                _idle.Add(db);
                return;
            }
            Console.Error.WriteLine($"WARNING: pooled test database '{db.Name}' on {Kind} could not be emptied; dropping it.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: resetting pooled test database '{db.Name}' on {Kind} failed; dropping it: {ex.Message}");
        }
        // Stays owned, so DropAll retries a drop that failed here.
        db.Dispose();
    }

    private bool TryEmpty(ITestDatabase db)
    {
        using var client = LiveDatabaseDdl.OpenClient(Kind, db.ConnectionString);
        client.Ado.CommandTimeOut = ResetCommandTimeoutSeconds;
        var maintenance = client.DbMaintenance;
        if (maintenance.GetViewInfoList(false).Count > 0) return false;
        for (var pass = 0; pass < MaxResetPasses; pass++)
        {
            var tables = maintenance.GetTableInfoList(false);
            if (tables.Count == 0) return true;
            var dropped = 0;
            foreach (var table in tables)
            {
                try
                {
                    maintenance.DropTable(table.Name);
                    dropped++;
                }
                catch (Exception ex)
                {
                    // A table still referenced by a foreign key drops on a later pass.
                    Console.Error.WriteLine($"Pooled reset on {Kind}: dropping '{table.Name}' failed: {ex.Message}");
                }
            }
            if (dropped == 0) return false;
        }
        return maintenance.GetTableInfoList(false).Count == 0;
    }
}
