using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Struo.Api.Hosting;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerilogConsoleCollection
{
    public const string Name = "SerilogConsole";
}

/// <summary>
/// The Console and File sinks are added in code from <c>Struo:Logging</c>: the Console prints the text
/// template or compact JSON, the File sink can be turned off, and a sink a deployer adds under
/// <c>Serilog:WriteTo</c> still loads on top. Each test builds the logger in-process into a temporary
/// directory and reads what it wrote.
/// </summary>
[Collection(SerilogConsoleCollection.Name)]
public sealed partial class StruoCmsLoggingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "struo-logging-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    // Builds the logger with the Console redirected, logs one line, and returns what the Console printed.
    private static string LogOnce(IConfiguration configuration, string logDirectory, string message)
    {
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            using var provider = new ServiceCollection().BuildServiceProvider();
            using var logger = StruoCmsLogging.Configure(new LoggerConfiguration(), configuration, provider, logDirectory)
                .CreateLogger();
            logger.Information(message);
        }
        finally
        {
            Console.SetOut(original);
        }
        return captured.ToString();
    }

    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2} INF\] .*hello-text.* <s:", RegexOptions.Multiline)]
    private static partial Regex TextLine();

    [Fact]
    public void By_default_the_Console_prints_the_text_template_and_a_file_sink_writes_under_the_log_directory()
    {
        var logs = Path.Combine(_root, "logs");

        var console = LogOnce(Config(), logs, "hello-text");

        console.Should().MatchRegex(TextLine());
        var file = Directory.GetFiles(logs).Should().ContainSingle().Subject;
        Path.GetFileName(file).Should().MatchRegex(@"^struo-\d{8}\.log$");
        File.ReadAllText(file).Should().Contain("hello-text").And.MatchRegex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[INF\]");
    }

    [Fact]
    public void Json_with_the_file_off_prints_compact_JSON_and_creates_no_log_directory()
    {
        var logs = Path.Combine(_root, "logs");

        var console = LogOnce(
            Config(("Struo:Logging:ConsoleFormat", "Json"), ("Struo:Logging:FileEnabled", "false")), logs, "hello-json");

        using var doc = JsonDocument.Parse(console.Trim());
        doc.RootElement.GetProperty("@m").GetString().Should().Be("hello-json");
        doc.RootElement.TryGetProperty("@t", out _).Should().BeTrue();
        Directory.Exists(logs).Should().BeFalse();
    }

    [Fact]
    public void The_format_is_matched_ignoring_case()
    {
        var console = LogOnce(Config(("Struo:Logging:ConsoleFormat", "json"), ("Struo:Logging:FileEnabled", "false")),
            Path.Combine(_root, "logs"), "hello-json");

        JsonDocument.Parse(console.Trim()).RootElement.GetProperty("@m").GetString().Should().Be("hello-json");
    }

    [Fact]
    public void A_sink_added_under_Serilog_WriteTo_still_loads_beside_the_built_in_ones()
    {
        var extra = Path.Combine(_root, "extra", "extra-.log");

        LogOnce(Config(
                ("Struo:Logging:FileEnabled", "false"),
                ("Serilog:WriteTo:0:Name", "File"),
                ("Serilog:WriteTo:0:Args:path", extra)),
            Path.Combine(_root, "logs"), "hello-extra");

        var file = Directory.GetFiles(Path.GetDirectoryName(extra)!, "extra-*.log").Should().ContainSingle().Subject;
        File.ReadAllText(file).Should().Contain("hello-extra");
        Directory.Exists(Path.Combine(_root, "logs")).Should().BeFalse();
    }

    [Fact]
    public void An_unknown_ConsoleFormat_fails_instead_of_falling_back()
    {
        var act = () => LogOnce(Config(("Struo:Logging:ConsoleFormat", "Xml")), Path.Combine(_root, "logs"), "x");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("ConsoleFormat");
    }

    [Fact]
    public void With_both_built_in_sinks_off_nothing_is_written_by_them_and_an_extra_sink_still_loads()
    {
        var extra = Path.Combine(_root, "extra", "extra-.log");

        var console = LogOnce(Config(
                ("Struo:Logging:ConsoleEnabled", "false"),
                ("Struo:Logging:FileEnabled", "false"),
                ("Serilog:WriteTo:0:Name", "File"),
                ("Serilog:WriteTo:0:Args:path", extra)),
            Path.Combine(_root, "logs"), "hello-only-extra");

        console.Should().BeEmpty();
        Directory.Exists(Path.Combine(_root, "logs")).Should().BeFalse();
        File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(extra)!, "extra-*.log").Should().ContainSingle().Subject)
            .Should().Contain("hello-only-extra");
    }

    [Fact]
    public void With_only_the_console_off_the_file_still_writes()
    {
        var logs = Path.Combine(_root, "logs");

        var console = LogOnce(Config(("Struo:Logging:ConsoleEnabled", "false")), logs, "hello-file-only");

        console.Should().BeEmpty();
        File.ReadAllText(Directory.GetFiles(logs).Should().ContainSingle().Subject).Should().Contain("hello-file-only");
    }

    [Theory]
    [InlineData("5")]
    [InlineData("-1")]
    public void An_undefined_numeric_ConsoleFormat_fails_like_an_unknown_name(string value)
    {
        var act = () => LogOnce(Config(("Struo:Logging:ConsoleFormat", value)), Path.Combine(_root, "logs"), "x");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("ConsoleFormat");
    }

    [Fact]
    public void A_leftover_override_of_the_old_second_entry_without_a_Name_fails_startup()
    {
        var act = () => LogOnce(Config(("Serilog:WriteTo:1:Args:path", "x/y-.log")), Path.Combine(_root, "logs"), "x");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Serilog:WriteTo:1:Name");
    }

    [Fact]
    public void Json_carries_the_trace_id_as_the_at_tr_property()
    {
        using var activity = new System.Diagnostics.Activity("request").Start();

        var console = LogOnce(Config(("Struo:Logging:ConsoleFormat", "Json"), ("Struo:Logging:FileEnabled", "false")),
            Path.Combine(_root, "logs"), "hello-trace");

        JsonDocument.Parse(console.Trim()).RootElement.GetProperty("@tr").GetString()
            .Should().Be(activity.TraceId.ToHexString());
    }

    [Fact]
    public void The_file_sink_is_shared_so_two_processes_can_write_the_same_day_file()
    {
        var logs = Path.Combine(_root, "logs");
        using var provider = new ServiceCollection().BuildServiceProvider();
        var config = Config(("Struo:Logging:ConsoleEnabled", "false"));

        using var first = StruoCmsLogging.Configure(new LoggerConfiguration(), config, provider, logs).CreateLogger();
        using var second = StruoCmsLogging.Configure(new LoggerConfiguration(), config, provider, logs).CreateLogger();
        first.Information("from-first");
        second.Information("from-second");
        (first as IDisposable)!.Dispose();
        (second as IDisposable)!.Dispose();

        var text = File.ReadAllText(Directory.GetFiles(logs).Should().ContainSingle().Subject);
        text.Should().Contain("from-first").And.Contain("from-second");
    }

    // The text and file templates are documented output; a change to either is a change to every log line.
    [Fact]
    public void The_template_constants_are_the_documented_text_and_file_templates()
    {
        StruoCmsLogging.ConsoleOutputTemplate.Should().Be(
            "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {Message:lj} <s:{SourceContext}>{NewLine}{Exception}");
        StruoCmsLogging.FileOutputTemplate.Should().Be(
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {TraceId} {Message:lj}{NewLine}{Exception}");
    }

    [Fact]
    public void The_shipped_Serilog_section_holds_the_minimum_level_and_no_sinks()
    {
        var path = Path.Combine(RepoRoot.Find(), "src", "Struo.Api", "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var serilog = doc.RootElement.GetProperty("Serilog");

        serilog.TryGetProperty("WriteTo", out _).Should().BeFalse();
        serilog.GetProperty("MinimumLevel").GetProperty("Default").GetString().Should().Be("Information");
    }

    [Fact]
    public void The_shipped_Struo_Logging_defaults_are_Text_with_both_sinks_on()
    {
        var path = Path.Combine(RepoRoot.Find(), "src", "Struo.Api", "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var logging = doc.RootElement.GetProperty("Struo").GetProperty("Logging");

        logging.GetProperty("ConsoleFormat").GetString().Should().Be("Text");
        logging.GetProperty("FileEnabled").GetBoolean().Should().BeTrue();
        logging.GetProperty("ConsoleEnabled").GetBoolean().Should().BeTrue();
    }
}
