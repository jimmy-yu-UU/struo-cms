using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class FixtureMigrationTests : IDisposable
{
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public ListLogger Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly string _prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Fixture_migration_applies_cleanly_alongside_core_on_a_fresh_database()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:DbType"] = _database.DbType.ToString(),
            ["Database:ConnectionString"] = _database.ConnectionString,
            ["Database:TablePrefix"] = _prefix,
        }).Build();
        using var sp = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddStruoInfrastructure()
            .AddStruoMetadata(typeof(Struo.Tests.Fixtures.FxArticle).Assembly)
            .BuildServiceProvider();
        var assemblies = sp.GetRequiredService<Struo.Infrastructure.Metadata.ScannedAssemblies>().All;
        var logs = new CapturingLoggerFactory();
        var host = new MigrationHost(
            new MigrationHostOptions(_database.DbType, _database.ConnectionString, _prefix, assemblies, LockTimeoutSeconds: 5)
            { Seed = CoreMigrationHarness.Seed() },
            logs);

        var applied = await host.ApplyAsync(default);

        applied.Should().Contain(m => m.Description == "CreateFixtureSchema");
        logs.Logger.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message)
            .Should().BeEmpty("the fixture migration applies with core without warning-level logs");
        var db = sp.GetRequiredService<ISqlSugarClient>();
        db.DbMaintenance.IsAnyTable("fx_articles", false).Should().BeTrue();
        db.DbMaintenance.GetColumnInfosByTableName("fx_article_tags", false)
            .Select(c => c.DbColumnName.ToLowerInvariant())
            .Should().Contain(["articleid", "tagid", "note", "sort"]);
        var report = SchemaChecker.Check(db, _database.DbType,
            sp.GetRequiredService<IEntityTypeCollector>().CollectForInitTables());
        report.Findings.Should().BeEmpty(report.ToString());
    }
}
