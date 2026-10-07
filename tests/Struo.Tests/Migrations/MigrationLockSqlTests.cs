using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class MigrationLockSqlTests
{
    [Fact]
    public void Lock_name_is_scoped_by_table_prefix() =>
        MigrationLockSql.LockName("acme_").Should().Be("struo_migrate:acme_");

    [Fact]
    public void Postgres_key_is_stable_and_prefix_sensitive()
    {
        var a = MigrationLockSql.PostgresKey("struo_migrate:struo_");
        a.Should().Be(MigrationLockSql.PostgresKey("struo_migrate:struo_"));
        a.Should().NotBe(MigrationLockSql.PostgresKey("struo_migrate:acme_"));
    }

    [Fact]
    public void Postgres_key_for_the_default_prefix_is_pinned() =>
        MigrationLockSql.PostgresKey("struo_migrate:struo_").Should().Be(511268103438724472L);

    [Theory]
    [InlineData(StruoDbType.PostgreSQL, "pg_try_advisory_lock", "pg_advisory_unlock")]
    [InlineData(StruoDbType.MySql, "GET_LOCK", "RELEASE_LOCK")]
    [InlineData(StruoDbType.SqlServer, "sp_getapplock", "sp_releaseapplock")]
    public void Locking_backends_have_acquire_and_release_sql(StruoDbType db, string acquire, string release)
    {
        MigrationLockSql.TryAcquireSql(db).Should().Contain(acquire);
        MigrationLockSql.ReleaseSql(db).Should().Contain(release);
    }

    [Theory]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.Oracle)]
    public void Sqlite_and_oracle_take_no_lock(StruoDbType db)
    {
        MigrationLockSql.TryAcquireSql(db).Should().BeNull();
        MigrationLockSql.ReleaseSql(db).Should().BeNull();
    }

    [Theory]
    [InlineData(StruoDbType.PostgreSQL, "Host=h;Database=d", "Host=h;Database=d;Pooling=false")]
    [InlineData(StruoDbType.SqlServer, "Server=s;Database=d;", "Server=s;Database=d;Pooling=false")]
    [InlineData(StruoDbType.Sqlite, "Data Source=a.db", "Data Source=a.db")]
    public void Lock_session_connection_string_disables_pooling_for_locking_backends(
        StruoDbType db, string input, string expected) =>
        MigrationLockSql.SessionConnectionString(db, input).Should().Be(expected);

    [Fact]
    public async Task Disposing_a_lock_twice_is_safe()
    {
        var gate = await MigrationLock.AcquireAsync(
            StruoDbType.Sqlite, "Data Source=:memory:", "t_", TimeSpan.FromSeconds(1), default);
        await gate.DisposeAsync();
        var act = async () => await gate.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}
