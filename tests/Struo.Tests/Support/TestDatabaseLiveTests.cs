using AwesomeAssertions;
using SqlSugar;
using Xunit;

namespace Struo.Tests.Support;

/// <summary>
/// Live lifecycle of <see cref="TestBackend.CreateLiveDatabase(TestBackendKind, string)"/>: each case
/// returns early when its backend is not configured (see <see cref="LiveBackend"/>).
/// </summary>
public sealed class TestDatabaseLiveTests
{
    public static TheoryData<string> Backends => LiveBackend.Names();

    private const string DropLeftoversSwitch = "STRUO_TEST_DROP_LEFTOVERS";

    [Theory, MemberData(nameof(Backends))]
    public void Created_database_exists_and_round_trips_a_row(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;

        using var database = TestBackend.CreateLiveDatabase(kind, "lifecycle");

        LiveDatabaseDdl.Exists(kind, database.Name).Should().BeTrue();
        database.ConnectionString.Should().Contain(database.Name);
        using var client = LiveDatabaseDdl.OpenClient(kind, database.ConnectionString);
        client.CodeFirst.InitTables<ProbeRow>();
        var id = Guid.NewGuid();
        client.Insertable(new ProbeRow { Id = id, Name = "probe" }).ExecuteCommand();
        client.Queryable<ProbeRow>().First(r => r.Id == id).Name.Should().Be("probe");
    }

    [Theory, MemberData(nameof(Backends))]
    public void Same_purpose_databases_are_independent_and_disposing_one_leaves_the_other(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        using var kept = TestBackend.CreateLiveDatabase(kind, "twin");
        var dropped = TestBackend.CreateLiveDatabase(kind, "twin");
        try
        {
            kept.Name.Should().NotBe(dropped.Name);
        }
        finally
        {
            dropped.Dispose();
        }

        LiveDatabaseDdl.Exists(kind, dropped.Name).Should().BeFalse();
        LiveDatabaseDdl.Exists(kind, kept.Name).Should().BeTrue();
        using var client = LiveDatabaseDdl.OpenClient(kind, kept.ConnectionString);
        client.CodeFirst.InitTables<ProbeRow>();
        client.Queryable<ProbeRow>().Count().Should().Be(0);
    }

    [Theory, MemberData(nameof(Backends))]
    public void Disposing_drops_the_database(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var database = TestBackend.CreateLiveDatabase(kind, "dispose");
        var name = database.Name;
        LiveDatabaseDdl.Exists(kind, name).Should().BeTrue();

        database.Dispose();

        LiveDatabaseDdl.Exists(kind, name).Should().BeFalse();
    }

    [Theory, MemberData(nameof(Backends))]
    public void Disposing_drops_the_database_while_a_connection_is_still_open(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var database = TestBackend.CreateLiveDatabase(kind, "open");
        var name = database.Name;
        using var client = LiveDatabaseDdl.OpenClient(kind, database.ConnectionString, keepOpen: true);
        client.Ado.GetInt("select 1").Should().Be(1);

        database.Dispose();

        LiveDatabaseDdl.Exists(kind, name).Should().BeFalse();
    }

    [Theory, MemberData(nameof(Backends))]
    public void Leftover_cleanup_finds_and_drops_only_databases_the_mechanism_created(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var runId = Guid.NewGuid().ToString("N")[..8];
        var configured = TestBackend.ConfiguredDatabaseName(kind);
        var nonHexSuffix = Guid.NewGuid().ToString("N")[..7] + "z";
        var hexName = TestBackend.DatabaseNameFor(configured, "decoy", 1, "0123abcd");
        var decoyName = hexName[..^8] + nonHexSuffix;
        using var own = TestBackend.CreateLiveDatabase(kind, "inuse");
        using var leftover = TestBackend.CreateLiveDatabase(kind, "leftover", runId);
        using var decoy = LiveDatabaseDdl.CreateNamed(kind, decoyName);

        TestBackend.FindLeftovers(kind, runId).Should().ContainSingle().Which.Should().Be(leftover.Name);
        TestBackend.FindLeftovers(kind).Should().Contain(leftover.Name).And.NotContain([decoyName, own.Name]);

        TestBackend.DropLeftovers(kind, runId);

        LiveDatabaseDdl.Exists(kind, leftover.Name).Should().BeFalse();
        LiveDatabaseDdl.Exists(kind, own.Name).Should().BeTrue();
        LiveDatabaseDdl.Exists(kind, decoyName).Should().BeTrue();
        LiveDatabaseDdl.Exists(kind, configured).Should().BeTrue();
    }

    [Theory, MemberData(nameof(Backends))]
    public void Creating_a_database_whose_name_exists_throws_and_keeps_the_existing_one(string backend)
    {
        if (!TryResolve(backend, out var kind)) return;
        var configured = TestBackend.ConfiguredDatabaseName(kind);
        var name = TestBackend.DatabaseNameFor(configured, "clash", 1, Guid.NewGuid().ToString("N")[..8]);
        using var first = LiveDatabaseDdl.CreateNamed(kind, name);

        var act = () => LiveDatabaseDdl.CreateNamed(kind, name);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(name);
        LiveDatabaseDdl.Exists(kind, name).Should().BeTrue();
    }

    /// <summary>
    /// Drops every leftover database from earlier crashed runs on the selected backend. Does nothing
    /// unless <c>STRUO_TEST_DROP_LEFTOVERS=1</c> and <c>STRUO_TEST_BACKEND</c> names a live backend.
    /// </summary>
    [Fact]
    public void Drop_leftover_databases_on_request()
    {
        if (Environment.GetEnvironmentVariable(DropLeftoversSwitch) != "1" || !TestBackend.IsLive) return;

        var kind = TestBackend.Current;
        TestBackend.DropLeftovers(kind);

        TestBackend.FindLeftovers(kind).Should().BeEmpty();
    }

    private static bool TryResolve(string backend, out TestBackendKind kind)
    {
        var live = LiveBackend.Get(backend);
        kind = TestBackend.KindOf(live);
        return live.Connection is not null;
    }

    [SugarTable("test_backend_probe")]
    private sealed class ProbeRow
    {
        [SugarColumn(IsPrimaryKey = true)]
        public Guid Id { get; set; }

        [SugarColumn(Length = 64)]
        public string Name { get; set; } = "";
    }
}
