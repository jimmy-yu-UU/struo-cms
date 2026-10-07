using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

[Collection("MigrationCli")] // OptionsOverride is static; keep these tests serial
public sealed class MigrationCliTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public MigrationCliTests() =>
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Tests.Migrations.Probes.Upgrade.V1" };

    public void Dispose()
    {
        MigrationCli.OptionsOverride = o => o;
        _file.Dispose();
    }

    private ServiceProvider Services(StruoDbType db = StruoDbType.Sqlite, string? conn = null) =>
        new ServiceCollection()
            .AddSingleton(Options.Create(new DatabaseOptions
                { DbType = db, ConnectionString = conn ?? _file.ConnectionString }))
            .AddSingleton(new ScannedAssemblies([typeof(MigrationCliTests).Assembly]))
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

    private static async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(args), sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Parse_splits_command_from_host_args()
    {
        var inv = MigrationCli.Parse(["migrate", "--Database:TablePrefix=acme_"]);
        inv.Command.Should().Be("migrate");
        inv.HostArgs.Should().Equal("--Database:TablePrefix=acme_");

        var web = MigrationCli.Parse(["--urls=http://localhost:5221"]);
        web.Command.Should().BeNull();
        web.HostArgs.Should().Equal("--urls=http://localhost:5221");
    }

    [Fact]
    public async Task Unknown_migrate_subcommand_is_a_usage_error()
    {
        var (code, _, err) = await Run(Services(), "migrate:nope");
        code.Should().Be(MigrationCli.Usage);
        err.Should().Contain("migrate:status");
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("migrate:status")]
    [InlineData("migrate:preview")]
    public async Task Command_arguments_on_a_migrate_command_are_a_usage_error(string command)
    {
        var (code, _, err) = await Run(Services(), command, "stray");
        code.Should().Be(MigrationCli.Usage);
        err.Should().Contain($"Unexpected argument 'stray' for {command}.");
    }

    [Fact]
    public async Task Status_then_migrate_then_status()
    {
        var sp = Services();

        var s1 = await Run(sp, "migrate:status");
        s1.Code.Should().Be(MigrationCli.Success);
        s1.Out.Should().Contain("Pending").And.Contain("0 applied, 2 pending");

        var m = await Run(sp, "migrate");
        m.Code.Should().Be(MigrationCli.Success);
        m.Out.Should().Contain("Applied 1").And.Contain("Applied 3");

        var again = await Run(sp, "migrate");
        again.Out.Should().Contain("Nothing to migrate");

        (await Run(sp, "migrate:status")).Out.Should().Contain("2 applied, 0 pending");
    }

    [Fact]
    public async Task Preview_prints_sql()
    {
        var (code, output, _) = await Run(Services(), "migrate:preview");
        code.Should().Be(MigrationCli.Success);
        output.Should().Contain("CREATE TABLE");
    }

    [Theory]
    [InlineData("migrate:status", "0 applied, 0 pending, 0 orphaned")]
    [InlineData("migrate", "Nothing to migrate")]
    [InlineData("migrate:preview", "0 migration(s) pending")]
    public async Task Assemblies_without_migrations_report_nothing_to_do(string command, string expected)
    {
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Tests.Migrations.NoSuchNamespace" };

        var (code, output, err) = await Run(Services(), command);

        err.Should().BeEmpty();
        code.Should().Be(MigrationCli.Success);
        output.Should().Contain(expected);
    }

    [Fact]
    public async Task Unreachable_database_fails_with_one_readable_line()
    {
        var sp = Services(StruoDbType.PostgreSQL,
            "Host=127.0.0.1;Port=1;Database=x;Username=REPLACE_ME;Password=REPLACE_ME;Timeout=2");

        var (code, _, err) = await Run(sp, "migrate");

        code.Should().Be(MigrationCli.Failure);
        err.Should().StartWith("migrate failed:");
        err.Should().NotContain("   at ");
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("migrate:status")]
    [InlineData("migrate:preview")]
    public async Task Invalid_database_options_fail_with_one_readable_line(string command)
    {
        var sp = new ServiceCollection()
            .AddOptions<DatabaseOptions>()
            .Configure(o =>
            {
                o.DbType = StruoDbType.Sqlite;
                o.ConnectionString = _file.ConnectionString;
                o.MigrationLockTimeoutSeconds = 0;
            })
            .Services
            .AddSingleton<IValidateOptions<DatabaseOptions>, DataAnnotationsValidateOptions<DatabaseOptions>>()
            .AddSingleton(new ScannedAssemblies([typeof(MigrationCliTests).Assembly]))
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var (code, _, err) = await Run(sp, command);

        code.Should().Be(MigrationCli.Failure);
        err.Should().StartWith($"{command} failed:");
        err.Should().NotContain("   at ");
    }
}
