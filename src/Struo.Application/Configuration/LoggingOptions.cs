namespace Struo.Application.Configuration;

/// <summary>How the Console sink formats an event.</summary>
public enum ConsoleLogFormat
{
    /// <summary>One human-readable line per event.</summary>
    Text,

    /// <summary>One compact JSON object per event (<c>RenderedCompactJsonFormatter</c>).</summary>
    Json,
}

/// <summary>
/// The two built-in Serilog sinks, added in code from <c>Struo:Logging</c>. A sink a deployer adds under
/// <c>Serilog:WriteTo</c> loads on top of these.
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "Struo:Logging";

    /// <summary>
    /// Whether the Console sink is added. Default <c>true</c>; a fork that turns both built-in sinks off
    /// keeps its own under <c>Serilog:WriteTo</c>.
    /// </summary>
    public bool ConsoleEnabled { get; set; } = true;

    /// <summary><c>Text</c> (the default) or <c>Json</c>; matched ignoring case, any other value, an undefined number such as <c>5</c> included, fails startup.</summary>
    public ConsoleLogFormat ConsoleFormat { get; set; } = ConsoleLogFormat.Text;

    /// <summary>
    /// Whether the rolling file sink (<c>logs/struo-.log</c>, one file per day, shared between processes)
    /// is added. Default <c>true</c>.
    /// </summary>
    public bool FileEnabled { get; set; } = true;
}
