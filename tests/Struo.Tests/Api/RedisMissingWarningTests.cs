using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Production without <c>Redis:ConnectionString</c> keeps sessions and the login throttle in process
/// memory; startup logs one warning saying so. Other environments, or a configured Redis, stay quiet.
/// </summary>
[Collection("ApiIntegration")]
public class RedisMissingWarningTests(ApiFactory factory)
{
    private const string WarningFragment = "Redis:ConnectionString";

    private List<(LogLevel Level, string Message)> StartHost(string environment, string redis)
    {
        var sink = new ListSink();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Redis:ConnectionString"] = redis }));
            b.ConfigureServices(s => s.AddSingleton<ILogEventSink>(sink));
        });
        _ = host.Services;
        return sink.Entries.ToList();
    }

    [Fact]
    public void Production_without_Redis_logs_one_warning()
    {
        var entries = StartHost("Production", "");

        entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains(WarningFragment))
            .Should().ContainSingle();
    }

    [Fact]
    public void Development_without_Redis_does_not_warn()
    {
        var entries = StartHost("Development", "");

        entries.Should().NotContain(e => e.Message.Contains(WarningFragment));
    }

    [Fact]
    public void Production_with_Redis_does_not_warn()
    {
        var entries = StartHost("Production", "localhost:6379,abortConnect=false");

        entries.Should().NotContain(e => e.Message.Contains(WarningFragment));
    }

    // The host logs through Serilog, which reads sinks registered in DI.
    private sealed class ListSink : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel, string)> _entries = new();

        public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries;

        public void Emit(LogEvent logEvent) =>
            _entries.Enqueue((ToLevel(logEvent.Level), logEvent.RenderMessage()));

        private static LogLevel ToLevel(LogEventLevel level) => level switch
        {
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => LogLevel.Information,
        };
    }
}
