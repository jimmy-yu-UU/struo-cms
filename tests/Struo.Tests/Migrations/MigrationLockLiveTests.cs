using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class MigrationLockLiveTests
{
    public static TheoryData<StruoDbType> Backends => new() { StruoDbType.PostgreSQL, StruoDbType.SqlServer };

    private static string? Conn(StruoDbType db) =>
        db == StruoDbType.PostgreSQL ? LiveDatabases.Postgres : LiveDatabases.SqlServer;

    private static MigrationHost Host(StruoDbType db, string conn, string prefix, int timeout) => new(
        new MigrationHostOptions(db, conn, prefix, [typeof(MigrationLockLiveTests).Assembly], timeout)
        { NamespaceFilter = "Struo.Tests.Migrations.Probes.Lock" },
        NullLoggerFactory.Instance);

    private static void Cleanup(StruoDbType db, string conn, string prefix)
    {
        var c = new SqlSugarClient(new ConnectionConfig
            { DbType = DbTypeMapper.Map(db), ConnectionString = conn, IsAutoCloseConnection = true });
        foreach (var t in new[] { prefix + "lockprobe", prefix + "schema_versions" })
            if (c.DbMaintenance.IsAnyTable(t, false)) c.DbMaintenance.DropTable(t);
    }

    private static void EnsureDatabase(StruoDbType db, string conn)
    {
        var c = new SqlSugarClient(new ConnectionConfig
            { DbType = DbTypeMapper.Map(db), ConnectionString = conn, IsAutoCloseConnection = true });
        try { c.DbMaintenance.CreateDatabase(); } catch { /* exists or not permitted */ }
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Concurrent_applies_run_each_migration_exactly_once(StruoDbType db)
    {
        if (Conn(db) is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        EnsureDatabase(db, conn);
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        try
        {
            var runs = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() => Host(db, conn, prefix, 30).ApplyAsync(default))));

            runs.Sum(r => r.Count).Should().Be(1);
            Host(db, conn, prefix, 30).GetStatus().Single().State.Should().Be(MigrationState.Applied);
        }
        finally { Cleanup(db, conn, prefix); }
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Apply_times_out_while_another_session_holds_the_lock(StruoDbType db)
    {
        if (Conn(db) is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        EnsureDatabase(db, conn);
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        try
        {
            await using (await MigrationLock.AcquireAsync(db, conn, prefix, TimeSpan.FromSeconds(5), default))
            {
                var act = () => Host(db, conn, prefix, timeout: 1).ApplyAsync(default);
                await act.Should().ThrowAsync<MigrationLockTimeoutException>()
                    .WithMessage("*Database:MigrationLockTimeoutSeconds*");
            }
            (await Host(db, conn, prefix, 5).ApplyAsync(default)).Should().ContainSingle();
        }
        finally { Cleanup(db, conn, prefix); }
    }
}
