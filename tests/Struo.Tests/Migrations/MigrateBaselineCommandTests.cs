using Struo.Tests.Support.Seeding;
using AwesomeAssertions;
using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

[Collection("MigrationCli")] // OptionsOverride is static; keep these tests serial
public sealed class MigrateBaselineCommandTests : IDisposable
{
    private const string AdminEmail = "admin@example.com";
    private const string AdminPassword = "s3cret-pw";
    private static readonly string[] PublicRead = ["article", "page"];

    private readonly SqliteTestDatabase _file = new();

    public MigrateBaselineCommandTests() =>
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations.Core" };

    public void Dispose()
    {
        MigrationCli.OptionsOverride = o => o;
        _file.Dispose();
    }

    private static string NewPrefix() => "t" + Guid.NewGuid().ToString("N")[..8] + "_";

    private static ISqlSugarClient Client(StruoDbType dbType, string conn, string prefix) =>
        SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = dbType, ConnectionString = conn, TablePrefix = prefix },
            new TestCurrentUserAccessor(Guid.Empty),
            new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
            {
                [typeof(FileTranslation)] = TranslationSidecarIndexPolicy.KeyFor(typeof(FileTranslation), "FileId", "Locale")
            }));

    private static ServiceProvider Services(StruoDbType dbType, string conn, string prefix) =>
        new ServiceCollection()
            .AddSingleton(Options.Create(new DatabaseOptions { DbType = dbType, ConnectionString = conn, TablePrefix = prefix }))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:BootstrapAdmin:Email"] = AdminEmail,
                ["Auth:BootstrapAdmin:Password"] = AdminPassword,
            }).Build())
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()
            .AddSingleton(new ScannedAssemblies([typeof(StruoMigration).Assembly]))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

    private static async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(args), sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    /// <summary>A database as a v0.8.x deployment left it: CodeFirst tables plus the three seeders.</summary>
    private static async Task SimulateV08(ISqlSugarClient db)
    {
        db.CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());
        var localization = new LocalizationOptions
        {
            DefaultLanguage = "en",
            Languages = [new LanguageSeedEntry { Code = "en", Name = "English" }, new LanguageSeedEntry { Code = "zh-TW", Name = "Chinese" }],
        };
        await LanguageSeeder.SeedAsync(db, localization);
        await AdminUserSeeder.SeedAsync(db, new Argon2idPasswordHasher(), AdminEmail, AdminPassword);
        await RbacSeeder.SeedAsync(db, AdminEmail, PublicRead);
    }

    private static int[] Counts(ISqlSugarClient db) =>
    [
        db.Queryable<Language>().Count(), db.Queryable<User>().Count(), db.Queryable<Role>().Count(),
        db.Queryable<UserRole>().Count(), db.Queryable<Permission>().Count(),
    ];

    private static async Task AssertBaselineJoinsDatabase(
        StruoDbType dbType, string conn, string prefix, bool nonUnicodeSqlServer = false)
    {
        var db = Client(dbType, conn, prefix);
        // v0.8 created SQL Server string columns as varchar; this client config belongs to this prefix only.
        if (nonUnicodeSqlServer) db.CurrentConnectionConfig.MoreSettings.SqlServerCodeFirstNvarchar = false;
        await SimulateV08(db);
        var before = Counts(db);
        var passwordBefore = db.Queryable<User>().First().Password;
        using var sp = Services(dbType, conn, prefix);

        var baseline = await Run(sp, "migrate:baseline");

        baseline.Code.Should().Be(MigrationCli.Success, baseline.Err);
        baseline.Out.Should().Contain("202610080000").And.Contain("202610080001").And.Contain("2 migration(s) recorded");
        Counts(db).Should().Equal(before);
        var user = db.Queryable<User>().First();
        user.Password.Should().Be(passwordBefore);
        new Argon2idPasswordHasher().Verify(user.Password, AdminPassword).Should().BeTrue();

        (await Run(sp, "migrate")).Out.Should().Contain("Nothing to migrate.");
        (await Run(sp, "migrate:status")).Out.Should().Contain("0 pending");
        (await Run(sp, "migrate:baseline")).Code.Should().Be(MigrationCli.Failure);

        var report = SchemaChecker.Check(db, dbType, FrameworkEntityTypes.All);
        report.HasErrors.Should().BeFalse(report.ToString());
        if (nonUnicodeSqlServer)
            report.Warnings.Should().Contain(f => f.Kind == FindingKind.NonUnicodeString, report.ToString());
    }

    [Fact]
    public async Task Baseline_records_every_migration_of_a_v08_database_without_touching_its_data() =>
        await AssertBaselineJoinsDatabase(StruoDbType.Sqlite, _file.ConnectionString, NewPrefix());

    [Fact]
    public async Task Migrate_on_a_v08_database_points_at_baseline_in_one_line_and_changes_nothing()
    {
        var prefix = NewPrefix();
        var db = Client(StruoDbType.Sqlite, _file.ConnectionString, prefix);
        await SimulateV08(db);
        var tables = db.DbMaintenance.GetTableInfoList(false).Select(t => t.Name).Order().ToArray();
        using var sp = Services(StruoDbType.Sqlite, _file.ConnectionString, prefix);

        var result = await Run(sp, "migrate");

        result.Code.Should().Be(MigrationCli.Failure);
        result.Out.Should().BeEmpty();
        result.Err.TrimEnd().Split('\n').Should().ContainSingle().Which.Should().Contain("migrate:baseline");
        db.DbMaintenance.GetTableInfoList(false).Select(t => t.Name).Order().Should().Equal(tables);
    }

    [Fact]
    public async Task A_second_baseline_is_refused_in_one_line_and_changes_nothing()
    {
        var prefix = NewPrefix();
        await SimulateV08(Client(StruoDbType.Sqlite, _file.ConnectionString, prefix));
        using var sp = Services(StruoDbType.Sqlite, _file.ConnectionString, prefix);
        (await Run(sp, "migrate:baseline")).Code.Should().Be(MigrationCli.Success);

        var second = await Run(sp, "migrate:baseline");

        second.Code.Should().Be(MigrationCli.Failure);
        second.Out.Should().BeEmpty();
        second.Err.TrimEnd().Split('\n').Should().ContainSingle().Which.Should().Contain("already");
    }

    [Fact]
    public async Task Baseline_on_an_empty_database_points_at_migrate_and_creates_nothing()
    {
        var prefix = NewPrefix();
        using var sp = Services(StruoDbType.Sqlite, _file.ConnectionString, prefix);

        var result = await Run(sp, "migrate:baseline");

        result.Code.Should().Be(MigrationCli.Failure);
        result.Err.TrimEnd().Split('\n').Should().ContainSingle().Which.Should().Contain("migrate");
        new SqlSugarClient(new ConnectionConfig
            { DbType = DbType.Sqlite, ConnectionString = _file.ConnectionString, IsAutoCloseConnection = true })
            .DbMaintenance.GetTableInfoList(false).Should().BeEmpty();
    }

    [Fact]
    public async Task Baseline_with_an_extra_argument_is_a_usage_error()
    {
        using var sp = Services(StruoDbType.Sqlite, _file.ConnectionString, NewPrefix());

        var result = await Run(sp, "migrate:baseline", "extra");

        result.Code.Should().Be(MigrationCli.Usage);
    }

    [Fact]
    public async Task Baseline_records_content_assembly_migrations_alongside_the_core_ones()
    {
        var prefix = NewPrefix();
        await SimulateV08(Client(StruoDbType.Sqlite, _file.ConnectionString, prefix));
        using var sp = new ServiceCollection()
            .AddSingleton(Options.Create(new DatabaseOptions
                { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString, TablePrefix = prefix }))
            .AddSingleton(new ScannedAssemblies([typeof(StruoMigration).Assembly, typeof(MigrateBaselineCommandTests).Assembly]))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var result = await Run(sp, "migrate:baseline");

        result.Code.Should().Be(MigrationCli.Success, result.Err);
        result.Out.Should().Contain("202610090000").And.Contain("3 migration(s) recorded");
        (await Run(sp, "migrate:status")).Out.Should().Contain("0 pending");
    }

    [Fact]
    public async Task A_failure_while_recording_leaves_no_version_rows_so_baseline_can_be_retried()
    {
        var prefix = NewPrefix();
        await SimulateV08(Client(StruoDbType.Sqlite, _file.ConnectionString, prefix));
        var failing = new MigrationHost(HostOptions(prefix, failOnSecondRecord: true), NullLoggerFactory.Instance);

        var act = () => failing.BaselineAsync(default);

        await act.Should().ThrowAsync<MigrationFailedException>().WithMessage("*injected*");
        var healthy = new MigrationHost(HostOptions(prefix), NullLoggerFactory.Instance);
        healthy.GetStatus().Should().OnlyContain(m => m.State == MigrationState.Pending);
        (await healthy.BaselineAsync(default)).Should().HaveCount(2);
    }

    private MigrationHostOptions HostOptions(string prefix, bool failOnSecondRecord = false) =>
        new(StruoDbType.Sqlite, _file.ConnectionString, prefix, [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 5)
        {
            NamespaceFilter = "Struo.Infrastructure.Migrations.Core",
            ConfigureRunnerServices = failOnSecondRecord
                ? services => services.AddScoped<IVersionLoader>(sp =>
                    new FailingVersionLoader(ActivatorUtilities.CreateInstance<VersionLoader>(sp), failAtCall: 2))
                : null,
        };

    public static TheoryData<string> Backends => LiveBackend.Names();

    [Theory, MemberData(nameof(Backends))]
    public async Task Baseline_joins_a_v08_database_on_every_live_backend(string backend)
    {
        var (_, dbType, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var prefix = NewPrefix();
        var db = Client(dbType, conn, prefix);
        try { await AssertBaselineJoinsDatabase(dbType, conn, prefix, nonUnicodeSqlServer: dbType == StruoDbType.SqlServer); }
        finally
        {
            foreach (var table in CoreSchemaParity.AllTableNames(db, prefix).Append(StruoVersionTableMetaData.TableNameFor(prefix)))
                if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
        }
    }
}
