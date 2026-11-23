using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// <c>migrate</c> says whether the run that applied the seed created the bootstrap admin, and the startup
/// gate logs the same statement. The statement never carries the password or its hash.
/// </summary>
[Collection("MigrationCli")] // OptionsOverride is static; keep these tests serial
public sealed class MigrateAdminReportTests : IDisposable
{
    private const string Password = "report-plain-pw-1";
    private const string AdminEmail = "report@example.com";
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "struo-content-root-probe");
    private readonly CoreMigrationHarness _h = new();

    public MigrateAdminReportTests() =>
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations.Core" };

    public void Dispose()
    {
        MigrationCli.OptionsOverride = o => o;
        _h.Dispose();
    }

    private ServiceProvider Services(string? email, string? password = Password, ILoggerFactory? loggers = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdmin:Email"] = email,
            ["Auth:BootstrapAdmin:Password"] = password,
        }).Build();
        return new ServiceCollection()
            .AddSingleton(Options.Create(_h.Database.Options(_h.Prefix)))
            .AddSingleton<IConfiguration>(config)
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { ContentRootPath = ContentRoot })
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()
            .AddSingleton(new ScannedAssemblies([typeof(StruoMigration).Assembly]))
            .AddSingleton(loggers ?? NullLoggerFactory.Instance)
            .BuildServiceProvider();
    }

    private static async Task<string> Migrate(IServiceProvider sp)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(["migrate"]), sp, o, e, default);
        code.Should().Be(MigrationCli.Success, e.ToString());
        return o.ToString();
    }

    [Fact]
    public async Task Migrate_names_the_admin_it_created()
    {
        using var sp = Services(AdminEmail);

        var output = await Migrate(sp);

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Last().Should().Contain(AdminEmail).And.Contain("created");
        lines.Should().Contain(l => l.StartsWith("Applied 202610080001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migrate_without_an_admin_email_names_the_key_the_content_root_and_the_switch()
    {
        using var sp = Services(email: null);

        var output = await Migrate(sp);

        var statement = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last();
        statement.Should().Contain("No bootstrap admin")
            .And.Contain("Auth:BootstrapAdmin:Email")
            .And.Contain(ContentRoot)
            .And.Contain("--contentRoot");
    }

    [Fact]
    public async Task A_second_migrate_prints_no_admin_statement()
    {
        using var sp = Services(AdminEmail);
        await Migrate(sp);

        var output = await Migrate(sp);

        output.Should().Contain("Nothing to migrate").And.NotContain("admin").And.NotContain(AdminEmail);
    }

    [Theory]
    [InlineData(AdminEmail)]
    [InlineData(null)]
    public async Task The_statement_carries_neither_the_password_nor_a_hash(string? email)
    {
        using var sp = Services(email);

        var output = await Migrate(sp);

        output.Should().NotContain(Password).And.NotContain("$argon2id");
    }

    private async Task<List<(LogLevel Level, string Message)>> StartupApply(string? email)
    {
        var capture = new ListLogger();
        using var sp = Services(email);
        var host = new MigrationHost(MigrationHostOptions.FromServices(sp, includeSeed: true) with
        {
            NamespaceFilter = "Struo.Infrastructure.Migrations.Core",
        }, NullLoggerFactory.Instance);
        var options = new DatabaseOptions
            { DbType = _h.Database.DbType, TablePrefix = _h.Prefix, MigrateOnStartup = true };

        await StartupMigrationGate.RunAsync(
            _h.Db(), host, options, isDevelopment: false, FrameworkEntityTypes.All, capture, default);
        return capture.Entries;
    }

    [Fact]
    public async Task Startup_logs_the_created_admin_as_information()
    {
        var entries = await StartupApply(AdminEmail);

        var entry = entries.Where(e => e.Message.Contains("bootstrap admin", StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain(AdminEmail).And.NotContain(Password).And.NotContain("$argon2id");
    }

    [Fact]
    public async Task Startup_without_an_admin_email_warns_with_the_key_and_the_content_root()
    {
        var entries = await StartupApply(email: null);

        var entry = entries.Where(e => e.Message.Contains("bootstrap admin", StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("Auth:BootstrapAdmin:Email").And.Contain(ContentRoot).And.Contain("--contentRoot");
    }

    [Fact]
    public async Task Startup_that_applies_no_seed_logs_no_admin_statement()
    {
        await StartupApply(AdminEmail);

        var again = await StartupApply(AdminEmail);

        again.Should().NotContain(e => e.Message.Contains("bootstrap admin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unknown_content_root_leaves_the_directory_out_and_a_run_without_seed_reports_nothing()
    {
        var seed = CoreMigrationHarness.Seed(email: null);
        var seeded = new[] { new MigrationInfo(Struo.Infrastructure.Migrations.Core.SeedCoreData.Version, "SeedCoreData", MigrationState.Applied, null, false) };

        var report = BootstrapAdminReport.For(seed, seeded);

        report!.Message.Should().Contain("--contentRoot").And.NotContain("were read from");
        BootstrapAdminReport.For(null, seeded).Should().BeNull();
        BootstrapAdminReport.For(seed, []).Should().BeNull();
    }
}
