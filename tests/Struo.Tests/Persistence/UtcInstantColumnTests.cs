using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// A <see cref="ColumnShape.TimestampWithTimeZone"/> column holds a UTC instant: what is written is the
/// instant stored, and what is read back is that instant, whatever the database server's time zone and
/// the test host's zone. On PostgreSQL and SQL Server the value also reads back as
/// <see cref="DateTimeKind.Utc"/>, and a <see cref="DateTimeKind.Local"/> write lands at its instant;
/// MySQL, MariaDB and SQLite store the wall clock as given. A <see cref="DateTimeKind.Unspecified"/>
/// write is taken as UTC on every backend.
/// </summary>
public sealed class UtcInstantColumnTests : IDisposable
{
    private static readonly DateTime Instant = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private readonly ITestDatabase _db = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _client;

    public UtcInstantColumnTests()
    {
        _client = SqlSugarClientFactory.Create(_db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        _client.CodeFirst.InitTables<UtcInstantRow>();
    }

    public void Dispose() => _db.Dispose();

    private static bool ZoneAwareBackend =>
        TestBackend.Current is TestBackendKind.PostgreSQL or TestBackendKind.SqlServer;

    private async Task<Guid> InsertAsync(DateTime at, DateTime? maybeAt = null)
    {
        var id = Guid.CreateVersion7();
        await _client.Insertable(new UtcInstantRow { Id = id, Label = "a", At = at, MaybeAt = maybeAt })
            .ExecuteCommandAsync();
        return id;
    }

    private Task<UtcInstantRow> ReadAsync(Guid id) =>
        _client.Queryable<UtcInstantRow>().Where(r => r.Id == id).FirstAsync();

    private static void ShouldBeInstant(DateTime actual, DateTime expected)
    {
        actual.Should().Be(expected);
        if (ZoneAwareBackend) actual.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Insert_reads_back_the_written_instant()
    {
        var id = await InsertAsync(Instant);

        ShouldBeInstant((await ReadAsync(id)).At, Instant);
    }

    [Fact]
    public async Task List_insert_writes_each_instant()
    {
        var rows = Enumerable.Range(0, 3)
            .Select(i => new UtcInstantRow { Id = Guid.CreateVersion7(), Label = "l" + i, At = Instant.AddHours(i) })
            .ToList();

        await _client.Insertable(rows).ExecuteCommandAsync();

        foreach (var row in rows) ShouldBeInstant((await ReadAsync(row.Id)).At, row.At);
    }

    [Fact]
    public void Converter_is_attached_only_on_postgresql_and_sql_server()
    {
        var columns = _client.EntityMaintenance.GetEntityInfo<UtcInstantRow>().Columns;

        foreach (var name in new[] { nameof(UtcInstantRow.At), nameof(UtcInstantRow.MaybeAt) })
            (columns.Single(c => c.PropertyName == name).SqlParameterDbType as Type)?.Name
                .Should().Be(ZoneAwareBackend ? "UtcInstantConverter" : null);
    }

    // Read through a client without the factory's column conventions. The driver's own reading is the
    // oracle: a Local value is converted to UTC, anything else already carries the stored UTC digits.
    [Fact]
    public async Task Stored_instant_is_the_written_instant_regardless_of_server_zone()
    {
        var id = await InsertAsync(Instant);

        using var raw = new SqlSugarClient(_db.RawConfig());
        var stored = (await raw.Queryable<UtcInstantRow>().Where(r => r.Id == id).FirstAsync()).At;

        (stored.Kind == DateTimeKind.Local ? stored.ToUniversalTime() : stored).Should().Be(Instant);
    }

    [Fact]
    public async Task Nullable_column_round_trips_null_and_an_instant()
    {
        var id = await InsertAsync(Instant);
        (await ReadAsync(id)).MaybeAt.Should().BeNull();

        var row = await ReadAsync(id);
        row.MaybeAt = Instant.AddDays(1);
        await _client.Updateable(row).ExecuteCommandAsync();

        var maybeAt = (await ReadAsync(id)).MaybeAt;
        maybeAt.Should().NotBeNull();
        ShouldBeInstant(maybeAt!.Value, Instant.AddDays(1));
    }

    // The generic item update path: read the whole row, change another column, write the whole row.
    [Fact]
    public async Task Full_row_updates_leave_the_instant_unchanged()
    {
        var id = await InsertAsync(Instant, Instant);

        for (var i = 0; i < 2; i++)
        {
            var row = await ReadAsync(id);
            row.Label = "edit " + i;
            await _client.Updateable(row).ExecuteCommandAsync();
        }

        var after = await ReadAsync(id);
        ShouldBeInstant(after.At, Instant);
        ShouldBeInstant(after.MaybeAt!.Value, Instant);
    }

    [Fact]
    public async Task SetColumns_member_init_writes_the_instant()
    {
        var id = await InsertAsync(Instant);
        var renewed = Instant.AddHours(16);

        await _client.Updateable<UtcInstantRow>()
            .SetColumns(r => new UtcInstantRow { Label = "renewed", At = renewed })
            .Where(r => r.Id == id).ExecuteCommandAsync();

        ShouldBeInstant((await ReadAsync(id)).At, renewed);
    }

    [Fact]
    public async Task SetColumns_equality_form_writes_the_instant()
    {
        var id = await InsertAsync(Instant);
        var renewed = Instant.AddHours(3);

        await _client.Updateable<UtcInstantRow>()
            .SetColumns(r => r.At == renewed)
            .Where(r => r.Id == id).ExecuteCommandAsync();

        ShouldBeInstant((await ReadAsync(id)).At, renewed);
    }

    [Fact]
    public async Task Where_compares_against_the_instant()
    {
        var earlier = await InsertAsync(Instant.AddMinutes(-1));
        await InsertAsync(Instant.AddMinutes(1));

        var cutoff = Instant;
        var ids = await _client.Queryable<UtcInstantRow>()
            .Where(r => r.At < cutoff).Select(r => r.Id).ToListAsync();

        ids.Should().Equal(earlier);
    }

    [Fact]
    public async Task Unspecified_write_is_taken_as_utc()
    {
        var id = await InsertAsync(DateTime.SpecifyKind(Instant, DateTimeKind.Unspecified));

        ShouldBeInstant((await ReadAsync(id)).At, Instant);
    }

    [Fact]
    public async Task Local_write_lands_at_its_instant_on_postgresql_and_sql_server()
    {
        if (!ZoneAwareBackend) return;

        var id = await InsertAsync(Instant.ToLocalTime());

        ShouldBeInstant((await ReadAsync(id)).At, Instant);
    }

    // SQL Server keeps 100 ns ticks in datetimeoffset(7); PostgreSQL keeps microseconds.
    [Fact]
    public async Task Sub_millisecond_precision_survives()
    {
        if (!ZoneAwareBackend) return;
        var precise = Instant.AddTicks(TestBackend.Current == TestBackendKind.SqlServer ? 1_234_567 : 1_234_560);

        var id = await InsertAsync(precise);

        ShouldBeInstant((await ReadAsync(id)).At, precise);
    }
}

[SugarTable("utc_instant_rows")]
public sealed class UtcInstantRow
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public string Label { get; set; } = "";

    [ColumnShape(ColumnShape.TimestampWithTimeZone)] public DateTime At { get; set; }

    [ColumnShape(ColumnShape.TimestampWithTimeZone)] public DateTime? MaybeAt { get; set; }
}
