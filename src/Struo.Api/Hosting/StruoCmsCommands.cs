// src/Struo.Api/Hosting/StruoCmsCommands.cs
using Struo.Domain.Metadata;
using Struo.Infrastructure.Migrations.Commands;

namespace Struo.Api.Hosting;

/// <summary>The command-line commands (<c>migrate*</c>, <c>make:migration</c>, <c>files:sweep</c>, <c>auth:unlock</c>) behind <c>TryRunStruoCommandAsync</c>.</summary>
public static class StruoCmsCommands
{
    /// <summary>
    /// Runs the command named on the command line, if there is one, and sets <c>Environment.ExitCode</c>
    /// to its result. Returns <c>true</c> when a command ran, so the caller returns without starting the host.
    /// </summary>
    public static async Task<bool> TryRunStruoCommandAsync(this WebApplication app, MigrationCliInvocation cli)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(cli);

        if (cli.Command is null) return false;

        Environment.ExitCode = await MigrationCli.RunAsync(
            cli, app.Services, Console.Out, Console.Error, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Reports a metadata scan failure that stopped the host while a command was requested: one line
    /// naming the command and the scan's message, no stack trace. Returns <c>false</c>, and writes
    /// nothing, when no command was requested or the failure is something else.
    /// </summary>
    public static bool TryReportStartupFailure(MigrationCliInvocation? cli, Exception failure, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(stderr);

        if (cli?.Command is not { } command || failure is not MetadataException) return false;

        stderr.WriteLine($"{command} failed: {failure.Message.ReplaceLineEndings(" ")}");
        return true;
    }
}
