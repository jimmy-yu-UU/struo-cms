using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Migrations.Commands;

/// <summary>The <c>migrate</c>, <c>migrate:status</c> and <c>migrate:preview</c> commands. Exit codes: 0 success, 1 failure, 2 usage error.</summary>
public static class MigrationCli
{
    public const int Success = 0, Failure = 1, Usage = 2;
    private static readonly string[] Known = ["migrate", "migrate:status", "migrate:preview"];

    /// <summary>Lets tests adjust the host options, for example to restrict the scanned namespace.</summary>
    internal static Func<MigrationHostOptions, MigrationHostOptions> OptionsOverride { get; set; } = o => o;

    public static MigrationCliInvocation Parse(string[] args) =>
        args.Length > 0 && args[0].StartsWith("migrate", StringComparison.Ordinal)
            ? new MigrationCliInvocation(args[0], args[1..])
            : new MigrationCliInvocation(null, args);

    public static async Task<int> RunAsync(
        MigrationCliInvocation invocation, IServiceProvider services,
        TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (invocation.Command is not { } command || !Known.Contains(command))
        {
            await stderr.WriteLineAsync(
                $"Unknown command '{invocation.Command}'. Available: {string.Join(", ", Known)}.");
            return Usage;
        }

        var db = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var host = new MigrationHost(
            OptionsOverride(new MigrationHostOptions(
                db.DbType, db.ConnectionString, db.TablePrefix,
                services.GetRequiredService<ScannedAssemblies>().All, db.MigrationLockTimeoutSeconds)),
            services.GetRequiredService<ILoggerFactory>());
        try
        {
            switch (command)
            {
                case "migrate":
                    var applied = await host.ApplyAsync(ct);
                    if (applied.Count == 0) await stdout.WriteLineAsync("Nothing to migrate.");
                    foreach (var m in applied) await stdout.WriteLineAsync($"Applied {m.Version}  {m.Description}");
                    break;
                case "migrate:status":
                    var status = host.GetStatus();
                    foreach (var m in status)
                        await stdout.WriteLineAsync(
                            $"{m.Version,-14} {m.State,-9} {(m.AppliedOn?.ToString("u") ?? "-"),-21} {m.Description}");
                    await stdout.WriteLineAsync(
                        $"{status.Count(m => m.State == MigrationState.Applied)} applied, " +
                        $"{status.Count(m => m.State == MigrationState.Pending)} pending, " +
                        $"{status.Count(m => m.State == MigrationState.Orphaned)} orphaned");
                    break;
                case "migrate:preview":
                    var pending = host.Preview(stdout);
                    await stdout.WriteLineAsync($"-- {pending.Count} migration(s) pending");
                    break;
            }
            return Success;
        }
        catch (Exception ex) when (ex is MigrationFailedException or MigrationLockTimeoutException)
        {
            await stderr.WriteLineAsync($"{command} failed: {ex.Message}");
            return Failure;
        }
    }
}
