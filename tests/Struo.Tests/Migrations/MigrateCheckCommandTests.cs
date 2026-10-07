using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

[Collection("MigrationCli")]
public sealed class MigrateCheckCommandTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var sp in _providers) sp.Dispose();
        _file.Dispose();
    }

    private sealed class StubCollector(IReadOnlyList<Type> types) : IEntityTypeCollector
    {
        public IReadOnlyList<Type> CollectForInitTables() => types;
    }

    // A unique prefix per test: SqlSugar caches entity info per prefix for the whole process.
    private ServiceProvider Services(string prefix, StruoDbType db = StruoDbType.Sqlite, string? conn = null)
    {
        var sp = new ServiceCollection()
            .AddSingleton<IEntityTypeCollector>(new StubCollector(FrameworkEntityTypes.All))
            .AddSingleton(Options.Create(new DatabaseOptions
                { DbType = db, ConnectionString = conn ?? _file.ConnectionString, TablePrefix = prefix }))
            .AddScoped<ISqlSugarClient>(p => SqlSugarClientFactory.Create(
                p.GetRequiredService<IOptions<DatabaseOptions>>().Value, new TestCurrentUserAccessor(Guid.Empty)))
            .BuildServiceProvider();
        _providers.Add(sp);
        return sp;
    }

    private static async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(args), sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task A_codefirst_schema_passes()
    {
        var sp = Services("mchka_");
        using (var scope = sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().CodeFirst
                .InitTables(FrameworkEntityTypes.All.ToArray());

        var (code, output, err) = await Run(sp, "migrate:check");

        err.Should().BeEmpty();
        code.Should().Be(MigrationCli.Success);
        output.Should().Contain("0 error(s)");
    }

    [Fact]
    public async Task An_empty_database_fails_with_missing_tables_and_stays_empty()
    {
        var sp = Services("mchkb_");

        var (code, output, err) = await Run(sp, "migrate:check");

        err.Should().BeEmpty();
        code.Should().Be(MigrationCli.Failure);
        output.Should().Contain("mchkb_users: table does not exist").And.Contain("11 error(s)");
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().DbMaintenance.GetTableInfoList(false)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreachable_database_fails_with_one_readable_line()
    {
        var sp = Services("mchkc_", StruoDbType.PostgreSQL,
            "Host=127.0.0.1;Port=1;Database=x;Username=REPLACE_ME;Password=REPLACE_ME;Timeout=2");

        var (code, output, err) = await Run(sp, "migrate:check");

        code.Should().Be(MigrationCli.Failure);
        output.Should().BeEmpty();
        err.Should().StartWith("migrate:check failed:");
        err.Trim().Split('\n').Should().HaveCount(1);
        err.Should().NotContain("   at ");
    }

    [Fact]
    public async Task Command_arguments_are_a_usage_error()
    {
        var (code, _, err) = await Run(Services("mchkd_"), "migrate:check", "stray");

        code.Should().Be(MigrationCli.Usage);
        err.Should().Contain("Unexpected argument 'stray' for migrate:check.");
    }
}
