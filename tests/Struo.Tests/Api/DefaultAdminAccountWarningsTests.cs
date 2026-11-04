using AwesomeAssertions;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Struo.Api.Auth;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Migrations;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Production warns once when an active user still has the default bootstrap email (the form the seed stores). A changed email, a
/// deactivated account, or any other environment logs nothing.
/// </summary>
public sealed class DefaultAdminAccountWarningsTests : IDisposable
{
    private readonly CoreMigrationHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<List<(LogLevel Level, string Message)>> Run(
        string environment, string? email = null, bool active = true)
    {
        await _h.Host(CoreMigrationHarness.Seed(email: null, password: "")).ApplyAsync(default);
        var db = _h.Db();
        if (email is not null)
        {
            await db.Insertable(new User
            {
                Id = Guid.CreateVersion7(), Email = email, Password = "x", IsActive = active,
            }).ExecuteCommandAsync();
        }
        var logger = new ListLogger();
        await DefaultAdminAccountWarnings.LogAsync(
            db, new HostingEnvironment { EnvironmentName = environment }, logger);
        return logger.Entries;
    }

    [Fact]
    public async Task Production_with_an_active_default_account_logs_one_warning()
    {
        var entries = await Run("Production", CoreSeedData.DefaultAdminEmail);

        var warning = entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(CoreSeedData.DefaultAdminEmail);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task Other_environments_log_nothing(string environment) =>
        (await Run(environment, CoreSeedData.DefaultAdminEmail)).Should().BeEmpty();

    [Fact]
    public async Task A_deactivated_default_account_logs_nothing() =>
        (await Run("Production", CoreSeedData.DefaultAdminEmail, active: false)).Should().BeEmpty();

    [Fact]
    public async Task A_changed_email_logs_nothing() =>
        (await Run("Production", "owner@example.com")).Should().BeEmpty();

    [Fact]
    public async Task No_users_log_nothing() =>
        (await Run("Production")).Should().BeEmpty();
}
