using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Migrations.Commands;

/// <summary>The migration commands. Exit codes: 0 success, 1 failure, 2 usage error.</summary>
public static class MigrationCli
{
    public const int Success = 0, Failure = 1, Usage = 2;
    private static readonly string[] Runnable = ["migrate", "migrate:status", "migrate:preview", "migrate:check", "migrate:baseline", "make:migration"];

    /// <summary>Lets tests adjust the host options, for example to restrict the scanned namespace.</summary>
    internal static Func<MigrationHostOptions, MigrationHostOptions> OptionsOverride { get; set; } = o => o;

    /// <summary>Lets tests fix the version stamp that <c>make:migration</c> derives from the UTC time.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    public static MigrationCliInvocation Parse(string[] args)
    {
        if (args.Length == 0 || !IsCommand(args[0])) return new MigrationCliInvocation(null, [], args);

        List<string> commandArgs = [], hostArgs = [];
        for (var i = 1; i < args.Length; i++)
        {
            if (!IsConfigKey(args[i])) { commandArgs.Add(args[i]); continue; }
            hostArgs.Add(args[i]);
            if (!args[i].Contains('=') && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                hostArgs.Add(args[++i]);
        }
        return new MigrationCliInvocation(args[0], commandArgs, [.. hostArgs]);
    }

    private static bool IsCommand(string arg) =>
        arg == "migrate" || arg.StartsWith("migrate:", StringComparison.Ordinal) || arg == "make:migration";

    private static readonly string[] HostSwitches = ["--environment", "--contentRoot", "--applicationName", "--urls"];

    // Config keys the host reads: --Key:Sub[=v], /Key:Sub[=v] and bare Key:Sub=v, plus the standard host
    // switches. A key containing a path separator is a path argument, not a config key.
    private static bool IsConfigKey(string arg)
    {
        var key = arg.Split('=', 2)[0];
        if (arg.StartsWith("--", StringComparison.Ordinal))
            return key.Contains(':') || HostSwitches.Contains(key, StringComparer.OrdinalIgnoreCase);

        var slash = key.StartsWith('/');
        var name = slash ? key[1..] : key;
        if (name.Contains('/') || name.Contains('\\') || !name.Contains(':')) return false;
        return slash || arg.Contains('=');
    }

    public static async Task<int> RunAsync(
        MigrationCliInvocation invocation, IServiceProvider services,
        TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (invocation.Command is not { } command || !Runnable.Contains(command))
        {
            await stderr.WriteLineAsync(
                $"Unknown command '{invocation.Command}'. Available: {string.Join(", ", Runnable)}.");
            return Usage;
        }

        if (command == "make:migration")
            return await MakeMigrationCommand.RunAsync(invocation.CommandArgs, services, stdout, stderr, Clock);

        if (invocation.CommandArgs.Count > 0)
        {
            await stderr.WriteLineAsync($"Unexpected argument '{invocation.CommandArgs[0]}' for {command}.");
            return Usage;
        }

        if (command == "migrate:check")
            return await MigrateCheckCommand.RunAsync(services, stdout, stderr);

        try
        {
            var host = new MigrationHost(
                OptionsOverride(MigrationHostOptions.FromServices(
                    services, includeSeed: command is "migrate" or "migrate:preview")),
                services.GetRequiredService<ILoggerFactory>());
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
                case "migrate:baseline":
                    var recorded = await host.BaselineAsync(ct);
                    foreach (var m in recorded) await stdout.WriteLineAsync($"Recorded {m.Version}  {m.Description}");
                    await stdout.WriteLineAsync($"{recorded.Count} migration(s) recorded as applied.");
                    break;
                default:
                    await stderr.WriteLineAsync($"Command '{command}' is not implemented.");
                    return Usage;
            }
            return Success;
        }
        catch (Exception ex) when (ex is MigrationFailedException or MigrationLockTimeoutException or MigrationBaselineRefusedException
            or OptionsValidationException)
        {
            await stderr.WriteLineAsync($"{command} failed: {ex.Message}");
            return Failure;
        }
    }
}
