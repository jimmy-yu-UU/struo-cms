using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Compact;
using Struo.Application.Configuration;

namespace Struo.Api.Hosting;

/// <summary>
/// Builds the host's Serilog configuration: levels and any extra sink from <c>Serilog:*</c>, the
/// Console and File sinks from <c>Struo:Logging</c> (<see cref="LoggingOptions"/>).
/// </summary>
internal static class StruoCmsLogging
{
    internal const string ConsoleOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {Message:lj} <s:{SourceContext}>{NewLine}{Exception}";

    internal const string FileOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {TraceId} {Message:lj}{NewLine}{Exception}";

    internal const string DefaultLogDirectory = "logs";

    private const string FileName = "struo-.log";

    /// <summary>
    /// Applies <c>Serilog:*</c> and the services' enrichers first, so a sink under <c>Serilog:WriteTo</c>
    /// loads, then adds the Console and File sinks the options ask for. <paramref name="logDirectory"/> is
    /// relative to the working directory unless rooted.
    /// </summary>
    internal static LoggerConfiguration Configure(
        LoggerConfiguration logger,
        IConfiguration configuration,
        IServiceProvider services,
        string logDirectory = DefaultLogDirectory)
    {
        var options = configuration.GetSection(LoggingOptions.SectionName).Get<LoggingOptions>() ?? new LoggingOptions();

        logger.ReadFrom.Configuration(configuration).ReadFrom.Services(services);

        if (options.ConsoleFormat == ConsoleLogFormat.Json)
            logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        else
            logger.WriteTo.Console(outputTemplate: ConsoleOutputTemplate);

        if (options.FileEnabled)
        {
            logger.WriteTo.File(
                System.IO.Path.Combine(logDirectory, FileName),
                rollingInterval: RollingInterval.Day,
                shared: true,
                outputTemplate: FileOutputTemplate);
        }

        return logger;
    }
}
