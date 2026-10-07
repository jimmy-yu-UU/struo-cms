using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Core;

[Collection("MigrationCli")] // OptionsOverride is static; keep these tests serial
public sealed class CoreMigrateCliTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly string _prefix = $"t{Guid.NewGuid():N}"[..9] + "_";

    public CoreMigrateCliTests() =>
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations.Core" };

    public void Dispose()
    {
        MigrationCli.OptionsOverride = o => o;
        _file.Dispose();
    }

    [Fact]
    public async Task Migrate_seeds_from_configuration_and_preview_leaves_the_database_empty()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdmin:Email"] = "cli@example.com",
            ["Auth:BootstrapAdmin:Password"] = "cli-plain-pw",
            ["Rbac:PublicReadCollections:0"] = "article",
        }).Build();
        using var sp = new ServiceCollection()
            .AddSingleton(Options.Create(new DatabaseOptions
                { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString, TablePrefix = _prefix }))
            .AddSingleton<IConfiguration>(config)
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()
            .AddSingleton(new ScannedAssemblies([typeof(StruoMigration).Assembly]))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var preview = await Run(sp, "migrate:preview");
        preview.Code.Should().Be(MigrationCli.Success);
        preview.Out.Should().Contain("INSERT INTO").And.NotContain("cli-plain-pw");
        Tables().Should().BeEmpty();

        var migrate = await Run(sp, "migrate");
        migrate.Code.Should().Be(MigrationCli.Success, migrate.Err);
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString, TablePrefix = _prefix },
            new TestCurrentUserAccessor(Guid.Empty),
            new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>()));
        db.Queryable<User>().Count().Should().Be(1);
        db.Queryable<Permission>().Count().Should().Be(1);
    }

    [Fact]
    public async Task Status_needs_neither_a_password_hasher_nor_configuration()
    {
        using var sp = new ServiceCollection()
            .AddSingleton(Options.Create(new DatabaseOptions
                { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString, TablePrefix = _prefix }))
            .AddSingleton(new ScannedAssemblies([typeof(StruoMigration).Assembly]))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var status = await Run(sp, "migrate:status");

        status.Code.Should().Be(MigrationCli.Success, status.Err);
        status.Out.Should().Contain("2 pending");
    }

    private string[] Tables() => new SqlSugarClient(new ConnectionConfig
        { DbType = DbType.Sqlite, ConnectionString = _file.ConnectionString, IsAutoCloseConnection = true })
        .DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant()).ToArray();

    private static async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(args), sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }
}
