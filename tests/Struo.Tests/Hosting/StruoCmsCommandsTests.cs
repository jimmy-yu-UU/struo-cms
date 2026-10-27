// tests/Struo.Tests/Hosting/StruoCmsCommandsTests.cs
using AwesomeAssertions;
using Struo.Api.Hosting;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// <c>TryRunStruoCommandAsync</c> runs the command named on the command line and reports it, or does
/// nothing when there is none. The commands themselves are covered by the migration CLI tests.
/// </summary>
[Collection("ApiIntegration")]
public class StruoCmsCommandsTests
{
    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_named_command_runs_sets_the_exit_code_and_returns_true()
    {
        await using var host = await ForkHost.StartAsync();
        var previousExitCode = Environment.ExitCode;
        var previousOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Environment.ExitCode = 99;
            Console.SetOut(captured);

            var ran = await host.App.TryRunStruoCommandAsync(MigrationCli.Parse(["migrate:status"]));

            ran.Should().BeTrue();
            Environment.ExitCode.Should().Be(0);
            captured.ToString().Should().Contain("applied").And.Contain("0 pending");
        }
        finally
        {
            Console.SetOut(previousOut);
            Environment.ExitCode = previousExitCode;
        }
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task Without_a_command_it_returns_false_and_leaves_the_exit_code_alone()
    {
        await using var host = await ForkHost.StartAsync();
        var previousExitCode = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 77;

            var ran = await host.App.TryRunStruoCommandAsync(MigrationCli.Parse([]));

            ran.Should().BeFalse();
            Environment.ExitCode.Should().Be(77);
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }
}
