using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Struo.Tests.Support;

/// <summary>Serilog sink that keeps every event, for tests that read what the host logged.</summary>
public sealed class CapturingLogSink(Action<LogEvent>? onEmit = null) : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public void Emit(LogEvent logEvent)
    {
        _events.Enqueue(logEvent);
        onEmit?.Invoke(logEvent);
    }

    public IReadOnlyList<LogEvent> Events => [.. _events];

    /// <summary>
    /// The request-log event carrying <paramref name="traceId"/>, waiting briefly because the request
    /// logger writes after the response has been handed to the client.
    /// </summary>
    public async Task<LogEvent?> WaitForRequestLogAsync(string traceId)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = Events.FirstOrDefault(e =>
                e.MessageTemplate.Text.Contains("responded", StringComparison.Ordinal)
                && e.TraceId?.ToHexString() == traceId);
            if (hit is not null) return hit;
            await Task.Delay(50);
        }
        return null;
    }
}
