// tests/Struo.Tests/Migrations/OutOfOrderMigrationTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

/// <summary>
/// A pending migration older than the highest applied one (a branch that merged late) is applied anyway,
/// but flagged: a warning from <c>migrate</c> and the startup gate, and an "out of order" mark in
/// <c>migrate:status</c>. The probe set <c>Upgrade.V1</c> holds versions 1 and 3; <c>Upgrade</c> adds 2.
/// </summary>
[Collection("MigrationCli")] // OptionsOverride is static; keep these tests serial
public sealed partial class OutOfOrderMigrationTests : IDisposable
{
    private const string Probes = "Struo.Tests.Migrations.Probes";
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly CapturingLoggerFactory _logs = new();

    public void Dispose()
    {
        MigrationCli.OptionsOverride = o => o;
        _database.Dispose();
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public ListLogger Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    private MigrationHost Host(string ns) => new(
        new MigrationHostOptions(_database.DbType, _database.ConnectionString, "struo_",
            [typeof(OutOfOrderMigrationTests).Assembly], LockTimeoutSeconds: 5) { NamespaceFilter = $"{Probes}.{ns}" },
        _logs);

    private IEnumerable<string> Warnings() =>
        _logs.Logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddSingleton(_ => Options.Create(new DatabaseOptions
                { DbType = _database.DbType, ConnectionString = _database.ConnectionString }))
            .AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton<Struo.Application.Security.IPasswordHasher, Struo.Infrastructure.Identity.Argon2idPasswordHasher>()
            .AddSingleton(new ScannedAssemblies([typeof(OutOfOrderMigrationTests).Assembly]))
            .AddSingleton<ILoggerFactory>(_logs)
            .BuildServiceProvider();

    private static async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(args), sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task Status_flags_a_pending_version_below_the_highest_applied_one()
    {
        await Host("Upgrade.V1").ApplyAsync(default);

        var status = Host("Upgrade").GetStatus();

        status.Single(m => m.Version == 2).OutOfOrder.Should().BeTrue();
        status.Where(m => m.Version != 2).Should().OnlyContain(m => !m.OutOfOrder);
    }

    [Fact]
    public async Task A_pending_version_above_every_applied_one_is_not_flagged()
    {
        await Host("Upgrade.V1").ApplyAsync(default);
        // Upgrade.V1 holds 1 and 3 as applied; the Failing set (10-12) is all newer.
        var status = Host("Failing").GetStatus();

        status.Where(m => m.State == MigrationState.Pending).Should().OnlyContain(m => !m.OutOfOrder);
    }

    [Fact]
    public async Task A_newer_orphaned_record_does_not_make_an_older_pending_migration_out_of_order()
    {
        await Host("Upgrade.V1").ApplyAsync(default);

        // Upgrade.V2 knows only version 2: versions 1 and 3 are now recorded but absent from the code.
        var status = Host("Upgrade.V2").GetStatus();
        status.Single(m => m.Version == 2).OutOfOrder.Should().BeFalse();
        await Host("Upgrade.V2").ApplyAsync(default);

        Warnings().Should().NotContain(m => m.Contains("out of order"));
    }

    [Fact]
    public void The_warning_names_the_highest_applied_migration_in_code_never_an_orphan()
    {
        var status = new List<MigrationInfo>
        {
            new(1, "one", MigrationState.Applied, DateTime.UtcNow),
            new(2, "two", MigrationState.Pending, null, OutOfOrder: true),
            new(3, "three", MigrationState.Applied, DateTime.UtcNow),
            new(99, "gone", MigrationState.Orphaned, DateTime.UtcNow),
        };

        var warning = MigrationInfo.OutOfOrderWarning(status);

        warning.Should().Contain("migration 3.").And.NotContain("99");
    }

    [Fact]
    public async Task A_fresh_database_applies_in_order_without_a_warning()
    {
        await Host("Upgrade").ApplyAsync(default);

        Warnings().Should().NotContain(m => m.Contains("out of order"));
    }

    [Fact]
    public async Task Apply_warns_naming_the_version_and_still_applies_it()
    {
        await Host("Upgrade.V1").ApplyAsync(default);

        var applied = await Host("Upgrade").ApplyAsync(default);

        applied.Select(m => m.Version).Should().Equal(2);
        Warnings().Should().ContainSingle().Which.Should()
            .StartWith("Migration(s) 2 are out of order: older than the highest applied migration 3.");
    }

    [Fact]
    public async Task The_migrate_command_warns_and_exits_zero()
    {
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = $"{Probes}.Upgrade.V1" };
        using var sp = Services();
        (await Run(sp, "migrate")).Code.Should().Be(0);
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = $"{Probes}.Upgrade" };

        var (code, output, _) = await Run(sp, "migrate");

        code.Should().Be(0);
        output.Should().Contain("Applied 2");
        Warnings().Should().ContainSingle(m => m.Contains("out of order"));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^2\s+(Pending|Applied)")]
    private static partial System.Text.RegularExpressions.Regex RowTwoPattern();

    private static bool IsRowTwo(string line) => RowTwoPattern().IsMatch(line);

    [Fact]
    public async Task Migrate_status_marks_the_row_and_keeps_the_other_lines()
    {
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = $"{Probes}.Upgrade.V1" };
        using var sp = Services();
        await Run(sp, "migrate");
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = $"{Probes}.Upgrade" };

        var (code, output, _) = await Run(sp, "migrate:status");

        code.Should().Be(0);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        lines.Single(IsRowTwo).Should().Contain("Pending").And.Contain("out of order");
        lines.Where(l => !IsRowTwo(l)).Should().NotContain(l => l.Contains("out of order"));
        lines[^1].Should().Be("2 applied, 1 pending, 0 orphaned");
    }

    [Fact]
    public async Task The_startup_gate_warns_when_it_applies_and_when_it_refuses()
    {
        await Host("Upgrade.V1").ApplyAsync(default);
        DatabaseOptions Options(bool migrate) =>
            new() { DbType = _database.DbType, TablePrefix = "struo_", MigrateOnStartup = migrate };
        using var db = new SqlSugarClient(_database.RawConfig());

        await StartupMigrationGate.RunAsync(
            db, Host("Upgrade"), Options(true), false, FrameworkEntityTypes.All, _logs.Logger, default);
        Warnings().Should().ContainSingle(m => m.Contains("out of order"));

        // Roll the set back to "2 pending, 3 applied" by recording a lower pending version again.
        db.Deleteable<SchemaVersionRow>().AS(StruoVersionTableMetaData.TableNameFor("struo_")).Where(r => r.Version == 2).ExecuteCommand();
        _logs.Logger.Entries.Clear();

        var refuse = () => StartupMigrationGate.RunAsync(
            db, Host("Upgrade"), Options(false), false, FrameworkEntityTypes.All, _logs.Logger, default);

        (await refuse.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("out of order");
        Warnings().Should().ContainSingle(m => m.Contains("out of order"));
    }
}
