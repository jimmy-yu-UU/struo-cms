using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Infrastructure.Migrations.Schema;

namespace Struo.Infrastructure.Migrations.Commands;

/// <summary>Compares the live database with the entity schema. Read-only: it takes no lock and creates nothing.</summary>
internal static class MigrateCheckCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, TextWriter stdout, TextWriter stderr)
    {
        SchemaCheckReport report;
        try
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            report = SchemaChecker.Check(
                sp.GetRequiredService<ISqlSugarClient>(),
                sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.DbType,
                sp.GetRequiredService<IEntityTypeCollector>().CollectForInitTables());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await stderr.WriteLineAsync($"migrate:check failed: {Innermost(ex).Message.ReplaceLineEndings(" ")}");
            return MigrationCli.Failure;
        }

        await stdout.WriteLineAsync(report.ToString());
        return report.HasErrors ? MigrationCli.Failure : MigrationCli.Success;
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }
}
