using System.Data.Common;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class UniqueViolationTests
{
    private sealed class FakeDbException(string? sqlState) : DbException("boom")
    {
        public override string? SqlState { get; } = sqlState;
    }

    // SQLite extended result codes.
    private const int Unique = 2067, PrimaryKey = 1555, NotNull = 1299, ForeignKey = 787;

    private static Exception Capture(Action act)
    {
        try { act(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("The insert did not fail.");
    }

    private static (ITestDatabase, ISqlSugarClient) NewClient()
    {
        var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<Role>();
        return (db, client);
    }

    // The backend under test (STRUO_TEST_BACKEND) raises a real duplicate on roles.name.
    [Fact]
    public void A_duplicate_on_a_core_unique_index_matches_on_the_live_backend()
    {
        var (db, client) = NewClient();
        using (db)
        {
            var name = $"dup-{Guid.NewGuid():N}";
            client.Insertable(new Role { Id = Guid.NewGuid(), Name = name }).ExecuteCommand();

            var ex = Capture(() =>
                client.Insertable(new Role { Id = Guid.NewGuid(), Name = name }).ExecuteCommand());

            UniqueViolation.IsMatch(ex).Should().BeTrue(ex.ToString());
        }
    }

    [Fact]
    public void A_duplicate_primary_key_matches_on_the_live_backend()
    {
        var (db, client) = NewClient();
        using (db)
        {
            var id = Guid.NewGuid();
            client.Insertable(new Role { Id = id, Name = $"a-{id:N}" }).ExecuteCommand();

            var ex = Capture(() =>
                client.Insertable(new Role { Id = id, Name = $"b-{id:N}" }).ExecuteCommand());

            UniqueViolation.IsMatch(ex).Should().BeTrue(ex.ToString());
        }
    }

    [Fact]
    public void A_null_in_a_required_column_does_not_match_on_the_live_backend()
    {
        var (db, client) = NewClient();
        using (db)
        {
            var ex = Capture(() =>
                client.Insertable(new Role { Id = Guid.NewGuid(), Name = null! }).ExecuteCommand());

            UniqueViolation.IsMatch(ex).Should().BeFalse(ex.ToString());
        }
    }

    [Theory]
    [InlineData(Unique, true)]
    [InlineData(PrimaryKey, true)]
    [InlineData(NotNull, false)]
    [InlineData(ForeignKey, false)]
    public void Sqlite_matches_on_the_extended_result_code(int extended, bool expected)
    {
        var ex = new SqliteException("constraint failed", 19, extended);

        UniqueViolation.IsMatch(ex).Should().Be(expected);
    }

    [Fact]
    public void A_wrapped_provider_exception_matches_through_the_inner_chain()
    {
        var inner = new SqliteException("constraint failed", 19, Unique);

        UniqueViolation.IsMatch(new InvalidOperationException("outer", new Exception("mid", inner)))
            .Should().BeTrue();
    }

    [Fact]
    public void Postgres_unique_violation_sqlstate_matches_and_other_states_do_not()
    {
        UniqueViolation.IsMatch(new FakeDbException("23505")).Should().BeTrue();
        UniqueViolation.IsMatch(new FakeDbException("23503")).Should().BeFalse();
        UniqueViolation.IsMatch(new FakeDbException(null)).Should().BeFalse();
    }

    [Fact]
    public void Message_text_alone_never_matches()
    {
        UniqueViolation.IsMatch(new InvalidOperationException("duplicate key value violates unique constraint"))
            .Should().BeFalse();
    }
}
