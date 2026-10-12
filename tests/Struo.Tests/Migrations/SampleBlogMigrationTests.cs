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

public sealed class SampleBlogMigrationTests : IDisposable
{
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly string _prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";

    public void Dispose() => _database.Dispose();

    private ServiceProvider Services()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:DbType"] = _database.DbType.ToString(),
            ["Database:ConnectionString"] = _database.ConnectionString,
            ["Database:TablePrefix"] = _prefix,
        }).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddStruoInfrastructure()
            .AddStruoMetadata(typeof(Struo.Sample.Blog.Article).Assembly)
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Core_and_sample_migrations_build_the_schema_the_entities_declare()
    {
        using var sp = Services();
        var assemblies = sp.GetRequiredService<Struo.Infrastructure.Metadata.ScannedAssemblies>().All;
        var host = new MigrationHost(
            new MigrationHostOptions(_database.DbType, _database.ConnectionString, _prefix, assemblies, LockTimeoutSeconds: 5)
            { Seed = CoreMigrationHarness.Seed() },
            NullLoggerFactory.Instance);

        var applied = await host.ApplyAsync(default);

        applied.Should().Contain(m => m.Description == "CreateBlogSchema");
        var report = SchemaChecker.Check(
            sp.GetRequiredService<ISqlSugarClient>(), _database.DbType,
            sp.GetRequiredService<IEntityTypeCollector>().CollectForInitTables());
        report.HasErrors.Should().BeFalse(report.ToString());
        report.Findings.Should().BeEmpty(report.ToString());
    }
}
