using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Infrastructure.Migrations.Core;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Migrations.Utf8;

[Collection("MigrationCli")] // OptionsOverride is static; keep these tests serial
public sealed class Utf8Mb4CollisionLiveTests(ITestOutputHelper output) : IDisposable
{
    public void Dispose() => MigrationCli.OptionsOverride = o => o;

    private static async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(args), sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    private static ServiceProvider Services(Utf8LiveDatabase database)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdmin:Email"] = "cli@example.com",
            ["Auth:BootstrapAdmin:Password"] = "cli-plain-pw-1",
        }).Build();
        var options = new DatabaseOptions
        {
            DbType = database.DbType, ConnectionString = database.Connection, TablePrefix = database.Prefix,
        };
        return new ServiceCollection()
            .AddSingleton(Options.Create(options))
            .AddSingleton<IConfiguration>(config)
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()
            .AddSingleton(new ScannedAssemblies([typeof(StruoMigration).Assembly]))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
    }

    [LiveBackendFact("MySql")]
    public async Task A_unique_collision_stops_migrate_with_the_duplicate_entry_and_a_rerun_succeeds_once_it_is_fixed()
    {
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations.Core" };
        using var database = Utf8LiveDatabase.Create("MySql");
        database.ApplyLegacy();
        database.InsertRole("stra" + Utf8LiveDatabase.Eszett + "e");
        database.InsertRole("strasse");
        using var sp = Services(database);

        var failed = await Run(sp, "migrate");

        output.WriteLine("stdout: " + failed.Out);
        output.WriteLine("stderr: " + failed.Err);
        failed.Code.Should().Be(MigrationCli.Failure);
        // The server names whichever of the two values it checks second.
        failed.Err.Should().MatchRegex("Duplicate entry '(strasse|stra\u00DFe)' for key")
            .And.Contain("ux_" + database.Prefix + "roles_name");
        database.Host().GetStatus().Single(m => m.Version == ConvertStringColumnsToUtf8Mb4.Version)
            .State.Should().Be(MigrationState.Pending);

        database.Db.Deleteable<Role>().Where(r => r.Name == "strasse").ExecuteCommand();
        var rerun = await Run(sp, "migrate");

        output.WriteLine("rerun stdout: " + rerun.Out);
        rerun.Code.Should().Be(MigrationCli.Success, rerun.Err);
        database.NonUtf8Mb4Columns().Should().BeEmpty();
    }
}
