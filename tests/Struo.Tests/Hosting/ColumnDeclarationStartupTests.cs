using AwesomeAssertions;
using Struo.Api.Hosting;
using Struo.Domain.Metadata;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// A column declaration the scan rejects stops the host while services are registered, in every
/// environment and before any database work, and a command reports the message without a stack trace.
/// </summary>
[Collection("ApiIntegration")]
public sealed class ColumnDeclarationStartupTests
{
    private const string InvalidAssembly = "Struo.Tests.InvalidFixtures";

    private static void AddInvalidContent(Dictionary<string, string?> settings) =>
        settings["Struo:ContentAssemblies:1"] = InvalidAssembly;

    [SqliteOnlyTheory("starts a host on a SQLite temp database")]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_content_assembly_with_ColumnShape_on_a_JSON_interface_fails_at_host_start(string environment)
    {
        var error = await ForkHost.StartupErrorAsync(mutate: AddInvalidContent, environment: environment);

        var rejected = error.Should().BeOfType<MetadataException>().Which;
        rejected.Message.Should().Contain("'InvalidShapeOnJsonCollection.Tags'")
            .And.Contain("[ColumnShape] and Interface = Tags")
            .And.Contain("Remove [ColumnShape]");
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task Both_environments_report_the_same_message()
    {
        var development = await ForkHost.StartupErrorAsync(mutate: AddInvalidContent, environment: "Development");
        var production = await ForkHost.StartupErrorAsync(mutate: AddInvalidContent, environment: "Production");

        development!.Message.Should().Be(production!.Message);
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task Core_and_the_test_fixtures_start_in_Production_too()
    {
        var error = await ForkHost.StartupErrorAsync(environment: "Production");

        error.Should().BeNull();
    }

    [Fact]
    public void A_command_prints_the_scan_message_and_fails_without_a_stack_trace()
    {
        var cli = MigrationCli.Parse(["migrate:check"]);
        var stderr = new StringWriter();

        var handled = StruoCmsCommands.TryReportStartupFailure(cli, new MetadataException("Field 'A.B' is wrong."), stderr);

        handled.Should().BeTrue();
        stderr.ToString().Should().Be("migrate:check failed: Field 'A.B' is wrong." + Environment.NewLine);
    }

    [Theory]
    [InlineData("make:migration")]
    [InlineData("migrate")]
    public void Every_command_reports_a_scan_failure(string command)
    {
        var stderr = new StringWriter();

        StruoCmsCommands.TryReportStartupFailure(MigrationCli.Parse([command]), new MetadataException("bad"), stderr)
            .Should().BeTrue();

        stderr.ToString().Should().StartWith($"{command} failed: bad");
    }

    [Fact]
    public void Without_a_command_the_failure_is_left_to_the_caller()
    {
        var stderr = new StringWriter();

        StruoCmsCommands.TryReportStartupFailure(MigrationCli.Parse([]), new MetadataException("bad"), stderr)
            .Should().BeFalse();

        stderr.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Other_startup_failures_are_left_to_the_caller()
    {
        var stderr = new StringWriter();

        StruoCmsCommands.TryReportStartupFailure(MigrationCli.Parse(["migrate"]), new InvalidOperationException("other"), stderr)
            .Should().BeFalse();
        StruoCmsCommands.TryReportStartupFailure(null, new MetadataException("bad"), stderr).Should().BeFalse();

        stderr.ToString().Should().BeEmpty();
    }
}
