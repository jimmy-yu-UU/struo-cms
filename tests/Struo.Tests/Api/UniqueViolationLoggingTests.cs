using AwesomeAssertions;
using HotChocolate;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Struo.Api.GraphQl;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

// A unique violation answers with a fixed 409, so the server log is the only place the constraint name
// survives: both protocols log a Warning with the exception and keep it out of the response.
public class UniqueViolationLoggingTests
{
    private const string Constraint = "UNIQUE constraint failed: roles.name (ux_roles_name)";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }

    private static InvalidOperationException UniqueFailure() =>
        new InvalidOperationException(Constraint, new SqliteException(Constraint, 19, 2067));

    [Fact]
    public void Rest_handler_logs_a_warning_with_the_exception_and_hides_the_constraint()
    {
        var logger = new CapturingLogger<StruoExceptionHandler>();
        var failure = UniqueFailure();

        var (status, body) = StruoExceptionHandler.Map(failure, authenticated: true, logger);

        status.Should().Be(StatusCodes.Status409Conflict);
        body.Message.Should().Be(DomainErrorMap.UniqueViolationMessage).And.NotContain("ux_roles_name");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == failure);
    }

    [Fact]
    public void GraphQl_filter_logs_a_warning_and_detaches_the_exception()
    {
        var logger = new CapturingLogger<StruoErrorFilter>();
        var filter = new StruoErrorFilter(logger, new HttpContextAccessor { HttpContext = null });
        var failure = UniqueFailure();
        var error = ErrorBuilder.New().SetMessage("original").SetException(failure).Build();

        var mapped = filter.OnError(error);

        mapped.Code.Should().Be("CONFLICT");
        mapped.Message.Should().Be(DomainErrorMap.UniqueViolationMessage).And.NotContain("ux_roles_name");
        mapped.Exception.Should().BeNull();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == failure);
    }
}
