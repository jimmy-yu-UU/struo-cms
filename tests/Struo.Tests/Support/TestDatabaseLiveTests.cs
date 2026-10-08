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
        const string runId = "deadbeef";
        var leftover = TestBackend.CreateLiveDatabase(kind, "leftover", runId);
        var configured = TestBackend.ConfiguredDatabaseName(kind);
        var decoyName = TestBackend.DatabaseNameFor(configured, "decoy", "nothex");
        using var decoy = LiveDatabaseDdl.CreateNamed(kind, decoyName);

        TestBackend.FindLeftovers(kind, runId).Should().ContainSingle().Which.Should().Be(leftover.Name);
        TestBackend.FindLeftovers(kind).Should().NotContain(decoyName);

        TestBackend.DropLeftovers(kind, runId);

        LiveDatabaseDdl.Exists(kind, leftover.Name).Should().BeFalse();
        LiveDatabaseDdl.Exists(kind, decoyName).Should().BeTrue();
        LiveDatabaseDdl.Exists(kind, configured).Should().BeTrue();
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
