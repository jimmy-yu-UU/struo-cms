using FluentMigrator.Runner;
using Microsoft.Extensions.Logging;

namespace Struo.Infrastructure.Migrations;

/// <summary>
/// Hands out loggers that drop FluentMigrator's executed-SQL events and forward everything else. A seed
/// migration's INSERT statements carry the bootstrap administrator's password hash, which must not
/// reach the application's log sinks.
/// </summary>
internal sealed class SqlSuppressingLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new SqlSuppressingLogger(inner.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose()
    {
        // The wrapped factory belongs to the application.
    }

    private sealed class SqlSuppressingLogger(ILogger target) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => target.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => target.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == RunnerEventIds.Sql.Id) return;
            target.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
