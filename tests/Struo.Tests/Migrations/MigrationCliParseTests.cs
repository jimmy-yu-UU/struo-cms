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

    [Theory]
    [InlineData("migrate")]
    [InlineData("migrate:check")]
    [InlineData("make:migration")]
    public void Known_commands_are_recognised(string command) =>
        MigrationCli.Parse([command]).Command.Should().Be(command);
}
