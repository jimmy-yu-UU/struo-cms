using Microsoft.Extensions.Logging;

namespace Struo.Tests.Support;

/// <summary>Minimal ILogger capturing (level, message) pairs for assertions.</summary>
public sealed class ListLogger : ILogger
{
    public readonly List<(LogLevel Level, string Message)> Entries = [];
    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
