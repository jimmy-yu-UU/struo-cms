using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Migrations;
using Xunit;

namespace Struo.Tests.Migrations.Core;

public sealed class CoreSeedLoggingTests : IDisposable
{
    private const string Password = "s3cret-pw";

    private readonly CoreMigrationHarness _h = new();

    public void Dispose() => _h.Dispose();

    private sealed class CapturingProvider : ILoggerProvider, ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Applying_the_seed_logs_the_progress_but_neither_the_password_nor_its_hash()
    {
        var hasher = new Argon2idPasswordHasher();
        var capture = new CapturingProvider();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        var host = new MigrationHost(_h.HostOptions(CoreMigrationHarness.Seed(hasher: hasher)), factory);

        await host.ApplyAsync(default);

        var hash = _h.Db().Queryable<User>().Single().Password;
        var logged = string.Join("\n", capture.Entries.Select(e => e.Message));
        logged.Should().NotContain(hash).And.NotContain(Password);
        logged.Should().Contain("SeedCoreData");
    }
}
