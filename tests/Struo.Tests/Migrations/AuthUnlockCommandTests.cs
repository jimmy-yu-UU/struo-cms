using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Tests.Security;
using Struo.Infrastructure.Migrations.Commands;
using Xunit;

namespace Struo.Tests.Migrations;

[Collection("MigrationCli")]
public sealed class AuthUnlockCommandTests
{
    private static readonly string? RedisConnection =
        Environment.GetEnvironmentVariable(RedisLoginAttemptThrottleTests.EnvVar);

    private sealed class RecordingThrottle : ILoginAttemptThrottle
    {
        public readonly List<string> Resets = [];
        public Exception? FailWith { get; init; }

        public Task<LoginThrottleState> TryReserveAsync(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LoginThrottleState> GetStateAsync(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ResetAsync(string email, CancellationToken ct = default)
        {
            if (FailWith is not null) throw FailWith;
            Resets.Add(email);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider Services(
        string? redis, ILoginAttemptThrottle throttle, bool throttleEnabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(redis is null ? [] : [new("Redis:ConnectionString", redis)])
            .Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddSingleton(throttle)
            .AddSingleton(Options.Create(new LoginAccountRateLimitOptions { Enabled = throttleEnabled }))
            .BuildServiceProvider();
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(IServiceProvider sp, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var invocation = MigrationCli.Parse(["auth:unlock", .. args]);
        var code = await MigrationCli.RunAsync(invocation, sp, stdout, stderr, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Parse_treats_auth_unlock_as_a_command_and_keeps_its_arguments()
    {
        var inv = MigrationCli.Parse(["auth:unlock", "--email", "a@b.c", "--Redis:ConnectionString=localhost"]);
        inv.Command.Should().Be("auth:unlock");
        inv.CommandArgs.Should().Equal("--email", "a@b.c");
        inv.HostArgs.Should().Equal("--Redis:ConnectionString=localhost");
    }

    [Theory]
    [InlineData]
    [InlineData("--email")]
    [InlineData("--email", "")]
    [InlineData("--email", "   ")]
    [InlineData("--email=")]
    [InlineData("a@b.c")]
    [InlineData("--email", "a@b.c", "--extra")]
    [InlineData("--email", "a@b.c", "--email", "d@e.f")]
    public async Task A_missing_blank_or_unexpected_argument_is_a_usage_error(params string[] args)
    {
        var throttle = new RecordingThrottle();
        using var sp = Services("localhost:6379", throttle);

        var (code, stdout, stderr) = await RunAsync(sp, args);

        code.Should().Be(MigrationCli.Usage);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("Usage: auth:unlock --email <email>");
        throttle.Resets.Should().BeEmpty();
    }

    [Fact]
    public async Task An_email_longer_than_the_column_is_a_usage_error()
    {
        var throttle = new RecordingThrottle();
        using var sp = Services("localhost:6379", throttle);

        var (code, _, stderr) = await RunAsync(sp, "--email", new string('a', EmailAddress.MaxLength + 1));

        code.Should().Be(MigrationCli.Usage);
        stderr.Should().Contain("Usage: auth:unlock --email <email>");
        throttle.Resets.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_redis_it_explains_the_restart_and_fails(string? redis)
    {
        var throttle = new RecordingThrottle();
        using var sp = Services(redis, throttle);

        var (code, stdout, stderr) = await RunAsync(sp, "--email", "admin@struo.test");

        code.Should().Be(MigrationCli.Failure);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("memory of the API process").And.Contain("Restart the API");
        stderr.Should().NotContain("admin@struo.test");
        throttle.Resets.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--email", "  Admin@Struo.Test ")]
    [InlineData("--email=  Admin@Struo.Test ")]
    public async Task With_redis_it_resets_the_account_and_does_not_echo_the_email(params string[] args)
    {
        var throttle = new RecordingThrottle();
        using var sp = Services("localhost:6379", throttle);

        var (code, stdout, stderr) = await RunAsync(sp, args);

        code.Should().Be(MigrationCli.Success);
        stderr.Should().BeEmpty();
        stdout.Should().Contain("login lock");
        stdout.ToLowerInvariant().Should().NotContain("admin@struo.test");
        throttle.Resets.Should().Equal(["  Admin@Struo.Test "]);
    }

    [Theory]
    [InlineData("localhost:6379")]
    [InlineData(null)]
    public async Task With_the_throttle_disabled_there_is_no_lock_to_clear(string? redis)
    {
        var throttle = new RecordingThrottle();
        using var sp = Services(redis, throttle, throttleEnabled: false);

        var (code, stdout, _) = await RunAsync(sp, "--email", "a@b.c");

        code.Should().Be(MigrationCli.Success);
        stdout.Should().Contain("disabled");
        throttle.Resets.Should().BeEmpty();
    }

    [Fact]
    public async Task A_redis_failure_fails_without_leaking_the_connection_string()
    {
        var throttle = new RecordingThrottle { FailWith = new InvalidOperationException("boom password=hunter2") };
        using var sp = Services("localhost:6379,password=hunter2", throttle);

        var (code, stdout, stderr) = await RunAsync(sp, "--email", "a@b.c");

        code.Should().Be(MigrationCli.Failure);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("InvalidOperationException").And.Contain("Redis").And.NotContain("hunter2");
    }

    [Fact]
    public async Task A_cancellation_is_not_reported_as_a_failed_reset()
    {
        var throttle = new RecordingThrottle { FailWith = new OperationCanceledException() };
        using var sp = Services("localhost:6379", throttle);

        var act = () => RunAsync(sp, "--email", "a@b.c");

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Against_a_live_redis_it_clears_a_locked_account()
    {
        if (RedisConnection is null) return;

        var email = $"unlock-{Guid.NewGuid():N}@struo.test";
        var options = Options.Create(new LoginAccountRateLimitOptions { Enabled = true, PermitLimit = 2, WindowSeconds = 60 });
        using var throttle = new RedisLoginAttemptThrottle(RedisConnection, options);
        try
        {
            await throttle.TryReserveAsync(email);
            await throttle.TryReserveAsync(email);
            (await throttle.GetStateAsync(email)).IsBlocked.Should().BeTrue();

            using var sp = Services(RedisConnection, throttle);
            var (code, _, _) = await RunAsync(sp, "--email", email.ToUpperInvariant());

            code.Should().Be(MigrationCli.Success);
            (await throttle.GetStateAsync(email)).IsBlocked.Should().BeFalse();
        }
        finally
        {
            await throttle.ResetAsync(email);
        }
    }
}
