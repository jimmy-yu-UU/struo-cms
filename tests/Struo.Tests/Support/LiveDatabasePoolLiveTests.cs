using System.Collections.Concurrent;
using AwesomeAssertions;
using SqlSugar;
using Xunit;

namespace Struo.Tests.Support;

/// <summary>
/// Live behaviour of <see cref="LiveDatabasePool"/>. Each case uses its own pool so the counts are
/// deterministic, and returns early when its backend is not configured (see <see cref="LiveBackend"/>).
/// </summary>
public sealed class LiveDatabasePoolLiveTests
{
    private const string ShapeTable = "pool_shape_probe";
    private static readonly TimeSpan DisposeLimit = TimeSpan.FromSeconds(60);

    public static TheoryData<string> Backends => LiveBackend.Names();

    [Theory, MemberData(nameof(Backends))]
    public void Returned_database_is_emptied_and_handed_out_again(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var pool = new LiveDatabasePool(kind);
        try
        {
            string first;
            using (var db = pool.Lease())
            {
                first = db.Name;
                using var client = LiveDatabaseDdl.OpenClient(kind, db.ConnectionString);
                client.CodeFirst.InitTables<ProbeRow>();
            }
            pool.IdleCount.Should().Be(1);

            using var again = pool.Lease();
            again.Name.Should().Be(first);
            using var check = LiveDatabaseDdl.OpenClient(kind, again.ConnectionString);
            check.DbMaintenance.GetTableInfoList(false).Should().BeEmpty();
            pool.CreatedCount.Should().Be(1);
        }
        finally
        {
            pool.DropAll();
        }
    }

    [Theory, MemberData(nameof(Backends))]
    public void A_reused_database_shows_the_new_shape_of_a_same_named_table(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var pool = new LiveDatabasePool(kind);
        try
        {
            string first;
            using (var db = pool.Lease())
            {
                first = db.Name;
                using var client = LiveDatabaseDdl.OpenClient(kind, db.ConnectionString);
                client.CodeFirst.InitTables<ShapeA>();
                client.Insertable(new ShapeA { Id = Guid.NewGuid(), Alpha = "a" }).ExecuteCommand();
            }

            using var again = pool.Lease();
            again.Name.Should().Be(first);
            using var second = LiveDatabaseDdl.OpenClient(kind, again.ConnectionString);
            second.CodeFirst.InitTables<ShapeB>();
            second.DbMaintenance.GetColumnInfosByTableName(ShapeTable, false)
                .Select(c => c.DbColumnName.ToLowerInvariant())
                .Should().BeEquivalentTo("id", "beta", "gamma");
            var id = Guid.NewGuid();
            second.Insertable(new ShapeB { Id = id, Beta = 7, Gamma = "g" }).ExecuteCommand();
            var row = second.Queryable<ShapeB>().Single();
            (row.Id, row.Beta, row.Gamma).Should().Be((id, 7, "g"));
        }
        finally
        {
            pool.DropAll();
        }
    }

    [Theory, MemberData(nameof(Backends))]
    public void A_wrapper_whose_connection_is_never_read_creates_nothing(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var pool = new LiveDatabasePool(kind);
        var db = pool.Lease();

        db.DbType.Should().Be(TestBackend.DbTypeOf(kind));
        db.Dispose();

        pool.CreatedCount.Should().Be(0);
        pool.IdleCount.Should().Be(0);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task A_database_held_by_an_open_transaction_is_discarded_without_hanging(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var pool = new LiveDatabasePool(kind);
        try
        {
            var db = pool.Lease();
            var name = db.Name;
            var holder = LiveDatabaseDdl.OpenClient(kind, db.ConnectionString, keepOpen: true);
            try
            {
                holder.CodeFirst.InitTables<ProbeRow>();
                holder.Ado.BeginTran();
                holder.Insertable(new ProbeRow { Id = Guid.NewGuid(), Name = "locked" }).ExecuteCommand();

                var dispose = Task.Run(db.Dispose);
                await dispose.WaitAsync(DisposeLimit);

                dispose.IsCompletedSuccessfully.Should().BeTrue();
                pool.IdleCount.Should().Be(0);
                // MySQL and MariaDB cannot sever the holder, so their DROP DATABASE waits for its lock.
                if (kind is TestBackendKind.PostgreSQL or TestBackendKind.SqlServer)
                    LiveDatabaseDdl.Exists(kind, name).Should().BeFalse();
            }
            finally
            {
                DisposeQuietly(holder);
            }

            using (var next = pool.Lease())
            {
                next.Name.Should().NotBe(name);
                using var check = LiveDatabaseDdl.OpenClient(kind, next.ConnectionString);
                check.DbMaintenance.GetTableInfoList(false).Should().BeEmpty();
            }

            pool.DropAll();
            LiveDatabaseDdl.Exists(kind, name).Should().BeFalse();
        }
        finally
        {
            pool.DropAll();
        }
    }

    [Theory, MemberData(nameof(Backends))]
    public void Concurrent_leases_never_share_a_database(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        const int leases = 8;
        var pool = new LiveDatabasePool(kind);
        var held = new ConcurrentBag<ITestDatabase>();
        try
        {
            using (var a = pool.Lease())
            using (var b = pool.Lease())
            {
                _ = a.Name;
                _ = b.Name;
            }
            pool.IdleCount.Should().Be(2);

            Parallel.For(0, leases, _ =>
            {
                var db = pool.Lease();
                held.Add(db);
                db.Name.Should().NotBeEmpty();
            });

            held.Select(d => d.Name).Distinct().Should().HaveCount(leases);
            pool.IdleCount.Should().Be(0);
            pool.CreatedCount.Should().Be(leases);
        }
        finally
        {
            foreach (var db in held) db.Dispose();
            pool.DropAll();
        }
    }

    [Theory, MemberData(nameof(Backends))]
    public void DropAll_drops_idle_and_leased_databases(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var pool = new LiveDatabasePool(kind);
        var leased = pool.Lease();
        string idleName;
        using (var idle = pool.Lease())
        {
            idleName = idle.Name;
        }
        var leasedName = leased.Name;

        pool.DropAll();

        LiveDatabaseDdl.Exists(kind, idleName).Should().BeFalse();
        LiveDatabaseDdl.Exists(kind, leasedName).Should().BeFalse();
        pool.IdleCount.Should().Be(0);
        var act = leased.Dispose;
        act.Should().NotThrow();
        pool.IdleCount.Should().Be(0);
    }

    private static bool TryResolve(string backend, out TestBackendKind kind)
    {
        var live = LiveBackend.Get(backend);
        kind = TestBackend.KindOf(live);
        return live.Connection is not null;
    }

    // The holder's connection may already be severed by the drop.
    private static void DisposeQuietly(SqlSugarClient client)
    {
        try
        {
            client.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Holder client dispose failed after the drop: {ex.Message}");
        }
    }

    [SugarTable("pool_probe")]
    private sealed class ProbeRow
    {
        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }

        [SugarColumn(Length = 64)]
        public string Name { get; set; } = "";
    }

    [SugarTable(ShapeTable)]
    private sealed class ShapeA
    {
        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }

        [SugarColumn(Length = 32)]
        public string Alpha { get; set; } = "";
    }

    [SugarTable(ShapeTable)]
    private sealed class ShapeB
    {
        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }

        public int Beta { get; set; }

        [SugarColumn(Length = 32)]
        public string Gamma { get; set; } = "";
    }
}
