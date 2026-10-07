using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Struo.Infrastructure.Migrations;

/// <summary>Warns about <c>Database</c> keys the application ignores, so a configuration carried over from
/// an earlier version does not expect them to work.</summary>
public static class RemovedDatabaseKeyWarnings
{
    private static readonly (string Key, string Replacement)[] Removed =
    [
        ("Database:AutoSyncSchema", "the schema comes from migrations; set Database:MigrateOnStartup or run the 'migrate' command"),
        ("Database:MigrationsPath", "migrations are code in the scanned assemblies and are applied with the 'migrate' command"),
    ];

    public static void Log(IConfiguration configuration, ILogger logger)
    {
        foreach (var (key, replacement) in Removed)
            if (configuration.GetSection(key).Exists())
                logger.LogWarning("The configuration key {Key} is not used: {Replacement}.", key, replacement);
    }
}
