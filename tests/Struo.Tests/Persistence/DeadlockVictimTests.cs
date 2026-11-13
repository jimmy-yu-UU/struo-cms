using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Struo.Api.Http;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Persistence;

public class DeadlockVictimTests
{
    private sealed class FakeDbException(string? sqlState) : DbException("boom")
    {
        public override string? SqlState { get; } = sqlState;
    }

    // MySqlException has no public constructor taking an error code; use the one that does.
    private static MySqlException MySql(int number)
    {
        var ctor = typeof(MySqlException)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(c => c.GetParameters() is [{ ParameterType: var t }, ..] && t == typeof(MySqlErrorCode));
        var args = ctor.GetParameters().Select((pi, i) => i == 0
            ? (object)(MySqlErrorCode)number
            : pi.ParameterType == typeof(string) ? "boom" : null).ToArray();
        return (MySqlException)ctor.Invoke(args);
    }

    // SqlException has no public constructor; build one whose first error carries the number.
    private static SqlException SqlServer(int number)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var error = (SqlError)RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
        typeof(SqlError).GetField("_number", all)!.SetValue(error, number);
        var errors = (SqlErrorCollection)RuntimeHelpers.GetUninitializedObject(typeof(SqlErrorCollection));
        typeof(SqlErrorCollection).GetField("_errors", all)!.SetValue(errors, new List<object> { error });
        var ex = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        typeof(SqlException).GetField("_errors", all)!.SetValue(ex, errors);
        return ex;
    }

    [Fact]
    public void Postgres_deadlock_sqlstate_matches_and_lock_timeout_does_not()
    {
        DeadlockVictim.IsMatch(new FakeDbException("40P01")).Should().BeTrue();
        DeadlockVictim.IsMatch(new FakeDbException("55P03")).Should().BeFalse();
        DeadlockVictim.IsMatch(new FakeDbException(null)).Should().BeFalse();
    }

    [Theory]
    [InlineData(1213, true)]
    [InlineData(1020, true)]
    [InlineData(1205, false)]
    [InlineData(1062, false)]
    public void MySql_and_MariaDb_match_on_the_error_number(int number, bool expected) =>
        DeadlockVictim.IsMatch(MySql(number)).Should().Be(expected);

    [Theory]
    [InlineData(1205, true)]
    [InlineData(1222, false)]
    [InlineData(2627, false)]
    public void SqlServer_matches_on_the_error_number(int number, bool expected) =>
        DeadlockVictim.IsMatch(SqlServer(number)).Should().Be(expected);

    [Fact]
    public void A_wrapped_provider_exception_matches_through_the_inner_chain() =>
        DeadlockVictim.IsMatch(new InvalidOperationException("outer", new Exception("mid", new FakeDbException("40P01"))))
            .Should().BeTrue();

    [Fact]
    public void Message_text_alone_never_matches() =>
        DeadlockVictim.IsMatch(new InvalidOperationException("deadlock detected")).Should().BeFalse();

    [Theory]
    [InlineData("pg")]
    [InlineData("mysql")]
    [InlineData("mariadb")]
    [InlineData("sqlserver")]
    public void Every_deadlock_answers_409_with_the_fixed_message(string backend)
    {
        Exception ex = backend switch
        {
            "pg" => new FakeDbException("40P01"),
            "mysql" => MySql(1213),
            "mariadb" => MySql(1020),
            _ => SqlServer(1205),
        };

        var (status, body) = StruoExceptionHandler.Map(new InvalidOperationException("wrapped", ex), true);

        status.Should().Be(409);
        body.Code.Should().Be(ErrorCodes.Conflict);
        body.Message.Should().Be(DomainErrorMap.DeadlockMessage);
    }

    [Fact]
    public void A_lock_timeout_stays_a_500()
    {
        StruoExceptionHandler.Map(new FakeDbException("55P03"), true).Status.Should().Be(500);
    }
}

