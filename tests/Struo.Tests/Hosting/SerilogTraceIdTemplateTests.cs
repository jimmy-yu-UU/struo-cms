using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>The shipped Serilog sinks print the trace id, so an id quoted from an error response can be searched for.</summary>
public class SerilogTraceIdTemplateTests
{
    private static IEnumerable<(string Sink, string Template)> ShippedTemplates()
    {
        var path = Path.Combine(RepoRoot.Find(), "src", "Struo.Api", "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var sink in doc.RootElement.GetProperty("Serilog").GetProperty("WriteTo").EnumerateArray())
            yield return (
                sink.GetProperty("Name").GetString()!,
                sink.GetProperty("Args").GetProperty("outputTemplate").GetString()!);
    }

    [Fact]
    public void Every_shipped_sink_template_renders_the_trace_id_of_the_current_activity()
    {
        using var activity = new Activity("request").Start();
        LogEvent? captured = null;
        using var logger = new LoggerConfiguration().WriteTo.Sink(new CapturingLogSink(e => captured = e)).CreateLogger();
        logger.Information("hello");

        var templates = ShippedTemplates().ToList();
        templates.Should().NotBeEmpty();
        foreach (var (sink, template) in templates)
        {
            var writer = new StringWriter();
            new MessageTemplateTextFormatter(template).Format(captured!, writer);
            writer.ToString().Should().Contain(activity.TraceId.ToHexString(), $"the {sink} sink template must print it");
        }
    }
}
