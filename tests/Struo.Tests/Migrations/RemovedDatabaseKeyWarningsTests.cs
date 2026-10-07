using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Struo.Infrastructure.Migrations;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class RemovedDatabaseKeyWarningsTests
{
    private static ListLogger Run(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build();
        var logger = new ListLogger();
        RemovedDatabaseKeyWarnings.Log(config, logger);
        return logger;
    }

    [Fact]
    public void No_removed_key_logs_nothing() =>
        Run(("Database:MigrateOnStartup", "true")).Entries.Should().BeEmpty();

    [Fact]
    public void AutoSyncSchema_logs_one_warning_naming_the_key_and_migrations()
    {
        var entry = Run(("Database:AutoSyncSchema", "false")).Entries.Should().ContainSingle().Which;

        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("Database:AutoSyncSchema").And.Contain("migrations");
    }

    [Fact]
    public void MigrationsPath_logs_one_warning_naming_the_key_and_the_migrate_command()
    {
        var entry = Run(("Database:MigrationsPath", "db/migrations")).Entries.Should().ContainSingle().Which;

        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("Database:MigrationsPath").And.Contain("migrate");
    }

    [Fact]
    public void Both_keys_log_two_warnings() =>
        Run(("Database:AutoSyncSchema", "true"), ("Database:MigrationsPath", "x")).Entries.Should().HaveCount(2);
}
