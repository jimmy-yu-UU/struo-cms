using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// The SQL Server startup check warns once when READ_COMMITTED_SNAPSHOT is off, stays silent when it
/// is on, and issues no query on any other backend. The live cases return early when SQL Server is
/// not configured (see <see cref="LiveBackend"/>).
/// </summary>
public sealed class SqlServerIsolationCheckTests
{
    [Theory]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.MySql)]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.Oracle)]
    public async Task Other_backends_run_no_query_and_log_nothing(StruoDbType dbType)
    {
        using var file = TestBackend.CreateSqliteDatabase();
        var client = SqlSugarClientFactory.Create(file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        var statements = 0;
        client.Aop.OnLogExecuting = (_, _) => statements++;
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(client, dbType, logger);

        statements.Should().Be(0);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_database_with_snapshot_isolation_on_logs_nothing()
    {
        if (!TryResolve(out var kind)) return;
        using var database = TestBackend.CreateLiveDatabase(kind, "rcsion");
        using var client = LiveDatabaseDdl.OpenClient(kind, database.ConnectionString);
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(client, StruoDbType.SqlServer, logger);

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_database_with_snapshot_isolation_off_logs_one_warning_with_the_remedy()
    {
        if (!TryResolve(out var kind)) return;
        using var database = TestBackend.CreateLiveDatabase(kind, "rcsioff");
        LiveDatabaseDdl.DisableReadCommittedSnapshot(database.ConnectionString);
        using var client = LiveDatabaseDdl.OpenClient(kind, database.ConnectionString);
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(client, StruoDbType.SqlServer, logger);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("READ_COMMITTED_SNAPSHOT").And.Contain("deadlock")
            .And.Contain("ALTER DATABASE").And.Contain($"[{database.Name}]");
    }

    [Fact]
    public async Task Setting_on_logs_nothing()
    {
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(
            StruoDbType.SqlServer, () => Task.FromResult<SqlServerIsolationCheck.SnapshotSetting?>(new(true, "app")), logger);

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Setting_off_logs_one_warning_naming_the_database_and_the_statement()
    {
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(
            StruoDbType.SqlServer, () => Task.FromResult<SqlServerIsolationCheck.SnapshotSetting?>(new(false, "my]db")), logger);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("READ_COMMITTED_SNAPSHOT").And.Contain("deadlock")
            .And.Contain("ALTER DATABASE [my]]db] SET READ_COMMITTED_SNAPSHOT ON");
    }

    [Fact]
    public async Task No_catalog_row_logs_nothing()
    {
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(
            StruoDbType.SqlServer, () => Task.FromResult<SqlServerIsolationCheck.SnapshotSetting?>(null), logger);

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_read_logs_one_warning_without_connection_details_and_does_not_throw()
    {
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(
            StruoDbType.SqlServer,
            () => throw new InvalidOperationException("Server=secret;Password=hunter2"),
            logger);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("READ_COMMITTED_SNAPSHOT")
            .And.NotContain("Server=").And.NotContain("Password");
    }

    [Theory]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.MySql)]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.Oracle)]
    public async Task Other_backends_never_invoke_the_read(StruoDbType dbType)
    {
        var invoked = false;
        var logger = new ListLogger();

        await SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(
            dbType, () => { invoked = true; return Task.FromResult<SqlServerIsolationCheck.SnapshotSetting?>(null); }, logger);

        invoked.Should().BeFalse();
        logger.Entries.Should().BeEmpty();
    }

    private static bool TryResolve(out TestBackendKind kind)
    {
        var live = LiveBackend.Get("SqlServer");
        kind = TestBackend.KindOf(live);
        return live.Connection is not null;
    }
}
