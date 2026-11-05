using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Core;

/// <summary>
/// The seed logs one warning when it creates the bootstrap admin with the default email in Production,
/// and none otherwise. The warning never carries the password.
/// </summary>
[Collection("MigrationCli")] // the CLI tests set a static options override; keep these serial with them
public sealed class DefaultAdminEmailSeedWarningTests : IDisposable
{
    private const string Password = "configured-pw-123";
    private readonly CoreMigrationHarness _h = new();

    public DefaultAdminEmailSeedWarningTests() =>
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations.Core" };

    public void Dispose()
    {
        MigrationCli.OptionsOverride = o => o;
        _h.Dispose();
    }

    private sealed class CapturingProvider(ListLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }

    private async Task<List<(LogLevel Level, string Message)>> Migrate(string environment, string email)
    {
        var capture = new ListLogger();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingProvider(capture)));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdmin:Email"] = email,
            ["Auth:BootstrapAdmin:Password"] = Password,
        }).Build();
        using var sp = new ServiceCollection()
            .AddSingleton(Options.Create(_h.Database.Options(_h.Prefix)))
            .AddSingleton<IConfiguration>(config)
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment })
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton(Options.Create(new PasswordPolicyOptions()))
            .AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()
            .AddSingleton(new ScannedAssemblies([typeof(StruoMigration).Assembly]))
            .AddSingleton(factory)
            .BuildServiceProvider();
        var code = await MigrationCli.RunAsync(
            MigrationCli.Parse(["migrate"]), sp, new StringWriter(), new StringWriter(), default);
        code.Should().Be(MigrationCli.Success);
        return capture.Entries;
    }

    [Fact]
    public async Task Production_with_the_default_email_logs_one_warning_without_the_password()
    {
        var entries = await Migrate("Production", CoreSeedData.DefaultAdminEmail);

        var warning = entries.Where(e => e.Level == LogLevel.Warning).Should().ContainSingle().Subject;
        warning.Message.Should().Contain(CoreSeedData.DefaultAdminEmail).And.NotContain(Password);
    }

    [Fact]
    public async Task The_default_email_is_matched_after_normalization()
    {
        var entries = await Migrate("Production", "  Admin@Admin.COM ");

        entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Production_with_another_email_logs_no_warning()
    {
        var entries = await Migrate("Production", "owner@example.com");

        entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task Other_environments_log_no_warning(string environment)
    {
        var entries = await Migrate(environment, CoreSeedData.DefaultAdminEmail);

        entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }
}
