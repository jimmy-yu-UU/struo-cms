using AwesomeAssertions;
using Struo.Infrastructure.Migrations.Commands;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class MigrationCliParseTests
{
    [Fact]
    public void Make_migration_is_a_command_and_keeps_its_arguments()
    {
        var inv = MigrationCli.Parse(["make:migration", "CreatePosts", "--entity", "Post", "--output", "Migrations",
            "--Database:TablePrefix=acme_"]);
        inv.Command.Should().Be("make:migration");
        inv.CommandArgs.Should().Equal("CreatePosts", "--entity", "Post", "--output", "Migrations");
        inv.HostArgs.Should().Equal("--Database:TablePrefix=acme_");
    }

    [Fact]
    public void Host_key_without_equals_takes_the_next_token_as_its_value()
    {
        var inv = MigrationCli.Parse(["migrate:check", "--Database:DbType", "Sqlite"]);
        inv.CommandArgs.Should().BeEmpty();
        inv.HostArgs.Should().Equal("--Database:DbType", "Sqlite");
    }

    [Fact]
    public void Web_host_args_are_untouched() =>
        MigrationCli.Parse(["--urls=http://localhost:5221"]).Should()
            .BeEquivalentTo(new { Command = (string?)null, HostArgs = new[] { "--urls=http://localhost:5221" } });

    [Fact]
    public void Web_host_args_have_no_command_args() =>
        MigrationCli.Parse(["--urls=http://localhost:5221"]).CommandArgs.Should().BeEmpty();

    [Fact]
    public void Command_must_be_migrate_or_a_migrate_subcommand() =>
        MigrationCli.Parse(["migrateX"]).Command.Should().BeNull();

    [Fact]
    public void Standard_host_switch_with_a_separate_value_is_a_host_arg()
    {
        var inv = MigrationCli.Parse(["migrate", "--environment", "Production"]);
        inv.HostArgs.Should().Equal("--environment", "Production");
        inv.CommandArgs.Should().BeEmpty();
    }

    [Fact]
    public void Standard_host_switch_with_equals_is_a_host_arg()
    {
        var inv = MigrationCli.Parse(["make:migration", "X", "--urls=http://x", "--output", "d"]);
        inv.HostArgs.Should().Equal("--urls=http://x");
        inv.CommandArgs.Should().Equal("X", "--output", "d");
    }

    [Fact]
    public void Host_key_followed_by_another_option_has_no_value()
    {
        var inv = MigrationCli.Parse(["migrate:check", "--Database:DbType", "--Database:TablePrefix=x"]);
        inv.HostArgs.Should().Equal("--Database:DbType", "--Database:TablePrefix=x");
        inv.CommandArgs.Should().BeEmpty();
    }

    [Fact]
    public void Host_key_as_the_last_token_has_no_value()
    {
        var inv = MigrationCli.Parse(["migrate:check", "--Database:DbType"]);
        inv.HostArgs.Should().Equal("--Database:DbType");
        inv.CommandArgs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("migrate:check")]
    [InlineData("make:migration")]
    public void Known_commands_are_recognised(string command) =>
        MigrationCli.Parse([command]).Command.Should().Be(command);
}
