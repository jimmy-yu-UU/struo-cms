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

    private static async Task AssertBaselineJoinsDatabase(StruoDbType dbType, string conn, string prefix)
    {
        var db = Client(dbType, conn, prefix);
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
    }

    [Fact]
    public async Task Baseline_records_every_migration_of_a_v08_database_without_touching_its_data() =>
        await AssertBaselineJoinsDatabase(StruoDbType.Sqlite, _file.ConnectionString, NewPrefix());

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
    public async Task Baseline_on_PostgreSQL_joins_a_v08_database()
    {
        if (LiveDatabases.Postgres is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var prefix = NewPrefix();
        var db = Client(StruoDbType.PostgreSQL, conn, prefix);
        try { await AssertBaselineJoinsDatabase(StruoDbType.PostgreSQL, conn, prefix); }
        finally
        {
            foreach (var table in CoreSchemaParity.AllTableNames(db, prefix).Append(StruoVersionTableMetaData.TableNameFor(prefix)))
                if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
        }
    }
}
