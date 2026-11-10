using System.Diagnostics;
using AwesomeAssertions;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Struo.Api.Hosting;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>The built-in Serilog sinks print the trace id, so an id quoted from an error response can be searched for.</summary>
public class SerilogTraceIdTemplateTests
{
    private static IEnumerable<(string Sink, string Template)> BuiltInTemplates() =>
    [
        ("Console", StruoCmsLogging.ConsoleOutputTemplate),
        ("File", StruoCmsLogging.FileOutputTemplate),
    ];

    [Fact]
    public void Every_built_in_sink_template_renders_the_trace_id_of_the_current_activity()
    {
        using var activity = new Activity("request").Start();
        LogEvent? captured = null;
        using var logger = new LoggerConfiguration().WriteTo.Sink(new CapturingLogSink(e => captured = e)).CreateLogger();
        logger.Information("hello");

        foreach (var (sink, template) in BuiltInTemplates())
        {
            using var writer = new StringWriter();
            new MessageTemplateTextFormatter(template).Format(captured!, writer);
            writer.ToString().Should().Contain(activity.TraceId.ToHexString(), $"the {sink} sink template must print it");
        }
    }
}
